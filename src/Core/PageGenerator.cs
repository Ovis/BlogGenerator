using System.Text;
using System.Text.RegularExpressions;
using BlogGenerator.Core.Interfaces;
using BlogGenerator.Enums;
using BlogGenerator.Models;
using RazorLight;

namespace BlogGenerator.Core;

public class PageGenerator : IPageGenerator
{
    private static readonly Regex TemplateNamePattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    private readonly RazorLightEngine _razorLightEngine;
    private readonly SiteOption _siteOption;
    private readonly IFileSystemHelper _fileSystemHelper;
    private readonly string _themePath;

    public PageGenerator(RazorLightEngine razorLightEngine, SiteOption siteOption, IFileSystemHelper fileSystemHelper)
        : this(razorLightEngine, siteOption, fileSystemHelper, new ThemeSettings(string.Empty))
    {
    }

    public PageGenerator(RazorLightEngine razorLightEngine, SiteOption siteOption, IFileSystemHelper fileSystemHelper, ThemeSettings themeSettings)
    {
        _razorLightEngine = razorLightEngine;
        _siteOption = siteOption;
        _fileSystemHelper = fileSystemHelper;
        _themePath = themeSettings.ThemePath;
    }

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

    public async Task ValidateArticlePageAsync(Article article, List<Article> publishedArticles, TrustedHtml sideBarHtml)
    {
        var regularArticles = GetRegularArticles(publishedArticles).ToList();
        if (!article.IsFixedPage)
        {
            regularArticles.Add(article);
        }

        var model = CreateArticlePageModel(article, sideBarHtml, CreateTagCatalog(regularArticles));
        _ = await RenderLayoutTemplateAsync(model);
    }

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

    public async Task GenerateIndexPagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml)
    {
        var regularArticles = GetRegularArticles(articles).ToList();
        var tagCatalog = CreateTagCatalog(regularArticles);
        var pagedArticles = regularArticles.Select((article, index) => new { article, index }).GroupBy(x => x.index / 10).Select(g => g.Select(x => x.article).ToList()).ToList();
        var pageIndex = 0;
        foreach (var pageArticles in pagedArticles)
        {
            var outputFilePath = pageIndex == 0 ? _fileSystemHelper.CombineFilePath(outputDir, "index.html") : _fileSystemHelper.CombineFilePath(outputDir, $"{pageIndex + 1}.html");
            var model = new PageModel
            {
                SiteOption = _siteOption, PageType = PageType.PageList, SideBarHtml = sideBarHtml, Articles = pageArticles, TagCatalog = tagCatalog,
                Pagination = new PaginationModel { CurrentPage = pageIndex + 1, TotalPages = pagedArticles.Count, MaxPagesToShow = 6, RelativeDirectoryPath = PageModelBase.CombineUrlPath(_siteOption.BaseAbsolutePath) }
            };
            await File.WriteAllTextAsync(outputFilePath, await RenderLayoutTemplateAsync(model), Encoding.UTF8);
            pageIndex++;
        }
    }

    public async Task GenerateTagPagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml)
    {
        var regularArticles = GetRegularArticles(articles).ToArray();
        var tagCatalog = CreateTagCatalog(regularArticles);
        var outputFilePath = _fileSystemHelper.CombineFilePath(outputDir, Path.Combine("tags", "index.html"));
        _fileSystemHelper.EnsureDirectoryExists(Path.GetDirectoryName(outputFilePath)!);
        await File.WriteAllTextAsync(outputFilePath, await RenderLayoutTemplateAsync(new PageModel { SiteOption = _siteOption, PageType = PageType.Tag, SideBarHtml = sideBarHtml, Articles = regularArticles, TagCatalog = tagCatalog }), Encoding.UTF8);

        foreach (var tagEntry in tagCatalog.Entries)
        {
            var tagArticles = tagEntry.Articles.OrderByDescending(x => x.Published).ToArray();
            var pagedArticles = tagArticles.Select((article, index) => new { article, index }).GroupBy(x => x.index / 10).Select(g => g.Select(x => x.article).ToList()).ToList();
            var pageIndex = 0;
            foreach (var articleList in pagedArticles)
            {
                outputFilePath = pageIndex == 0 ? Path.Combine(outputDir, "tags", tagEntry.Slug, "index.html") : Path.Combine(outputDir, "tags", tagEntry.Slug, $"{pageIndex + 1}.html");
                _fileSystemHelper.EnsureDirectoryExists(Path.GetDirectoryName(outputFilePath)!);
                var model = new PageModel
                {
                    SiteOption = _siteOption, PageType = PageType.PageList, SideBarHtml = sideBarHtml, Articles = articleList, TagCatalog = tagCatalog,
                    Pagination = new PaginationModel { CurrentPage = pageIndex + 1, TotalPages = pagedArticles.Count, MaxPagesToShow = 6, RelativeDirectoryPath = PageModelBase.CombineUrlPath(_siteOption.BaseAbsolutePath, "tags", tagEntry.Slug) }
                };
                await File.WriteAllTextAsync(outputFilePath, await RenderLayoutTemplateAsync(model), Encoding.UTF8);
                pageIndex++;
            }
        }
    }

    public async Task GenerateArchivePagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml)
    {
        var regularArticles = GetRegularArticles(articles).ToList();
        var tagCatalog = CreateTagCatalog(regularArticles);
        var yearMonthArticles = regularArticles.GroupBy(x => x.Published!.Value.ToString("yyyy/MM")).Select(group => new { YearMonth = group.Key, Articles = group.OrderByDescending(x => x.Published).ToArray() }).ToArray();
        foreach (var yearMonthArticle in yearMonthArticles)
        {
            var pagedArticles = yearMonthArticle.Articles.Select((article, index) => new { article, index }).GroupBy(x => x.index / 10).Select(g => g.Select(x => x.article).ToList()).ToList();
            var pageIndex = 0;
            foreach (var articleList in pagedArticles)
            {
                var outputFilePath = pageIndex == 0
                    ? _fileSystemHelper.CombineFilePath(outputDir, Path.Combine(yearMonthArticle.YearMonth.Replace("/", Path.DirectorySeparatorChar.ToString()), "index.html"))
                    : _fileSystemHelper.CombineFilePath(outputDir, Path.Combine(yearMonthArticle.YearMonth.Replace("/", Path.DirectorySeparatorChar.ToString()), $"{pageIndex + 1}.html"));
                _fileSystemHelper.EnsureDirectoryExists(Path.GetDirectoryName(outputFilePath)!);
                var model = new PageModel
                {
                    SiteOption = _siteOption, PageType = PageType.PageList, SideBarHtml = sideBarHtml, Articles = articleList, TagCatalog = tagCatalog,
                    Pagination = new PaginationModel { CurrentPage = pageIndex + 1, TotalPages = pagedArticles.Count, MaxPagesToShow = 6, RelativeDirectoryPath = PageModelBase.CombineUrlPath(_siteOption.BaseAbsolutePath, yearMonthArticle.YearMonth) }
                };
                await File.WriteAllTextAsync(outputFilePath, await RenderLayoutTemplateAsync(model), Encoding.UTF8);
                pageIndex++;
            }
        }
    }

    private PageModel CreateArticlePageModel(Article article, TrustedHtml sideBarHtml, TagCatalog tagCatalog) => new()
    {
        SiteOption = _siteOption,
        PageType = PageType.Article,
        SideBarHtml = sideBarHtml,
        Articles = [article],
        TagCatalog = tagCatalog,
        ContentTemplate = article.IsFixedPage ? ResolveFixedPageTemplate(article.Template) : "Content.cshtml"
    };

    private string ResolveFixedPageTemplate(string configuredTemplate)
    {
        var templateName = string.IsNullOrWhiteSpace(configuredTemplate) ? "Page" : configuredTemplate.Trim();
        if (!TemplateNamePattern.IsMatch(templateName)) throw new InvalidOperationException($"Invalid fixed page template name '{configuredTemplate}'. Only letters, digits, '_' and '-' are allowed.");
        if (string.IsNullOrEmpty(_themePath)) return $"{templateName}.cshtml";
        var matches = Directory.GetFiles(_themePath, "*.cshtml", SearchOption.TopDirectoryOnly).Where(path => string.Equals(Path.GetFileNameWithoutExtension(path), templateName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0) throw new InvalidOperationException($"Fixed page template '{templateName}.cshtml' was not found in theme '{_themePath}'.");
        if (matches.Length > 1) throw new InvalidOperationException($"Fixed page template '{templateName}' is ambiguous. Matching files: {string.Join(", ", matches.Select(Path.GetFileName))}.");
        return Path.GetFileName(matches[0]);
    }

    private async Task<string> RenderLayoutTemplateAsync(PageModel model)
    {
        var cacheResult = _razorLightEngine.Handler.Cache.RetrieveTemplate("Layout.cshtml");
        return cacheResult.Success ? await _razorLightEngine.RenderTemplateAsync(cacheResult.Template.TemplatePageFactory(), model) : await _razorLightEngine.CompileRenderAsync("Layout.cshtml", model);
    }

    private static TagCatalog CreateTagCatalog(IEnumerable<Article> articles) => TagCatalog.Build(articles, message => Console.Error.WriteLine($"[tag warning] {message}"));
    private static IEnumerable<Article> GetRegularArticles(IEnumerable<Article> articles) => articles.Where(article => !article.IsFixedPage && IsRenderableContent(article));
    private static bool IsRenderableContent(Article article) => article.Published is { } published && published != DateTimeOffset.MinValue;
}
