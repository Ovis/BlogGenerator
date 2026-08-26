using System.Net;
using System.Net.Http.Headers;

namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// Amazon商品ページをHTTPで取得するfetcher
/// </summary>
public sealed class AmazonProductHttpFetcher : IAmazonProductPageFetcher
{
    public static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromSeconds(2);

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _requestSemaphore = new(1, 1);
    private readonly Func<DateTimeOffset> _utcNowProvider;
    private readonly Func<TimeSpan, Task> _delayAsync;
    private DateTimeOffset? _lastRequestStartedAt;

    public AmazonProductHttpFetcher(
        HttpClient httpClient,
        Func<DateTimeOffset>? utcNowProvider = null,
        Func<TimeSpan, Task>? delayAsync = null)
    {
        _httpClient = httpClient;
        _utcNowProvider = utcNowProvider ?? (() => DateTimeOffset.UtcNow);
        _delayAsync = delayAsync ?? Task.Delay;
    }

    /// <summary>
    /// Amazon商品ページ取得用のHTTPクライアントを作成する
    /// </summary>
    public static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            // Amazonは通常gzip圧縮でHTMLを返すため、展開せずに文字列化するとparserが商品ページを認識できない
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public async Task<AmazonProductFetchResult> FetchAsync(string asin)
    {
        await _requestSemaphore.WaitAsync();
        try
        {
            await WaitForRequestIntervalAsync();
            _lastRequestStartedAt = _utcNowProvider();

            var productUrl = $"https://www.amazon.co.jp/dp/{asin}/";
            using var request = new HttpRequestMessage(HttpMethod.Get, productUrl);
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            request.Headers.AcceptLanguage.ParseAdd("ja-JP,ja;q=0.9,en-US;q=0.8,en;q=0.7");
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36");

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
        finally
        {
            _requestSemaphore.Release();
        }
    }

    private async Task WaitForRequestIntervalAsync()
    {
        if (!_lastRequestStartedAt.HasValue)
        {
            return;
        }

        var elapsed = _utcNowProvider() - _lastRequestStartedAt.Value;
        var delay = MinimumRequestInterval - elapsed;
        if (delay > TimeSpan.Zero)
        {
            await _delayAsync(delay);
        }
    }
}
