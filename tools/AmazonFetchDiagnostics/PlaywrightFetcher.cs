using System.Net;
using BlogGenerator.MarkdigExtension;
using Microsoft.Playwright;

// 診断用。通常のChromiumで閲覧するだけで、確認フォーム・CAPTCHAは操作しない。
sealed class PlaywrightFetcher(IPlaywright playwright, IBrowser browser)
    : IAmazonProductPageFetcher, IAsyncDisposable
{
    private int _attempt;
    public int? HttpStatus { get; private set; }
    public string? FinalUrl { get; private set; }

    public static async Task<PlaywrightFetcher> CreateAsync()
    {
        var playwright = await Playwright.CreateAsync();
        try
        {
            var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            return new PlaywrightFetcher(playwright, browser);
        }
        catch
        {
            playwright.Dispose();
            throw;
        }
    }

    public async Task<AmazonProductFetchResult> FetchAsync(string asin)
    {
        _attempt++;
        HttpStatus = null;
        FinalUrl = null;
        await using var context = await browser.NewContextAsync(new() { Locale = "ja-JP" });
        var page = await context.NewPageAsync();
        try
        {
            var response = await page.GotoAsync($"https://www.amazon.co.jp/dp/{asin}/",
                new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
            HttpStatus = response?.Status;
            if (response is not null)
                await File.WriteAllTextAsync($"amazon-browser-{_attempt}-initial.html", await response.TextAsync());
            try
            {
                await page.WaitForLoadStateAsync(LoadState.Load, new() { Timeout = 10000 });
            }
            catch (TimeoutException) { /* 外部リソースの遅延でも現在のDOMを保存する */ }
            FinalUrl = page.Url;
            var content = await page.ContentAsync();
            await File.WriteAllTextAsync($"amazon-browser-{_attempt}-dom.html", content);
            try
            {
                await page.ScreenshotAsync(new() { Path = $"amazon-browser-{_attempt}.png", Timeout = 10000 });
            }
            catch (Exception error) when (error is PlaywrightException or TimeoutException)
            {
                Console.Error.WriteLine($"Screenshot unavailable: {error.GetType().Name}");
            }
            return HttpStatus is >= 200 and <= 299
                ? AmazonProductFetchResult.Success(content)
                : AmazonProductFetchResult.Failure(HttpStatus is null ? null : (HttpStatusCode)HttpStatus, content);
        }
        catch (Exception error) when (error is PlaywrightException or TimeoutException)
        {
            FinalUrl = page.Url;
            return AmazonProductFetchResult.Failure(HttpStatus is null ? null : (HttpStatusCode)HttpStatus,
                string.Empty, error);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await browser.CloseAsync();
        playwright.Dispose();
    }
}
