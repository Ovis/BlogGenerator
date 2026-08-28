using System.Text;
using BlogGenerator.Core.Interfaces;
using BlogGenerator.Enums;
using BlogGenerator.Models;
using RazorLight;

namespace BlogGenerator.Core.Rendering;

/// <summary>
/// Razorテンプレートのレンダリングと、ページネーション付き一覧ページの共通出力処理を提供する
/// </summary>
internal sealed class PageRenderingService(
    RazorLightEngine razorLightEngine,
    SiteOption siteOption,
    IFileSystemHelper fileSystemHelper)
{
    private const int ArticlesPerPage = 10;
    private const int MaxPaginationLinks = 6;

    /// <summary>
    /// 指定されたページモデルをレイアウトテンプレートでレンダリングする
    /// </summary>
    /// <remarks>
    /// RazorLightがすでにコンパイル済みのレイアウトを保持している場合は、それを再利用して再コンパイルを避ける
    /// </remarks>
    public async Task<string> RenderLayoutTemplateAsync(PageModel model)
    {
        var cacheResult = razorLightEngine.Handler.Cache.RetrieveTemplate("Layout.cshtml");
        return cacheResult.Success
            ? await razorLightEngine.RenderTemplateAsync(cacheResult.Template.TemplatePageFactory(), model)
            : await razorLightEngine.CompileRenderAsync("Layout.cshtml", model);
    }

    /// <summary>
    /// 記事集合を一定件数ごとに分割し、共通形式の一覧ページとして書き出す
    /// </summary>
    public async Task GeneratePagedArticleListPagesAsync(
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
            fileSystemHelper.EnsureDirectoryExists(Path.GetDirectoryName(outputFilePath)!);

            var model = new PageModel
            {
                SiteOption = siteOption,
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
}
