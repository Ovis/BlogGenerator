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
    private readonly SemaphoreSlim _renderSemaphore = new(BuildConcurrency.RenderingDegreeOfParallelism);
    private readonly SemaphoreSlim _layoutCompilationSemaphore = new(1, 1);

    /// <summary>
    /// 指定されたページモデルをレイアウトテンプレートでレンダリングする
    /// </summary>
    /// <remarks>
    /// 初回コンパイルだけは直列化し、以後はRazorLightのコンパイル済みテンプレートを上限付きで並列レンダリングする
    /// </remarks>
    public async Task<string> RenderLayoutTemplateAsync(PageModel model)
    {
        await _renderSemaphore.WaitAsync();
        try
        {
            var cacheResult = razorLightEngine.Handler.Cache.RetrieveTemplate("Layout.cshtml");
            if (cacheResult.Success)
                return await razorLightEngine.RenderTemplateAsync(cacheResult.Template.TemplatePageFactory(), model);

            // 複数ページが同時に初回レンダリングへ到達してもLayout.cshtmlを重複コンパイルしない
            await _layoutCompilationSemaphore.WaitAsync();
            try
            {
                cacheResult = razorLightEngine.Handler.Cache.RetrieveTemplate("Layout.cshtml");
                return cacheResult.Success
                    ? await razorLightEngine.RenderTemplateAsync(cacheResult.Template.TemplatePageFactory(), model)
                    : await razorLightEngine.CompileRenderAsync("Layout.cshtml", model);
            }
            finally
            {
                _layoutCompilationSemaphore.Release();
            }
        }
        finally
        {
            _renderSemaphore.Release();
        }
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
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = BuildConcurrency.RenderingDegreeOfParallelism
        };

        await Parallel.ForEachAsync(Enumerable.Range(0, pagedArticles.Count), parallelOptions, async (pageIndex, _) =>
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
        });
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
