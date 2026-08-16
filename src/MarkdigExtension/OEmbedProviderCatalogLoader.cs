using System.Text.Json;
using BlogGenerator.MarkdigExtension.Models;

namespace BlogGenerator.MarkdigExtension;

public class OEmbedProviderCatalogLoader
{
    private readonly OEmbedHttpFetcher _fetcher;

    public OEmbedProviderCatalogLoader(HttpClient httpClient)
        : this(new OEmbedHttpFetcher(httpClient))
    {
    }

    public OEmbedProviderCatalogLoader(OEmbedHttpFetcher fetcher)
    {
        _fetcher = fetcher;
    }

    /// <summary>
    /// providers.json からprovider catalogを読み込む
    /// </summary>
    public async ValueTask<OEmbedProviderCatalog> LoadAsync()
    {
        try
        {
            var fetchResult = await _fetcher.FetchAsync("https://oembed.com/providers.json");

            if (!fetchResult.IsSuccess || string.IsNullOrEmpty(fetchResult.Content))
                return new OEmbedProviderCatalog([]);

            return Parse(fetchResult.Content);
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
}
