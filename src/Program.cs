using System.CommandLine;
using System.Diagnostics;
using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;
using BlogGenerator.Enums;
using BlogGenerator.MarkdigExtension;
using BlogGenerator.Models;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Renderers;
using Markdig.Syntax;
using Microsoft.Extensions.Configuration;
using RazorLight;
using YamlDotNet.Serialization;

var sw = Stopwatch.StartNew();

var inputOption = new Option<DirectoryInfo>(
        aliases: ["/input", "--input", "-i"],
        description: "入力フォルダー")
{ IsRequired = true };

var outputOption = new Option<DirectoryInfo>(
        aliases: ["/output", "--output", "-o"],
        description: "出力フォルダー")
{ IsRequired = true };

var themeOption = new Option<DirectoryInfo>(
        aliases: ["/theme", "--theme"],
        description: "出力フォルダー")
{ IsRequired = true };

var oEmbedOption = new Option<string>(
        aliases: ["/oembed", "--oembed"],
        description: "oEmbedキャッシュファイル")
{ IsRequired = false };

var rootCommand = new RootCommand("Markdown to HTML generator")
{
    inputOption,
    outputOption,
    themeOption,
    oEmbedOption
};

rootCommand.SetHandler(async (input, output, theme, oEmbedDir) =>
{
    var configuration = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
        .Build();

    var siteOption = configuration.GetSection("SiteOption").Get<SiteOption>();

    if (siteOption == null)
    {
        throw new ArgumentNullException($"{nameof(SiteOption)} is not found");
    }

    // 文字エンコーディング
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    // RazorLightエンジンの初期化
    var razorLightEngine = new RazorLightEngineBuilder()
        .UseFileSystemProject(theme.FullName)
        .UseMemoryCachingProvider()
        .DisableEncoding()
        .Build();

    // Markdig初期化
    MarkdownPipeline markDigPipeline;
    {
        markDigPipeline = new MarkdownPipelineBuilder()
            .UseYamlFrontMatter()
            .Use(new AmazonAssociateExtension(siteOption.AmazonAssociateTag))
            .Use<OEmbedCardExtension>()
            .UseAdvancedExtensions()
            .Build();

        if (!string.IsNullOrEmpty(oEmbedDir))
        {
            await OEmbedCardExtension.LoadOEmbedCacheAsync(oEmbedDir);
        }
    }




    // 出力先が存在しない場合は、フォルダを作成
    if (!Directory.Exists(output.FullName))
    {
        Directory.CreateDirectory(output.FullName);
    }

    // themeDirに渡されたフォルダパスから、 cshtmlファイル以外のファイル、フォルダをoutputDirにコピー
    foreach (var themeFile in Directory.GetFiles(theme.FullName, "*", SearchOption.AllDirectories).Where(x => !x.EndsWith(".cshtml") && !Path.GetFileName(x).StartsWith(".")))
    {
        var relativePath = Path.GetRelativePath(theme.FullName, themeFile);
        var outputPath = Path.Combine(output.FullName, relativePath);

        var outputDirPath = Path.GetDirectoryName(outputPath);
        if (!Directory.Exists(outputDirPath))
        {
            Directory.CreateDirectory(outputDirPath!);
        }

        File.Copy(themeFile, outputPath, true);
    }


    // inputDirに渡されたフォルダパスから、その階層にあるすべてのMarkdownファイルを取得
    var articles = Directory.GetFiles(input.FullName, "*.md", SearchOption.AllDirectories)
        .AsParallel()
        .Select(filePath =>
        {
            var relativePathExcludeFileName = Path.GetRelativePath(input.FullName, Path.GetDirectoryName(filePath)!).Replace("\\", "/");
            relativePathExcludeFileName = relativePathExcludeFileName == "." ? string.Empty : relativePathExcludeFileName;

            var routeRelativePath = Path.Combine(siteOption.BaseAbsolutePath, relativePathExcludeFileName);

            // Markdownファイルの内容を読み込む
            var (html, frontMatter) = ParseMarkdownWithFrontmatter(markDigPipeline, filePath, routeRelativePath);

            // コンテンツ系ファイルはMarkdownファイルを除いてそのままコピー
            CopyContentFile(input.FullName, output.FullName, filePath);

            return new Article(
                FileName: Path.ChangeExtension(Path.GetFileNameWithoutExtension(filePath), ".html"),
                Body: html,
                Title: frontMatter.Title,
                Tags: frontMatter.Tags ?? [],
                Published: frontMatter.Published,
                RelativeDirectoryPath: relativePathExcludeFileName,
                RootRelativeDirectoryPath: routeRelativePath,
                IsFixedPage: frontMatter.IsFixedPage
            );
        })
        .OrderByDescending(x => x.Published)
        .ToList();

    if (!string.IsNullOrEmpty(oEmbedDir))
    {
        await OEmbedCardExtension.SaveOEmbedCacheAsync(oEmbedDir);
    }

    // サイドバーのHTML生成
    var sideBarHtml = await razorLightEngine.CompileRenderAsync("SideBar.cshtml", new SideBarModel
    {
        SiteOption = siteOption,
        Articles = articles
    });


    // 各記事のHTMLファイルを生成
    foreach (var article in articles)
    {
        // Usage
        var outputFilePathWithoutExtension = Path.Combine(output.FullName, article.RelativeDirectoryPath, article.FileName);
        // 出力フォルダパス
        var outputDirPath = Path.GetDirectoryName(outputFilePathWithoutExtension);
        if (!Directory.Exists(outputDirPath))
        {
            Directory.CreateDirectory(outputDirPath!);
        }

        var model = new PageModel
        {
            SiteOption = siteOption,
            PageType = PageType.Article,
            SideBarHtml = sideBarHtml,
            Articles = [article]
        };

        // HTMLファイルを生成
        var cacheResult = razorLightEngine.Handler.Cache.RetrieveTemplate("Layout.cshtml");

        var result = cacheResult.Success
            ? await razorLightEngine.RenderTemplateAsync(cacheResult.Template.TemplatePageFactory(), model)
            : await razorLightEngine.CompileRenderAsync("Layout.cshtml", model);

        await File.WriteAllTextAsync(outputFilePathWithoutExtension, result, Encoding.UTF8);
    }


    // 10記事単位でページングしたHTMLファイルを生成
    {
        var pagedArticles = articles
            .Where(r => r.Published != DateTimeOffset.MinValue)
            .Select((article, index) => new { article, index })
            .GroupBy(x => x.index / 10)
            .Select(g => g.Select(x => x.article).ToList())
            .ToList();

        int pageIndex = 0;
        foreach (var pageArticles in pagedArticles)
        {
            var outputFilePath = pageIndex == 0
                ? CombineFilePath(output.FullName, "index.html")
                : CombineFilePath(output.FullName, $"{pageIndex + 1}.html");

            var model = new PageModel
            {
                SiteOption = siteOption,
                PageType = PageType.PageList,
                SideBarHtml = sideBarHtml,
                Articles = pageArticles,
                Pagination = new PaginationModel
                {
                    CurrentPage = pageIndex + 1,
                    TotalPages = pagedArticles.Count,
                    MaxPagesToShow = 6,
                    RelativeDirectoryPath = Path.Combine(siteOption.BaseAbsolutePath)
                }
            };

            var cacheResult = razorLightEngine.Handler.Cache.RetrieveTemplate("Layout.cshtml");

            var result = cacheResult.Success
                ? await razorLightEngine.RenderTemplateAsync(cacheResult.Template.TemplatePageFactory(), model)
                : await razorLightEngine.CompileRenderAsync("Layout.cshtml", model);

            await File.WriteAllTextAsync(outputFilePath, result, Encoding.UTF8);
            pageIndex++;
        }
    }


    // タグごとの記事一覧（Publishedで昇順並び替え）を取得
    var tagArticles = articles.SelectMany(x => x.Tags).Distinct().Select(tag => new
    {
        Tag = tag,
        Articles = articles.Where(x => x.Tags.Contains(tag)).OrderByDescending(x => x.Published).ToArray()
    }).ToArray();

    // タグ単位のHTMLを生成
    {
        foreach (var tagArticle in tagArticles)
        {
            var pagedArticles = tagArticle.Articles
                .Select((article, index) => new { article, index })
                .GroupBy(x => x.index / 10)
                .Select(g => g.Select(x => x.article).ToList())
                .ToList();

            var pageIndex = 0;
            foreach (var articleList in pagedArticles)
            {
                // 出力フォルダパス
                var outputFilePath = pageIndex == 0
                    ? Path.Combine(output.FullName, "tags", tagArticle.Tag, "index.html")
                    : Path.Combine(output.FullName, "tags", tagArticle.Tag, $"{pageIndex + 1}.html");

                var outputDirPath = Path.GetDirectoryName(outputFilePath);
                if (!Directory.Exists(outputDirPath))
                {
                    Directory.CreateDirectory(outputDirPath!);
                }

                var model = new PageModel
                {
                    SiteOption = siteOption,
                    PageType = PageType.PageList,
                    SideBarHtml = sideBarHtml,
                    Articles = articleList,
                    Pagination = new PaginationModel
                    {
                        CurrentPage = pageIndex + 1,
                        TotalPages = pagedArticles.Count,
                        MaxPagesToShow = 6,
                        RelativeDirectoryPath = Path.Combine(siteOption.BaseAbsolutePath, "tags", tagArticle.Tag)
                    }
                };

                var cacheResult = razorLightEngine.Handler.Cache.RetrieveTemplate("Layout.cshtml");

                var result = cacheResult.Success
                    ? await razorLightEngine.RenderTemplateAsync(cacheResult.Template.TemplatePageFactory(), model)
                    : await razorLightEngine.CompileRenderAsync("Layout.cshtml", model);

                await File.WriteAllTextAsync(outputFilePath, result, Encoding.UTF8);
                pageIndex++;
            }
        }
    }


    // 年月ごとの記事一覧（Publishedで昇順並び替え）を取得
    var yearMonthArticles = articles.GroupBy(x => x.Published.ToString("yyyy/MM"))
        .Select(group => new
        {
            YearMonth = group.Key,
            Articles = group.OrderByDescending(x => x.Published).ToArray()
        })
        .ToArray();



    // 年月単位の記事一覧ページを生成
    {
        foreach (var yearMonthArticle in yearMonthArticles)
        {
            var pagedArticles = yearMonthArticle.Articles
                .Where(r => r.Published != DateTimeOffset.MinValue)
                .Select((article, index) => new { article, index })
                .GroupBy(x => x.index / 10)
                .Select(g => g.Select(x => x.article).ToList())
                .ToList();

            var pageIndex = 0;
            foreach (var articleList in pagedArticles)
            {
                // 出力フォルダパス
                var outputFilePath = pageIndex == 0
                    ? CombineFilePath(output.FullName, Path.Combine(yearMonthArticle.YearMonth.Replace("/", Path.DirectorySeparatorChar.ToString()), "index.html"))
                    : CombineFilePath(output.FullName, Path.Combine(yearMonthArticle.YearMonth.Replace("/", Path.DirectorySeparatorChar.ToString()), $"{pageIndex + 1}.html"));

                var outputDirPath = Path.GetDirectoryName(outputFilePath);
                if (!Directory.Exists(outputDirPath))
                {
                    Directory.CreateDirectory(outputDirPath!);
                }

                var model = new PageModel
                {
                    SiteOption = siteOption,
                    PageType = PageType.PageList,
                    SideBarHtml = sideBarHtml,
                    Articles = articleList,
                    Pagination = new PaginationModel
                    {
                        CurrentPage = pageIndex + 1,
                        TotalPages = pagedArticles.Count,
                        MaxPagesToShow = 6,
                        RelativeDirectoryPath = Path.Combine(siteOption.BaseAbsolutePath, Path.Combine(yearMonthArticle.YearMonth.Replace("/", Path.DirectorySeparatorChar.ToString())))
                    }
                };

                var cacheResult = razorLightEngine.Handler.Cache.RetrieveTemplate("Layout.cshtml");

                var result = cacheResult.Success
                    ? await razorLightEngine.RenderTemplateAsync(cacheResult.Template.TemplatePageFactory(), model)
                    : await razorLightEngine.CompileRenderAsync("Layout.cshtml", model);

                await File.WriteAllTextAsync(outputFilePath, result, Encoding.UTF8);
                pageIndex++;
            }
        }
    }

    // タグ一覧ページを生成
    {
        var outputFilePath = CombineFilePath(output.FullName, Path.Combine("tags", "index.html"));
        var outputDirPath = Path.GetDirectoryName(outputFilePath);
        if (!Directory.Exists(outputDirPath))
        {
            Directory.CreateDirectory(outputDirPath!);
        }
        var model = new PageModel
        {
            SiteOption = siteOption,
            PageType = PageType.Tag,
            SideBarHtml = sideBarHtml,
            Articles = articles,
        };
        var cacheResult = razorLightEngine.Handler.Cache.RetrieveTemplate("Layout.cshtml");
        var result = cacheResult.Success
            ? await razorLightEngine.RenderTemplateAsync(cacheResult.Template.TemplatePageFactory(), model)
            : await razorLightEngine.CompileRenderAsync("Layout.cshtml", model);
        await File.WriteAllTextAsync(outputFilePath, result, Encoding.UTF8);
    }


    // RSSファイルを生成
    {
        var rssFeed = new SyndicationFeed(
            title: siteOption.SiteName,
            description: siteOption.SiteDescription,
            feedAlternateLink: new Uri(siteOption.SiteUrl),
            id: siteOption.SiteUrl,
            lastUpdatedTime: new DateTimeOffset(DateTime.UtcNow.AddHours(9).Ticks, TimeSpan.FromHours(9)))
        {
            Language = "ja-JP",
            Items = articles.Take(10).Select(article => new SyndicationItem(
                title: article.Title,
                content: article.ExcerptHtml,
                itemAlternateLink: new Uri($"{siteOption.SiteUrl.TrimEnd('/')}/{article.RelativeDirectoryPath.TrimEnd('/')}/{article.FileName}"),
                id: new Uri($"{siteOption.SiteUrl.TrimEnd('/')}/{article.RelativeDirectoryPath.TrimEnd('/')}/{article.FileName}").ToString(),
                lastUpdatedTime: article.Published
            ))
        };

        var writerRss20 = new Rss20FeedFormatter(rssFeed);

        var writerAtom10 = new Atom10FeedFormatter(rssFeed);

        await using var rssFile = File.Create(CombineFilePath(output.FullName, "feed.rss"));
        await using var atomFile = File.Create(CombineFilePath(output.FullName, "feed.atom"));

        await using (var rssWriter = XmlWriter.Create(rssFile, new XmlWriterSettings { Async = true, Indent = true, Encoding = new UTF8Encoding(false) }))
        {
            writerRss20.WriteTo(rssWriter);
        }

        await using (var atomWriter = XmlWriter.Create(atomFile, new XmlWriterSettings { Async = true, Indent = true, Encoding = new UTF8Encoding(false) }))
        {
            writerAtom10.WriteTo(atomWriter);
        }
    }

    // end
    Console.WriteLine("Completed: " + sw.Elapsed);
}, inputOption, outputOption, themeOption, oEmbedOption);

return await rootCommand.InvokeAsync(args);





static string CombineFilePath(string outputDir, string relativePath, string? extension = null)
{
    var combinedPath = Path.Combine(outputDir, relativePath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
    return extension == null ? combinedPath : Path.ChangeExtension(combinedPath, extension);
}

static void CopyContentFile(string inputDir, string outputDir, string filePath)
{
    var relativePath = Path.GetRelativePath(inputDir, filePath);
    var outputPath = Path.Combine(outputDir, relativePath);

    // 出力フォルダパス
    var outputDirPath = Path.GetDirectoryName(outputPath);
    if (!Directory.Exists(outputDirPath))
    {
        Directory.CreateDirectory(outputDirPath!);
    }

    var directoryInfo = new DirectoryInfo(Path.GetDirectoryName(filePath)!);
    foreach (var dir in directoryInfo.GetDirectories("*", SearchOption.AllDirectories))
    {
        var targetDir = dir.FullName.Replace(inputDir, outputDir);
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }
    }

    foreach (var fileInfo in directoryInfo.GetFiles("*", SearchOption.AllDirectories))
    {
        if (fileInfo.FullName != filePath && Path.GetExtension(fileInfo.FullName) != ".md" && !Path.GetFileName(fileInfo.FullName).StartsWith("."))
        {
            var targetFile = fileInfo.FullName.Replace(inputDir, outputDir);

            const int maxRetries = 3;
            const int delayMilliseconds = 3000;
            var attempt = 0;
            var success = false;

            while (!success)
            {
                try
                {
                    File.Copy(fileInfo.FullName, targetFile, true);
                    success = true;
                }
                catch (IOException ex) when (ex.Message.Contains("being used by another process"))
                {
                    attempt++;
                    if (attempt < maxRetries)
                    {
                        Thread.Sleep(delayMilliseconds);
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }
    }
}



(string html, Frontmatter frontMatter) ParseMarkdownWithFrontmatter(MarkdownPipeline markDigPipeline, string path, string basePath)
{
    var markdown = File.ReadAllText(path);

    var writer = new StringWriter();
    var renderer = new HtmlRenderer(writer);
    markDigPipeline.Setup(renderer);


    var document = Markdown.Parse(markdown, markDigPipeline);
    var yamlBlock = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault();

    var frontMatter = new Frontmatter();
    if (yamlBlock != null)
    {
        var yaml = yamlBlock.Lines.ToString();

        var deserializer = new Deserializer();

        frontMatter = deserializer.Deserialize<Frontmatter>(yaml);
    }

    var markdownContent = markdown;
    if (yamlBlock != null)
    {
        var yamlEndIndex = yamlBlock.Span.End;
        markdownContent = markdown[(yamlEndIndex + 1)..].TrimStart();
    }

    var markdownDocument = Markdown.Parse(markdownContent, markDigPipeline);

    // 画像パスを置換
    foreach (var link in markdownDocument.Descendants<Markdig.Syntax.Inlines.LinkInline>())
    {
        if (link.IsImage)
        {
            // SiteOptionのBaseUrlを使って、画像の相対パスを絶対パスに変換
            link.Url = Path.Combine(basePath, link.Url!).Replace("\\", "/");
        }
    }

    writer.GetStringBuilder().Clear();
    renderer.Render(markdownDocument);
    writer.Flush();

    var html = writer.ToString();

    return (html, frontMatter);
}
