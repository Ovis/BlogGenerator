namespace BlogGenerator.MarkdigExtension;

public sealed class OEmbedCacheEntry
{
    public string HtmlContent { get; init; } = string.Empty;

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

    public static OEmbedCacheEntry CreateSuccess(string htmlContent, DateTimeOffset now, TimeSpan successTtl) =>
        new()
        {
            HtmlContent = htmlContent,
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
            Status = Status,
            LastSuccessAt = LastSuccessAt,
            FreshUntil = FreshUntil,
            LastFailureAt = now,
            NextRetryAt = now.Add(failureTtl),
            ErrorSummary = errorSummary
        };
}
