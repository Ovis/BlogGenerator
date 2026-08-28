using System.Net;
using System.Text;

namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// oEmbedコンテナ、標準リンク、Gist埋め込みなどテーマ非依存のHTMLを生成する
/// </summary>
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

    public static string CreateStandardLink(string url) => CreateStandardLink(url, url);

    public static string CreateStandardLink(string href, string displayUrl)
    {
        if (!TryGetSafeHttpUrl(href, out var safeHref))
        {
            return $"<span>{EscapeText(displayUrl)}</span>";
        }

        return new StringBuilder()
            .Append("<a href=\"")
            .Append(EscapeAttribute(safeHref))
            .Append("\" rel=\"noopener noreferrer\" target=\"_blank\">")
            .Append(EscapeText(displayUrl))
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
