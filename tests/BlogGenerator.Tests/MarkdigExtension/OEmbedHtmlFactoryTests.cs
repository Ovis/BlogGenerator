using BlogGenerator.MarkdigExtension;
using BlogGenerator.MarkdigExtension.Models;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class OEmbedHtmlFactoryTests
{
    [Test]
    public void paragraphラッパーは動画だけclassを付ける()
    {
        var normal = OEmbedHtmlFactory.WrapInParagraph("<a>link</a>");
        var video = OEmbedHtmlFactory.WrapInParagraph("<iframe></iframe>", isVideo: true);

        Assert.Multiple(() =>
        {
            Assert.That(normal, Is.EqualTo("<p><a>link</a></p>"));
            Assert.That(video, Is.EqualTo("<p class='oembed-video'><iframe></iframe></p>"));
        });
    }

    [Test]
    public void 標準リンクHTMLを生成できる()
    {
        const string url = "https://example.com/post";

        var html = OEmbedHtmlFactory.CreateStandardLink(url);

        Assert.That(html, Is.EqualTo("<a href=\"https://example.com/post\" target=\"_blank\">https://example.com/post</a>"));
    }

    [Test]
    public void gist埋め込みHTMLを生成できる()
    {
        const string url = "https://gist.github.com/ovis/123";

        var html = OEmbedHtmlFactory.CreateGistEmbed(url);

        Assert.That(html, Is.EqualTo("<script src=\"https://gist.github.com/ovis/123.js\"></script>"));
    }

    [Test]
    public void ogpカードHTMLを生成できる()
    {
        const string url = "https://example.com/post";
        var metaData = new SiteMetaData
        {
            Title = "Example title",
            OgDescription = "Example description",
            OgImage = "https://example.com/image.png",
            OgSiteName = "Example site"
        };

        var html = OEmbedHtmlFactory.CreateOgpCard(url, metaData);

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("bcard-wrapper"));
            Assert.That(html, Does.Contain("Example title"));
            Assert.That(html, Does.Contain("Example description"));
            Assert.That(html, Does.Contain("https://example.com/image.png"));
            Assert.That(html, Does.Contain("//b.hatena.ne.jp/entry/s/example.com/post"));
        });
    }
}
