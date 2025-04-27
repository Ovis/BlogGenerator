using BlogGenerator.MarkdigExtension;
using BlogGenerator.Models;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Renderers;
using Markdig.Syntax;
using YamlDotNet.Serialization;

namespace BlogGenerator.Core;

public class MarkdownProcessor
{
    private readonly SiteOption _siteOption;
    private readonly string? _oEmbedDir;
    private MarkdownPipeline _markdownPipeline;
    private readonly FileSystemHelper _fileSystemHelper = new();

    public MarkdownProcessor(SiteOption siteOption, string? oEmbedDir)
    {
        _siteOption = siteOption;
        _oEmbedDir = oEmbedDir;

        _markdownPipeline = new MarkdownPipelineBuilder()
            .UseYamlFrontMatter()
            .Use(new AmazonAssociateExtension(siteOption.AmazonAssociateTag))
            .Use<OEmbedCardExtension>()
            .UseAdvancedExtensions()
            .Build();
    }

    public async Task InitializeAsync()
    {
        if (!string.IsNullOrEmpty(_oEmbedDir))
        {
            await OEmbedCardExtension.LoadOEmbedCacheAsync(_oEmbedDir);
        }
    }

    public async Task<List<Article>> ProcessMarkdownFilesAsync(string inputDir, string outputDir, string baseAbsolutePath)
    {
        var articles = await Task.Run(() => Directory.GetFiles(inputDir, "*.md", SearchOption.AllDirectories)
            .AsParallel()
            .Select(filePath => ProcessMarkdownFile(inputDir, outputDir, filePath, baseAbsolutePath))
            .OrderByDescending(x => x.Published)
            .ToList());

        return articles;
    }

    private Article ProcessMarkdownFile(string inputDir, string outputDir, string filePath, string baseAbsolutePath)
    {
        var relativePathExcludeFileName = Path.GetRelativePath(inputDir, Path.GetDirectoryName(filePath)!).Replace("\\", "/");
        relativePathExcludeFileName = relativePathExcludeFileName == "." ? string.Empty : relativePathExcludeFileName;

        var routeRelativePath = Path.Combine(baseAbsolutePath, relativePathExcludeFileName);

        // Markdownファイルの内容を読み込む
        var (html, frontMatter) = ParseMarkdownWithFrontmatter(filePath, routeRelativePath);

        // コンテンツ系ファイルはMarkdownファイルを除いてそのままコピー
        _fileSystemHelper.CopyContentFile(inputDir, outputDir, filePath);

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
    }

    private (string html, Frontmatter frontMatter) ParseMarkdownWithFrontmatter(string path, string basePath)
    {
        var markdown = File.ReadAllText(path);

        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        _markdownPipeline.Setup(renderer);

        var document = Markdown.Parse(markdown, _markdownPipeline);
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

        var markdownDocument = Markdown.Parse(markdownContent, _markdownPipeline);

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
}
