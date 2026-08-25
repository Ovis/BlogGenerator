using System.Collections.Concurrent;
using System.Net;
using BlogGenerator.MarkdigExtension;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class AmazonProductMetadataResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task 取得成功時は1年間のキャッシュへ保存する()
    {
        var fetcher = new StubAmazonProductPageFetcher(AmazonProductFetchResult.Success(CreateProductHtml()));
        var cache = new ConcurrentDictionary<string, AmazonProductMetadataCacheEntry>();
        var resolver = CreateResolver(fetcher, cache);

        var metadata = await resolver.ResolveAsync("b0abc12345");

        Assert.Multiple(() =>
        {
            Assert.That(metadata, Is.EqualTo(new AmazonProductMetadata("商品名", "https://m.media-amazon.com/images/I/product.jpg")));
            Assert.That(fetcher.CallCount, Is.EqualTo(1));
            Assert.That(cache["B0ABC12345"].FreshUntil, Is.EqualTo(Now.AddDays(365)));
            Assert.That(cache["B0ABC12345"].Status, Is.EqualTo(AmazonProductMetadataCacheEntryStatus.Success));
        });
    }

    [Test]
    public async Task 有効なキャッシュはHTTP取得せず返す()
    {
        var cachedMetadata = new AmazonProductMetadata("キャッシュ商品", null);
        var cache = new ConcurrentDictionary<string, AmazonProductMetadataCacheEntry>();
        cache["B0ABC12345"] = AmazonProductMetadataCacheEntry.CreateSuccess(
            "B0ABC12345",
            cachedMetadata,
            Now,
            TimeSpan.FromDays(365));
        var fetcher = new StubAmazonProductPageFetcher(AmazonProductFetchResult.Success(CreateProductHtml()));
        var resolver = CreateResolver(fetcher, cache);

        var metadata = await resolver.ResolveAsync("B0ABC12345");

        Assert.Multiple(() =>
        {
            Assert.That(metadata, Is.EqualTo(cachedMetadata));
            Assert.That(fetcher.CallCount, Is.Zero);
        });
    }

    [Test]
    public async Task 期限切れ成功キャッシュは取得失敗時も保持して再試行を抑止する()
    {
        var staleMetadata = new AmazonProductMetadata("古い商品名", "https://m.media-amazon.com/images/I/old.jpg");
        var cache = new ConcurrentDictionary<string, AmazonProductMetadataCacheEntry>();
        cache["B0ABC12345"] = AmazonProductMetadataCacheEntry.CreateSuccess(
            "B0ABC12345",
            staleMetadata,
            Now.AddDays(-366),
            TimeSpan.FromDays(365));
        var fetcher = new StubAmazonProductPageFetcher(AmazonProductFetchResult.Failure(HttpStatusCode.ServiceUnavailable, string.Empty));
        var resolver = CreateResolver(fetcher, cache);

        var metadata = await resolver.ResolveAsync("B0ABC12345");

        Assert.Multiple(() =>
        {
            Assert.That(metadata, Is.EqualTo(staleMetadata));
            Assert.That(cache["B0ABC12345"].Status, Is.EqualTo(AmazonProductMetadataCacheEntryStatus.Success));
            Assert.That(cache["B0ABC12345"].NextRetryAt, Is.EqualTo(Now.AddHours(6)));
            Assert.That(cache["B0ABC12345"].FailureKind, Is.EqualTo(AmazonProductMetadataFailureKind.NetworkError));
        });
    }

    [Test]
    public async Task NotFound応答は30日間の失敗キャッシュへ保存する()
    {
        var fetcher = new StubAmazonProductPageFetcher(AmazonProductFetchResult.Failure(HttpStatusCode.NotFound, string.Empty));
        var cache = new ConcurrentDictionary<string, AmazonProductMetadataCacheEntry>();
        var resolver = CreateResolver(fetcher, cache);

        var metadata = await resolver.ResolveAsync("B0ABC12345");

        Assert.Multiple(() =>
        {
            Assert.That(metadata, Is.Null);
            Assert.That(cache["B0ABC12345"].FailureKind, Is.EqualTo(AmazonProductMetadataFailureKind.NotFound));
            Assert.That(cache["B0ABC12345"].NextRetryAt, Is.EqualTo(Now.AddDays(30)));
        });
    }

    [Test]
    public async Task CAPTCHA応答は6時間のblockedキャッシュへ保存する()
    {
        var fetcher = new StubAmazonProductPageFetcher(AmazonProductFetchResult.Failure(
            HttpStatusCode.ServiceUnavailable,
            "<html>To discuss automated access to Amazon data please contact api-services-support@amazon.com. captcha</html>"));
        var cache = new ConcurrentDictionary<string, AmazonProductMetadataCacheEntry>();
        var resolver = CreateResolver(fetcher, cache);

        var metadata = await resolver.ResolveAsync("B0ABC12345");

        Assert.Multiple(() =>
        {
            Assert.That(metadata, Is.Null);
            Assert.That(cache["B0ABC12345"].FailureKind, Is.EqualTo(AmazonProductMetadataFailureKind.Blocked));
            Assert.That(cache["B0ABC12345"].NextRetryAt, Is.EqualTo(Now.AddHours(6)));
        });
    }

    [Test]
    public async Task 商品名を取得できない応答は7日間のparseMissキャッシュへ保存する()
    {
        var fetcher = new StubAmazonProductPageFetcher(AmazonProductFetchResult.Success("<html><body></body></html>"));
        var cache = new ConcurrentDictionary<string, AmazonProductMetadataCacheEntry>();
        var resolver = CreateResolver(fetcher, cache);

        var metadata = await resolver.ResolveAsync("B0ABC12345");

        Assert.Multiple(() =>
        {
            Assert.That(metadata, Is.Null);
            Assert.That(cache["B0ABC12345"].FailureKind, Is.EqualTo(AmazonProductMetadataFailureKind.ParseMiss));
            Assert.That(cache["B0ABC12345"].NextRetryAt, Is.EqualTo(Now.AddDays(7)));
        });
    }

    private static AmazonProductMetadataResolver CreateResolver(
        IAmazonProductPageFetcher fetcher,
        ConcurrentDictionary<string, AmazonProductMetadataCacheEntry> cache) =>
        new(fetcher, new AmazonProductPageParser(), cache, () => Now);

    private static string CreateProductHtml() =>
        "<html><body><span id=\"productTitle\">商品名</span><img id=\"landingImage\" src=\"https://m.media-amazon.com/images/I/product.jpg\" /></body></html>";

    private sealed class StubAmazonProductPageFetcher(AmazonProductFetchResult result) : IAmazonProductPageFetcher
    {
        private readonly AmazonProductFetchResult _result = result;

        public int CallCount { get; private set; }

        public Task<AmazonProductFetchResult> FetchAsync(string asin)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }
}
