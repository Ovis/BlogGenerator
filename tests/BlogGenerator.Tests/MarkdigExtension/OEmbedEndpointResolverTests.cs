using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BlogGenerator.MarkdigExtension;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class OEmbedEndpointResolverTests
{
    [Test]
    public async Task JSONのHTMLレスポンスをそのまま返せる()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.That(request.RequestUri!.ToString(), Is.EqualTo("https://example.com/oembed?url=https%3A%2F%2Fexample.com%2Fpost"));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"type":"video","html":"<iframe></iframe>"}""", Encoding.UTF8, "application/json")
            };
        });

        var resolver = new OEmbedEndpointResolver(new HttpClient(handler));

        var result = await resolver.GetEmbedResultAsync("https://example.com/oembed", "https://example.com/post");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.RichLinkString, Is.EqualTo("<iframe></iframe>"));
            Assert.That(result.IsVideo, Is.True);
            Assert.That(result.Error, Is.Null);
        });
    }

    [Test]
    public async Task URL未指定時はendpointをそのまま使う()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.That(request.RequestUri!.ToString(), Is.EqualTo("https://example.com/oembed"));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"type":"rich","html":"<div>card</div>"}""", Encoding.UTF8, "application/json")
            };
        });

        var resolver = new OEmbedEndpointResolver(new HttpClient(handler));

        var result = await resolver.GetEmbedResultAsync("https://example.com/oembed", string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.RichLinkString, Is.EqualTo("<div>card</div>"));
        });
    }

    [Test]
    public async Task XMLのphotoレスポンスをimg要素へ変換できる()
    {
        const string xml = """
            <oembed>
              <type>photo</type>
              <url>https://example.com/image.png</url>
              <width>640</width>
              <height>480</height>
            </oembed>
            """;

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(xml, Encoding.UTF8, "text/xml")
        });

        var resolver = new OEmbedEndpointResolver(new HttpClient(handler));

        var result = await resolver.GetEmbedResultAsync("https://example.com/oembed", "https://example.com/post");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.RichLinkString, Is.EqualTo("<img src=\"https://example.com/image.png\" width=\"640\" height=\"480\" />"));
            Assert.That(result.IsVideo, Is.False);
        });
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = _responder(request);
            response.RequestMessage = request;
            response.Content.Headers.ContentType ??= new MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        }
    }
}
