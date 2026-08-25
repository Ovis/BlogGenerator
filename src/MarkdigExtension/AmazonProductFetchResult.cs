using System.Net;

namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// Amazon商品ページ取得の結果
/// </summary>
public sealed class AmazonProductFetchResult
{
    private AmazonProductFetchResult(bool isSuccess, string content, HttpStatusCode? statusCode, Exception? error)
    {
        IsSuccess = isSuccess;
        Content = content;
        StatusCode = statusCode;
        Error = error;
    }

    public bool IsSuccess { get; }

    public string Content { get; }

    public HttpStatusCode? StatusCode { get; }

    public Exception? Error { get; }

    public static AmazonProductFetchResult Success(string content) =>
        new(true, content, HttpStatusCode.OK, null);

    public static AmazonProductFetchResult Failure(HttpStatusCode? statusCode, string content, Exception? error = null) =>
        new(false, content, statusCode, error);
}
