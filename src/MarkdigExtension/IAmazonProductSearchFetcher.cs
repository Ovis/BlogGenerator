namespace BlogGenerator.MarkdigExtension;

/// <summary>ASINでAmazonの検索結果ページを取得するサービス。</summary>
public interface IAmazonProductSearchFetcher
{
    Task<AmazonProductFetchResult> FetchSearchAsync(string asin);
}
