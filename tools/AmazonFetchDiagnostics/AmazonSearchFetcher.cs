using BlogGenerator.MarkdigExtension;

// 診断専用。商品ページと同じヘッダーで検索結果を取得する。
sealed class AmazonSearchFetcher(HttpClient client) : IAmazonProductPageFetcher
{
    public async Task<AmazonProductFetchResult> FetchAsync(string asin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://www.amazon.co.jp/s?k={Uri.EscapeDataString(asin)}");
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
        request.Headers.AcceptLanguage.ParseAdd("ja-JP,ja;q=0.9,en-US;q=0.8,en;q=0.7");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36");
        try
        {
            using var response = await client.SendAsync(request);
            var html = await response.Content.ReadAsStringAsync();
            return response.IsSuccessStatusCode
                ? AmazonProductFetchResult.Success(html)
                : AmazonProductFetchResult.Failure(response.StatusCode, html);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            return AmazonProductFetchResult.Failure(null, string.Empty, error);
        }
    }
}
