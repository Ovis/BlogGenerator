using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BlogGenerator.MarkdigExtension;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class OEmbedHttpFetcherTests
{
    [Test]
    public async Task 相対リダイレクトを追従して本文を取得できる()
    {
        const string requestUrl = "https://example.com/entry";
        const string redirectedUrl = "https://example.com/entries/hello";
        const string html = "<html><body>ok</body></html>";

        var fetcher = new OEmbedHttpFetcher(new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.ToString() == requestUrl)
            {
                return new HttpResponseMessage(HttpStatusCode.MovedPermanently)
                {
                    Headers =
                    {
                        Location = new Uri("/entries/hello", UriKind.Relative)
                    }
                };
            }

            Assert.That(request.RequestUri!.ToString(), Is.EqualTo(redirectedUrl));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            };
        })));

        var result = await fetcher.FetchAsync(requestUrl);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Content, Is.EqualTo(html));
            Assert.That(result.MediaType, Is.EqualTo("text/html"));
            Assert.That(result.EffectiveUrl, Is.EqualTo(redirectedUrl));
            Assert.That(result.Error, Is.Null);
        });
    }

    [Test]
    public async Task HttpRequestExceptionは失敗結果として返す()
    {
        var fetcher = new OEmbedHttpFetcher(new HttpClient(new StubHttpMessageHandler(_ =>
            throw new HttpRequestException("boom"))));

        var result = await fetcher.FetchAsync("https://example.com/entry");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Is.TypeOf<HttpRequestException>());
            Assert.That(result.Content, Is.Empty);
        });
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = _responder(request);
            response.RequestMessage = request;
            response.Content ??= new StringContent(string.Empty, Encoding.UTF8);
            response.Content.Headers.ContentType ??= new MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        }
    }
}
