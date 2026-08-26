namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// Amazon商品ページを取得するサービス
/// </summary>
public interface IAmazonProductPageFetcher
{
    Task<AmazonProductFetchResult> FetchAsync(string asin);
}
