using System.Collections.Concurrent;
using BlogGenerator.Core.Interfaces;
using BlogGenerator.MarkdigExtension;
using BlogGenerator.Models;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Renderers;
using Markdig.Syntax;
using YamlDotNet.Serialization;

namespace BlogGenerator.Core;

public class MarkdownProcessor : IMarkdownProcessor
{
    private readonly SiteOption _siteOption;
    private readonly string _oEmbedDir;
    private readonly bool _usesDefaultOEmbedResolver;
    private readonly OEmbedCardParser _oEmbedParser;
    private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);
    private readonly MarkdownPipeline _frontMatterPipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .Build();

    private OEmbedResolver _oEmbedResolver;
    private MarkdownPipeline? _contentPipeline;
    private bool _isInitialized;

    public MarkdownProcessor(
        SiteOption siteOption,
        string oEmbedDir,
        OEmbedResolver? oEmbedResolver = null,
        OEmbedCardParser? oEmbedParser = null)
    {
        _siteOption = siteOption;
        _oEmbedDir = oEmbedDir;
        _usesDefaultOEmbedResolver = oEmbedResolver is null;
        _oEmbedResolver = oEmbedResolver ?? CreateDefaultResolver();
        _oEmbedParser = oEmbedParser ?? new OEmbedCardParser();
    }

    public ConcurrentDictionary<string, string> OEmbedCache => _oEmbedResolver.OEmbedCache;

    public async Task InitializeAsync()
    {
        await EnsureInitializedAsync();
    }

    public async Task<List<Article>> ProcessMarkdownFilesAsync(string inputDir, string outputDir, string baseAbsolutePath)
    {
        await EnsureInitializedAsync();

        var filePaths = Directory.GetFiles(inputDir, "*", SearchOption.AllDirectories)
            .Where(filePath => string.Equals(Path.GetExtension(filePath), ".md", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var articles = await Task.WhenAll(filePaths.Select(filePath => ProcessMarkdownFileAsync(inputDir, outputDir, filePath, baseAbsolutePath)));
        return articles.OrderByDescending(x => x.Published).ToList();
    }

    private async Task<Article> ProcessMarkdownFileAsync(string inputDir, string outputDir, string filePath, string baseAbsolutePath)
    {
        var relativePathExcludeFileName = Path.GetRelativePath(inputDir, Path.GetDirectoryName(filePath)!).Replace("\\", "/");
        relativePathExcludeFileName = relativePathExcludeFileName == "." ? string.Empty : relativePathExcludeFileName;

        var routeRelativePath = PageModelBase.CombineUrlPath(baseAbsolutePath, relativePathExcludeFileName);

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
        _contentPipeline!.Setup(renderer);

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

        var markdownDocument = Markdown.Parse(markdownContent, _contentPipeline!);

        // 画像パスを置換
        foreach (var link in markdownDocument.Descendants<Markdig.Syntax.Inlines.LinkInline>())
        {
            if (link.IsImage)
            {
                if (!IsExternalUrl(link.Url!))
                {
                    // SiteOptionのBaseUrlを使って、画像の相対パスを絶対パスに変換
                    link.Url = PageModelBase.CombineUrlPath(basePath, link.Url!);
                }
            }
        }

        await OEmbedDocumentResolver.ResolveAsync(markdownDocument, _oEmbedResolver);

        writer.GetStringBuilder().Clear();
        renderer.Render(markdownDocument);
        writer.Flush();

        var html = writer.ToString();

        return (html, frontMatter);
    }

    private static bool IsExternalUrl(string url)
    {
        return url.StartsWith("/", StringComparison.Ordinal)
            || url.StartsWith("//", StringComparison.Ordinal)
            || Uri.TryCreate(url, UriKind.Absolute, out _);
    }

    private async Task EnsureInitializedAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        await _initializationSemaphore.WaitAsync();
        try
        {
            if (_isInitialized)
            {
                return;
            }

            if (_usesDefaultOEmbedResolver)
            {
                var httpClient = CreateOEmbedHttpClient();
                var providerCatalog = await new OEmbedProviderCatalogLoader(httpClient).LoadAsync();
                _oEmbedResolver = new OEmbedResolver(providerCatalog, httpClient, _oEmbedResolver.OEmbedCache);
            }

            if (!string.IsNullOrEmpty(_oEmbedDir))
            {
                await OEmbedCacheStore.LoadAsync(_oEmbedDir, _oEmbedResolver.OEmbedCache);
            }

            _contentPipeline = new MarkdownPipelineBuilder()
                .UseYamlFrontMatter()
                .Use(new AmazonAssociateExtension(_siteOption.AmazonAssociateTag))
                .Use(new OEmbedCardExtension(_oEmbedParser))
                .UseAdvancedExtensions()
                .Build();

            _isInitialized = true;
        }
        finally
        {
            _initializationSemaphore.Release();
        }
    }

    private static OEmbedResolver CreateDefaultResolver()
    {
        var httpClient = CreateOEmbedHttpClient();
        return new OEmbedResolver(new OEmbedProviderCatalog([]), httpClient);
    }

    private static HttpClient CreateOEmbedHttpClient()
    {
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BlogGenerator");
        return httpClient;
    }
}
