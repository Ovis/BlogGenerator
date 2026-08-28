using System.Text;
using BlogGenerator.Core.Interfaces;
using BlogGenerator.Enums;
using BlogGenerator.Models;

namespace BlogGenerator.Core;

/// <summary>
/// タグ一覧ページとタグ別の記事一覧ページを生成する
/// </summary>
internal sealed class TagPageGenerator(
    SiteOption siteOption,
    IFileSystemHelper fileSystemHelper,
    PageRenderingService renderingService)
{
    /// <summary>
    /// 公開済み通常記事からタグ一覧とタグ別ページを生成する
    /// </summary>
    public async Task GenerateAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml)
    {
        var regularArticles = PageGenerationContent.GetRegularArticles(articles).ToArray();
        var tagCatalog = PageGenerationContent.CreateTagCatalog(regularArticles);
        var outputFilePath = fileSystemHelper.CombineFilePath(outputDir, Path.Combine("tags", "index.html"));
        fileSystemHelper.EnsureDirectoryExists(Path.GetDirectoryName(outputFilePath)!);

        // タグ一覧は記事一覧とはテンプレート種別が異なるため、ページネーション共通処理には含めない
        await File.WriteAllTextAsync(
            outputFilePath,
            await renderingService.RenderLayoutTemplateAsync(new PageModel
            {
                SiteOption = siteOption,
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

            await renderingService.GeneratePagedArticleListPagesAsync(
                tagArticles,
                sideBarHtml,
                tagCatalog,
                PageModelBase.CombineUrlPath(siteOption.BaseAbsolutePath, "tags", tagEntry.Slug),
                pageNumber => pageNumber == 1
                    ? Path.Combine(tagOutputDirectory, "index.html")
                    : Path.Combine(tagOutputDirectory, $"{pageNumber}.html"));
        }
    }
}
