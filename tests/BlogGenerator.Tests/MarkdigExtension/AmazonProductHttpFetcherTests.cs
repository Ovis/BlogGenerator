using System.Net;
using BlogGenerator.MarkdigExtension;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class AmazonProductHttpFetcherTests
{
    [Test]
    public async Task 商品ページを日本語優先のHTMLリクエストとして取得する()
    {
        HttpRequestMessage? capturedRequest = null;
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html></html>")
            };
        }));
        var fetcher = new AmazonProductHttpFetcher(httpClient);

        var result = await fetcher.FetchAsync("B0ABC12345");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(capturedRequest!.RequestUri!.ToString(), Is.EqualTo("https://www.amazon.co.jp/dp/B0ABC12345/"));
            Assert.That(capturedRequest.Headers.AcceptLanguage.ToString(), Does.Contain("ja-JP"));
            Assert.That(capturedRequest.Headers.Accept.ToString(), Does.Contain("text/html"));
            Assert.That(capturedRequest.Headers.UserAgent.ToString(), Does.Contain("BlogGenerator"));
        });
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}
