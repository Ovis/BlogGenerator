using System.Net;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using BlogGenerator.MarkdigExtension.Models;
using Hnx8.ReadJEnc;

namespace BlogGenerator.MarkdigExtension;

public class OEmbedSiteMetaDataExtractor(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;

    /// <summary>
    /// URLからサイトメタデータを取得する
    /// </summary>
    public async Task<(bool IsSuccess, SiteMetaData Data)> GetSiteMetaDataAsync(string url)
    {
        var (isSuccess, contentHtml, _, effectiveUrl, _) = await GetWebsiteContentAsync(url);
        if (!isSuccess || string.IsNullOrEmpty(contentHtml))
        {
            return (false, new SiteMetaData());
        }

        try
        {
            return (true, Parse(effectiveUrl ?? url, contentHtml));
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
            OgImage = document.QuerySelector("meta[property='og:image']")?.GetAttribute("content") ?? string.Empty,
            OgDescription = document.QuerySelector("meta[property='og:description']")?.GetAttribute("content") ?? string.Empty,
            OgType = document.QuerySelector("meta[property='og:type']")?.GetAttribute("content") ?? string.Empty,
            OgUrl = document.QuerySelector("meta[property='og:url']")?.GetAttribute("content") ?? string.Empty,
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

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        return Uri.TryCreate(new Uri(baseUrl), candidate, out var relativeUri)
            ? relativeUri.ToString()
            : candidate;
    }

    /// <summary>
    /// Webサイトコンテンツを取得する
    /// </summary>
    private async Task<(bool IsSuccess, string? Content, string? MediaType, string? EffectiveUrl, Exception? Error)> GetWebsiteContentAsync(string url)
    {
        try
        {
            var response = await _httpClient.GetAsync(url);

            if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.MovedPermanently)
            {
                var redirectUrl = response.Headers.Location?.OriginalString ?? string.Empty;
                if (!string.IsNullOrEmpty(redirectUrl))
                {
                    response = await _httpClient.GetAsync(ResolveUrl(url, redirectUrl));
                }
            }

            response.EnsureSuccessStatusCode();

            if (response.IsSuccessStatusCode)
            {
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                var byteArray = await response.Content.ReadAsByteArrayAsync();
                ReadJEnc.JP.GetEncoding(byteArray, byteArray.Length, out var content);
                return (true, content, mediaType, response.RequestMessage?.RequestUri?.ToString() ?? url, null);
            }
        }
        catch (TaskCanceledException e)
        {
            Console.WriteLine($"Request timeout: {url}");
            return (false, null, null, null, e);
        }
        catch (HttpRequestException ex)
        {
            if (ex.HttpRequestError == HttpRequestError.Unknown)
            {
                Console.WriteLine($"HTTP error: {ex.StatusCode}, URL: {url}");
            }
            else
            {
                Console.WriteLine($"HTTP request error: {ex.HttpRequestError}, URL: {url}");
            }

            return (false, null, null, null, ex);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error fetching content: {e.Message}, URL: {url}");
            return (false, null, null, null, e);
        }

        return (false, null, null, null, null);
    }
}
