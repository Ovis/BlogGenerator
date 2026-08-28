using BlogGenerator.MarkdigExtension;
using BlogGenerator.Models;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace BlogGenerator.Core;

/// <summary>
/// 1つのMarkdown文書をfrontmatterと本文HTMLへ変換する
/// </summary>
internal sealed class MarkdownContentProcessor(
    SiteOption siteOption,
    FrontMatterParser frontMatterParser,
    MarkdownPipeline frontMatterPipeline,
    MarkdownPipeline contentPipeline,
    IAmazonCardTemplateRenderer? amazonCardTemplateRenderer,
    AmazonProductMetadataResolver? amazonProductMetadataResolver,
    MarkdownEmbedResolver embedResolver)
{
    /// <summary>
    /// Markdownファイルを読み込み、frontmatterと本文HTMLを生成する
    /// </summary>
    /// <param name="path">処理対象のMarkdownファイル</param>
    /// <param name="basePath">相対画像URLの解決基準となる公開URL</param>
    public async Task<(string Html, Frontmatter FrontMatter)> ProcessAsync(string path, string basePath)
    {
        var markdown = File.ReadAllText(path);

        // frontmatter抽出用と本文変換用でパイプラインを分け、本文側の拡張記法を二重評価しない
        var frontMatterDocument = Markdown.Parse(markdown, frontMatterPipeline);
        var yamlBlock = frontMatterDocument.Descendants<YamlFrontMatterBlock>().FirstOrDefault();
        var frontMatter = yamlBlock is null
            ? new Frontmatter()
            : frontMatterParser.Parse(yamlBlock.Lines.ToString(), path);

        var markdownContent = yamlBlock is null
            ? markdown
            : markdown[(yamlBlock.Span.End + 1)..].TrimStart();
        var markdownDocument = Markdown.Parse(markdownContent, contentPipeline);

        await ResolveAmazonCardsAsync(markdownDocument);
        RewriteRelativeImageUrls(markdownDocument, basePath);
        await embedResolver.ResolveAsync(markdownDocument);

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        contentPipeline.Setup(renderer);
        renderer.Render(markdownDocument);
        writer.Flush();

        return (writer.ToString(), frontMatter);
    }

    /// <summary>
    /// Amazon記法を商品カードへ解決する
    /// </summary>
    private async Task ResolveAmazonCardsAsync(MarkdownDocument document)
    {
        if (amazonCardTemplateRenderer is null || !document.Descendants<AmazonInline>().Any())
            return;

        await AmazonDocumentResolver.ResolveAsync(
            document,
            amazonCardTemplateRenderer,
            siteOption.AmazonAssociateTag,
            amazonProductMetadataResolver);
    }

    /// <summary>
    /// 記事からの相対画像URLをサイト上の絶対パスへ変換する
    /// </summary>
    private static void RewriteRelativeImageUrls(MarkdownDocument document, string basePath)
    {
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (link.IsImage && !IsExternalUrl(link.Url!))
                link.Url = PageModelBase.CombineUrlPath(basePath, link.Url!);
        }
    }

    /// <summary>
    /// 記事ディレクトリを基準に再解決してはいけないURLか判定する
    /// </summary>
    private static bool IsExternalUrl(string url) =>
        url.StartsWith("/", StringComparison.Ordinal)
        || url.StartsWith("//", StringComparison.Ordinal)
        || Uri.TryCreate(url, UriKind.Absolute, out _);
}
