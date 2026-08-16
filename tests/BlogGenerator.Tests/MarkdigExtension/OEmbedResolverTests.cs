using System.Collections.Concurrent;
using System.Net;
using System.Text;
using BlogGenerator.MarkdigExtension;
using BlogGenerator.MarkdigExtension.Models;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class OEmbedResolverTests
{
    [Test]
    public async Task キャッシュ済みURLはHTTPアクセスせず返せる()
    {
        const string url = "https://example.com/post";
        const string cachedHtml = "<p>cached</p>";
        var cache = new ConcurrentDictionary<string, OEmbedCacheEntry>();
        cache[url] = OEmbedCacheEntry.CreateSuccess(cachedHtml, DateTimeOffset.UtcNow, TimeSpan.FromDays(180));

        var resolver = new OEmbedResolver(
            new OEmbedProviderCatalog([]),
            new HttpClient(new ThrowIfCalledHandler()),
            cache);

        var html = await resolver.GetOEmbedHtmlAsync(url);

        Assert.That(html, Is.EqualTo(cachedHtml));
    }

    [Test]
    public async Task provider対応URLはoEmbedレスポンスを段落で包んで返せる()
    {
        const string targetUrl = "https://www.youtube.com/watch?v=abc123";
        var providerCatalog = new OEmbedProviderCatalog(
        [
            new OEmbedProviderJson
            {
                ProviderUrl = "https://www.youtube.com/",
                EndPoints =
                [
                    new Endpoint
                    {
                        Url = "https://www.youtube.com/oembed",
                        Schemes = ["https://www.youtube.com/watch*"]
                    }
                ]
            }
        ]);

        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.That(
                request.RequestUri!.ToString(),
                Is.EqualTo("https://www.youtube.com/oembed?url=https%3A%2F%2Fwww.youtube.com%2Fwatch%3Fv%3Dabc123"));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"type":"video","html":"<iframe></iframe>"}""", Encoding.UTF8, "application/json")
            };
        });

        var resolver = new OEmbedResolver(providerCatalog, new HttpClient(handler));

        var html = await resolver.GetOEmbedHtmlAsync(targetUrl);

        Assert.That(html, Is.EqualTo("<div class=\"oembed-container oembed-video\"><iframe></iframe></div>"));
    }

    [Test]
    public async Task OGPだけ取得できるURLはカードHTMLへフォールバックする()
    {
        const string targetUrl = "https://example.com/post";
        const string html = """
            <html>
              <head>
                <title>Document title</title>
                <meta property="og:title" content="OG title" />
                <meta property="og:image" content="https://example.com/image.png" />
                <meta property="og:description" content="Example description" />
                <meta property="og:url" content="https://example.com/post" />
                <meta property="og:site_name" content="Example site" />
              </head>
              <body></body>
            </html>
            """;

        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.That(request.RequestUri!.ToString(), Is.EqualTo(targetUrl));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            };
        });

        var resolver = new OEmbedResolver(new OEmbedProviderCatalog([]), new HttpClient(handler));

        var result = await resolver.GetOEmbedHtmlAsync(targetUrl);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.StartWith("<div class=\"oembed-container\"><div class=\"bcard-wrapper\">"));
            Assert.That(result, Does.Contain("OG title"));
            Assert.That(result, Does.Not.Contain("Document title"));
            Assert.That(result, Does.Contain("Example description"));
            Assert.That(result, Does.Contain("Example site"));
        });
    }

    [Test]
    public async Task og_urlが無くてもog_titleがあればカードHTMLへフォールバックする()
    {
        const string targetUrl = "https://example.com/post";
        const string html = """
            <html>
              <head>
                <title>Document title</title>
                <meta property="og:title" content="OG title" />
                <meta property="og:image" content="https://example.com/image.png" />
                <meta property="og:description" content="Example description" />
                <meta property="og:site_name" content="Example site" />
              </head>
              <body></body>
            </html>
            """;

        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.That(request.RequestUri!.ToString(), Is.EqualTo(targetUrl));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            };
        });

        var resolver = new OEmbedResolver(new OEmbedProviderCatalog([]), new HttpClient(handler));

        var result = await resolver.GetOEmbedHtmlAsync(targetUrl);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.StartWith("<div class=\"oembed-container\"><div class=\"bcard-wrapper\">"));
            Assert.That(result, Does.Contain("href=\"https://example.com/post\""));
            Assert.That(result, Does.Contain("OG title"));
        });
    }

    [Test]
    public async Task discoveryの相対oembedリンクを絶対URLへ解決して取得できる()
    {
        const string targetUrl = "https://example.com/post";
        const string pageHtml = """
            <html>
              <head>
                <title>Example title</title>
                <link type="application/json+oembed" href="/oembed?url=post" />
              </head>
              <body></body>
            </html>
            """;

        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.ToString() == targetUrl)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(pageHtml, Encoding.UTF8, "text/html")
                };
            }

            Assert.That(request.RequestUri!.ToString(), Is.EqualTo("https://example.com/oembed?url=post"));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"type":"rich","html":"<blockquote>embed</blockquote>"}""", Encoding.UTF8, "application/json")
            };
        });

        var resolver = new OEmbedResolver(new OEmbedProviderCatalog([]), new HttpClient(handler));

        var result = await resolver.GetOEmbedHtmlAsync(targetUrl);

        Assert.That(result, Is.EqualTo("<div class=\"oembed-container\"><blockquote>embed</blockquote></div>"));
    }

    [Test]
    public async Task discoveryのbare_endpointには元URLを付与して取得できる()
    {
        const string targetUrl = "https://example.com/post";
        const string pageHtml = """
            <html>
              <head>
                <title>Example title</title>
                <link type="application/json+oembed" href="/oembed" />
              </head>
              <body></body>
            </html>
            """;

        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.ToString() == targetUrl)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(pageHtml, Encoding.UTF8, "text/html")
                };
            }

            Assert.That(
                request.RequestUri!.ToString(),
                Is.EqualTo("https://example.com/oembed?url=https%3A%2F%2Fexample.com%2Fpost"));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"type":"rich","html":"<blockquote>embed</blockquote>"}""", Encoding.UTF8, "application/json")
            };
        });

        var resolver = new OEmbedResolver(new OEmbedProviderCatalog([]), new HttpClient(handler));

        var result = await resolver.GetOEmbedHtmlAsync(targetUrl);

        Assert.That(result, Is.EqualTo("<div class=\"oembed-container\"><blockquote>embed</blockquote></div>"));
    }

    [Test]
    public async Task discovery経由のvideoレスポンスは動画用クラス付きで返せる()
    {
        const string targetUrl = "https://example.com/video";
        const string pageHtml = """
            <html>
              <head>
                <link type="application/json+oembed" href="/oembed" />
              </head>
              <body></body>
            </html>
            """;

        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.ToString() == targetUrl)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(pageHtml, Encoding.UTF8, "text/html")
                };
            }

            Assert.That(
                request.RequestUri!.ToString(),
                Is.EqualTo("https://example.com/oembed?url=https%3A%2F%2Fexample.com%2Fvideo"));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"type":"video","html":"<iframe></iframe>"}""", Encoding.UTF8, "application/json")
            };
        });

        var resolver = new OEmbedResolver(new OEmbedProviderCatalog([]), new HttpClient(handler));

        var result = await resolver.GetOEmbedHtmlAsync(targetUrl);

        Assert.That(result, Is.EqualTo("<div class=\"oembed-container oembed-video\"><iframe></iframe></div>"));
    }

    [Test]
    public async Task gist文字列を含むだけのURLはgist埋め込み扱いしない()
    {
        const string targetUrl = "https://example.com/post?next=gist.github.com/ovis/123";

        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.That(request.RequestUri!.ToString(), Is.EqualTo(targetUrl));

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var resolver = new OEmbedResolver(new OEmbedProviderCatalog([]), new HttpClient(handler));

        var result = await resolver.GetOEmbedHtmlAsync(targetUrl);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("<a href=\"https://example.com/post?next=gist.github.com/ovis/123\""));
            Assert.That(result, Does.Not.Contain("<script src="));
        });
    }

    [Test]
    public async Task wordpress文字列を含むだけのproviderUrlにはforパラメータを付与しない()
    {
        const string targetUrl = "https://example.com/post";
        var providerCatalog = new OEmbedProviderCatalog(
        [
            new OEmbedProviderJson
            {
                ProviderUrl = "https://example.com/wordpress.com/",
                EndPoints =
                [
                    new Endpoint
                    {
                        Url = "https://api.example.com/oembed",
                        Schemes = ["https://example.com/post*"]
                    }
                ]
            }
        ]);

        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.That(
                request.RequestUri!.ToString(),
                Is.EqualTo("https://api.example.com/oembed?url=https%3A%2F%2Fexample.com%2Fpost"));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"type":"rich","html":"<div>card</div>"}""", Encoding.UTF8, "application/json")
            };
        });

        var resolver = new OEmbedResolver(providerCatalog, new HttpClient(handler));

        var result = await resolver.GetOEmbedHtmlAsync(targetUrl);

        Assert.That(result, Is.EqualTo("<div class=\"oembed-container\"><div>card</div></div>"));
    }

    [Test]
    public async Task 期限切れ成功キャッシュは再取得失敗時にstale_if_errorで返せる()
    {
        const string targetUrl = "https://example.com/post";
        const string staleHtml = "<div class=\"oembed-container\"><iframe></iframe></div>";
        var now = new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero);
        var cache = new ConcurrentDictionary<string, OEmbedCacheEntry>();
        cache[targetUrl] = OEmbedCacheEntry.CreateSuccess(staleHtml, now.AddDays(-200), TimeSpan.FromDays(180));

        var resolver = new OEmbedResolver(
            new OEmbedProviderCatalog([]),
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))),
            cache,
            () => now);

        var result = await resolver.GetOEmbedHtmlAsync(targetUrl);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(staleHtml));
            Assert.That(resolver.OEmbedCache[targetUrl].Status, Is.EqualTo(OEmbedCacheEntryStatus.Success));
            Assert.That(resolver.OEmbedCache[targetUrl].NextRetryAt, Is.EqualTo(now.AddHours(6)));
            Assert.That(resolver.OEmbedCache[targetUrl].LastFailureAt, Is.EqualTo(now));
        });
    }

    [Test]
    public async Task 失敗キャッシュの再試行待ち中はHTTPアクセスせず返せる()
    {
        const string targetUrl = "https://example.com/post";
        const string cachedFallback = "<div class=\"oembed-container\"><a href=\"https://example.com/post\">https://example.com/post</a></div>";
        var now = new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero);
        var cache = new ConcurrentDictionary<string, OEmbedCacheEntry>();
        cache[targetUrl] = OEmbedCacheEntry.CreateFailure(cachedFallback, now.AddHours(-1), TimeSpan.FromHours(6), "failed");

        var resolver = new OEmbedResolver(
            new OEmbedProviderCatalog([]),
            new HttpClient(new ThrowIfCalledHandler()),
            cache,
            () => now);

        var result = await resolver.GetOEmbedHtmlAsync(targetUrl);

        Assert.That(result, Is.EqualTo(cachedFallback));
    }

    private sealed class ThrowIfCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new AssertionException($"HTTP should not be called. URL: {request.RequestUri}");
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
