using BlogGenerator.Core.Interfaces;
using BlogGenerator.Models;

namespace BlogGenerator.Core;

/// <summary>
/// 公開済み記事を年月単位に分類し、アーカイブ一覧ページを生成する
/// </summary>
internal sealed class ArchivePageGenerator(
    SiteOption siteOption,
    IFileSystemHelper fileSystemHelper,
    PageRenderingService renderingService)
{
    /// <summary>
    /// 公開済み通常記事から年月別のページネーション付きアーカイブを生成する
    /// </summary>
    public async Task GenerateAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml)
    {
        var regularArticles = PageGenerationContent.GetRegularArticles(articles).ToList();
        var tagCatalog = PageGenerationContent.CreateTagCatalog(regularArticles);
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

            await renderingService.GeneratePagedArticleListPagesAsync(
                yearMonthArticle.Articles,
                sideBarHtml,
                tagCatalog,
                PageModelBase.CombineUrlPath(siteOption.BaseAbsolutePath, yearMonthArticle.YearMonth),
                pageNumber => pageNumber == 1
                    ? fileSystemHelper.CombineFilePath(archiveDirectory, "index.html")
                    : fileSystemHelper.CombineFilePath(archiveDirectory, $"{pageNumber}.html"));
        }
    }
}
