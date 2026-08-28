using System.Text;
using BlogGenerator.Core.Interfaces;
using BlogGenerator.Enums;
using BlogGenerator.Models;

namespace BlogGenerator.Core.Rendering;

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
    public async Task GenerateAsync(PageGenerationContext context, string outputDir, TrustedHtml sideBarHtml)
    {
        var outputFilePath = fileSystemHelper.CombineFilePath(outputDir, Path.Combine("tags", "index.html"));
        fileSystemHelper.EnsureDirectoryExists(Path.GetDirectoryName(outputFilePath)!);

        // タグ一覧は記事一覧とはテンプレート種別が異なるため、ページネーション共通処理には含めない
        var tagIndexTask = File.WriteAllTextAsync(
            outputFilePath,
            await renderingService.RenderLayoutTemplateAsync(new PageModel
            {
                SiteOption = siteOption,
                PageType = PageType.Tag,
                SideBarHtml = sideBarHtml,
                Articles = context.RegularArticles,
                TagCatalog = context.TagCatalog
            }),
            Encoding.UTF8);

        var tagPageTasks = context.TagCatalog.Entries.Select(tagEntry =>
        {
            var tagArticles = tagEntry.Articles.OrderByDescending(x => x.Published).ToArray();
            var tagOutputDirectory = Path.Combine(outputDir, "tags", tagEntry.Slug);

            return renderingService.GeneratePagedArticleListPagesAsync(
                tagArticles,
                sideBarHtml,
                context.TagCatalog,
                PageModelBase.CombineUrlPath(siteOption.BaseAbsolutePath, "tags", tagEntry.Slug),
                pageNumber => pageNumber == 1
                    ? Path.Combine(tagOutputDirectory, "index.html")
                    : Path.Combine(tagOutputDirectory, $"{pageNumber}.html"));
        });

        await Task.WhenAll(tagPageTasks.Prepend(tagIndexTask));
    }
}
