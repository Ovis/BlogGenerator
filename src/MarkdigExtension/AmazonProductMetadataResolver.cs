using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;

namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// Amazon商品メタデータをTTL付きキャッシュとともに解決するサービス
/// </summary>
public sealed class AmazonProductMetadataResolver
{
    public static readonly TimeSpan SuccessTtl = TimeSpan.FromDays(365);
    public static readonly TimeSpan BlockedNetworkAndUnexpectedResponseTtl = TimeSpan.FromHours(6);
    public static readonly TimeSpan ParseMissTtl = TimeSpan.FromDays(1);
    public static readonly TimeSpan NotFoundTtl = TimeSpan.FromDays(30);

    private readonly IAmazonProductPageFetcher _fetcher;
    private readonly AmazonProductPageParser _parser;
    private readonly Func<DateTimeOffset> _utcNowProvider;
    private long _cacheHits;
    private long _cacheMisses;
    private long _fetchElapsedTicks;

    public AmazonProductMetadataResolver(
        IAmazonProductPageFetcher fetcher,
        AmazonProductPageParser parser,
        ConcurrentDictionary<string, AmazonProductMetadataCacheEntry>? cache = null,
        Func<DateTimeOffset>? utcNowProvider = null)
    {
        _fetcher = fetcher;
        _parser = parser;
        Cache = cache ?? [];
        _utcNowProvider = utcNowProvider ?? (() => DateTimeOffset.UtcNow);
    }

    public ConcurrentDictionary<string, AmazonProductMetadataCacheEntry> Cache { get; }

    /// <summary>
    /// 現在までのキャッシュ利用状況と商品ページ取得の累積時間を取得する
    /// </summary>
    internal (long CacheHits, long CacheMisses, TimeSpan FetchElapsed) GetMetrics() =>
        (
            Interlocked.Read(ref _cacheHits),
            Interlocked.Read(ref _cacheMisses),
            TimeSpan.FromTicks(Interlocked.Read(ref _fetchElapsedTicks)));

    /// <summary>
    /// ASINからカード用メタデータを解決する
    /// </summary>
    public async Task<AmazonProductMetadata?> ResolveAsync(string asin)
    {
        var normalizedAsin = asin.ToUpperInvariant();
        var now = _utcNowProvider();
        if (Cache.TryGetValue(normalizedAsin, out var cachedEntry) &&
            (cachedEntry.IsFresh(now) || cachedEntry.ShouldSkipRetry(now)))
        {
            Interlocked.Increment(ref _cacheHits);
            return cachedEntry.ToMetadata();
        }

        // Amazon商品ページはcache missごとに1回取得するため、miss件数はHTTP取得件数としても利用できる
        Interlocked.Increment(ref _cacheMisses);
        var fetchStopwatch = Stopwatch.StartNew();
        AmazonProductFetchResult fetchResult;
        try
        {
            fetchResult = await _fetcher.FetchAsync(normalizedAsin);
        }
        finally
        {
            Interlocked.Add(ref _fetchElapsedTicks, fetchStopwatch.Elapsed.Ticks);
        }

        var resolution = ResolveFetchResult(fetchResult);
        if (resolution.Metadata is not null)
        {
            Cache[normalizedAsin] = AmazonProductMetadataCacheEntry.CreateSuccess(
                normalizedAsin,
                resolution.Metadata,
                now,
                SuccessTtl);
            return resolution.Metadata;
        }

        var failureTtl = GetFailureTtl(resolution.FailureKind);
        if (cachedEntry?.ToMetadata() is not null)
        {
            // 一度取得できた商品情報は、一時的なAmazon側障害でカードを壊さないよう保持する
            Cache[normalizedAsin] = cachedEntry.MarkRefreshFailure(
                resolution.FailureKind,
                now,
                failureTtl,
                resolution.ErrorSummary);
            return cachedEntry.ToMetadata();
        }

        Cache[normalizedAsin] = AmazonProductMetadataCacheEntry.CreateFailure(
            normalizedAsin,
            resolution.FailureKind,
            now,
            failureTtl,
            resolution.ErrorSummary);
        return null;
    }

    private AmazonProductMetadataResolution ResolveFetchResult(AmazonProductFetchResult fetchResult)
    {
        if (ContainsBlockMarker(fetchResult.Content))
        {
            return new AmazonProductMetadataResolution(null, AmazonProductMetadataFailureKind.Blocked, "Amazon blocked the product page request");
        }

        if (!fetchResult.IsSuccess)
        {
            return new AmazonProductMetadataResolution(
                null,
                ClassifyFetchFailure(fetchResult),
                fetchResult.Error?.Message ?? fetchResult.StatusCode?.ToString() ?? "Amazon product page request failed");
        }

        if (!LooksLikeProductPage(fetchResult.Content))
        {
            return new AmazonProductMetadataResolution(null, AmazonProductMetadataFailureKind.UnexpectedResponse, "Amazon returned a non-product page");
        }

        var metadata = _parser.Parse(fetchResult.Content);
        return metadata.Title is null
            ? new AmazonProductMetadataResolution(null, AmazonProductMetadataFailureKind.ParseMiss, "Product title could not be extracted")
            : new AmazonProductMetadataResolution(metadata, AmazonProductMetadataFailureKind.NetworkError, string.Empty);
    }

    private static AmazonProductMetadataFailureKind ClassifyFetchFailure(AmazonProductFetchResult fetchResult) =>
        fetchResult.StatusCode switch
        {
            HttpStatusCode.NotFound => AmazonProductMetadataFailureKind.NotFound,
            _ => AmazonProductMetadataFailureKind.NetworkError
        };

    private static bool ContainsBlockMarker(string content) =>
        content.Contains("captcha", StringComparison.OrdinalIgnoreCase) ||
        content.Contains("unusual traffic", StringComparison.OrdinalIgnoreCase) ||
        content.Contains("automated access to Amazon data", StringComparison.OrdinalIgnoreCase) ||
        content.Contains("ロボットではありません", StringComparison.Ordinal);

    private static bool LooksLikeProductPage(string content) =>
        content.Contains("id=\"productTitle\"", StringComparison.OrdinalIgnoreCase) ||
        content.Contains("id='productTitle'", StringComparison.OrdinalIgnoreCase) ||
        content.Contains("id=\"dp\"", StringComparison.OrdinalIgnoreCase) ||
        content.Contains("id='dp'", StringComparison.OrdinalIgnoreCase) ||
        content.Contains("data-asin=", StringComparison.OrdinalIgnoreCase);

    private static TimeSpan GetFailureTtl(AmazonProductMetadataFailureKind? failureKind) =>
        failureKind switch
        {
            AmazonProductMetadataFailureKind.NotFound => NotFoundTtl,
            AmazonProductMetadataFailureKind.ParseMiss => ParseMissTtl,
            _ => BlockedNetworkAndUnexpectedResponseTtl
        };

    private sealed record AmazonProductMetadataResolution(
        AmazonProductMetadata? Metadata,
        AmazonProductMetadataFailureKind FailureKind,
        string ErrorSummary);
}
