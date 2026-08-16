using System.Collections.Concurrent;
using System.Net.Http;
using BlogGenerator.MarkdigExtension;
using Markdig;
using Markdig.Syntax;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class OEmbedDocumentResolverTests
{
    [Test]
    public async Task 文書内のoEmbedInlineへ解決済みHTMLを設定できる()
    {
        const string targetUrl = "https://example.com/post";
        const string expectedHtml = "<p>cached</p>";
        var cache = new ConcurrentDictionary<string, string>();
        cache[targetUrl] = expectedHtml;
        var resolver = new OEmbedResolver(new OEmbedProviderCatalog([]), new HttpClient(new ThrowIfCalledHandler()), cache);

        var pipeline = new MarkdownPipelineBuilder()
            .Use(new ParserOnlyOEmbedExtension(resolver))
            .Build();

        var document = Markdown.Parse($"""[oembed:"{targetUrl}"]""", pipeline);
        var oEmbedInline = document.Descendants<OEmbedInline>().Single();

        await OEmbedDocumentResolver.ResolveAsync(document, resolver);

        Assert.That(oEmbedInline.HtmlContent, Is.EqualTo(expectedHtml));
    }

    private sealed class ParserOnlyOEmbedExtension(OEmbedResolver oEmbedResolver) : IMarkdownExtension
    {
        private readonly OEmbedResolver _oEmbedResolver = oEmbedResolver;

        public void Setup(MarkdownPipelineBuilder pipeline)
        {
            if (!pipeline.InlineParsers.Contains<OEmbedCardParser>())
            {
                pipeline.InlineParsers.Insert(0, new OEmbedCardParser());
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
            throw new AssertionException($"HTTP should not be called. URL: {request.RequestUri}");
        }
    }
}
