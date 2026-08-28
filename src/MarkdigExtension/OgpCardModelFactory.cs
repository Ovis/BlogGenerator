using BlogGenerator.MarkdigExtension.Models;

namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// 外部サイトから取得したメタデータをOGPカード表示モデルへ変換する
/// </summary>
internal static class OgpCardModelFactory
{
    /// <summary>
    /// 外部由来URLを検証し、安全にテンプレートへ渡せる表示モデルを生成する
    /// </summary>
    public static bool TryCreate(string sourceUrl, SiteMetaData metaData, out OgpCardModel model)
    {
        if (!TryGetSafeHttpUrl(sourceUrl, out var safeSourceUrl))
        {
            model = null!;
            return false;
        }

        var cardUrl = TryGetSafeHttpUrl(metaData.OgUrl, out var safeOgUrl)
            ? safeOgUrl
            : safeSourceUrl;
        var imageUrl = TryGetSafeHttpUrl(metaData.OgImage, out var safeImageUrl)
            ? safeImageUrl
            : null;
        var cardUri = new Uri(cardUrl);
        var displayUrl = cardUri.Authority + cardUri.PathAndQuery;
        var siteName = string.IsNullOrWhiteSpace(metaData.OgSiteName)
            ? cardUri.Host
            : metaData.OgSiteName.Trim();
        var title = string.IsNullOrWhiteSpace(metaData.OgTitle)
            ? metaData.Title
            : metaData.OgTitle;
        var faviconUrl = $"https://www.google.com/s2/favicons?domain_url={Uri.EscapeDataString(cardUrl)}&sz=32";

        model = new OgpCardModel(
            cardUrl,
            displayUrl,
            title ?? string.Empty,
            metaData.OgDescription ?? string.Empty,
            siteName,
            imageUrl,
            faviconUrl);
        return true;
    }

    /// <summary>
    /// URL属性へ使用できるhttp/https URLだけを許可する
    /// </summary>
    private static bool TryGetSafeHttpUrl(string? url, out string safeUrl)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            safeUrl = uri.AbsoluteUri;
            return true;
        }

        safeUrl = string.Empty;
        return false;
    }
}
