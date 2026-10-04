using AngleSharp.Html.Parser;

namespace BlogGenerator.MarkdigExtension;

/// <summary>検索結果のうち指定ASINと一致する商品からタイトルと画像を抽出する。</summary>
public sealed class AmazonSearchResultParser
{
    public AmazonProductMetadata? Parse(string html, string asin)
    {
        var document = new HtmlParser().ParseDocument(html);
        foreach (var result in document.QuerySelectorAll("[data-component-type='s-search-result'][data-asin]"))
        {
            if (!string.Equals(result.GetAttribute("data-asin"), asin, StringComparison.OrdinalIgnoreCase))
                continue;

            var heading = result.QuerySelector("h2 a span") ?? result.QuerySelector("h2 span") ?? result.QuerySelector("h2");
            var title = string.Join(' ', (heading?.TextContent ?? string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            var image = result.QuerySelector("img.s-image")?.GetAttribute("src");
            if (title.Length == 0 || !Uri.TryCreate(image, UriKind.Absolute, out var imageUri) ||
                imageUri.Scheme is not ("http" or "https"))
                continue;

            return new AmazonProductMetadata(title, imageUri.AbsoluteUri);
        }

        return null;
    }
}
