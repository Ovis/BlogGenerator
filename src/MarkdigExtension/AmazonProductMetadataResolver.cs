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
    private readonly ConcurrentDictionary<string, Lazy<Task<AmazonProductMetadata?>>> _inFlightResolutions = [];
    private long _cacheHits;
    private long _cacheMisses;
    private long _httpRequests;
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
    internal (long CacheHits, long CacheMisses, long HttpRequests, TimeSpan FetchElapsed) GetMetrics() =>
        (
            Interlocked.Read(ref _cacheHits),
            Interlocked.Read(ref _cacheMisses),
            Interlocked.Read(ref _httpRequests),
            TimeSpan.FromTicks(Interlocked.Read(ref _fetchElapsedTicks)));

    /// <summary>
    /// ASINからカード用メタデータを解決する
    /// </summary>
    public async Task<AmazonProductMetadata?> ResolveAsync(string asin)
    {
        var normalizedAsin = asin.ToUpperInvariant();
        var now = _utcNowProvider();
        if (TryGetCachedMetadata(normalizedAsin, now, out var cachedMetadata))
        {
            Interlocked.Increment(ref _cacheHits);
            return cachedMetadata;
        }

        Interlocked.Increment(ref _cacheMisses);

        // Markdownは複数ファイルを並列処理するため、同じASINが同時に現れると全呼び出しがcache missになり得る
        // ASIN単位で解決Taskを共有し、Amazonへの不要な重複アクセスを防ぐ
        var candidate = new Lazy<Task<AmazonProductMetadata?>>(
            () => ResolveAndCacheAsync(normalizedAsin),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var resolution = _inFlightResolutions.GetOrAdd(normalizedAsin, candidate);

        try
        {
            return await resolution.Value;
        }
        finally
        {
            // 完了したTaskは保持せず、以後は通常のTTLキャッシュ判定へ戻す
            _inFlightResolutions.TryRemove(new KeyValuePair<string, Lazy<Task<AmazonProductMetadata?>>>(normalizedAsin, resolution));
        }
    }

    /// <summary>
    /// キャッシュを再確認したうえでAmazon商品ページを取得し、結果をキャッシュへ保存する
    /// </summary>
    private async Task<AmazonProductMetadata?> ResolveAndCacheAsync(string normalizedAsin)
    {
        var now = _utcNowProvider();

        // 最初のcache missからin-flight登録までの間に別Taskが取得を完了した場合はHTTPアクセスを省略する
        if (TryGetCachedMetadata(normalizedAsin, now, out var cachedMetadata))
        {
            return cachedMetadata;
        }

        Cache.TryGetValue(normalizedAsin, out var cachedEntry);

        Interlocked.Increment(ref _httpRequests);
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

    /// <summary>
    /// TTLまたは再試行抑止期間が有効なキャッシュから商品情報を取得する
    /// </summary>
    private bool TryGetCachedMetadata(string normalizedAsin, DateTimeOffset now, out AmazonProductMetadata? metadata)
    {
        if (Cache.TryGetValue(normalizedAsin, out var cachedEntry) &&
            (cachedEntry.IsFresh(now) || cachedEntry.ShouldSkipRetry(now)))
        {
            metadata = cachedEntry.ToMetadata();
            return true;
        }

        metadata = null;
        return false;
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
