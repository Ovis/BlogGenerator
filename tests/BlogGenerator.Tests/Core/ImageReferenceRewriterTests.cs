using System.Xml.Linq;
using BlogGenerator.Core.Images;
using NUnit.Framework;

namespace BlogGenerator.Tests.Core;

[TestFixture]
public class ImageReferenceRewriterTests
{
    private static readonly Uri Site = new("https://example.test/blog/");
    private static ImageReferenceRewriter Create() => new(new ImageUrlRewriter(Site,
        new Dictionary<string, string> { ["posts/photo.jpg"] = "posts/photo.webp", ["posts/日本語.png"] = "posts/日本語.webp" }));

    [Test]
    public void ローカル参照だけを書き換えて文書の書式とコードを保持する()
    {
        var html = """
            <!doctype html>
            <html><head><meta property="og:image" content="https://example.test/blog/posts/photo.jpg?size=1&amp;x=2#crop"><meta property="og:image:type" content="image/jpeg"></head>
            <body><img src='photo.jpg' alt='photo.jpg'><a href=photo.jpg>original</a>
            <img src="https://outside.test/blog/posts/photo.jpg"><img src="//outside.test/blog/posts/photo.jpg">
            <img src="/blog/posts/%E6%97%A5%E6%9C%AC%E8%AA%9E.png#part">
            <pre>&lt;img src="photo.jpg"&gt;</pre><script>const sample = '<img src="photo.jpg">';</script>
            </body></html>
            """;
        var updated = Create().RewriteHtml(html, new Uri(Site, "posts/page.html"));

        Assert.That(updated, Is.EqualTo(html
            .Replace("content=\"https://example.test/blog/posts/photo.jpg", "content=\"https://example.test/blog/posts/photo.webp")
            .Replace("content=\"image/jpeg\"", "content=\"image/webp\"")
            .Replace("src='photo.jpg'", "src='photo.webp'")
            .Replace("href=photo.jpg", "href=\"photo.webp\"")
            .Replace("%E6%97%A5%E6%9C%AC%E8%AA%9E.png", "%E6%97%A5%E6%9C%AC%E8%AA%9E.webp")));
    }

    [Test]
    public void SrcsetとCSSの参照を更新する()
    {
        var html = """
            <img srcset="photo.jpg 1x, /blog/posts/日本語.png 2x, data:image/png;base64,AAAA 3x">
            <source srcset="photo.jpg 800w"><video poster="photo.jpg"></video>
            <div style="background:url('photo.jpg?x=1&amp;y=2')"></div>
            <style>.hero { background: url(/blog/posts/photo.jpg); }</style>
            """;
        var updated = Create().RewriteHtml(html, new Uri(Site, "posts/page.html"));

        Assert.Multiple(() =>
        {
            Assert.That(updated, Does.Contain("photo.webp 1x, /blog/posts/日本語.webp 2x, data:image/png;base64,AAAA 3x"));
            Assert.That(updated, Does.Contain("srcset=\"photo.webp 800w\""));
            Assert.That(updated, Does.Contain("poster=\"photo.webp\""));
            Assert.That(updated, Does.Contain("url(&#39;photo.webp?x=1&amp;y=2&#39;)"));
            Assert.That(updated, Does.Contain("url(/blog/posts/photo.webp)"));
        });
        Assert.That(Create().RewriteCss("url('../posts/photo.jpg#x')", new Uri(Site, "css/site.css")),
            Is.EqualTo("url('../posts/photo.webp#x')"));
    }

    [Test]
    public void Base要素を考慮し型指定とDownload属性のある参照は保持する()
    {
        var html = """
            <base href="/blog/posts/"><img src="photo.jpg">
            <source type="image/jpeg" srcset="photo.jpg 1x"><a download="original.jpg" href="photo.jpg">download</a>
            """;
        Assert.That(Create().RewriteHtml(html, new Uri(Site, "index.html")),
            Is.EqualTo(html.Replace("<img src=\"photo.jpg\">", "<img src=\"photo.webp\">")));
        var external = "<base href=\"https://outside.test/blog/posts/\"><img src=\"photo.jpg\">";
        Assert.That(Create().RewriteHtml(external, Site), Is.EqualTo(external));
    }

    [Test]
    public void RSSとAtomのHTML本文を更新してテキストを保持する()
    {
        var rss = """
            <?xml version="1.0" encoding="utf-8"?>
            <rss><channel><item><link>https://example.test/blog/posts/page.html</link><description>&lt;img src="photo.jpg"&gt;</description><title>photo.jpg</title></item></channel></rss>
            """;
        var updated = XDocument.Parse(Create().RewriteFeed(rss, new Uri(Site, "feed.rss")));
        Assert.That(updated.Descendants("description").Single().Value, Is.EqualTo("<img src=\"photo.webp\">"));
        Assert.That(updated.Descendants("title").Single().Value, Is.EqualTo("photo.jpg"));

        var atom = """
            <feed xmlns="http://www.w3.org/2005/Atom" xml:base="https://example.test/blog/posts/">
            <entry><link rel="alternate" href="page.html"/><content type="html">&lt;img src="photo.jpg"&gt;</content></entry>
            </feed>
            """;
        var content = XDocument.Parse(Create().RewriteFeed(atom, new Uri(Site, "feed.atom")))
            .Descendants(XName.Get("content", "http://www.w3.org/2005/Atom")).Single();
        Assert.That(content.Value, Is.EqualTo("<img src=\"photo.webp\">"));
        var textAtom = atom.Replace("type=\"html\"", "type=\"text\"");
        var updatedTextAtom = XDocument.Parse(Create().RewriteFeed(textAtom, new Uri(Site, "feed.atom")))
            .Descendants(XName.Get("content", "http://www.w3.org/2005/Atom")).Single();
        Assert.That(updatedTextAtom.Value, Is.EqualTo("<img src=\"photo.webp\">"));
        Assert.That((string?)updatedTextAtom.Attribute("type"), Is.EqualTo("text"));
    }

    [TestCase("photo.jpg?download=1#crop", "photo.webp?download=1#crop")]
    [TestCase("/blog/posts/photo.jpg", "/blog/posts/photo.webp")]
    [TestCase("../posts/photo.jpg", "../posts/photo.webp")]
    [TestCase("//example.test/blog/posts/photo.jpg", "//example.test/blog/posts/photo.webp")]
    [TestCase("https://outside.test/blog/posts/photo.jpg", "https://outside.test/blog/posts/photo.jpg")]
    [TestCase("/posts/photo.jpg", "/posts/photo.jpg")]
    [TestCase("data:image/jpeg;base64,AAAA", "data:image/jpeg;base64,AAAA")]
    public void URLの解決とサフィックスを維持する(string original, string expected)
    {
        var urls = new ImageUrlRewriter(Site, new Dictionary<string, string> { ["posts/photo.jpg"] = "posts/photo.webp" });
        Assert.That(urls.Rewrite(original, new Uri(Site, "posts/page.html")), Is.EqualTo(expected));
    }

    [Test]
    public void 末尾スラッシュのないサイトURLもディレクトリとして扱う()
    {
        var urls = new ImageUrlRewriter(new Uri("https://example.test/blog"),
            new Dictionary<string, string> { ["posts/photo.jpg"] = "posts/photo.webp" });
        var documentUrl = urls.DocumentUrl("posts/page.html");
        Assert.That(documentUrl.AbsoluteUri, Is.EqualTo("https://example.test/blog/posts/page.html"));
        Assert.That(urls.Rewrite("photo.jpg", documentUrl), Is.EqualTo("photo.webp"));
    }
}
