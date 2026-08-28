using BlogGenerator.Core.Interfaces;
using BlogGenerator.Models;

namespace BlogGenerator.Core.Rendering;

/// <summary>
/// サイトトップの記事一覧ページを生成する
/// </summary>
internal sealed class IndexPageGenerator(
    SiteOption siteOption,
    IFileSystemHelper fileSystemHelper,
    PageRenderingService renderingService)
{
    /// <summary>
    /// 公開済み通常記事からページネーション付きのトップページを生成する
    /// </summary>
    public Task GenerateAsync(PageGenerationContext context, string outputDir, TrustedHtml sideBarHtml) =>
        renderingService.GeneratePagedArticleListPagesAsync(
            context.RegularArticles,
            sideBarHtml,
            context.TagCatalog,
            PageModelBase.CombineUrlPath(siteOption.BaseAbsolutePath),
            pageNumber => pageNumber == 1
                ? fileSystemHelper.CombineFilePath(outputDir, "index.html")
                : fileSystemHelper.CombineFilePath(outputDir, $"{pageNumber}.html"));
}
