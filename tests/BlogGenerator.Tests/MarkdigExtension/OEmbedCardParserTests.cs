using System.Net.Http;
using BlogGenerator.MarkdigExtension;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class OEmbedCardParserTests
{
    [Test]
    public void Parse段階ではHTTP解決せずoEmbedInlineを生成する()
    {
        var pipeline = new MarkdownPipelineBuilder()
            .Use(new ParserOnlyOEmbedExtension(new OEmbedResolver(new OEmbedProviderCatalog([]), new HttpClient(new ThrowIfCalledHandler()))))
            .Build();

        var document = Markdown.Parse("""[oembed:"https://example.com/post"]""", pipeline);
        var oEmbedInline = document.Descendants<OEmbedInline>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(oEmbedInline.Url, Is.EqualTo("https://example.com/post"));
            Assert.That(oEmbedInline.HtmlContent, Is.EqualTo(string.Empty));
        });
    }

    [Test]
    public void 通常リンクの後続にあるoEmbedだけを解析できる()
    {
        var pipeline = new MarkdownPipelineBuilder()
            .Use(new ParserOnlyOEmbedExtension(new OEmbedResolver(new OEmbedProviderCatalog([]), new HttpClient(new ThrowIfCalledHandler()))))
            .UseAdvancedExtensions()
            .Build();

        var document = Markdown.Parse("""[link](https://example.com) [oembed:"https://example.com/post"]""", pipeline);

        Assert.Multiple(() =>
        {
            Assert.That(document.Descendants<OEmbedInline>().Count(), Is.EqualTo(1));
            Assert.That(document.Descendants<LinkInline>().Count(link => !link.IsImage), Is.EqualTo(1));
        });
    }

    private sealed class ParserOnlyOEmbedExtension(OEmbedResolver oEmbedResolver) : IMarkdownExtension
    {
        private readonly OEmbedResolver _oEmbedResolver = oEmbedResolver;

        public void Setup(MarkdownPipelineBuilder pipeline)
        {
            if (!pipeline.InlineParsers.Contains<OEmbedCardParser>())
            {
                pipeline.InlineParsers.Insert(0, new OEmbedCardParser(_oEmbedResolver));
            }
        }

        public void Setup(MarkdownPipeline pipeline, Markdig.Renderers.IMarkdownRenderer renderer)
        {
        }
    }

    private sealed class ThrowIfCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new AssertionException($"HTTP should not be called during parse. URL: {request.RequestUri}");
        }
    }
}
