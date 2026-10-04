using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlogGenerator.MarkdigExtension;
using AngleSharp.Html.Parser;

if (args.Length is < 2 or > 3 || !Regex.IsMatch(args[0], "^[A-Za-z0-9]{10}$") ||
    !int.TryParse(args[1], out var attempts) || attempts is < 1 or > 3 ||
    (args.Length == 3 && args[2] is not ("http" or "playwright" or "search")))
{
    Console.Error.WriteLine("Usage: AmazonFetchDiagnostics <10-character ASIN> <attempts: 1-3> [http|playwright|search]");
    return 2;
}

var asin = args[0].ToUpperInvariant();
using var client = AmazonProductHttpFetcher.CreateHttpClient();
var engine = args.Length == 3 ? args[2] : "http";
await using var browserFetcher = engine == "playwright" ? await PlaywrightFetcher.CreateAsync() : null;
IAmazonProductPageFetcher rawFetcher = engine switch
{
    "search" => new AmazonSearchFetcher(client),
    "playwright" => browserFetcher!,
    _ => new AmazonProductHttpFetcher(client)
};
var fetcher = new RecordingFetcher(rawFetcher);
var results = new List<object>();
var failures = 0;
for (var attempt = 1; attempt <= attempts; attempt++)
{
    // 本番と同じfetcher・parser・判定を使うが、毎回空のメモリキャッシュで実際に取得する。
    var resolver = new AmazonProductMetadataResolver(fetcher, new AmazonProductPageParser());
    var startedAt = DateTimeOffset.UtcNow;
    var stopwatch = Stopwatch.StartNew();
    AmazonProductMetadata? metadata;
    if (engine == "search")
    {
        var searchResponse = await fetcher.FetchAsync(asin);
        metadata = searchResponse.IsSuccess
            ? new AmazonSearchResultParser().Parse(searchResponse.Content, asin) : null;
    }
    else
    {
        metadata = await resolver.ResolveAsync(asin);
    }
    stopwatch.Stop();
    var response = fetcher.LastResult!;
    resolver.Cache.TryGetValue(asin, out var entry);
    var htmlFile = $"amazon-response-{attempt}.html";
    await File.WriteAllTextAsync(htmlFile, response.Content, new UTF8Encoding(false));
    var blockMarkers = new[]
    {
        "captcha", "unusual traffic", "automated access to Amazon data", "ロボットではありません"
    }.Where(marker => response.Content.Contains(marker, StringComparison.OrdinalIgnoreCase)).ToArray();
    if (engine == "search" && blockMarkers.Length != 0) metadata = null;
    var outcome = metadata is not null ? "Success" : engine != "search" ? entry?.FailureKind?.ToString()
        : blockMarkers.Length != 0 ? "Blocked" : !response.IsSuccess ? "NetworkError" : "SearchResultMissing";
    var parsedPage = new HtmlParser().ParseDocument(response.Content);
    var pageTitle = parsedPage.Title;
    foreach (var element in parsedPage.QuerySelectorAll("script, style")) element.Remove();
    var pageText = string.Join(' ', (parsedPage.Body?.TextContent ?? string.Empty)
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    if (pageText.Length > 800) pageText = pageText[..800];
    var result = new
    {
        attempt,
        engine,
        browserHttpStatus = browserFetcher?.HttpStatus,
        finalUrl = browserFetcher?.FinalUrl,
        startedAt,
        asin,
        elapsedMilliseconds = stopwatch.ElapsedMilliseconds,
        httpStatus = response.StatusCode is null ? (int?)null : (int)response.StatusCode,
        responseLength = response.Content.Length,
        responseSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(response.Content))),
        htmlFile,
        pageTitle,
        pageText,
        blockMarkers,
        exceptionType = response.Error?.GetType().Name,
        outcome,
        errorSummary = entry?.ErrorSummary ?? response.Error?.Message ??
            (outcome == "Success" ? "" : outcome == "Blocked" ? "Amazon returned a verification page"
                : outcome == "SearchResultMissing" ? "No exact ASIN result with title and image" : "Search request failed"),
        title = metadata?.Title,
        imageUrl = metadata?.ImageUrl,
        runnerOs = Environment.GetEnvironmentVariable("RUNNER_OS"),
        runId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID"),
        commit = Environment.GetEnvironmentVariable("GITHUB_SHA")
    };
    results.Add(result);
    Console.WriteLine(JsonSerializer.Serialize(result));
    if (metadata is null) failures++;
    if (attempt < attempts) await Task.Delay(TimeSpan.FromSeconds(10));
}

await File.WriteAllTextAsync("amazon-diagnostics.json", JsonSerializer.Serialize(results,
    new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Succeeded: {attempts - failures}; failed: {failures}. Response HTML is saved; cookies and request headers are not saved.");
return failures == 0 ? 0 : 1;

sealed class RecordingFetcher(IAmazonProductPageFetcher inner) : IAmazonProductPageFetcher
{
    public AmazonProductFetchResult? LastResult { get; private set; }

    public async Task<AmazonProductFetchResult> FetchAsync(string asin)
    {
        LastResult = await inner.FetchAsync(asin);
        return LastResult;
    }
}
