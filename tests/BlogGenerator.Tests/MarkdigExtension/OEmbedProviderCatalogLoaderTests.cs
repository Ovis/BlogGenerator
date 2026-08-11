using System.Net;
using System.Text;
using BlogGenerator.MarkdigExtension;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class OEmbedProviderCatalogLoaderTests
{
    [Test]
    public void JSON文字列からproviderCatalogを構築できる()
    {
        const string json = """
            [
              {
                "provider_name": "YouTube",
                "provider_url": "https://www.youtube.com/",
                "endpoints": [
                  {
                    "url": "https://www.youtube.com/oembed",
                    "schemes": [ "https://www.youtube.com/watch*" ]
                  }
                ]
              }
            ]
            """;

        var loader = new OEmbedProviderCatalogLoader(new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));

        var catalog = loader.Parse(json);

        Assert.Multiple(() =>
        {
            Assert.That(catalog.FindMatchingProviderUrl("https://www.youtube.com/watch?v=abc123"), Is.EqualTo("https://www.youtube.com/"));
            Assert.That(catalog.GetProviderEndpointUrl("https://www.youtube.com/", "https://www.youtube.com/watch?v=abc123"), Is.EqualTo("https://www.youtube.com/oembed"));
        });
    }

    [Test]
    public async Task LoadAsyncはリダイレクト先のproviders_jsonを読める()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.ToString() == "https://oembed.com/providers.json")
            {
                return new HttpResponseMessage(HttpStatusCode.MovedPermanently)
                {
                    Headers =
                    {
                        Location = new Uri("https://example.com/providers.json")
                    }
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    [
                      {
                        "provider_name": "YouTube",
                        "provider_url": "https://www.youtube.com/",
                        "endpoints": [
                          {
                            "url": "https://www.youtube.com/oembed",
                            "schemes": [ "https://www.youtube.com/watch*" ]
                          }
                        ]
                      }
                    ]
                    """, Encoding.UTF8, "application/json")
            };
        });

        var loader = new OEmbedProviderCatalogLoader(new HttpClient(handler));

        var catalog = await loader.LoadAsync();

        Assert.That(catalog.FindMatchingProviderUrl("https://www.youtube.com/watch?v=abc123"), Is.EqualTo("https://www.youtube.com/"));
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
