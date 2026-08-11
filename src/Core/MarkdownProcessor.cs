using BlogGenerator.Core.Interfaces;
using BlogGenerator.MarkdigExtension;
using BlogGenerator.Models;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Renderers;
using Markdig.Syntax;
using YamlDotNet.Serialization;

namespace BlogGenerator.Core;

public class MarkdownProcessor(SiteOption siteOption, string oEmbedDir)
    : IMarkdownProcessor
{
    private readonly MarkdownPipeline _frontMatterPipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .Build();

    private readonly MarkdownPipeline _contentPipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .Use(new AmazonAssociateExtension(siteOption.AmazonAssociateTag))
        .Use<OEmbedCardExtension>()
        .UseAdvancedExtensions()
        .Build();

    public async Task InitializeAsync()
    {
        if (!string.IsNullOrEmpty(oEmbedDir))
        {
            await OEmbedCardExtension.LoadOEmbedCacheAsync(oEmbedDir);
        }
    }

    public async Task<List<Article>> ProcessMarkdownFilesAsync(string inputDir, string outputDir, string baseAbsolutePath)
    {
        var filePaths = Directory.GetFiles(inputDir, "*.md", SearchOption.AllDirectories);
        var articles = await Task.WhenAll(filePaths.Select(filePath => ProcessMarkdownFileAsync(inputDir, outputDir, filePath, baseAbsolutePath)));
        return articles.OrderByDescending(x => x.Published).ToList();
    }

    private async Task<Article> ProcessMarkdownFileAsync(string inputDir, string outputDir, string filePath, string baseAbsolutePath)
    {
        var relativePathExcludeFileName = Path.GetRelativePath(inputDir, Path.GetDirectoryName(filePath)!).Replace("\\", "/");
        relativePathExcludeFileName = relativePathExcludeFileName == "." ? string.Empty : relativePathExcludeFileName;

        var routeRelativePath = Path.Combine(baseAbsolutePath, relativePathExcludeFileName);

        // Markdownファイルの内容を読み込む
        var (html, frontMatter) = await ParseMarkdownWithFrontmatterAsync(filePath, routeRelativePath);

        return new Article(
            FileName: Path.ChangeExtension(Path.GetFileNameWithoutExtension(filePath), ".html"),
            Body: html,
            Title: frontMatter.Title,
            Tags: frontMatter.Tags ?? [],
            Published: frontMatter.Published,
            RelativeDirectoryPath: relativePathExcludeFileName,
            RootRelativeDirectoryPath: routeRelativePath,
            IsFixedPage: frontMatter.IsFixedPage
        );
    }

    private async Task<(string html, Frontmatter frontMatter)> ParseMarkdownWithFrontmatterAsync(string path, string basePath)
    {
        var markdown = File.ReadAllText(path);

        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        _contentPipeline.Setup(renderer);

        var document = Markdown.Parse(markdown, _frontMatterPipeline);
        var yamlBlock = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault();

        var frontMatter = new Frontmatter();
        if (yamlBlock != null)
        {
            var yaml = yamlBlock.Lines.ToString();
            var deserializer = new Deserializer();
            frontMatter = deserializer.Deserialize<Frontmatter>(yaml);
        }

        var markdownContent = markdown;
        if (yamlBlock != null)
        {
            var yamlEndIndex = yamlBlock.Span.End;
            markdownContent = markdown[(yamlEndIndex + 1)..].TrimStart();
        }

        var markdownDocument = Markdown.Parse(markdownContent, _contentPipeline);

        // 画像パスを置換
        foreach (var link in markdownDocument.Descendants<Markdig.Syntax.Inlines.LinkInline>())
        {
            if (link.IsImage)
            {
                // SiteOptionのBaseUrlを使って、画像の相対パスを絶対パスに変換
                link.Url = Path.Combine(basePath, link.Url!).Replace("\\", "/");
            }
        }

        await OEmbedDocumentResolver.ResolveAsync(markdownDocument, OEmbedCardExtension.OEmbedResolver);

        writer.GetStringBuilder().Clear();
        renderer.Render(markdownDocument);
        writer.Flush();

        var html = writer.ToString();

        return (html, frontMatter);
    }
}
