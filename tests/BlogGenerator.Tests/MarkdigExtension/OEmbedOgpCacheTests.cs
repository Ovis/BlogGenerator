using System.Collections.Concurrent;
using System.Net;
using System.Text;
using BlogGenerator.MarkdigExtension;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class OEmbedOgpCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task OGPモデルキャッシュはHTTPアクセスせず現在のテンプレートで再描画する()
    {
        const string url = "https://example.com/post";
        var cache = new ConcurrentDictionary<string, OEmbedCacheEntry>();
        cache[url] = OEmbedCacheEntry.CreateOgpSuccess(
            CreateModel("キャッシュ済みタイトル"),
            Now,
            TimeSpan.FromDays(180));
        var renderer = new RecordingRenderer();
        var resolver = new OEmbedResolver(
            new OEmbedProviderCatalog([]),
            new HttpClient(new ThrowIfCalledHandler()),
            cache,
            () => Now,
            renderer);

        var html = await resolver.GetOEmbedHtmlAsync(url);

        Assert.Multiple(() =>
        {
            Assert.That(html, Is.EqualTo("<div class=\"oembed-container\"><span class=\"rendered-ogp\">キャッシュ済みタイトル</span></div>"));
            Assert.That(renderer.CallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task 旧bcardキャッシュは再取得してモデルキャッシュへ移行する()
    {
        const string url = "https://example.com/post";
        const string pageHtml = """
            <html>
              <head>
                <meta property="og:title" content="新しいタイトル" />
                <meta property="og:site_name" content="Example" />
              </head>
            </html>
            """;
        var cache = new ConcurrentDictionary<string, OEmbedCacheEntry>();
        cache[url] = OEmbedCacheEntry.CreateSuccess(
            "<div class=\"oembed-container\"><div class=\"bcard-wrapper\">legacy</div></div>",
            Now,
            TimeSpan.FromDays(180));
        var handler = new CountingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(pageHtml, Encoding.UTF8, "text/html")
        });
        var renderer = new RecordingRenderer();
        var resolver = new OEmbedResolver(
            new OEmbedProviderCatalog([]),
            new HttpClient(handler),
            cache,
            () => Now,
            renderer);

        var html = await resolver.GetOEmbedHtmlAsync(url);

        Assert.Multiple(() =>
        {
            Assert.That(handler.CallCount, Is.EqualTo(1));
            Assert.That(html, Does.Contain("新しいタイトル"));
            Assert.That(cache[url].OgpCard, Is.Not.Null);
            Assert.That(cache[url].HtmlContent, Is.Empty);
        });
    }

    private static OgpCardModel CreateModel(string title) => new(
        "https://example.com/post",
        "example.com/post",
        title,
        string.Empty,
        "Example",
        null,
        "https://example.com/favicon.ico");

    private sealed class RecordingRenderer : IOgpCardTemplateRenderer
    {
        public int CallCount { get; private set; }

        public Task<string> RenderAsync(OgpCardModel model)
        {
            CallCount++;
            return Task.FromResult($"<span class=\"rendered-ogp\">{model.Title}</span>");
        }
    }

    private sealed class ThrowIfCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new AssertionException($"HTTP should not be called. URL: {request.RequestUri}");
    }

    private sealed class CountingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response = response;

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            _response.RequestMessage = request;
            return Task.FromResult(_response);
        }
    }
}
