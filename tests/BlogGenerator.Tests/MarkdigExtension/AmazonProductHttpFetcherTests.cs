using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using BlogGenerator.MarkdigExtension;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class AmazonProductHttpFetcherTests
{
    [Test]
    public async Task 商品ページを一般的なWindows版Chrome相当のヘッダーで取得する()
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
            Assert.That(capturedRequest.Headers.Accept.ToString(), Does.Contain("application/xhtml+xml"));
            Assert.That(capturedRequest.Headers.UserAgent.ToString(), Does.Contain("Windows NT 10.0"));
            Assert.That(capturedRequest.Headers.UserAgent.ToString(), Does.Contain("Chrome/142.0.0.0"));
        });
    }

    [Test]
    public async Task 連続取得時はリクエスト開始間隔を最低2秒空ける()
    {
        var now = new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);
        var requestedDelays = new List<TimeSpan>();
        var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html></html>")
        }));
        var fetcher = new AmazonProductHttpFetcher(
            httpClient,
            () => now,
            delay =>
            {
                requestedDelays.Add(delay);
                now += delay;
                return Task.CompletedTask;
            });

        await fetcher.FetchAsync("B0ABC12345");
        now += TimeSpan.FromMilliseconds(500);
        await fetcher.FetchAsync("B0ABC12346");

        Assert.That(requestedDelays, Is.EqualTo(new[] { TimeSpan.FromMilliseconds(1500) }));
    }

    [Test]
    public async Task Amazon用HTTPクライアントはgzip圧縮HTMLを展開できる()
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{GetAvailablePort()}/");
        listener.Start();

        var responseTask = RespondWithGzipAsync(listener, "<html><title>商品名</title></html>");
        using var httpClient = AmazonProductHttpFetcher.CreateHttpClient();

        var response = await httpClient.GetStringAsync(listener.Prefixes.Single());
        await responseTask;

        Assert.That(response, Is.EqualTo("<html><title>商品名</title></html>"));
    }

    private static int GetAvailablePort()
    {
        using var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        tcpListener.Start();
        return ((IPEndPoint)tcpListener.LocalEndpoint).Port;
    }

    private static async Task RespondWithGzipAsync(HttpListener listener, string responseBody)
    {
        var context = await listener.GetContextAsync();
        var uncompressed = Encoding.UTF8.GetBytes(responseBody);
        await using var compressedStream = new MemoryStream();
        await using (var gzipStream = new GZipStream(compressedStream, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            await gzipStream.WriteAsync(uncompressed);
        }

        var compressed = compressedStream.ToArray();
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.AddHeader("Content-Encoding", "gzip");
        context.Response.ContentLength64 = compressed.Length;
        await context.Response.OutputStream.WriteAsync(compressed);
        context.Response.Close();
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}
