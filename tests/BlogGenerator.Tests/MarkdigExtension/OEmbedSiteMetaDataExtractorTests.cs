using BlogGenerator.MarkdigExtension;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class OEmbedSiteMetaDataExtractorTests
{
    [Test]
    public void HTMLからOGPとoEmbed情報を抽出できる()
    {
        const string html = """
            <html>
              <head>
                <title>Sample title</title>
                <meta property="og:title" content="OG Title" />
                <meta property="og:image" content="https://example.com/image.png" />
                <meta property="og:description" content="OG Description" />
                <meta property="og:type" content="article" />
                <meta property="og:url" content="https://example.com/post" />
                <meta property="og:site_name" content="Example Site" />
                <link type="application/json+oembed" href="https://example.com/oembed.json" />
                <link type="application/xml+oembed" href="https://example.com/oembed.xml" />
              </head>
              <body></body>
            </html>
            """;

        var extractor = new OEmbedSiteMetaDataExtractor(new HttpClient());

        var metaData = extractor.Parse("https://example.com/post", html);

        Assert.Multiple(() =>
        {
            Assert.That(metaData.Url, Is.EqualTo("https://example.com/post"));
            Assert.That(metaData.Title, Is.EqualTo("Sample title"));
            Assert.That(metaData.OgTitle, Is.EqualTo("OG Title"));
            Assert.That(metaData.OgImage, Is.EqualTo("https://example.com/image.png"));
            Assert.That(metaData.OgDescription, Is.EqualTo("OG Description"));
            Assert.That(metaData.OgType, Is.EqualTo("article"));
            Assert.That(metaData.OgUrl, Is.EqualTo("https://example.com/post"));
            Assert.That(metaData.OgSiteName, Is.EqualTo("Example Site"));
            Assert.That(metaData.OembedJson, Is.EqualTo("https://example.com/oembed.json"));
            Assert.That(metaData.OembedXml, Is.EqualTo("https://example.com/oembed.xml"));
        });
    }

    [Test]
    public void XMLのoEmbedリンクはtext_xmlも扱える()
    {
        const string html = """
            <html>
              <head>
                <link type="text/xml+oembed" href="https://example.com/oembed.xml" />
              </head>
              <body></body>
            </html>
            """;

        var extractor = new OEmbedSiteMetaDataExtractor(new HttpClient());

        var metaData = extractor.Parse("https://example.com/post", html);

        Assert.That(metaData.OembedXml, Is.EqualTo("https://example.com/oembed.xml"));
    }

    [Test]
    public void 相対oEmbedリンクは元ページ基準で絶対URL化する()
    {
        const string html = """
            <html>
              <head>
                <link type="application/json+oembed" href="/oembed?url=post" />
                <link type="application/xml+oembed" href="oembed.xml" />
              </head>
              <body></body>
            </html>
            """;

        var extractor = new OEmbedSiteMetaDataExtractor(new HttpClient());

        var metaData = extractor.Parse("https://example.com/posts/hello", html);

        Assert.Multiple(() =>
        {
            Assert.That(metaData.OembedJson, Is.EqualTo("https://example.com/oembed?url=post"));
            Assert.That(metaData.OembedXml, Is.EqualTo("https://example.com/posts/oembed.xml"));
        });
    }

    [Test]
    public void oEmbedエンドポイントはjsonを優先して解決する()
    {
        var metaData = new BlogGenerator.MarkdigExtension.Models.SiteMetaData
        {
            OembedJson = "https://example.com/oembed.json",
            OembedXml = "https://example.com/oembed.xml"
        };

        var endpoint = OEmbedSiteMetaDataExtractor.GetOEmbedEndpoint(metaData);

        Assert.That(endpoint, Is.EqualTo("https://example.com/oembed.json"));
    }
}
