using BlogGenerator.MarkdigExtension;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class AmazonProductPageParserTests
{
    private readonly AmazonProductPageParser _parser = new();

    [Test]
    public void 商品ページの優先selectorから商品名と高解像度画像を抽出できる()
    {
        const string html = """
            <html><head>
                <meta name="title" content="メタ商品名" />
                <meta property="og:title" content="OGP商品名" />
            </head><body>
                <span id="productTitle">  商品
                  名  </span>
                <img id="landingImage" data-old-hires="https://m.media-amazon.com/images/I/high.jpg" src="https://m.media-amazon.com/images/I/small.jpg" />
            </body></html>
            """;

        var metadata = _parser.Parse(html);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.Title, Is.EqualTo("商品 名"));
            Assert.That(metadata.ImageUrl, Is.EqualTo("https://m.media-amazon.com/images/I/high.jpg"));
        });
    }

    [Test]
    public void dynamicImageから最大面積の画像を選択できる()
    {
        const string html = """
            <html><body>
                <img id="landingImage" data-a-dynamic-image='{"https://m.media-amazon.com/images/I/small.jpg":[120,120],"https://m.media-amazon.com/images/I/large.jpg":[800,600]}' />
            </body></html>
            """;

        var metadata = _parser.Parse(html);

        Assert.That(metadata.ImageUrl, Is.EqualTo("https://m.media-amazon.com/images/I/large.jpg"));
    }

    [Test]
    public void 商品selectorがない時はメタタグとOGP画像へfallbackする()
    {
        const string html = """
            <html><head>
                <meta name="title" content="  メタ商品名  " />
                <meta property="og:image" content="https://m.media-amazon.com/images/I/og.jpg" />
            </head><body></body></html>
            """;

        var metadata = _parser.Parse(html);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.Title, Is.EqualTo("メタ商品名"));
            Assert.That(metadata.ImageUrl, Is.EqualTo("https://m.media-amazon.com/images/I/og.jpg"));
        });
    }

    [Test]
    public void 不正な画像は無視して後続候補を使う()
    {
        const string html = """
            <html><head>
                <meta property="og:image" content="https://m.media-amazon.com/images/I/product.jpg" />
            </head><body>
                <img id="landingImage" data-old-hires="javascript:alert(1)" src="https://m.media-amazon.com/images/I/icon.jpg" />
            </body></html>
            """;

        var metadata = _parser.Parse(html);

        Assert.That(metadata.ImageUrl, Is.EqualTo("https://m.media-amazon.com/images/I/product.jpg"));
    }

    [Test]
    public void titleも画像も抽出できない場合はnullを返す()
    {
        var metadata = _parser.Parse("<html><head><title>   </title></head><body></body></html>");

        Assert.Multiple(() =>
        {
            Assert.That(metadata.Title, Is.Null);
            Assert.That(metadata.ImageUrl, Is.Null);
        });
    }
}
