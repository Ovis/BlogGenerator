using BlogGenerator.Models;

namespace BlogGenerator.Core;

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
    public async Task GenerateAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml)
    {
        var regularArticles = PageGenerationContent.GetRegularArticles(articles).ToList();
        var tagCatalog = PageGenerationContent.CreateTagCatalog(regularArticles);

        await renderingService.GeneratePagedArticleListPagesAsync(
            regularArticles,
            sideBarHtml,
            tagCatalog,
            PageModelBase.CombineUrlPath(siteOption.BaseAbsolutePath),
            pageNumber => pageNumber == 1
                ? fileSystemHelper.CombineFilePath(outputDir, "index.html")
                : fileSystemHelper.CombineFilePath(outputDir, $"{pageNumber}.html"));
    }
}
