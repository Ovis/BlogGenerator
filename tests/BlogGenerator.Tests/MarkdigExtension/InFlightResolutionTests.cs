using System.Net;
using System.Text;
using BlogGenerator.MarkdigExtension;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class InFlightResolutionTests
{
    [Test]
    public async Task 同一ASINの同時解決はAmazon商品ページを1回だけ取得する()
    {
        var fetcher = new BlockingAmazonProductPageFetcher();
        var resolver = new AmazonProductMetadataResolver(fetcher, new AmazonProductPageParser());

        var first = resolver.ResolveAsync("B0ABC12345");
        await fetcher.RequestStarted.Task;

        // 1件目のHTTP取得が完了していない間に、同じASINをもう一度解決する
        var second = resolver.ResolveAsync("b0abc12345");

        Assert.That(fetcher.CallCount, Is.EqualTo(1));

        fetcher.ReleaseRequest.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Multiple(() =>
        {
            Assert.That(fetcher.CallCount, Is.EqualTo(1));
            Assert.That(results[0], Is.EqualTo(results[1]));
            Assert.That(results[0]?.Title, Is.EqualTo("商品名"));
        });
    }

    [Test]
    public async Task 同一URLの同時解決はoEmbed外部取得を1回だけ実行する()
    {
        const string targetUrl = "https://example.com/post";
        var handler = new BlockingHttpMessageHandler();
        var resolver = new OEmbedResolver(new OEmbedProviderCatalog([]), new HttpClient(handler));

        var first = resolver.GetOEmbedHtmlAsync(targetUrl).AsTask();
        await handler.RequestStarted.Task;

        // 1件目のHTTP取得が完了していない間に、同じURLをもう一度解決する
        var second = resolver.GetOEmbedHtmlAsync(targetUrl).AsTask();

        Assert.That(handler.CallCount, Is.EqualTo(1));

        handler.ReleaseRequest.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Multiple(() =>
        {
            Assert.That(handler.CallCount, Is.EqualTo(1));
            Assert.That(results[0], Is.EqualTo(results[1]));
            Assert.That(results[0], Does.Contain("OG title"));
        });
    }

    /// <summary>
    /// テスト側から解放されるまでAmazon商品ページ取得を完了させないfetcher
    /// </summary>
    private sealed class BlockingAmazonProductPageFetcher : IAmazonProductPageFetcher
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);
        public TaskCompletionSource RequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AmazonProductFetchResult> FetchAsync(string asin)
        {
            Interlocked.Increment(ref _callCount);
            RequestStarted.TrySetResult();
            await ReleaseRequest.Task;

            return AmazonProductFetchResult.Success(
                "<html><body><span id=\"productTitle\">商品名</span></body></html>");
        }
    }

    /// <summary>
    /// テスト側から解放されるまでHTTP応答を返さないhandler
    /// </summary>
    private sealed class BlockingHttpMessageHandler : HttpMessageHandler
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);
        public TaskCompletionSource RequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            RequestStarted.TrySetResult();
            await ReleaseRequest.Task.WaitAsync(cancellationToken);

            const string html = """
                <html>
                  <head>
                    <meta property="og:title" content="OG title" />
                  </head>
                  <body></body>
                </html>
                """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html"),
                RequestMessage = request
            };
        }
    }
}
