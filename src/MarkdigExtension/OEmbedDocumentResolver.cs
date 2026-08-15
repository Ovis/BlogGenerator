using Markdig.Syntax;

namespace BlogGenerator.MarkdigExtension;

public static class OEmbedDocumentResolver
{
    /// <summary>
    /// Markdown文書内のoEmbedノードへ解決済みHTMLを設定する
    /// </summary>
    public static async Task ResolveAsync(MarkdownDocument markdownDocument, OEmbedResolver oEmbedResolver)
    {
        foreach (var oEmbedInline in markdownDocument.Descendants<OEmbedInline>())
        {
            oEmbedInline.HtmlContent = await oEmbedResolver.GetOEmbedHtmlAsync(oEmbedInline.Url);
        }
    }
}
