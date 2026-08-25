using System.Text.RegularExpressions;
using Markdig.Syntax;

namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// Markdown文書内のAmazonノードへカードHTMLを設定する
/// </summary>
public static partial class AmazonDocumentResolver
{
    public static async Task ResolveAsync(
        MarkdownDocument markdownDocument,
        IAmazonCardTemplateRenderer templateRenderer,
        string affiliateId)
    {
        if (string.IsNullOrWhiteSpace(affiliateId))
        {
            // タグ未設定時はカードを出さず、rendererが通常リンクへ劣化表示する
            return;
        }

        foreach (var amazonInline in markdownDocument.Descendants<AmazonInline>())
        {
            if (string.IsNullOrWhiteSpace(amazonInline.ManualTitle))
            {
                // 自動取得は後続段階で追加するため、現段階では手動titleを持つ入力だけをカード化する
                continue;
            }

            var model = new AmazonCardModel(
                amazonInline.Asin,
                CreateAffiliateProductUrl(amazonInline.Asin, affiliateId),
                amazonInline.ManualTitle,
                CreateImageUrl(amazonInline.ManualImageId));
            amazonInline.HtmlContent = await templateRenderer.RenderAsync(model);
        }
    }

    private static string CreateAffiliateProductUrl(string asin, string affiliateId) =>
        $"https://www.amazon.co.jp/dp/{asin}/?tag={Uri.EscapeDataString(affiliateId)}";

    private static string? CreateImageUrl(string? imageId)
    {
        if (string.IsNullOrWhiteSpace(imageId) || !ImageIdRegex().IsMatch(imageId))
        {
            return null;
        }

        return $"https://m.media-amazon.com/images/I/{imageId}._SL500_.jpg";
    }

    [GeneratedRegex(@"^[A-Za-z0-9+_-]+$")]
    private static partial Regex ImageIdRegex();
}
