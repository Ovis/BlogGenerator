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
    private readonly OEmbedCardParser _oEmbedParser;
    private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);
    private readonly SemaphoreSlim _oEmbedProviderSemaphore = new(1, 1);
    private readonly MarkdownPipeline _frontMatterPipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .Build();
    private readonly Func<Task<OEmbedProviderCatalog>> _oEmbedProviderCatalogLoader;
    private readonly IAmazonCardTemplateRenderer? _amazonCardTemplateRenderer;
    private readonly AmazonProductMetadataResolver? _amazonProductMetadataResolver;
    private readonly AmazonProductMetadataCacheSettings? _amazonCacheSettings;

    private OEmbedResolver _oEmbedResolver;
    private MarkdownPipeline? _contentPipeline;
    private bool _isInitialized;
    private bool _oEmbedProviderCatalogLoaded;

    public MarkdownProcessor(
        SiteOption siteOption,
        string oEmbedDir,
        OEmbedResolver? oEmbedResolver = null,
        OEmbedCardParser? oEmbedParser = null,
        Func<Task<OEmbedProviderCatalog>>? oEmbedProviderCatalogLoader = null,
        IAmazonCardTemplateRenderer? amazonCardTemplateRenderer = null,
        AmazonProductMetadataResolver? amazonProductMetadataResolver = null,
        AmazonProductMetadataCacheSettings? amazonCacheSettings = null)
    {
        _siteOption = siteOption;
        _oEmbedDir = oEmbedDir;
        _oEmbedResolver = oEmbedResolver ?? CreateDefaultResolver();
        _oEmbedParser = oEmbedParser ?? new OEmbedCardParser();
        _oEmbedProviderCatalogLoader = oEmbedProviderCatalogLoader ?? LoadDefaultProviderCatalogAsync;
        _amazonCardTemplateRenderer = amazonCardTemplateRenderer;
        _amazonProductMetadataResolver = amazonProductMetadataResolver;
        _amazonCacheSettings = amazonCacheSettings;
        _oEmbedProviderCatalogLoaded = oEmbedResolver is not null;
    }

    public ConcurrentDictionary<string, OEmbedCacheEntry> OEmbedCache => _oEmbedResolver.OEmbedCache;
    public ConcurrentDictionary<string, AmazonProductMetadataCacheEntry> AmazonProductMetadataCache => _amazonProductMetadataResolver?.Cache ?? [];

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

        if (_amazonCardTemplateRenderer is not null && markdownDocument.Descendants<AmazonInline>().Any())
        {
            await AmazonDocumentResolver.ResolveAsync(
                markdownDocument,
                _amazonCardTemplateRenderer,
                _siteOption.AmazonAssociateTag,
                _amazonProductMetadataResolver);
        }

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

        var oEmbedInlines = markdownDocument.Descendants<OEmbedInline>().ToArray();
        var amazonFallbackInlines = markdownDocument.Descendants<AmazonInline>()
            .Where(amazonInline => !string.IsNullOrEmpty(amazonInline.OEmbedFallbackUrl))
            .ToArray();
        if (oEmbedInlines.Length != 0 || amazonFallbackInlines.Length != 0)
        {
            await EnsureOEmbedProviderCatalogLoadedAsync();
            if (oEmbedInlines.Length != 0)
            {
                await OEmbedDocumentResolver.ResolveAsync(markdownDocument, _oEmbedResolver);
            }

            foreach (var amazonInline in amazonFallbackInlines)
            {
                amazonInline.HtmlContent = await _oEmbedResolver.GetOEmbedHtmlAsync(amazonInline.OEmbedFallbackUrl!);
            }
        }

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

            if (!string.IsNullOrEmpty(_oEmbedDir))
            {
                await OEmbedCacheStore.LoadAsync(_oEmbedDir, _oEmbedResolver.OEmbedCache);
            }

            if (_amazonProductMetadataResolver is not null && !string.IsNullOrEmpty(_amazonCacheSettings?.FilePath))
            {
                await AmazonProductMetadataCacheStore.LoadAsync(_amazonCacheSettings.FilePath, _amazonProductMetadataResolver.Cache);
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

    private async Task EnsureOEmbedProviderCatalogLoadedAsync()
    {
        if (_oEmbedProviderCatalogLoaded)
        {
            return;
        }

        await _oEmbedProviderSemaphore.WaitAsync();
        try
        {
            if (_oEmbedProviderCatalogLoaded)
            {
                return;
            }

            var providerCatalog = await _oEmbedProviderCatalogLoader();
            _oEmbedResolver.SetProviderCatalog(providerCatalog);
            _oEmbedProviderCatalogLoaded = true;
        }
        finally
        {
            _oEmbedProviderSemaphore.Release();
        }
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

    private static async Task<OEmbedProviderCatalog> LoadDefaultProviderCatalogAsync()
    {
        var httpClient = CreateOEmbedHttpClient();
        return await new OEmbedProviderCatalogLoader(new OEmbedHttpFetcher(httpClient)).LoadAsync();
    }
}
