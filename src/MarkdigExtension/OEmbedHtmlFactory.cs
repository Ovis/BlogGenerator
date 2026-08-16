using System.Net;
using System.Text;
using BlogGenerator.MarkdigExtension.Models;

namespace BlogGenerator.MarkdigExtension;

public static class OEmbedHtmlFactory
{
    public static string WrapInContainer(string html, bool isVideo = false)
    {
        var className = isVideo ? " class=\"oembed-container oembed-video\"" : " class=\"oembed-container\"";

        return new StringBuilder()
            .Append("<div")
            .Append(className)
            .Append(">")
            .Append(html)
            .Append("</div>")
            .ToString();
    }

    public static string CreateStandardLink(string url)
    {
        if (!TryGetSafeHttpUrl(url, out var safeUrl))
        {
            return $"<span>{EscapeText(url)}</span>";
        }

        return new StringBuilder()
            .Append("<a href=\"")
            .Append(EscapeAttribute(safeUrl))
            .Append("\" rel=\"noopener noreferrer\" target=\"_blank\">")
            .Append(EscapeText(safeUrl))
            .Append("</a>")
            .ToString();
    }

    public static string CreateGistEmbed(string url)
    {
        if (!TryGetSafeHttpUrl(url, out var safeUrl))
        {
            return CreateStandardLink(url);
        }

        return new StringBuilder()
            .Append("<script src=\"")
            .Append(EscapeAttribute(safeUrl))
            .Append(".js\">")
            .Append("</script>")
            .ToString();
    }

    public static string CreateOgpCard(string url, SiteMetaData metaData)
    {
        if (!TryGetSafeHttpUrl(url, out var safeUrl))
        {
            return CreateStandardLink(url);
        }

        var displayTitle = string.IsNullOrWhiteSpace(metaData.OgTitle) ? metaData.Title : metaData.OgTitle;
        var safeOgUrl = TryGetSafeHttpUrl(metaData.OgUrl, out var ogUrl) ? ogUrl : safeUrl;
        var safeOgImage = TryGetSafeHttpUrl(metaData.OgImage, out var ogImage) ? ogImage : null;
        var noSchemeUrl = new Uri(safeUrl).Authority + new Uri(safeUrl).PathAndQuery;
        var faviconUrl = $"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(safeUrl)}";

        return new StringBuilder()
            .Append("<div class=\"bcard-wrapper\">")
            .Append("<span class=\"bcard-header withgfav\">")
            .Append($"<div class=\"bcard-favicon\" style=\"{BuildBackgroundImageStyle(faviconUrl)}\"></div>")
            .Append("<div class=\"bcard-site\">")
            .Append(CreateExternalLink(safeOgUrl, metaData.OgSiteName, "nofollow noopener noreferrer"))
            .Append("</div>")
            .Append("<div class=\"bcard-url\">")
            .Append(CreateExternalLink(safeOgUrl, safeOgUrl, "nofollow noopener noreferrer"))
            .Append("</div>")
            .Append("</span>")
            .Append("<span class=\"bcard-main withogimg\">")
            .Append("<div class=\"bcard-title\">")
            .Append(CreateExternalLink(safeOgUrl, displayTitle, "nofollow noopener noreferrer"))
            .Append("</div>")
            .Append("<div class=\"bcard-description\">")
            .Append(EscapeText(metaData.OgDescription))
            .Append("</div>")
            .Append(CreateOgpImageLink(safeOgUrl, safeOgImage))
            .Append("</span>")
            .Append("<span>")
            .Append(CreateHatenaBookmarkLink(noSchemeUrl, safeUrl))
            .Append("</span>")
            .Append("</div>")
            .ToString();
    }

    private static string CreateExternalLink(string href, string text, string rel)
    {
        return new StringBuilder()
            .Append("<a href=\"")
            .Append(EscapeAttribute(href))
            .Append("\" rel=\"")
            .Append(EscapeAttribute(rel))
            .Append("\" target=\"_blank\">")
            .Append(EscapeText(text))
            .Append("</a>")
            .ToString();
    }

    private static string CreateOgpImageLink(string href, string? imageUrl)
    {
        var builder = new StringBuilder()
            .Append("<a href=\"")
            .Append(EscapeAttribute(href))
            .Append("\" rel=\"nofollow noopener noreferrer\" target=\"_blank\">")
            .Append("<div class=\"bcard-img\"");

        if (!string.IsNullOrEmpty(imageUrl))
        {
            builder.Append(" style=\"")
                .Append(BuildBackgroundImageStyle(imageUrl))
                .Append("\"");
        }

        builder.Append("></div>")
            .Append("</a>");

        return builder.ToString();
    }

    private static string CreateHatenaBookmarkLink(string noSchemeUrl, string url)
    {
        return new StringBuilder()
            .Append("<a href=\"https://b.hatena.ne.jp/entry/s/")
            .Append(EscapeAttribute(noSchemeUrl))
            .Append("\" rel=\"nofollow noopener noreferrer\" target=\"_blank\">")
            .Append("<img src=\"https://b.st-hatena.com/entry/image/")
            .Append(EscapeAttribute(url))
            .Append("\" alt=\"")
            .Append(EscapeAttribute("[はてなブックマークで表示]"))
            .Append("\"></a>")
            .ToString();
    }

    private static string BuildBackgroundImageStyle(string url)
    {
        return $"background-image: url('{EscapeAttribute(url)}')";
    }

    private static string EscapeText(string value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string EscapeAttribute(string value) => WebUtility.HtmlEncode(value ?? string.Empty);

    // 外部由来のURLを属性へ入れるため、http/https だけに絞る
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
