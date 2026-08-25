using System.Net;
using Markdig.Renderers;
using Markdig.Renderers.Html;

namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// AmazonInlineを通常リンクとして描画するrenderer
/// </summary>
public sealed class AmazonInlineRenderer(string affiliateId) : HtmlObjectRenderer<AmazonInline>
{
    protected override void Write(HtmlRenderer renderer, AmazonInline obj)
    {
        var productUrl = $"https://www.amazon.co.jp/dp/{obj.Asin}/";
        var displayUrl = string.IsNullOrWhiteSpace(affiliateId)
            ? productUrl
            : $"{productUrl}?tag={Uri.EscapeDataString(affiliateId)}";
        var linkText = string.IsNullOrWhiteSpace(obj.ManualTitle) ? "Amazonで見る" : obj.ManualTitle;

        // 手動指定値はMarkdown由来の外部入力なので、属性値と表示文字列を個別にエスケープする
        renderer.Write("<a class=\"amazon-link\" href=\"")
            .Write(WebUtility.HtmlEncode(displayUrl))
            .Write("\" target=\"_blank\" rel=\"noopener noreferrer\">")
            .Write(WebUtility.HtmlEncode(linkText))
            .Write("</a>");
    }
}
