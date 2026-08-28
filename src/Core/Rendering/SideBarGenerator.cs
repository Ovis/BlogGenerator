using BlogGenerator.Models;
using RazorLight;

namespace BlogGenerator.Core.Rendering;

/// <summary>
/// 公開済み通常記事からサイドバーHTMLを生成する
/// </summary>
internal sealed class SideBarGenerator(RazorLightEngine razorLightEngine, SiteOption siteOption)
{
    /// <summary>
    /// 記事一覧、タグ一覧、アーカイブ一覧を含むサイドバーHTMLを生成する
    /// </summary>
    public async Task<TrustedHtml> GenerateAsync(PageGenerationContext context)
    {
        var html = await razorLightEngine.CompileRenderAsync("SideBar.cshtml", new SideBarModel
        {
            SiteOption = siteOption,
            Articles = context.RegularArticles,
            TagCatalog = context.TagCatalog
        });

        return new TrustedHtml(html);
    }
}
