using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlogGenerator.MarkdigExtension;
using AngleSharp.Html.Parser;

if (args.Length != 2 || !Regex.IsMatch(args[0], "^[A-Za-z0-9]{10}$") ||
    !int.TryParse(args[1], out var attempts) || attempts is < 1 or > 3)
{
    Console.Error.WriteLine("Usage: AmazonFetchDiagnostics <10-character ASIN> <attempts: 1-3>");
    return 2;
}

var asin = args[0].ToUpperInvariant();
using var client = AmazonProductHttpFetcher.CreateHttpClient();
var fetcher = new RecordingFetcher(new AmazonProductHttpFetcher(client));
var results = new List<object>();
var failures = 0;
for (var attempt = 1; attempt <= attempts; attempt++)
{
    // 本番と同じfetcher・parser・判定を使うが、毎回空のメモリキャッシュで実際に取得する。
    var resolver = new AmazonProductMetadataResolver(fetcher, new AmazonProductPageParser());
    var startedAt = DateTimeOffset.UtcNow;
    var stopwatch = Stopwatch.StartNew();
    var metadata = await resolver.ResolveAsync(asin);
    stopwatch.Stop();
    var response = fetcher.LastResult!;
    var entry = resolver.Cache[asin];
    var htmlFile = $"amazon-response-{attempt}.html";
    await File.WriteAllTextAsync(htmlFile, response.Content, new UTF8Encoding(false));
    var blockMarkers = new[]
    {
        "captcha", "unusual traffic", "automated access to Amazon data", "ロボットではありません"
    }.Where(marker => response.Content.Contains(marker, StringComparison.OrdinalIgnoreCase)).ToArray();
    var pageTitle = new HtmlParser().ParseDocument(response.Content).Title;
    var result = new
    {
        attempt,
        startedAt,
        asin,
        elapsedMilliseconds = stopwatch.ElapsedMilliseconds,
        httpStatus = response.StatusCode is null ? (int?)null : (int)response.StatusCode,
        responseLength = response.Content.Length,
        responseSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(response.Content))),
        htmlFile,
        pageTitle,
        blockMarkers,
        exceptionType = response.Error?.GetType().Name,
        outcome = metadata is null ? entry.FailureKind?.ToString() : "Success",
        errorSummary = entry.ErrorSummary,
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
