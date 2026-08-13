using System.Net;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Xml.Serialization;
using BlogGenerator.Converters;
using BlogGenerator.MarkdigExtension.Models;
using Hnx8.ReadJEnc;
using Microsoft.AspNetCore.WebUtilities;

namespace BlogGenerator.MarkdigExtension;

public class OEmbedEndpointResolver(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;

    /// <summary>
    /// oEmbedエンドポイントからEmbedレスポンスを取得する
    /// </summary>
    public async Task<(bool IsSuccess, string? RichLinkString, bool IsVideo, Exception? Error)> GetEmbedResultAsync(string endpoint, string url)
    {
        var requestUrl = BuildRequestUrl(endpoint, url);

        try
        {
            var (isSuccess, content, mediaType, error) = await GetWebsiteContentAsync(requestUrl);
            if (!isSuccess || string.IsNullOrEmpty(content))
            {
                return (false, null, false, error);
            }

            var embedResponse = DeserializeEmbedResponse(content, mediaType);

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

                var imgHtml = $"<img src=\"{embedResponse.Url}\" width=\"{embedResponse.Width}\" height=\"{embedResponse.Height}\" />";
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

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        return new Uri(new Uri(baseUrl), candidate).ToString();
    }

    /// <summary>
    /// Webサイトコンテンツを取得する
    /// </summary>
    private async Task<(bool IsSuccess, string? Content, string? MediaType, Exception? Error)> GetWebsiteContentAsync(string url)
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
                return (true, content, mediaType, null);
            }
        }
        catch (TaskCanceledException e)
        {
            Console.WriteLine($"Request timeout: {url}");
            return (false, null, null, e);
        }
        catch (HttpRequestException ex)
        {
            LogHttpRequestError(ex, url);
            return (false, null, null, ex);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error fetching content: {e.Message}, URL: {url}");
            return (false, null, null, e);
        }

        return (false, null, null, null);
    }

    /// <summary>
    /// HTTPリクエストエラーをログ出力する
    /// </summary>
    private static void LogHttpRequestError(HttpRequestException ex, string url)
    {
        if (ex.HttpRequestError == HttpRequestError.Unknown)
        {
            Console.WriteLine($"HTTP error: {ex.StatusCode}, URL: {url}");
        }
        else
        {
            Console.WriteLine($"HTTP request error: {ex.HttpRequestError}, URL: {url}");
        }
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
