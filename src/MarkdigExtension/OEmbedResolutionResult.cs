namespace BlogGenerator.MarkdigExtension;

internal sealed class OEmbedResolutionResult
{
    public required string HtmlContent { get; init; }

    public required bool IsSuccess { get; init; }

    public string ErrorSummary { get; init; } = string.Empty;
}
