namespace BlogGenerator.Core;

/// <summary>
/// Markdown処理中に発生した外部埋め込み解決の計測結果
/// </summary>
public sealed record ExternalResolutionMetrics(
    long OEmbedCacheHits,
    long OEmbedCacheMisses,
    long OEmbedHttpRequests,
    TimeSpan OEmbedHttpElapsed,
    long AmazonCacheHits,
    long AmazonCacheMisses,
    long AmazonHttpRequests,
    TimeSpan AmazonFetchElapsed);
