using BlogGenerator.Core.Interfaces;
using BlogGenerator.Enums;
using BlogGenerator.Models;
using RazorLight;
using System.Text;

namespace BlogGenerator.Core;

public class PageGenerator : IPageGenerator
{
    private readonly RazorLightEngine _razorLightEngine;
    private readonly SiteOption _siteOption;
    private readonly IFileSystemHelper _fileSystemHelper;

    public PageGenerator(RazorLightEngine razorLightEngine, SiteOption siteOption, IFileSystemHelper fileSystemHelper)
    {
        _razorLightEngine = razorLightEngine;
        _siteOption = siteOption;
        _fileSystemHelper = fileSystemHelper;
    }

    public async Task<string> GenerateSideBarHtmlAsync(List<Article> articles)
    {
        return await _razorLightEngine.CompileRenderAsync("SideBar.cshtml", new SideBarModel
        {
            SiteOption = _siteOption,
            Articles = articles
        });
    }

    public async Task GenerateArticlePagesAsync(List<Article> articles, string outputDir, string sideBarHtml)
    {
        foreach (var article in articles)
        {
            // Usage
            var outputFilePathWithoutExtension = Path.Combine(outputDir, article.RelativeDirectoryPath, article.FileName);
            // 出力フォルダパス
            var outputDirPath = Path.GetDirectoryName(outputFilePathWithoutExtension);
            _fileSystemHelper.EnsureDirectoryExists(outputDirPath!);

            var model = new PageModel
            {
                SiteOption = _siteOption,
                PageType = PageType.Article,
                SideBarHtml = sideBarHtml,
                Articles = [article]
            };

            // HTMLファイルを生成
            var result = await RenderLayoutTemplateAsync(model);

            await File.WriteAllTextAsync(outputFilePathWithoutExtension, result, Encoding.UTF8);
        }
    }

    public async Task GenerateIndexPagesAsync(List<Article> articles, string outputDir, string sideBarHtml)
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
                ? _fileSystemHelper.CombineFilePath(outputDir, "index.html")
                : _fileSystemHelper.CombineFilePath(outputDir, $"{pageIndex + 1}.html");

            var model = new PageModel
            {
                SiteOption = _siteOption,
                PageType = PageType.PageList,
                SideBarHtml = sideBarHtml,
                Articles = pageArticles,
                Pagination = new PaginationModel
                {
                    CurrentPage = pageIndex + 1,
                    TotalPages = pagedArticles.Count,
                    MaxPagesToShow = 6,
                    RelativeDirectoryPath = Path.Combine(_siteOption.BaseAbsolutePath)
                }
            };

            var result = await RenderLayoutTemplateAsync(model);

            await File.WriteAllTextAsync(outputFilePath, result, Encoding.UTF8);
            pageIndex++;
        }
    }

    public async Task GenerateTagPagesAsync(List<Article> articles, string outputDir, string sideBarHtml)
    {
        // タグごとの記事一覧（Publishedで昇順並び替え）を取得
        var tagArticles = articles.SelectMany(x => x.Tags).Distinct().Select(tag => new
        {
            Tag = tag,
            Articles = articles.Where(x => x.Tags.Contains(tag)).OrderByDescending(x => x.Published).ToArray()
        }).ToArray();

        // タグ一覧ページを生成
        var outputFilePath = _fileSystemHelper.CombineFilePath(outputDir, Path.Combine("tags", "index.html"));
        var outputDirPath = Path.GetDirectoryName(outputFilePath);
        _fileSystemHelper.EnsureDirectoryExists(outputDirPath!);

        var tagIndexModel = new PageModel
        {
            SiteOption = _siteOption,
            PageType = PageType.Tag,
            SideBarHtml = sideBarHtml,
            Articles = articles,
        };

        var tagIndexHtml = await RenderLayoutTemplateAsync(tagIndexModel);
        await File.WriteAllTextAsync(outputFilePath, tagIndexHtml, Encoding.UTF8);

        // タグ単位のHTMLを生成
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
                outputFilePath = pageIndex == 0
                    ? Path.Combine(outputDir, "tags", tagArticle.Tag, "index.html")
                    : Path.Combine(outputDir, "tags", tagArticle.Tag, $"{pageIndex + 1}.html");

                outputDirPath = Path.GetDirectoryName(outputFilePath);
                _fileSystemHelper.EnsureDirectoryExists(outputDirPath!);

                var model = new PageModel
                {
                    SiteOption = _siteOption,
                    PageType = PageType.PageList,
                    SideBarHtml = sideBarHtml,
                    Articles = articleList,
                    Pagination = new PaginationModel
                    {
                        CurrentPage = pageIndex + 1,
                        TotalPages = pagedArticles.Count,
                        MaxPagesToShow = 6,
                        RelativeDirectoryPath = Path.Combine(_siteOption.BaseAbsolutePath, "tags", tagArticle.Tag)
                    }
                };

                var result = await RenderLayoutTemplateAsync(model);
                await File.WriteAllTextAsync(outputFilePath, result, Encoding.UTF8);
                pageIndex++;
            }
        }
    }

    public async Task GenerateArchivePagesAsync(List<Article> articles, string outputDir, string sideBarHtml)
    {
        // 年月ごとの記事一覧（Publishedで昇順並び替え）を取得
        var yearMonthArticles = articles.GroupBy(x => x.Published.ToString("yyyy/MM"))
            .Select(group => new
            {
                YearMonth = group.Key,
                Articles = group.OrderByDescending(x => x.Published).ToArray()
            })
            .ToArray();

        // 年月単位の記事一覧ページを生成
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
                    Pagination = new PaginationModel
                    {
                        CurrentPage = pageIndex + 1,
                        TotalPages = pagedArticles.Count,
                        MaxPagesToShow = 6,
                        RelativeDirectoryPath = Path.Combine(_siteOption.BaseAbsolutePath, Path.Combine(yearMonthArticle.YearMonth.Replace("/", Path.DirectorySeparatorChar.ToString())))
                    }
                };

                var result = await RenderLayoutTemplateAsync(model);
                await File.WriteAllTextAsync(outputFilePath, result, Encoding.UTF8);
                pageIndex++;
            }
        }
    }

    private async Task<string> RenderLayoutTemplateAsync(PageModel model)
    {
        var cacheResult = _razorLightEngine.Handler.Cache.RetrieveTemplate("Layout.cshtml");

        return cacheResult.Success
            ? await _razorLightEngine.RenderTemplateAsync(cacheResult.Template.TemplatePageFactory(), model)
            : await _razorLightEngine.CompileRenderAsync("Layout.cshtml", model);
    }
}
