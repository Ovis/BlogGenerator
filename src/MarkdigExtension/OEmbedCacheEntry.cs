namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// oEmbed解決結果のTTL付きキャッシュエントリ
/// </summary>
public sealed class OEmbedCacheEntry
{
    /// <summary>
    /// provider由来HTML、標準リンク、失敗時フォールバックなどテーマ非依存のHTML
    /// </summary>
    public string HtmlContent { get; init; } = string.Empty;

    /// <summary>
    /// OGP fallbackカードの表示モデル
    /// </summary>
    /// <remarks>
    /// 最終HTMLではなくモデルを保存し、テーマ変更時にキャッシュを消さなくても再描画できるようにする
    /// </remarks>
    public OgpCardModel? OgpCard { get; init; }

    public OEmbedCacheEntryStatus Status { get; init; }

    public DateTimeOffset? LastSuccessAt { get; init; }

    public DateTimeOffset? FreshUntil { get; init; }

    public DateTimeOffset? LastFailureAt { get; init; }

    public DateTimeOffset? NextRetryAt { get; init; }

    public string ErrorSummary { get; init; } = string.Empty;

    public bool IsFresh(DateTimeOffset now) =>
        Status == OEmbedCacheEntryStatus.Success &&
        FreshUntil.HasValue &&
        now <= FreshUntil.Value;

    public bool ShouldSkipRetry(DateTimeOffset now) =>
        NextRetryAt.HasValue && now < NextRetryAt.Value;

    /// <summary>
    /// テンプレート化以前に保存されたOGPカードHTMLか判定する
    /// </summary>
    public bool IsLegacyOgpHtml =>
        OgpCard is null &&
        HtmlContent.Contains("bcard-wrapper", StringComparison.Ordinal);

    public static OEmbedCacheEntry CreateSuccess(string htmlContent, DateTimeOffset now, TimeSpan successTtl) =>
        new()
        {
            HtmlContent = htmlContent,
            Status = OEmbedCacheEntryStatus.Success,
            LastSuccessAt = now,
            FreshUntil = now.Add(successTtl)
        };

    /// <summary>
    /// OGPカード表示モデルを成功キャッシュとして保存する
    /// </summary>
    public static OEmbedCacheEntry CreateOgpSuccess(OgpCardModel model, DateTimeOffset now, TimeSpan successTtl) =>
        new()
        {
            OgpCard = model,
            Status = OEmbedCacheEntryStatus.Success,
            LastSuccessAt = now,
            FreshUntil = now.Add(successTtl)
        };

    public static OEmbedCacheEntry CreateLegacySuccess(string htmlContent, DateTimeOffset now, TimeSpan successTtl) =>
        CreateSuccess(htmlContent, now, successTtl);

    public static OEmbedCacheEntry CreateFailure(string htmlContent, DateTimeOffset now, TimeSpan failureTtl, string errorSummary) =>
        new()
        {
            HtmlContent = htmlContent,
            Status = OEmbedCacheEntryStatus.Failure,
            LastFailureAt = now,
            NextRetryAt = now.Add(failureTtl),
            ErrorSummary = errorSummary
        };

    public OEmbedCacheEntry MarkRefreshFailure(DateTimeOffset now, TimeSpan failureTtl, string errorSummary) =>
        new()
        {
            HtmlContent = HtmlContent,
            OgpCard = OgpCard,
            Status = Status,
            LastSuccessAt = LastSuccessAt,
            FreshUntil = FreshUntil,
            LastFailureAt = now,
            NextRetryAt = now.Add(failureTtl),
            ErrorSummary = errorSummary
        };
}
