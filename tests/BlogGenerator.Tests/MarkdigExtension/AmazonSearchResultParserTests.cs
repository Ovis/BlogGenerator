using BlogGenerator.MarkdigExtension;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class AmazonSearchResultParserTests
{
    [Test]
    public void 別商品の結果を無視して指定ASINの商品だけ取得する()
    {
        const string html = """
            <div data-component-type="s-search-result" data-asin="B000000000">
              <h2>別商品</h2><img class="s-image" src="https://example.com/wrong.jpg">
            </div>
            <div data-component-type="s-search-result" data-asin="B0CDWSWLWV">
              <h2><a><span> Stream Deck &amp; MK.2 </span></a></h2>
              <img class="s-image" src="https://m.media-amazon.com/images/I/example.jpg">
            </div>
            """;
        var metadata = new AmazonSearchResultParser().Parse(html, "B0CDWSWLWV");
        Assert.Multiple(() =>
        {
            Assert.That(metadata?.Title, Is.EqualTo("Stream Deck & MK.2"));
            Assert.That(metadata?.ImageUrl, Is.EqualTo("https://m.media-amazon.com/images/I/example.jpg"));
        });
    }

    [TestCase("<h2>確認画面</h2>")]
    [TestCase("<div data-component-type='s-search-result' data-asin='B000000000'><h2>別商品</h2><img class='s-image' src='https://example.com/image.jpg'></div>")]
    [TestCase("<div data-component-type='s-search-result' data-asin='B0CDWSWLWV'><h2>商品</h2><img class='s-image' src='data:image/gif;base64,abc'></div>")]
    [TestCase("<div data-component-type='s-search-result' data-asin='B0CDWSWLWV'><h2>商品</h2></div>")]
    public void 一致する商品情報が揃わなければ取得成功としない(string html)
    {
        Assert.That(new AmazonSearchResultParser().Parse(html, "B0CDWSWLWV"), Is.Null);
    }
}
