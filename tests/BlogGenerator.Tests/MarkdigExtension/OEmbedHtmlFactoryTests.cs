using BlogGenerator.MarkdigExtension;
using BlogGenerator.MarkdigExtension.Models;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class OEmbedHtmlFactoryTests
{
    [Test]
    public void containerラッパーは動画だけ追加classを付ける()
    {
        var normal = OEmbedHtmlFactory.WrapInContainer("<a>link</a>");
        var video = OEmbedHtmlFactory.WrapInContainer("<iframe></iframe>", isVideo: true);

        Assert.Multiple(() =>
        {
            Assert.That(normal, Is.EqualTo("<div class=\"oembed-container\"><a>link</a></div>"));
            Assert.That(video, Is.EqualTo("<div class=\"oembed-container oembed-video\"><iframe></iframe></div>"));
        });
    }

    [Test]
    public void 標準リンクHTMLを生成できる()
    {
        const string url = "https://example.com/post";

        var html = OEmbedHtmlFactory.CreateStandardLink(url);

        Assert.That(html, Is.EqualTo("<a href=\"https://example.com/post\" rel=\"noopener noreferrer\" target=\"_blank\">https://example.com/post</a>"));
    }

    [Test]
    public void 標準リンクは危険なschemeをhrefへ出力しない()
    {
        const string url = "javascript:alert(1)";

        var html = OEmbedHtmlFactory.CreateStandardLink(url);

        Assert.Multiple(() =>
        {
            Assert.That(html, Is.EqualTo("<span>javascript:alert(1)</span>"));
            Assert.That(html, Does.Not.Contain("href="));
        });
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
            Title = "Document title",
            OgTitle = "OG title",
            OgDescription = "Example description",
            OgImage = "https://example.com/image.png",
            OgSiteName = "Example site"
        };

        var html = OEmbedHtmlFactory.CreateOgpCard(url, metaData);

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("bcard-wrapper"));
            Assert.That(html, Does.Contain("OG title"));
            Assert.That(html, Does.Not.Contain("Document title"));
            Assert.That(html, Does.Contain("Example description"));
            Assert.That(html, Does.Contain("https://example.com/image.png"));
            Assert.That(html, Does.Contain("https://b.hatena.ne.jp/entry/s/example.com/post"));
            Assert.That(html, Does.Contain("rel=\"nofollow noopener noreferrer\""));
        });
    }

    [Test]
    public void ogpカードは外部由来文字列をエスケープする()
    {
        const string url = "https://example.com/post";
        var metaData = new SiteMetaData
        {
            Title = "<script>alert(1)</script>",
            OgDescription = "<b>description</b>",
            OgImage = "javascript:alert(2)",
            OgSiteName = "<unsafe site>",
            OgUrl = "javascript:alert(3)"
        };

        var html = OEmbedHtmlFactory.CreateOgpCard(url, metaData);

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("&lt;script&gt;alert(1)&lt;/script&gt;"));
            Assert.That(html, Does.Not.Contain("<script>alert(1)</script>"));
            Assert.That(html, Does.Contain("&lt;b&gt;description&lt;/b&gt;"));
            Assert.That(html, Does.Not.Contain("javascript:alert(2)"));
            Assert.That(html, Does.Not.Contain("javascript:alert(3)"));
            Assert.That(html, Does.Contain("&lt;unsafe site&gt;"));
        });
    }
}
