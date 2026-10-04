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
            "B0ABC12345", cachedMetadata, Now, TimeSpan.FromDays(365));
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
            "B0ABC12345", staleMetadata, Now.AddDays(-366), TimeSpan.FromDays(365));
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

        await resolver.ResolveAsync("B0ABC12345");

        Assert.Multiple(() =>
        {
            Assert.That(cache["B0ABC12345"].FailureKind, Is.EqualTo(AmazonProductMetadataFailureKind.NotFound));
            Assert.That(cache["B0ABC12345"].NextRetryAt, Is.EqualTo(Now.AddDays(30)));
        });
    }

    [TestCase(HttpStatusCode.ServiceUnavailable)]
    [TestCase(HttpStatusCode.OK)]
    public async Task CAPTCHA応答はHTTPステータスに関係なく6時間のblockedキャッシュへ保存する(HttpStatusCode statusCode)
    {
        const string blockedHtml = "<html>To discuss automated access to Amazon data please contact api-services-support@amazon.com. captcha</html>";
        var result = statusCode == HttpStatusCode.OK
            ? AmazonProductFetchResult.Success(blockedHtml)
            : AmazonProductFetchResult.Failure(statusCode, blockedHtml);
        var cache = new ConcurrentDictionary<string, AmazonProductMetadataCacheEntry>();
        var resolver = CreateResolver(new StubAmazonProductPageFetcher(result), cache);

        await resolver.ResolveAsync("B0ABC12345");

        Assert.Multiple(() =>
        {
            Assert.That(cache["B0ABC12345"].FailureKind, Is.EqualTo(AmazonProductMetadataFailureKind.Blocked));
            Assert.That(cache["B0ABC12345"].NextRetryAt, Is.EqualTo(Now.AddHours(6)));
        });
    }

    [Test]
    public async Task 商品ページではない200応答は6時間のunexpectedResponseキャッシュへ保存する()
    {
        var cache = new ConcurrentDictionary<string, AmazonProductMetadataCacheEntry>();
        var resolver = CreateResolver(
            new StubAmazonProductPageFetcher(AmazonProductFetchResult.Success("<html><body>トップページ</body></html>")),
            cache);

        await resolver.ResolveAsync("B0ABC12345");

        Assert.Multiple(() =>
        {
            Assert.That(cache["B0ABC12345"].FailureKind, Is.EqualTo(AmazonProductMetadataFailureKind.UnexpectedResponse));
            Assert.That(cache["B0ABC12345"].NextRetryAt, Is.EqualTo(Now.AddHours(6)));
        });
    }

    [Test]
    public async Task 商品ページだが商品名を取得できない応答は1日のparseMissキャッシュへ保存する()
    {
        var cache = new ConcurrentDictionary<string, AmazonProductMetadataCacheEntry>();
        var resolver = CreateResolver(
            new StubAmazonProductPageFetcher(AmazonProductFetchResult.Success("<html><body><div id=\"dp\"></div></body></html>")),
            cache);

        await resolver.ResolveAsync("B0ABC12345");

        Assert.Multiple(() =>
        {
            Assert.That(cache["B0ABC12345"].FailureKind, Is.EqualTo(AmazonProductMetadataFailureKind.ParseMiss));
            Assert.That(cache["B0ABC12345"].NextRetryAt, Is.EqualTo(Now.AddDays(1)));
        });
    }

    [TestCase("blocked")]
    [TestCase("network")]
    [TestCase("parse")]
    [TestCase("notfound")]
    public async Task 商品ページ取得失敗時は検索結果を成功キャッシュへ保存する(string failure)
    {
        var product = failure switch
        {
            "blocked" => AmazonProductFetchResult.Success("<html>captcha</html>"),
            "network" => AmazonProductFetchResult.Failure(HttpStatusCode.ServiceUnavailable, ""),
            "notfound" => AmazonProductFetchResult.Failure(HttpStatusCode.NotFound, ""),
            _ => AmazonProductFetchResult.Success("<div id='dp'></div>")
        };
        var fetcher = new DualFetcher(product, AmazonProductFetchResult.Success(CreateSearchHtml()));
        var cache = new ConcurrentDictionary<string, AmazonProductMetadataCacheEntry>();
        var resolver = CreateResolver(fetcher, cache);

        var result = await resolver.ResolveAsync("b0abc12345");
        var again = await resolver.ResolveAsync("B0ABC12345");
        Assert.Multiple(() =>
        {
            Assert.That(result?.Title, Is.EqualTo("検索商品名"));
            Assert.That(result?.ImageUrl, Is.EqualTo("https://m.media-amazon.com/images/I/search.jpg"));
            Assert.That(again, Is.EqualTo(result));
            Assert.That(fetcher.PageCalls, Is.EqualTo(1));
            Assert.That(fetcher.SearchCalls, Is.EqualTo(1));
            Assert.That(fetcher.SearchAsin, Is.EqualTo("B0ABC12345"));
            Assert.That(cache["B0ABC12345"].FreshUntil, Is.EqualTo(Now.AddDays(365)));
            Assert.That(cache["B0ABC12345"].Status, Is.EqualTo(AmazonProductMetadataCacheEntryStatus.Success));
            Assert.That(resolver.GetMetrics().HttpRequests, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task 商品ページで取得成功なら検索しない()
    {
        var fetcher = new DualFetcher(AmazonProductFetchResult.Success(CreateProductHtml()),
            AmazonProductFetchResult.Success(CreateSearchHtml()));
        var result = await CreateResolver(fetcher, []).ResolveAsync("B0ABC12345");
        Assert.Multiple(() =>
        {
            Assert.That(result?.Title, Is.EqualTo("商品名"));
            Assert.That(fetcher.SearchCalls, Is.Zero);
        });
    }

    [Test]
    public async Task 有効な成功キャッシュは商品ページも検索も取得しない()
    {
        var cache = new ConcurrentDictionary<string, AmazonProductMetadataCacheEntry>();
        cache["B0ABC12345"] = AmazonProductMetadataCacheEntry.CreateSuccess(
            "B0ABC12345", new("キャッシュ", null), Now, TimeSpan.FromDays(365));
        var fetcher = new DualFetcher(AmazonProductFetchResult.Success(CreateProductHtml()),
            AmazonProductFetchResult.Success(CreateSearchHtml()));
        var result = await CreateResolver(fetcher, cache).ResolveAsync("B0ABC12345");
        Assert.Multiple(() =>
        {
            Assert.That(result?.Title, Is.EqualTo("キャッシュ"));
            Assert.That(fetcher.PageCalls, Is.Zero);
            Assert.That(fetcher.SearchCalls, Is.Zero);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task 両経路失敗でも古い成功情報を保持して両経路の再試行を抑止する(bool searchBlocked)
    {
        var stale = new AmazonProductMetadata("古い商品", "https://example.com/old.jpg");
        var cache = new ConcurrentDictionary<string, AmazonProductMetadataCacheEntry>();
        cache["B0ABC12345"] = AmazonProductMetadataCacheEntry.CreateSuccess(
            "B0ABC12345", stale, Now.AddDays(-366), TimeSpan.FromDays(365));
        var fetcher = new DualFetcher(AmazonProductFetchResult.Success("captcha"),
            AmazonProductFetchResult.Success(searchBlocked ? "captcha" : "<html>検索結果なし</html>"));
        var resolver = CreateResolver(fetcher, cache);

        var result = await resolver.ResolveAsync("B0ABC12345");
        var again = await resolver.ResolveAsync("B0ABC12345");
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(stale));
            Assert.That(again, Is.EqualTo(stale));
            Assert.That(cache["B0ABC12345"].Status, Is.EqualTo(AmazonProductMetadataCacheEntryStatus.Success));
            Assert.That(cache["B0ABC12345"].NextRetryAt, Is.EqualTo(Now.AddHours(6)));
            Assert.That(fetcher.PageCalls, Is.EqualTo(1));
            Assert.That(fetcher.SearchCalls, Is.EqualTo(1));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task 検索結果なしや検索404を商品不存在として保存しない(bool searchNotFound)
    {
        var fetcher = new DualFetcher(AmazonProductFetchResult.Success("captcha"),
            searchNotFound ? AmazonProductFetchResult.Failure(HttpStatusCode.NotFound, "")
                : AmazonProductFetchResult.Success(CreateSearchHtml().Replace("B0ABC12345", "B000000000")));
        var cache = new ConcurrentDictionary<string, AmazonProductMetadataCacheEntry>();
        var resolver = CreateResolver(fetcher, cache);
        var result = await resolver.ResolveAsync("B0ABC12345");
        await resolver.ResolveAsync("B0ABC12345");
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(cache["B0ABC12345"].FailureKind, Is.EqualTo(AmazonProductMetadataFailureKind.Blocked));
            Assert.That(cache["B0ABC12345"].NextRetryAt, Is.EqualTo(Now.AddHours(6)));
            Assert.That(fetcher.PageCalls, Is.EqualTo(1));
            Assert.That(fetcher.SearchCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task 同じASINの並列取得はフォールバックを含めて一度だけ実行する()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetcher = new DualFetcher(AmazonProductFetchResult.Success("captcha"),
            AmazonProductFetchResult.Success(CreateSearchHtml()), gate);
        var resolver = CreateResolver(fetcher, []);
        var first = resolver.ResolveAsync("B0ABC12345");
        var second = resolver.ResolveAsync("B0ABC12345");
        gate.SetResult(true);
        var results = await Task.WhenAll(first, second);
        Assert.Multiple(() =>
        {
            Assert.That(results[0], Is.EqualTo(results[1]));
            Assert.That(results[0]?.Title, Is.EqualTo("検索商品名"));
            Assert.That(fetcher.PageCalls, Is.EqualTo(1));
            Assert.That(fetcher.SearchCalls, Is.EqualTo(1));
        });
    }

    private static string CreateSearchHtml() =>
        "<div data-component-type='s-search-result' data-asin='B0ABC12345'><h2>検索商品名</h2><img class='s-image' src='https://m.media-amazon.com/images/I/search.jpg'></div>";

    private sealed class DualFetcher(AmazonProductFetchResult page, AmazonProductFetchResult search,
        TaskCompletionSource<bool>? gate = null) : IAmazonProductPageFetcher, IAmazonProductSearchFetcher
    {
        public int PageCalls { get; private set; }
        public int SearchCalls { get; private set; }
        public string? SearchAsin { get; private set; }
        public async Task<AmazonProductFetchResult> FetchAsync(string asin)
        {
            PageCalls++;
            if (gate is not null) await gate.Task;
            return page;
        }
        public Task<AmazonProductFetchResult> FetchSearchAsync(string asin)
        {
            SearchCalls++;
            SearchAsin = asin;
            return Task.FromResult(search);
        }
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
