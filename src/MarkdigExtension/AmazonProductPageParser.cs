using System.Text.Json;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// Amazon商品ページHTMLからカード用メタデータを抽出するparser
/// </summary>
public sealed class AmazonProductPageParser
{
    /// <summary>
    /// HTMLから商品名と商品画像URLを抽出する
    /// </summary>
    public AmazonProductMetadata Parse(string contentHtml)
    {
        var document = new HtmlParser().ParseDocument(contentHtml);

        return new AmazonProductMetadata(
            GetTitle(document),
            GetImageUrl(document));
    }

    private static string? GetTitle(IHtmlDocument document)
    {
        var candidates = new[]
        {
            document.QuerySelector("#productTitle")?.TextContent,
            document.QuerySelector("meta[name='title']")?.GetAttribute("content"),
            document.QuerySelector("meta[property='og:title']")?.GetAttribute("content"),
            document.QuerySelector("title")?.TextContent
        };

        return candidates
            .Select(NormalizeText)
            .FirstOrDefault(candidate => !string.IsNullOrEmpty(candidate));
    }

    private static string? GetImageUrl(IHtmlDocument document)
    {
        var landingImage = document.QuerySelector("#landingImage");
        var imageWrapper = document.QuerySelector("#imgTagWrapperId img");
        var candidates = new[]
        {
            landingImage?.GetAttribute("data-old-hires"),
            GetLargestDynamicImageUrl(landingImage?.GetAttribute("data-a-dynamic-image")),
            landingImage?.GetAttribute("src"),
            imageWrapper?.GetAttribute("data-old-hires"),
            imageWrapper?.GetAttribute("src"),
            document.QuerySelector("meta[property='og:image']")?.GetAttribute("content")
        };

        return candidates
            .Select(NormalizeProductImageUrl)
            .FirstOrDefault(candidate => !string.IsNullOrEmpty(candidate));
    }

    private static string? GetLargestDynamicImageUrl(string? dynamicImageJson)
    {
        if (string.IsNullOrWhiteSpace(dynamicImageJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(dynamicImageJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? largestImageUrl = null;
            long largestArea = -1;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var imageUrl = NormalizeProductImageUrl(property.Name);
                if (imageUrl is null || property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() < 2 ||
                    !property.Value[0].TryGetInt32(out var width) || !property.Value[1].TryGetInt32(out var height) ||
                    width <= 0 || height <= 0)
                {
                    continue;
                }

                var area = (long)width * height;
                if (area > largestArea)
                {
                    largestArea = area;
                    largestImageUrl = imageUrl;
                }
            }

            return largestImageUrl;
        }
        catch (JsonException)
        {
            // Amazon側の属性値が壊れていても、後続のsrcやOGP画像へ安全にfallbackする
            return null;
        }
    }

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length == 0 ? null : normalized;
    }

    private static string? NormalizeProductImageUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var path = uri.AbsolutePath;
        if (path.Contains("sprite", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("icon", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("play-button", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri.ToString();
    }
}
