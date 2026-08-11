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

    [Test]
    public void 正規表現メタ文字を含むschemeでも厳密に一致判定できる()
    {
        var catalog = new OEmbedProviderCatalog(
        [
            new OEmbedProviderJson
            {
                ProviderUrl = "https://video.example.com/",
                EndPoints =
                [
                    new Endpoint
                    {
                        Url = "https://video.example.com/oembed",
                        Schemes = ["https://video.example.com/watch?video=*"]
                    }
                ]
            }
        ]);

        var matchedProviderUrl = catalog.FindMatchingProviderUrl("https://video.example.com/watch?video=abc123");
        var matchedEndpointUrl = catalog.GetProviderEndpointUrl(matchedProviderUrl, "https://video.example.com/watch?video=abc123");
        var falsePositiveProviderUrl = catalog.FindMatchingProviderUrl("https://videoXexampleYcom/watchvideo=abc123");
        var falsePositiveEndpointUrl = catalog.GetProviderEndpointUrl("https://video.example.com/", "https://videoXexampleYcom/watchvideo=abc123");

        Assert.Multiple(() =>
        {
            Assert.That(matchedProviderUrl, Is.EqualTo("https://video.example.com/"));
            Assert.That(matchedEndpointUrl, Is.EqualTo("https://video.example.com/oembed"));
            Assert.That(falsePositiveProviderUrl, Is.EqualTo(string.Empty));
            Assert.That(falsePositiveEndpointUrl, Is.EqualTo(string.Empty));
        });
    }
}
