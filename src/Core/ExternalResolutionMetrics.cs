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
    TimeSpan AmazonFetchElapsed)
{
    /// <summary>
    /// Amazon商品ページへのHTTP取得回数を返す
    /// </summary>
    /// <remarks>
    /// Amazon resolverはキャッシュmissごとに商品ページを1回取得するため、miss件数とHTTP取得件数は一致する
    /// </remarks>
    public long AmazonHttpRequests => AmazonCacheMisses;
}
