namespace BlogGenerator.MarkdigExtension;

public sealed class OEmbedFetchResult
{
    private OEmbedFetchResult(bool isSuccess, string content, string? mediaType, string effectiveUrl, Exception? error)
    {
        IsSuccess = isSuccess;
        Content = content;
        MediaType = mediaType;
        EffectiveUrl = effectiveUrl;
        Error = error;
    }

    public bool IsSuccess { get; }

    public string Content { get; }

    public string? MediaType { get; }

    public string EffectiveUrl { get; }

    public Exception? Error { get; }

    public static OEmbedFetchResult Success(string content, string? mediaType, string effectiveUrl) =>
        new(true, content, mediaType, effectiveUrl, null);

    public static OEmbedFetchResult Failure(string effectiveUrl, Exception? error) =>
        new(false, string.Empty, null, effectiveUrl, error);
}
