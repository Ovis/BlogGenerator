namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// Amazon商品メタデータのキャッシュ項目
/// </summary>
public sealed class AmazonProductMetadataCacheEntry
{
    public string Asin { get; init; } = string.Empty;

    public string Marketplace { get; init; } = "amazon.co.jp";

    public string? Title { get; init; }

    public string? ImageUrl { get; init; }

    public AmazonProductMetadataCacheEntryStatus Status { get; init; }

    public DateTimeOffset? FreshUntil { get; init; }

    public DateTimeOffset? NextRetryAt { get; init; }

    public DateTimeOffset? LastSuccessAt { get; init; }

    public DateTimeOffset? LastFailureAt { get; init; }

    public AmazonProductMetadataFailureKind? FailureKind { get; init; }

    public string ErrorSummary { get; init; } = string.Empty;

    public bool IsFresh(DateTimeOffset now) =>
        Status == AmazonProductMetadataCacheEntryStatus.Success &&
        FreshUntil.HasValue &&
        now <= FreshUntil.Value;

    public bool ShouldSkipRetry(DateTimeOffset now) =>
        NextRetryAt.HasValue && now < NextRetryAt.Value;

    public AmazonProductMetadata? ToMetadata() =>
        Status == AmazonProductMetadataCacheEntryStatus.Success && !string.IsNullOrWhiteSpace(Title)
            ? new AmazonProductMetadata(Title, ImageUrl)
            : null;

    public static AmazonProductMetadataCacheEntry CreateSuccess(
        string asin,
        AmazonProductMetadata metadata,
        DateTimeOffset now,
        TimeSpan successTtl) =>
        new()
        {
            Asin = asin,
            Title = metadata.Title,
            ImageUrl = metadata.ImageUrl,
            Status = AmazonProductMetadataCacheEntryStatus.Success,
            FreshUntil = now.Add(successTtl),
            LastSuccessAt = now
        };

    public static AmazonProductMetadataCacheEntry CreateFailure(
        string asin,
        AmazonProductMetadataFailureKind failureKind,
        DateTimeOffset now,
        TimeSpan failureTtl,
        string errorSummary) =>
        new()
        {
            Asin = asin,
            Status = AmazonProductMetadataCacheEntryStatus.Failure,
            LastFailureAt = now,
            NextRetryAt = now.Add(failureTtl),
            FailureKind = failureKind,
            ErrorSummary = errorSummary
        };

    public AmazonProductMetadataCacheEntry MarkRefreshFailure(
        AmazonProductMetadataFailureKind failureKind,
        DateTimeOffset now,
        TimeSpan failureTtl,
        string errorSummary) =>
        new()
        {
            Asin = Asin,
            Marketplace = Marketplace,
            Title = Title,
            ImageUrl = ImageUrl,
            Status = Status,
            FreshUntil = FreshUntil,
            LastSuccessAt = LastSuccessAt,
            LastFailureAt = now,
            NextRetryAt = now.Add(failureTtl),
            FailureKind = failureKind,
            ErrorSummary = errorSummary
        };
}
