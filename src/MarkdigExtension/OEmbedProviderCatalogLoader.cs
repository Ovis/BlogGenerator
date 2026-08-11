using System.Net;
using System.Text.Json;
using BlogGenerator.MarkdigExtension.Models;
using Hnx8.ReadJEnc;

namespace BlogGenerator.MarkdigExtension;

public class OEmbedProviderCatalogLoader(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;

    /// <summary>
    /// providers.json からprovider catalogを読み込む
    /// </summary>
    public async ValueTask<OEmbedProviderCatalog> LoadAsync()
    {
        try
        {
            var (isSuccess, content, _, _) = await GetWebsiteContentAsync("https://oembed.com/providers.json");

            if (!isSuccess || string.IsNullOrEmpty(content))
                return new OEmbedProviderCatalog([]);

            return Parse(content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"oEmbed provider json could not be obtained. Error:{ex.Message}");
            return new OEmbedProviderCatalog([]);
        }
    }

    /// <summary>
    /// JSON文字列からprovider catalogを構築する
    /// </summary>
    public OEmbedProviderCatalog Parse(string json)
    {
        var jsonData = JsonSerializer.Deserialize<List<OEmbedProviderJson>>(json);
        return jsonData == null
            ? new OEmbedProviderCatalog([])
            : new OEmbedProviderCatalog(jsonData);
    }

    private async ValueTask<(bool isSuccess, string content, string mediaType, Exception? error)> GetWebsiteContentAsync(string url)
    {
        try
        {
            var response = await _httpClient.GetAsync(url);

            if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.MovedPermanently)
            {
                var redirectUrl = response.Headers.Location?.OriginalString;
                if (redirectUrl != null)
                {
                    response = await _httpClient.GetAsync(redirectUrl);
                }
            }

            response.EnsureSuccessStatusCode();

            if (response.IsSuccessStatusCode)
            {
                var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                var byteArray = await response.Content.ReadAsByteArrayAsync();

                ReadJEnc.JP.GetEncoding(byteArray, byteArray.Length, out var content);
                return (true, content, mediaType, null);
            }
        }
        catch (TaskCanceledException e)
        {
            return (false, string.Empty, string.Empty, e);
        }
        catch (Exception e)
        {
            return (false, string.Empty, string.Empty, e);
        }

        return (false, string.Empty, string.Empty, null);
    }
}
