using System.Net;
using System.Net.Http.Headers;

namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// Amazon商品ページをHTTPで取得するfetcher
/// </summary>
public sealed class AmazonProductHttpFetcher(HttpClient httpClient) : IAmazonProductPageFetcher
{
    private readonly HttpClient _httpClient = httpClient;

    public async Task<AmazonProductFetchResult> FetchAsync(string asin)
    {
        var productUrl = $"https://www.amazon.co.jp/dp/{asin}/";
        using var request = new HttpRequestMessage(HttpMethod.Get, productUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.AcceptLanguage.ParseAdd("ja-JP,ja;q=0.9");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; BlogGenerator/1.0)");

        try
        {
            using var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            return response.IsSuccessStatusCode
                ? AmazonProductFetchResult.Success(content)
                : AmazonProductFetchResult.Failure(response.StatusCode, content);
        }
        catch (TaskCanceledException exception)
        {
            return AmazonProductFetchResult.Failure(null, string.Empty, exception);
        }
        catch (HttpRequestException exception)
        {
            return AmazonProductFetchResult.Failure(null, string.Empty, exception);
        }
    }
}
