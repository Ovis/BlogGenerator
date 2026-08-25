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
        string affiliateId,
        AmazonProductMetadataResolver? metadataResolver = null)
    {
        if (string.IsNullOrWhiteSpace(affiliateId))
        {
            // タグ未設定時はカードを出さず、rendererが通常リンクへ劣化表示する
            return;
        }

        foreach (var amazonInline in markdownDocument.Descendants<AmazonInline>())
        {
            var manualImageUrl = CreateImageUrl(amazonInline.ManualImageId);
            var fetchedMetadata = metadataResolver is not null &&
                (string.IsNullOrWhiteSpace(amazonInline.ManualTitle) || manualImageUrl is null)
                ? await metadataResolver.ResolveAsync(amazonInline.Asin)
                : null;
            var title = amazonInline.ManualTitle ?? fetchedMetadata?.Title;
            if (string.IsNullOrWhiteSpace(title))
            {
                // 商品名が得られない場合は、Amazon専用の壊れたカードを出さず既存のoEmbed経路へ戻す
                amazonInline.OEmbedFallbackUrl = CreateCanonicalProductUrl(amazonInline.Asin);
                continue;
            }

            var model = new AmazonCardModel(
                amazonInline.Asin,
                CreateAffiliateProductUrl(amazonInline.Asin, affiliateId),
                title,
                manualImageUrl ?? fetchedMetadata?.ImageUrl);
            amazonInline.HtmlContent = await templateRenderer.RenderAsync(model);
        }
    }

    private static string CreateAffiliateProductUrl(string asin, string affiliateId) =>
        $"{CreateCanonicalProductUrl(asin)}?tag={Uri.EscapeDataString(affiliateId)}";

    private static string CreateCanonicalProductUrl(string asin) =>
        $"https://www.amazon.co.jp/dp/{asin}/";

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
