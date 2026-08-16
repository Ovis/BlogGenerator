using System.Net.Mime;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Serialization;
using BlogGenerator.Converters;
using BlogGenerator.MarkdigExtension.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace BlogGenerator.MarkdigExtension;

public class OEmbedEndpointResolver
{
    private readonly OEmbedHttpFetcher _fetcher;

    public OEmbedEndpointResolver(HttpClient httpClient)
        : this(new OEmbedHttpFetcher(httpClient))
    {
    }

    public OEmbedEndpointResolver(OEmbedHttpFetcher fetcher)
    {
        _fetcher = fetcher;
    }

    /// <summary>
    /// oEmbedエンドポイントからEmbedレスポンスを取得する
    /// </summary>
    public async Task<(bool IsSuccess, string? RichLinkString, bool IsVideo, Exception? Error)> GetEmbedResultAsync(string endpoint, string url)
    {
        var requestUrl = BuildRequestUrl(endpoint, url);

        try
        {
            var fetchResult = await _fetcher.FetchAsync(requestUrl);
            if (!fetchResult.IsSuccess || string.IsNullOrEmpty(fetchResult.Content))
            {
                return (false, null, false, fetchResult.Error);
            }

            var embedResponse = DeserializeEmbedResponse(fetchResult.Content, fetchResult.MediaType);

            if (!string.IsNullOrEmpty(embedResponse.Html))
            {
                return (true, embedResponse.Html, embedResponse.Type == "video", null);
            }

            if (embedResponse.Type == "photo")
            {
                if (string.IsNullOrEmpty(embedResponse.Url) ||
                    string.IsNullOrEmpty(embedResponse.Width) ||
                    string.IsNullOrEmpty(embedResponse.Height))
                {
                    throw new InvalidDataException("Missing required oEmbed values for image type");
                }

                var imgHtml = BuildPhotoImageHtml(embedResponse.Url, embedResponse.Width, embedResponse.Height);
                return (true, imgHtml, false, null);
            }

            if (embedResponse.Type == "link")
            {
                return (false, null, false, null);
            }

            return (false, null, false, new InvalidDataException("Unsupported oEmbed content type"));
        }
        catch (Exception e)
        {
            return (false, null, false, e);
        }
    }

    /// <summary>
    /// リクエストURLを構築する
    /// </summary>
    private static string BuildRequestUrl(string endpoint, string url)
    {
        if (string.IsNullOrEmpty(url))
            return endpoint;

        if (HasUrlQueryParameter(endpoint))
            return endpoint;

        return QueryHelpers.AddQueryString(endpoint, new Dictionary<string, string?>
        {
            { "url", url }
        });
    }

    private static bool HasUrlQueryParameter(string endpoint)
    {
        var query = Uri.TryCreate(endpoint, UriKind.Absolute, out var absoluteUri)
            ? absoluteUri.Query
            : endpoint.Contains('?', StringComparison.Ordinal)
                ? endpoint[endpoint.IndexOf('?', StringComparison.Ordinal)..]
                : string.Empty;

        if (string.IsNullOrEmpty(query))
        {
            return false;
        }

        return QueryHelpers.ParseQuery(query).Keys.Any(key => string.Equals(key, "url", StringComparison.OrdinalIgnoreCase));
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

        return new Uri(new Uri(baseUrl), candidate).ToString();
    }

    // oEmbed photo は外部入力なので、画像URLと寸法を最低限検証してから属性へ出力する
    private static string BuildPhotoImageHtml(string imageUrl, string width, string height)
    {
        if (!TryGetSafeHttpUrl(imageUrl, out var safeImageUrl))
        {
            throw new InvalidDataException("Invalid oEmbed image url");
        }

        if (!TryParsePositiveInt(width, out var safeWidth) || !TryParsePositiveInt(height, out var safeHeight))
        {
            throw new InvalidDataException("Invalid oEmbed image dimensions");
        }

        return $"<img src=\"{WebUtility.HtmlEncode(safeImageUrl)}\" width=\"{safeWidth}\" height=\"{safeHeight}\" />";
    }

    private static bool TryParsePositiveInt(string value, out int parsedValue)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsedValue) &&
            parsedValue > 0;
    }

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

    /// <summary>
    /// メディアタイプに応じてEmbedResponseをデシリアライズする
    /// </summary>
    private static EmbedResponse DeserializeEmbedResponse(string content, string? mediaType)
    {
        switch (mediaType)
        {
            case MediaTypeNames.Application.Json:
            case MediaTypeNames.Text.Plain:
            case MediaTypeNames.Text.Html:
                var options = new JsonSerializerOptions();
                options.Converters.Add(new AutoNumberToStringConverter());
                return JsonSerializer.Deserialize<EmbedResponse>(content, options)
                       ?? new EmbedResponse();

            case MediaTypeNames.Application.Xml:
            case MediaTypeNames.Text.Xml:
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
                {
                    return (EmbedResponse)new XmlSerializer(typeof(EmbedResponse)).Deserialize(stream)!
                           ?? new EmbedResponse();
                }

            default:
                throw new InvalidDataException($"Unsupported media type: {mediaType}");
        }
    }
}
