using System.Text;
using System.Text.RegularExpressions;
using BlogGenerator.Core.Interfaces;
using BlogGenerator.Enums;
using BlogGenerator.Models;
using RazorLight;

namespace BlogGenerator.Core;

/// <summary>
/// 記事、一覧、タグ、アーカイブなどのHTMLページをRazorテンプレートから生成する
/// </summary>
public class PageGenerator : IPageGenerator
{
    private const int ArticlesPerPage = 10;
    private const int MaxPaginationLinks = 6;

    private static readonly Regex TemplateNamePattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    private readonly RazorLightEngine _razorLightEngine;
    private readonly SiteOption _siteOption;
    private readonly IFileSystemHelper _fileSystemHelper;
    private readonly string _themePath;

    /// <summary>
    /// テーマパスを明示せずにページジェネレーターを生成する
    /// </summary>
    /// <remarks>
    /// 主にテーマファイルの存在検証を必要としない呼び出しとの互換性を維持するためのコンストラクター
    /// </remarks>
    public PageGenerator(RazorLightEngine razorLightEngine, SiteOption siteOption, IFileSystemHelper fileSystemHelper)
        : this(razorLightEngine, siteOption, fileSystemHelper, new ThemeSettings(string.Empty))
    {
    }

    /// <summary>
    /// サイト設定とテーマ設定を使用してページジェネレーターを生成する
    /// </summary>
    public PageGenerator(RazorLightEngine razorLightEngine, SiteOption siteOption, IFileSystemHelper fileSystemHelper, ThemeSettings themeSettings)
    {
        _razorLightEngine = razorLightEngine;
        _siteOption = siteOption;
        _fileSystemHelper = fileSystemHelper;
        _themePath = themeSettings.ThemePath;
    }

    /// <summary>
    /// 公開済み通常記事からサイドバー用HTMLを生成する
    /// </summary>
    public async Task<TrustedHtml> GenerateSideBarHtmlAsync(List<Article> articles)
    {
        var regularArticles = GetRegularArticles(articles).ToList();
        var tagCatalog = CreateTagCatalog(regularArticles);
        var html = await _razorLightEngine.CompileRenderAsync("SideBar.cshtml", new SideBarModel
        {
            SiteOption = _siteOption,
            Articles = regularArticles,
            TagCatalog = tagCatalog
        });
        return new TrustedHtml(html);
    }

    /// <summary>
    /// 指定された記事を実際には書き出さずにレンダリングし、公開時に生成可能か検証する
    /// </summary>
    public async Task ValidateArticlePageAsync(Article article, List<Article> publishedArticles, TrustedHtml sideBarHtml)
    {
        var regularArticles = GetRegularArticles(publishedArticles).ToList();
        if (!article.IsFixedPage)
        {
            // 予約記事自身のタグも公開時には集計対象になるため、検証用カタログへ一時的に加える
            regularArticles.Add(article);
        }

        var model = CreateArticlePageModel(article, sideBarHtml, CreateTagCatalog(regularArticles));
        _ = await RenderLayoutTemplateAsync(model);
    }

    /// <summary>
    /// 公開対象の記事と固定ページの個別HTMLファイルを生成する
    /// </summary>
    public async Task GenerateArticlePagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml)
    {
        var regularArticles = GetRegularArticles(articles).ToList();
        var tagCatalog = CreateTagCatalog(regularArticles);
        foreach (var article in articles.Where(IsRenderableContent))
        {
            var outputFilePath = Path.Combine(outputDir, article.RelativeDirectoryPath, article.FileName);
            _fileSystemHelper.EnsureDirectoryExists(Path.GetDirectoryName(outputFilePath)!);
            var result = await RenderLayoutTemplateAsync(CreateArticlePageModel(article, sideBarHtml, tagCatalog));
            await File.WriteAllTextAsync(outputFilePath, result, Encoding.UTF8);
        }
    }

    /// <summary>
    /// サイトトップの記事一覧ページをページネーション付きで生成する
    /// </summary>
    public async Task GenerateIndexPagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml)
    {
        var regularArticles = GetRegularArticles(articles).ToList();
        var tagCatalog = CreateTagCatalog(regularArticles);

        await GeneratePagedArticleListPagesAsync(
            regularArticles,
            sideBarHtml,
            tagCatalog,
            PageModelBase.CombineUrlPath(_siteOption.BaseAbsolutePath),
            pageNumber => pageNumber == 1
                ? _fileSystemHelper.CombineFilePath(outputDir, "index.html")
                : _fileSystemHelper.CombineFilePath(outputDir, $"{pageNumber}.html"));
    }

    /// <summary>
    /// タグ一覧ページとタグごとの記事一覧ページを生成する
    /// </summary>
    public async Task GenerateTagPagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml)
    {
        var regularArticles = GetRegularArticles(articles).ToArray();
        var tagCatalog = CreateTagCatalog(regularArticles);
        var outputFilePath = _fileSystemHelper.CombineFilePath(outputDir, Path.Combine("tags", "index.html"));
        _fileSystemHelper.EnsureDirectoryExists(Path.GetDirectoryName(outputFilePath)!);

        // タグ一覧は記事一覧とはテンプレート種別が異なるため、ページネーション共通処理には含めない
        await File.WriteAllTextAsync(
            outputFilePath,
            await RenderLayoutTemplateAsync(new PageModel
            {
                SiteOption = _siteOption,
                PageType = PageType.Tag,
                SideBarHtml = sideBarHtml,
                Articles = regularArticles,
                TagCatalog = tagCatalog
            }),
            Encoding.UTF8);

        foreach (var tagEntry in tagCatalog.Entries)
        {
            var tagArticles = tagEntry.Articles.OrderByDescending(x => x.Published).ToArray();
            var tagOutputDirectory = Path.Combine(outputDir, "tags", tagEntry.Slug);

            await GeneratePagedArticleListPagesAsync(
                tagArticles,
                sideBarHtml,
                tagCatalog,
                PageModelBase.CombineUrlPath(_siteOption.BaseAbsolutePath, "tags", tagEntry.Slug),
                pageNumber => pageNumber == 1
                    ? Path.Combine(tagOutputDirectory, "index.html")
                    : Path.Combine(tagOutputDirectory, $"{pageNumber}.html"));
        }
    }

    /// <summary>
    /// 公開済み記事を年月単位に分類し、アーカイブ一覧ページを生成する
    /// </summary>
    public async Task GenerateArchivePagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml)
    {
        var regularArticles = GetRegularArticles(articles).ToList();
        var tagCatalog = CreateTagCatalog(regularArticles);
        var yearMonthArticles = regularArticles
            .GroupBy(x => x.Published!.Value.ToString("yyyy/MM"))
            .Select(group => new
            {
                YearMonth = group.Key,
                Articles = group.OrderByDescending(x => x.Published).ToArray()
            })
            .ToArray();

        foreach (var yearMonthArticle in yearMonthArticles)
        {
            var archiveDirectory = Path.Combine(
                outputDir,
                yearMonthArticle.YearMonth.Replace("/", Path.DirectorySeparatorChar.ToString()));

            await GeneratePagedArticleListPagesAsync(
                yearMonthArticle.Articles,
                sideBarHtml,
                tagCatalog,
                PageModelBase.CombineUrlPath(_siteOption.BaseAbsolutePath, yearMonthArticle.YearMonth),
                pageNumber => pageNumber == 1
                    ? _fileSystemHelper.CombineFilePath(archiveDirectory, "index.html")
                    : _fileSystemHelper.CombineFilePath(archiveDirectory, $"{pageNumber}.html"));
        }
    }

    /// <summary>
    /// 記事集合を一定件数ごとに分割し、共通形式の一覧ページとして書き出す
    /// </summary>
    /// <param name="articles">一覧に表示する記事</param>
    /// <param name="sideBarHtml">各ページで共有するサイドバーHTML</param>
    /// <param name="tagCatalog">リンク生成に使用するタグカタログ</param>
    /// <param name="relativeDirectoryPath">ページネーションリンクの基準URL</param>
    /// <param name="getOutputFilePath">1始まりのページ番号から出力ファイルパスを返す関数</param>
    private async Task GeneratePagedArticleListPagesAsync(
        IReadOnlyList<Article> articles,
        TrustedHtml sideBarHtml,
        TagCatalog tagCatalog,
        string relativeDirectoryPath,
        Func<int, string> getOutputFilePath)
    {
        var pagedArticles = SplitIntoPages(articles);

        for (var pageIndex = 0; pageIndex < pagedArticles.Count; pageIndex++)
        {
            var pageNumber = pageIndex + 1;
            var outputFilePath = getOutputFilePath(pageNumber);

            // タグやアーカイブは出力ディレクトリが動的に決まるため、共通処理側で必ず作成する
            _fileSystemHelper.EnsureDirectoryExists(Path.GetDirectoryName(outputFilePath)!);

            var model = new PageModel
            {
                SiteOption = _siteOption,
                PageType = PageType.PageList,
                SideBarHtml = sideBarHtml,
                Articles = pagedArticles[pageIndex],
                TagCatalog = tagCatalog,
                Pagination = new PaginationModel
                {
                    CurrentPage = pageNumber,
                    TotalPages = pagedArticles.Count,
                    MaxPagesToShow = MaxPaginationLinks,
                    RelativeDirectoryPath = relativeDirectoryPath
                }
            };

            await File.WriteAllTextAsync(outputFilePath, await RenderLayoutTemplateAsync(model), Encoding.UTF8);
        }
    }

    /// <summary>
    /// 記事集合を一覧ページ1ページあたりの件数で分割する
    /// </summary>
    private static IReadOnlyList<List<Article>> SplitIntoPages(IReadOnlyList<Article> articles) =>
        articles
            .Select((article, index) => new { article, index })
            .GroupBy(x => x.index / ArticlesPerPage)
            .Select(group => group.Select(x => x.article).ToList())
            .ToList();

    /// <summary>
    /// 記事ページ用のRazorモデルを生成する
    /// </summary>
    private PageModel CreateArticlePageModel(Article article, TrustedHtml sideBarHtml, TagCatalog tagCatalog) => new()
    {
        SiteOption = _siteOption,
        PageType = PageType.Article,
        SideBarHtml = sideBarHtml,
        Articles = [article],
        TagCatalog = tagCatalog,
        ContentTemplate = article.IsFixedPage ? ResolveFixedPageTemplate(article.Template) : "Content.cshtml"
    };

    /// <summary>
    /// 固定ページで使用するテンプレート名を検証し、実際のテンプレートファイル名へ解決する
    /// </summary>
    private string ResolveFixedPageTemplate(string configuredTemplate)
    {
        var templateName = string.IsNullOrWhiteSpace(configuredTemplate) ? "Page" : configuredTemplate.Trim();
        if (!TemplateNamePattern.IsMatch(templateName))
            throw new InvalidOperationException($"Invalid fixed page template name '{configuredTemplate}'. Only letters, digits, '_' and '-' are allowed.");

        // テーマパスがない旧来の呼び出しでは、存在確認を行わず従来どおりファイル名だけを返す
        if (string.IsNullOrEmpty(_themePath)) return $"{templateName}.cshtml";

        var matches = Directory.GetFiles(_themePath, "*.cshtml", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(Path.GetFileNameWithoutExtension(path), templateName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
            throw new InvalidOperationException($"Fixed page template '{templateName}.cshtml' was not found in theme '{_themePath}'.");
        if (matches.Length > 1)
            throw new InvalidOperationException($"Fixed page template '{templateName}' is ambiguous. Matching files: {string.Join(", ", matches.Select(Path.GetFileName))}.");

        return Path.GetFileName(matches[0]);
    }

    /// <summary>
    /// レイアウトテンプレートをレンダリングし、コンパイル済みテンプレートがあれば再利用する
    /// </summary>
    private async Task<string> RenderLayoutTemplateAsync(PageModel model)
    {
        var cacheResult = _razorLightEngine.Handler.Cache.RetrieveTemplate("Layout.cshtml");
        return cacheResult.Success
            ? await _razorLightEngine.RenderTemplateAsync(cacheResult.Template.TemplatePageFactory(), model)
            : await _razorLightEngine.CompileRenderAsync("Layout.cshtml", model);
    }

    /// <summary>
    /// 公開済み通常記事からタグカタログを生成する
    /// </summary>
    private static TagCatalog CreateTagCatalog(IEnumerable<Article> articles) =>
        TagCatalog.Build(articles, message => Console.Error.WriteLine($"[tag warning] {message}"));

    /// <summary>
    /// 固定ページと未公開コンテンツを除外し、記事一覧に表示できる通常記事だけを返す
    /// </summary>
    private static IEnumerable<Article> GetRegularArticles(IEnumerable<Article> articles) =>
        articles.Where(article => !article.IsFixedPage && IsRenderableContent(article));

    /// <summary>
    /// 公開日時が設定され、ページ生成対象として扱えるコンテンツか判定する
    /// </summary>
    private static bool IsRenderableContent(Article article) =>
        article.Published is { } published && published != DateTimeOffset.MinValue;
}
