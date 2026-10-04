using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace BlogGenerator.Core.Images;

/// <summary>HTMLは解析で識別した属性だけを編集し、本文・コード・スクリプトの内容を維持する。</summary>
internal sealed class ImageReferenceRewriter(ImageUrlRewriter urls)
{
    private static readonly Regex StartTag = new("<[A-Za-z](?:[^>\"']|\"[^\"]*\"|'[^']*')*>", RegexOptions.Compiled);
    private static readonly Regex Attribute = new(
        "(?<name>[\\w:-]+)\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s>]+))", RegexOptions.Compiled);
    private static readonly Regex CssUrl = new(
        "url\\(\\s*(?:\"(?<value>[^\"\\\\]*)\"|'(?<value>[^'\\\\]*)'|(?<value>[^\\s)'\"\\\\]+))\\s*\\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string RewriteHtml(string html, Uri documentUrl)
    {
        var parser = new HtmlParser(new HtmlParserOptions { IsKeepingSourceReferences = true });
        using var document = parser.ParseDocument(html);
        if (document.QuerySelector("base[href]")?.GetAttribute("href") is { } baseHref &&
            Uri.TryCreate(documentUrl, baseHref, out var baseUrl)) documentUrl = baseUrl;

        var edits = new List<Edit>();
        var convertedOgImage = false;
        foreach (var element in document.All)
        {
            if (element.SourceReference is not { } source || source.Position.Index < 0 || source.Position.Index >= html.Length) continue;
            var tag = StartTag.Match(html, source.Position.Index);
            if (!tag.Success || tag.Index != source.Position.Index) continue;
            var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 明示的なMIME type付き参照は、形式を変えるとpictureやダウンロードの意味が変わるため保持する。
            var typed = element.HasAttribute("type") && element.LocalName is "source" or "link";
            var downloadable = element.LocalName == "a" && element.HasAttribute("download");
            foreach (var attribute in element.Attributes)
            {
                var name = attribute.LocalName;
                var original = attribute.Value;
                var updated = original;
                if (name == "style") updated = RewriteCss(original, documentUrl);
                else if (!typed && !downloadable)
                {
                    if ((name == "src" && element.LocalName is "img" or "input" or "source") ||
                        (name == "href" && element.LocalName is "a" or "link") ||
                        (name == "poster" && element.LocalName == "video") ||
                        (name == "content" && IsImageMeta(element)))
                        updated = urls.Rewrite(original, documentUrl);
                    else if ((name == "srcset" && element.LocalName is "img" or "source") ||
                        (name == "imagesrcset" && element.LocalName == "link"))
                        updated = RewriteSrcset(original, documentUrl);
                }
                if (updated != original) replacements[name] = updated;
            }
            if (element.LocalName == "meta" && element.GetAttribute("property")?.ToLowerInvariant() == "og:image")
                convertedOgImage = replacements.ContainsKey("content");
            if (convertedOgImage && element.LocalName == "meta" && element.GetAttribute("property")?.ToLowerInvariant() == "og:image:type")
                replacements["content"] = "image/webp";
            if (replacements.Count > 0)
            {
                var updatedTag = Attribute.Replace(tag.Value, match =>
                {
                    if (!replacements.TryGetValue(match.Groups["name"].Value, out var value)) return match.Value;
                    var group = match.Groups["value"];
                    var encoded = WebUtility.HtmlEncode(value);
                    // 未引用の属性には引用符を付け、URL中の空白や=などを安全に扱う。
                    var quoted = group.Index > 0 && tag.Value[group.Index - 1] is '\'' or '"';
                    if (!quoted) encoded = "\"" + encoded + "\"";
                    var offset = group.Index - match.Index;
                    return match.Value[..offset] + encoded + match.Value[(offset + group.Length)..];
                });
                edits.Add(new(tag.Index, tag.Length, updatedTag));
            }
            if (element.LocalName == "style")
            {
                var start = tag.Index + tag.Length;
                var end = html.IndexOf("</style", start, StringComparison.OrdinalIgnoreCase);
                if (end >= start)
                {
                    var original = html[start..end];
                    var updated = RewriteCss(original, documentUrl);
                    if (updated != original) edits.Add(new(start, end - start, updated));
                }
            }
        }
        return ApplyEdits(html, edits);
    }

    public string RewriteCss(string css, Uri documentUrl)
    {
        // image-setはtype()による形式指定も可能。初期版では参照を保持して元画像を残す。
        if (css.Contains("image-set(", StringComparison.OrdinalIgnoreCase)) return css;
        return CssUrl.Replace(css, match =>
        {
            var value = match.Groups["value"];
            var updated = urls.Rewrite(value.Value, documentUrl);
            if (updated == value.Value) return match.Value;
            var offset = value.Index - match.Index;
            return match.Value[..offset] + updated + match.Value[(offset + value.Length)..];
        });
    }

    public string RewriteFeed(string xml, Uri documentUrl)
    {
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var changed = false;
        foreach (var element in document.Descendants())
        {
            // 既存の生成器はHTML本文をAtomのtype=textにも格納するため、こちらも属性だけを処理する。
            var htmlContent = element.Name.LocalName == "description" && element.Parent?.Name.LocalName == "item" ||
                element.Name.LocalName is "content" or "summary" &&
                element.Name.NamespaceName == "http://www.w3.org/2005/Atom" &&
                (string?)element.Attribute("type") is "html" or "text";
            if (!htmlContent || element.HasElements) continue;
            var context = documentUrl;
            foreach (var ancestor in element.AncestorsAndSelf().Reverse())
                if ((string?)ancestor.Attribute(XNamespace.Xml + "base") is { } xmlBase &&
                    Uri.TryCreate(context, xmlBase, out var inheritedBase)) context = inheritedBase;
            var link = element.Parent?.Elements().FirstOrDefault(x => x.Name.LocalName == "link" &&
                ((string?)x.Attribute("rel") is null or "alternate"));
            var href = (string?)link?.Attribute("href") ?? link?.Value;
            if (href is not null && Uri.TryCreate(context, href, out var itemUrl)) context = itemUrl;
            var updated = RewriteHtml(element.Value, context);
            if (updated != element.Value)
            {
                element.Value = updated;
                changed = true;
            }
        }
        if (!changed) return xml;
        return (document.Declaration is null ? "" : document.Declaration + "\n") + document.ToString(SaveOptions.DisableFormatting);
    }

    private string RewriteSrcset(string value, Uri documentUrl)
    {
        // URL内のカンマ（data URLなど）と、候補間のカンマを区別する。
        var edits = new List<Edit>();
        var index = 0;
        while (index < value.Length)
        {
            while (index < value.Length && (char.IsWhiteSpace(value[index]) || value[index] == ',')) index++;
            var start = index;
            while (index < value.Length && !char.IsWhiteSpace(value[index])) index++;
            var end = index;
            while (end > start && value[end - 1] == ',') end--;
            var url = value[start..end];
            var updated = urls.Rewrite(url, documentUrl);
            if (updated != url) edits.Add(new(start, end - start, updated));
            if (end < index) continue;
            while (index < value.Length && value[index] != ',') index++;
        }
        return ApplyEdits(value, edits);
    }

    private static bool IsImageMeta(IElement element) => element.LocalName == "meta" &&
        (element.GetAttribute("property")?.ToLowerInvariant() is "og:image" or "og:image:url" or "og:image:secure_url" ||
         element.GetAttribute("name")?.ToLowerInvariant() is "twitter:image" or "twitter:image:src" ||
         element.GetAttribute("itemprop") == "image");

    private static string ApplyEdits(string value, List<Edit> edits)
    {
        foreach (var edit in edits.OrderByDescending(x => x.Start))
            value = value[..edit.Start] + edit.Value + value[(edit.Start + edit.Length)..];
        return value;
    }

    private sealed record Edit(int Start, int Length, string Value);
}
