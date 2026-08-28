using BlogGenerator.Models;
using RazorLight;

namespace BlogGenerator.Core.Rendering;

/// <summary>
/// 公開済み通常記事からサイドバーHTMLを生成する
/// </summary>
internal sealed class SideBarGenerator(RazorLightEngine razorLightEngine, SiteOption siteOption)
{
    /// <summary>
    /// 記事一覧とタグカタログを使用してサイドバーをレンダリングする
    /// </summary>
    public async Task<TrustedHtml> GenerateAsync(List<Article> articles)
    {
        var regularArticles = PageGenerationContent.GetRegularArticles(articles).ToList();
        var tagCatalog = PageGenerationContent.CreateTagCatalog(regularArticles);
        var html = await razorLightEngine.CompileRenderAsync("SideBar.cshtml", new SideBarModel
        {
            SiteOption = siteOption,
            Articles = regularArticles,
            TagCatalog = tagCatalog
        });

        return new TrustedHtml(html);
    }
}
