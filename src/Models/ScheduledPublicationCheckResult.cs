namespace BlogGenerator.Models;

internal sealed record ScheduledPublicationCheckResult(
    DateTimeOffset After,
    DateTimeOffset Until,
    TimeZoneInfo TimeZone,
    IReadOnlyList<ScheduledPublicationItem> Items)
{
    public bool HasScheduled => Items.Count > 0;
    public int Count => Items.Count;
}

internal sealed record ScheduledPublicationItem(string Path, DateTimeOffset Published);

internal sealed record ScheduledPublicationError(string Path, Exception Exception);

internal sealed class ScheduledPublicationCheckException(IReadOnlyList<ScheduledPublicationError> errors)
    : Exception("Scheduled publication check failed.")
{
    public IReadOnlyList<ScheduledPublicationError> Errors { get; } = errors;
}
