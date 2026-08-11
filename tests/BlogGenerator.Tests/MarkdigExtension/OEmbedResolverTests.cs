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
        var cache = new ConcurrentDictionary<string, string>();
        cache[url] = cachedHtml;

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

        Assert.That(html, Is.EqualTo("<p class='oembed-video'><iframe></iframe></p>"));
    }

    [Test]
    public async Task OGPだけ取得できるURLはカードHTMLへフォールバックする()
    {
        const string targetUrl = "https://example.com/post";
        const string html = """
            <html>
              <head>
                <title>Example title</title>
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
            Assert.That(result, Does.StartWith("<p><div class=\"bcard-wrapper\">"));
            Assert.That(result, Does.Contain("Example title"));
            Assert.That(result, Does.Contain("Example description"));
            Assert.That(result, Does.Contain("Example site"));
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

        Assert.That(result, Is.EqualTo("<p><blockquote>embed</blockquote></p>"));
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
