using BlogGenerator.MarkdigExtension;
using BlogGenerator.MarkdigExtension.Models;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class OEmbedProviderCatalogTests
{
    [Test]
    public void URLに一致するproviderとendpointを解決できる()
    {
        var catalog = new OEmbedProviderCatalog(
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

        var providerUrl = catalog.FindMatchingProviderUrl("https://www.youtube.com/watch?v=abc123");
        var endpointUrl = catalog.GetProviderEndpointUrl(providerUrl, "https://www.youtube.com/watch?v=abc123");

        Assert.Multiple(() =>
        {
            Assert.That(providerUrl, Is.EqualTo("https://www.youtube.com/"));
            Assert.That(endpointUrl, Is.EqualTo("https://www.youtube.com/oembed"));
        });
    }

    [Test]
    public void 一致しないURLは空文字を返す()
    {
        var catalog = new OEmbedProviderCatalog(
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

        var providerUrl = catalog.FindMatchingProviderUrl("https://example.com/post");
        var endpointUrl = catalog.GetProviderEndpointUrl("https://example.com/", "https://example.com/post");

        Assert.Multiple(() =>
        {
            Assert.That(providerUrl, Is.EqualTo(string.Empty));
            Assert.That(endpointUrl, Is.EqualTo(string.Empty));
        });
    }
}
