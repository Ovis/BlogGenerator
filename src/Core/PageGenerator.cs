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

    public PageGenerator(
        RazorLightEngine razorLightEngine,
        SiteOption siteOption,
        IFileSystemHelper fileSystemHelper,
        ThemeSettings themeSettings)
    {
        _razorLightEngine = razorLightEngine;
        _siteOption = siteOption;
        _fileSystemHelper = fileSystemHelper;
        _themePath = themeSettings.ThemePath;
    }

    public async Task<TrustedHtml> GenerateSideBarHtmlAsync(List<Article> articles)
    {
        var publishedArticles = GetPublishedRegularArticles(articles).ToList();
        var tagCatalog = CreateTagCatalog(publishedArticles);

        var html = await _razorLightEngine.CompileRenderAsync("SideBar.cshtml", new SideBarModel
        {
            SiteOption = _siteOption,
            Articles = publishedArticles,
            TagCatalog = tagCatalog
        });

        return new TrustedHtml(html);
    }

    public async Task GenerateArticlePagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml)
    {
        var publishedPages = GetPublishedArticles(articles).ToList();
        var publishedRegularArticles = publishedPages.Where(article => !article.IsFixedPage).ToList();
        var tagCatalog = CreateTagCatalog(publishedRegularArticles);

        foreach (var article in publishedPages)
        {
            var outputFilePath = Path.Combine(outputDir, article.RelativeDirectoryPath, article.FileName);
            var outputDirPath = Path.GetDirectoryName(outputFilePath);
            _fileSystemHelper.EnsureDirectoryExists(outputDirPath!);

            var model = new PageModel
            {
                SiteOption = _siteOption,
                PageType = PageType.Article,
                SideBarHtml = sideBarHtml,
                Articles = [article],
                TagCatalog = tagCatalog,
                ContentTemplate = article.IsFixedPage
                    ? ResolveFixedPageTemplate(article.Template)
                    : "Content.cshtml"
            };

            var result = await RenderLayoutTemplateAsync(model);
            await File.WriteAllTextAsync(outputFilePath, result, Encoding.UTF8);
        }
    }

    public async Task GenerateIndexPagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml)
    {
        var publishedArticles = GetPublishedRegularArticles(articles).ToList();
        var tagCatalog = CreateTagCatalog(publishedArticles);
        var pagedArticles = publishedArticles
            .Select((article, index) => new { article, index })
            .GroupBy(x => x.index / 10)
            .Select(g => g.Select(x => x.article).ToList())
            .ToList();

        var pageIndex = 0;
        foreach (var pageArticles in pagedArticles)
        {
            var outputFilePath = pageIndex == 0
                ? _fileSystemHelper.CombineFilePath(outputDir, "index.html")
                : _fileSystemHelper.CombineFilePath(outputDir, $"{pageIndex + 1}.html");

            var model = new PageModel
            {
                SiteOption = _siteOption,
                PageType = PageType.PageList,
                SideBarHtml = sideBarHtml,
                Articles = pageArticles,
                TagCatalog = tagCatalog,
                Pagination = new PaginationModel
                {
                    CurrentPage = pageIndex + 1,
                    TotalPages = pagedArticles.Count,
                    MaxPagesToShow = 6,
                    RelativeDirectoryPath = PageModelBase.CombineUrlPath(_siteOption.BaseAbsolutePath)
                }
            };

            var result = await RenderLayoutTemplateAsync(model);
            await File.WriteAllTextAsync(outputFilePath, result, Encoding.UTF8);
            pageIndex++;
        }
    }

    public async Task GenerateTagPagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml)
    {
        var publishedArticles = GetPublishedRegularArticles(articles).ToArray();
        var tagCatalog = CreateTagCatalog(publishedArticles);

        var outputFilePath = _fileSystemHelper.CombineFilePath(outputDir, Path.Combine("tags", "index.html"));
        var outputDirPath = Path.GetDirectoryName(outputFilePath);
        _fileSystemHelper.EnsureDirectoryExists(outputDirPath!);

        var tagIndexModel = new PageModel
        {
            SiteOption = _siteOption,
            PageType = PageType.Tag,
            SideBarHtml = sideBarHtml,
            Articles = publishedArticles,
            TagCatalog = tagCatalog
        };

        var tagIndexHtml = await RenderLayoutTemplateAsync(tagIndexModel);
        await File.WriteAllTextAsync(outputFilePath, tagIndexHtml, Encoding.UTF8);

        foreach (var tagEntry in tagCatalog.Entries)
        {
            var tagArticles = tagEntry.Articles.OrderByDescending(x => x.Published).ToArray();
            var pagedArticles = tagArticles
                .Select((article, index) => new { article, index })
                .GroupBy(x => x.index / 10)
                .Select(g => g.Select(x => x.article).ToList())
                .ToList();

            var pageIndex = 0;
            foreach (var articleList in pagedArticles)
            {
                outputFilePath = pageIndex == 0
                    ? Path.Combine(outputDir, "tags", tagEntry.Slug, "index.html")
                    : Path.Combine(outputDir, "tags", tagEntry.Slug, $"{pageIndex + 1}.html");

                outputDirPath = Path.GetDirectoryName(outputFilePath);
                _fileSystemHelper.EnsureDirectoryExists(outputDirPath!);

                var model = new PageModel
                {
                    SiteOption = _siteOption,
                    PageType = PageType.PageList,
                    SideBarHtml = sideBarHtml,
                    Articles = articleList,
                    TagCatalog = tagCatalog,
                    Pagination = new PaginationModel
                    {
                        CurrentPage = pageIndex + 1,
                        TotalPages = pagedArticles.Count,
                        MaxPagesToShow = 6,
                        RelativeDirectoryPath = PageModelBase.CombineUrlPath(_siteOption.BaseAbsolutePath, "tags", tagEntry.Slug)
                    }
                };

                var result = await RenderLayoutTemplateAsync(model);
                await File.WriteAllTextAsync(outputFilePath, result, Encoding.UTF8);
                pageIndex++;
            }
        }
    }

    public async Task GenerateArchivePagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml)
    {
        var publishedArticles = GetPublishedRegularArticles(articles).ToList();
        var tagCatalog = CreateTagCatalog(publishedArticles);
        var yearMonthArticles = publishedArticles.GroupBy(x => x.Published.ToString("yyyy/MM"))
            .Select(group => new
            {
                YearMonth = group.Key,
                Articles = group.OrderByDescending(x => x.Published).ToArray()
            })
            .ToArray();

        foreach (var yearMonthArticle in yearMonthArticles)
        {
            var pagedArticles = yearMonthArticle.Articles
                .Select((article, index) => new { article, index })
                .GroupBy(x => x.index / 10)
                .Select(g => g.Select(x => x.article).ToList())
                .ToList();

            var pageIndex = 0;
            foreach (var articleList in pagedArticles)
            {
                var outputFilePath = pageIndex == 0
                    ? _fileSystemHelper.CombineFilePath(outputDir, Path.Combine(yearMonthArticle.YearMonth.Replace("/", Path.DirectorySeparatorChar.ToString()), "index.html"))
                    : _fileSystemHelper.CombineFilePath(outputDir, Path.Combine(yearMonthArticle.YearMonth.Replace("/", Path.DirectorySeparatorChar.ToString()), $"{pageIndex + 1}.html"));

                var outputDirPath = Path.GetDirectoryName(outputFilePath);
                _fileSystemHelper.EnsureDirectoryExists(outputDirPath!);

                var model = new PageModel
                {
                    SiteOption = _siteOption,
                    PageType = PageType.PageList,
                    SideBarHtml = sideBarHtml,
                    Articles = articleList,
                    TagCatalog = tagCatalog,
                    Pagination = new PaginationModel
                    {
                        CurrentPage = pageIndex + 1,
                        TotalPages = pagedArticles.Count,
                        MaxPagesToShow = 6,
                        RelativeDirectoryPath = PageModelBase.CombineUrlPath(_siteOption.BaseAbsolutePath, yearMonthArticle.YearMonth)
                    }
                };

                var result = await RenderLayoutTemplateAsync(model);
                await File.WriteAllTextAsync(outputFilePath, result, Encoding.UTF8);
                pageIndex++;
            }
        }
    }

    private string ResolveFixedPageTemplate(string configuredTemplate)
    {
        var templateName = string.IsNullOrWhiteSpace(configuredTemplate) ? "Page" : configuredTemplate.Trim();

        if (!TemplateNamePattern.IsMatch(templateName))
        {
            throw new InvalidOperationException(
                $"Invalid fixed page template name '{configuredTemplate}'. Only letters, digits, '_' and '-' are allowed.");
        }

        if (string.IsNullOrEmpty(_themePath))
        {
            return $"{templateName}.cshtml";
        }

        var matches = Directory.GetFiles(_themePath, "*.cshtml", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(
                Path.GetFileNameWithoutExtension(path),
                templateName,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
        {
            throw new InvalidOperationException($"Fixed page template '{templateName}.cshtml' was not found in theme '{_themePath}'.");
        }

        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"Fixed page template '{templateName}' is ambiguous. Matching files: {string.Join(", ", matches.Select(Path.GetFileName))}.");
        }

        return Path.GetFileName(matches[0]);
    }

    private async Task<string> RenderLayoutTemplateAsync(PageModel model)
    {
        var cacheResult = _razorLightEngine.Handler.Cache.RetrieveTemplate("Layout.cshtml");

        return cacheResult.Success
            ? await _razorLightEngine.RenderTemplateAsync(cacheResult.Template.TemplatePageFactory(), model)
            : await _razorLightEngine.CompileRenderAsync("Layout.cshtml", model);
    }

    private static TagCatalog CreateTagCatalog(IEnumerable<Article> articles) =>
        TagCatalog.Build(articles, message => Console.Error.WriteLine($"[tag warning] {message}"));

    private static IEnumerable<Article> GetPublishedArticles(IEnumerable<Article> articles) =>
        articles.Where(article => article.Published != DateTimeOffset.MinValue);

    private static IEnumerable<Article> GetPublishedRegularArticles(IEnumerable<Article> articles) =>
        GetPublishedArticles(articles).Where(article => !article.IsFixedPage);
}
