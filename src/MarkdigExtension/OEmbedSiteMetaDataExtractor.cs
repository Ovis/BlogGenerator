using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using BlogGenerator.MarkdigExtension.Models;

namespace BlogGenerator.MarkdigExtension;

public class OEmbedSiteMetaDataExtractor
{
    private readonly OEmbedHttpFetcher _fetcher;

    public OEmbedSiteMetaDataExtractor(HttpClient httpClient)
        : this(new OEmbedHttpFetcher(httpClient))
    {
    }

    public OEmbedSiteMetaDataExtractor(OEmbedHttpFetcher fetcher)
    {
        _fetcher = fetcher;
    }

    /// <summary>
    /// URLからサイトメタデータを取得する
    /// </summary>
    public async Task<(bool IsSuccess, SiteMetaData Data)> GetSiteMetaDataAsync(string url)
    {
        var fetchResult = await _fetcher.FetchAsync(url);
        if (!fetchResult.IsSuccess || string.IsNullOrEmpty(fetchResult.Content))
        {
            return (false, new SiteMetaData());
        }

        try
        {
            return (true, Parse(fetchResult.EffectiveUrl, fetchResult.Content));
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error parsing HTML: {e.Message}, URL: {url}");
            return (false, new SiteMetaData());
        }
    }

    /// <summary>
    /// HTMLからサイトメタデータを抽出する
    /// </summary>
    public SiteMetaData Parse(string url, string contentHtml)
    {
        var document = new HtmlParser().ParseDocument(contentHtml);

        return new SiteMetaData
        {
            Url = url,
            Title = document.QuerySelector("title")?.TextContent ?? string.Empty,
            OgTitle = document.QuerySelector("meta[property='og:title']")?.GetAttribute("content") ?? string.Empty,
            OgImage = ResolveUrl(url, document.QuerySelector("meta[property='og:image']")?.GetAttribute("content")),
            OgDescription = document.QuerySelector("meta[property='og:description']")?.GetAttribute("content") ?? string.Empty,
            OgType = document.QuerySelector("meta[property='og:type']")?.GetAttribute("content") ?? string.Empty,
            OgUrl = ResolveUrl(url, document.QuerySelector("meta[property='og:url']")?.GetAttribute("content")),
            OgSiteName = document.QuerySelector("meta[property='og:site_name']")?.GetAttribute("content") ?? string.Empty,
            OembedJson = ResolveUrl(url, document.QuerySelector("link[type='application/json+oembed']")?.GetAttribute("href")),
            OembedXml = ResolveUrl(url, GetXmlOembedLink(document))
        };
    }

    /// <summary>
    /// メタデータからoEmbedエンドポイントを取得する
    /// </summary>
    public static string GetOEmbedEndpoint(SiteMetaData metaData)
    {
        if (!string.IsNullOrEmpty(metaData.OembedJson))
            return metaData.OembedJson;

        if (!string.IsNullOrEmpty(metaData.OembedXml))
            return metaData.OembedXml;

        return string.Empty;
    }

    /// <summary>
    /// XMLのoEmbedリンクを取得する
    /// </summary>
    private static string GetXmlOembedLink(IHtmlDocument document)
    {
        var xmlLink = document.QuerySelector("link[type='application/xml+oembed']")?.GetAttribute("href");
        if (!string.IsNullOrEmpty(xmlLink))
            return xmlLink;

        return document.QuerySelector("link[type='text/xml+oembed']")?.GetAttribute("href") ?? string.Empty;
    }

    private static string ResolveUrl(string baseUrl, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        // Linux では /path が file:///path と解釈されうるため、http/https の絶対URLだけをそのまま受け入れる
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
        {
            return absoluteUri.ToString();
        }

        return Uri.TryCreate(new Uri(baseUrl), candidate, out var relativeUri)
            ? relativeUri.ToString()
            : candidate;
    }

}
