using System.Net;
using Hnx8.ReadJEnc;

namespace BlogGenerator.MarkdigExtension;

public class OEmbedHttpFetcher
{
    private readonly HttpClient _httpClient;
    private readonly ExternalRequestMetrics? _requestMetrics;

    public OEmbedHttpFetcher(HttpClient httpClient)
        : this(httpClient, null)
    {
    }

    /// <summary>
    /// HTTP取得の計測先を指定してoEmbed fetcherを生成する
    /// </summary>
    /// <param name="httpClient">HTTP取得に使用するクライアント</param>
    /// <param name="requestMetrics">取得件数と累積時間の記録先</param>
    internal OEmbedHttpFetcher(HttpClient httpClient, ExternalRequestMetrics? requestMetrics)
    {
        _httpClient = httpClient;
        _requestMetrics = requestMetrics;
    }

    /// <summary>
    /// oEmbed関連のHTTP取得を共通化する
    /// </summary>
    public async Task<OEmbedFetchResult> FetchAsync(string url)
    {
        try
        {
            using var response = await SendAsyncFollowingRedirectAsync(url);
            response.EnsureSuccessStatusCode();

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            var byteArray = await response.Content.ReadAsByteArrayAsync();
            ReadJEnc.JP.GetEncoding(byteArray, byteArray.Length, out var content);

            return OEmbedFetchResult.Success(
                content ?? string.Empty,
                mediaType,
                response.RequestMessage?.RequestUri?.ToString() ?? url);
        }
        catch (TaskCanceledException e)
        {
            return OEmbedFetchResult.Failure(url, e);
        }
        catch (HttpRequestException ex)
        {
            return OEmbedFetchResult.Failure(url, ex);
        }
        catch (Exception e)
        {
            return OEmbedFetchResult.Failure(url, e);
        }
    }

    private async Task<HttpResponseMessage> SendAsyncFollowingRedirectAsync(string url)
    {
        var response = await GetAsyncMeasured(url);

        // 既存実装互換のため、明示的に1回だけリダイレクト先を追従する
        if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.MovedPermanently)
        {
            var redirectUrl = ResolveUrl(url, response.Headers.Location?.OriginalString);
            if (!string.IsNullOrEmpty(redirectUrl))
            {
                response.Dispose();
                return await GetAsyncMeasured(redirectUrl);
            }
        }

        return response;
    }

    /// <summary>
    /// 実際のHTTP GET 1回を実行し、計測が有効な場合は件数と所要時間を記録する
    /// </summary>
    private Task<HttpResponseMessage> GetAsyncMeasured(string url) =>
        _requestMetrics is null
            ? _httpClient.GetAsync(url)
            : _requestMetrics.MeasureAsync(() => _httpClient.GetAsync(url));

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
