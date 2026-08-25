using BlogGenerator.MarkdigExtension;
using Markdig;
using Markdig.Syntax;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class AmazonAssociateParserTests
{
    [Test]
    public void 既存記法をAmazonInlineへ変換できる()
    {
        var document = Markdown.Parse("[amazon:b0abc12345]", CreatePipeline());

        var amazonInline = document.Descendants<AmazonInline>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(amazonInline.Asin, Is.EqualTo("B0ABC12345"));
            Assert.That(amazonInline.ManualTitle, Is.Null);
            Assert.That(amazonInline.ManualImageId, Is.Null);
        });
    }

    [Test]
    public void 手動指定したtitleと画像IDをAmazonInlineへ保持できる()
    {
        var document = Markdown.Parse("[amazon:4844339648,title=\"商品名\",image=\"91+IeF0u9eL\"]", CreatePipeline());

        var amazonInline = document.Descendants<AmazonInline>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(amazonInline.Asin, Is.EqualTo("4844339648"));
            Assert.That(amazonInline.ManualTitle, Is.EqualTo("商品名"));
            Assert.That(amazonInline.ManualImageId, Is.EqualTo("91+IeF0u9eL"));
        });
    }

    [TestCase("[amazon:4844339648,unknown=\"value\"]")]
    [TestCase("[amazon:4844339648,title=\"first\",title=\"second\"]")]
    [TestCase("[amazon:4844339648,title=商品名]")]
    [TestCase("[amazon:too-short]")]
    public void 不正なshortcodeはAmazonInlineとして解釈しない(string markdown)
    {
        var document = Markdown.Parse(markdown, CreatePipeline());

        Assert.That(document.Descendants<AmazonInline>(), Is.Empty);
    }

    [Test]
    public void タグ設定時はアフィリエイトリンクを安全に描画できる()
    {
        var html = Markdown.ToHtml("[amazon:4844339648,title=\"商品 & 名\"]", CreatePipeline("test-tag"));

        Assert.That(
            html,
            Is.EqualTo("<p><a class=\"amazon-link\" href=\"https://www.amazon.co.jp/dp/4844339648/?tag=test-tag\" target=\"_blank\" rel=\"noopener noreferrer\">商品 &amp; 名</a></p>\n"));
    }

    [Test]
    public void タグ未設定時は通常リンクへfallbackする()
    {
        var html = Markdown.ToHtml("[amazon:4844339648]", CreatePipeline());

        Assert.That(
            html,
            Is.EqualTo("<p><a class=\"amazon-link\" href=\"https://www.amazon.co.jp/dp/4844339648/\" target=\"_blank\" rel=\"noopener noreferrer\">Amazonで見る</a></p>\n"));
    }

    private static MarkdownPipeline CreatePipeline(string affiliateId = "") =>
        new MarkdownPipelineBuilder()
            .Use(new AmazonAssociateExtension(affiliateId))
            .Build();
}
