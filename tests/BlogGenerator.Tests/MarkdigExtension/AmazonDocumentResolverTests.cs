using BlogGenerator.MarkdigExtension;
using Markdig;
using Markdig.Syntax;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class AmazonDocumentResolverTests
{
    [Test]
    public async Task 手動titleを持つshortcodeはカードモデルとして描画する()
    {
        var document = Markdown.Parse(
            "[amazon:4844339648,title=\"商品名\",image=\"91+IeF0u9eL\"]",
            CreatePipeline());
        var templateRenderer = new StubAmazonCardTemplateRenderer();

        await AmazonDocumentResolver.ResolveAsync(document, templateRenderer, "test-tag");

        var amazonInline = document.Descendants<AmazonInline>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(amazonInline.HtmlContent, Is.EqualTo("<div>商品名</div>"));
            Assert.That(templateRenderer.Model, Is.Not.Null);
            Assert.That(templateRenderer.Model!.ProductUrl, Is.EqualTo("https://www.amazon.co.jp/dp/4844339648/?tag=test-tag"));
            Assert.That(templateRenderer.Model.ImageUrl, Is.EqualTo("https://m.media-amazon.com/images/I/91+IeF0u9eL._SL500_.jpg"));
        });
    }

    [Test]
    public async Task タグ未設定時はテンプレートを描画せず通常リンクへ委ねる()
    {
        var document = Markdown.Parse("[amazon:4844339648,title=\"商品名\"]", CreatePipeline());
        var templateRenderer = new StubAmazonCardTemplateRenderer();

        await AmazonDocumentResolver.ResolveAsync(document, templateRenderer, string.Empty);

        var amazonInline = document.Descendants<AmazonInline>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(amazonInline.HtmlContent, Is.Empty);
            Assert.That(templateRenderer.Model, Is.Null);
        });
    }

    [Test]
    public async Task 不正な画像IDは画像なしカードとして描画する()
    {
        var document = Markdown.Parse("[amazon:4844339648,title=\"商品名\",image=\"https://example.com/image.jpg\"]", CreatePipeline());
        var templateRenderer = new StubAmazonCardTemplateRenderer();

        await AmazonDocumentResolver.ResolveAsync(document, templateRenderer, "test-tag");

        Assert.That(templateRenderer.Model!.ImageUrl, Is.Null);
    }

    [Test]
    public async Task 商品名を取得できない場合は通常oEmbed経路用URLを設定する()
    {
        var document = Markdown.Parse("[amazon:4844339648]", CreatePipeline());
        var templateRenderer = new StubAmazonCardTemplateRenderer();
        var metadataResolver = new AmazonProductMetadataResolver(
            new StubAmazonProductPageFetcher(),
            new AmazonProductPageParser());

        await AmazonDocumentResolver.ResolveAsync(document, templateRenderer, "test-tag", metadataResolver);

        var amazonInline = document.Descendants<AmazonInline>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(amazonInline.HtmlContent, Is.Empty);
            Assert.That(amazonInline.OEmbedFallbackUrl, Is.EqualTo("https://www.amazon.co.jp/dp/4844339648/"));
        });
    }

    private static MarkdownPipeline CreatePipeline() =>
        new MarkdownPipelineBuilder()
            .Use(new AmazonAssociateExtension("test-tag"))
            .Build();

    private sealed class StubAmazonCardTemplateRenderer : IAmazonCardTemplateRenderer
    {
        public AmazonCardModel? Model { get; private set; }

        public Task<string> RenderAsync(AmazonCardModel model)
        {
            Model = model;
            return Task.FromResult($"<div>{model.Title}</div>");
        }
    }

    private sealed class StubAmazonProductPageFetcher : IAmazonProductPageFetcher
    {
        public Task<AmazonProductFetchResult> FetchAsync(string asin) =>
            Task.FromResult(AmazonProductFetchResult.Success("<html><body></body></html>"));
    }
}
