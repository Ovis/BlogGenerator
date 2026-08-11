using System.Text;
using BlogGenerator.MarkdigExtension.Models;

namespace BlogGenerator.MarkdigExtension;

public static class OEmbedHtmlFactory
{
    public static string WrapInParagraph(string html, bool isVideo = false)
    {
        return new StringBuilder()
            .Append("<p")
            .Append(isVideo ? " class='oembed-video'" : string.Empty)
            .Append(">")
            .Append(html)
            .Append("</p>")
            .ToString();
    }

    public static string CreateStandardLink(string url)
    {
        return new StringBuilder()
            .Append("<a href=\"")
            .Append(url)
            .Append("\" target=\"_blank\">")
            .Append(url)
            .Append("</a>")
            .ToString();
    }

    public static string CreateGistEmbed(string url)
    {
        return new StringBuilder()
            .Append("<script src=\"")
            .Append(url)
            .Append(".js\">")
            .Append("</script>")
            .ToString();
    }

    public static string CreateOgpCard(string url, SiteMetaData metaData)
    {
        var noSchemeUrl = url.Replace($"{new Uri(url).Scheme}://", string.Empty);

        return new StringBuilder()
            .Append("<div class=\"bcard-wrapper\">")
            .Append("<span class=\"bcard-header withgfav\">")
            .Append($"<div class=\"bcard-favicon\" style=\"background-image: url(https://www.google.com/s2/favicons?domain={url})\"></div>")
            .Append("<div class=\"bcard-site\">")
            .Append($"<a href=\"{url}\" rel=\"nofollow\" target=\"_blank\">{metaData.OgSiteName}</a>")
            .Append("</div>")
            .Append("<div class=\"bcard-url\">")
            .Append($"<a href=\"{url}\" rel=\"nofollow\" target=\"_blank\">{url}</a>")
            .Append("</div>")
            .Append("</span>")
            .Append("<span class=\"bcard-main withogimg\">")
            .Append("<div class=\"bcard-title\">")
            .Append($"<a href=\"{url}\" rel=\"nofollow\" target=\"_blank\">{metaData.Title}</a>")
            .Append("</div>")
            .Append("<div class=\"bcard-description\">")
            .Append(metaData.OgDescription)
            .Append("</div>")
            .Append($"<a href=\"{url}\" rel=\"nofollow\" target=\"_blank\">")
            .Append($"<div class=\"bcard-img\" style=\"background-image: url({metaData.OgImage})\"></div>")
            .Append("</a>")
            .Append("</span>")
            .Append("<span>")
            .Append($"<a href=\"//b.hatena.ne.jp/entry/s/{noSchemeUrl}\" ref=\"nofollow\" target=\"_blank\">")
            .Append($"<img src=\"//b.st-hatena.com/entry/image/{url}\" alt=\"[はてなブックマークで表示]\"></a>")
            .Append("</span>")
            .Append("</div>")
            .ToString();
    }
}
