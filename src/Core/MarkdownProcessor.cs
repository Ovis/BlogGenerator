using System.Collections.Concurrent;
using BlogGenerator.Core.Interfaces;
using BlogGenerator.MarkdigExtension;
using BlogGenerator.Models;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Renderers;
using Markdig.Syntax;

namespace BlogGenerator.Core;

public class MarkdownProcessor : IMarkdownProcessor
{
    private readonly SiteOption _siteOption;
    private readonly string _oEmbedDir;
    private readonly FrontMatterParser _frontMatterParser;
    private readonly OEmbedCardParser _oEmbedParser;
    private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);
    private readonly SemaphoreSlim _oEmbedProviderSemaphore = new(1, 1);
    private readonly MarkdownPipeline _frontMatterPipeline = new MarkdownPipelineBuilder().UseYamlFrontMatter().Build();
    private readonly Func<Task<OEmbedProviderCatalog>> _oEmbedProviderCatalogLoader;
    private readonly IAmazonCardTemplateRenderer? _amazonCardTemplateRenderer;
    private readonly AmazonProductMetadataResolver? _amazonProductMetadataResolver;
    private readonly AmazonProductMetadataCacheSettings? _amazonCacheSettings;
    private OEmbedResolver _oEmbedResolver;
    private MarkdownPipeline? _contentPipeline;
    private bool _isInitialized;
    private bool _oEmbedProviderCatalogLoaded;

    public MarkdownProcessor(SiteOption siteOption, string oEmbedDir, OEmbedResolver? oEmbedResolver = null, OEmbedCardParser? oEmbedParser = null, Func<Task<OEmbedProviderCatalog>>? oEmbedProviderCatalogLoader = null, IAmazonCardTemplateRenderer? amazonCardTemplateRenderer = null, AmazonProductMetadataResolver? amazonProductMetadataResolver = null, AmazonProductMetadataCacheSettings? amazonCacheSettings = null)
        : this(siteOption, oEmbedDir, TimeZoneInfo.Local, oEmbedResolver, oEmbedParser, oEmbedProviderCatalogLoader, amazonCardTemplateRenderer, amazonProductMetadataResolver, amazonCacheSettings) { }

    public MarkdownProcessor(SiteOption siteOption, string oEmbedDir, TimeZoneInfo timeZone, OEmbedResolver? oEmbedResolver = null, OEmbedCardParser? oEmbedParser = null, Func<Task<OEmbedProviderCatalog>>? oEmbedProviderCatalogLoader = null, IAmazonCardTemplateRenderer? amazonCardTemplateRenderer = null, AmazonProductMetadataResolver? amazonProductMetadataResolver = null, AmazonProductMetadataCacheSettings? amazonCacheSettings = null)
    {
        _siteOption = siteOption;
        _oEmbedDir = oEmbedDir;
        _frontMatterParser = new FrontMatterParser(timeZone);
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
    public Task InitializeAsync() => EnsureInitializedAsync();

    public async Task<List<Article>> ProcessMarkdownFilesAsync(string inputDir, string outputDir, string baseAbsolutePath)
    {
        await EnsureInitializedAsync();
        var filePaths = Directory.GetFiles(inputDir, "*", SearchOption.AllDirectories).Where(filePath => string.Equals(Path.GetExtension(filePath), ".md", StringComparison.OrdinalIgnoreCase)).ToArray();
        var articles = await Task.WhenAll(filePaths.Select(filePath => ProcessMarkdownFileAsync(inputDir, filePath, baseAbsolutePath)));
        return articles.OrderByDescending(x => x.Published).ToList();
    }

    private async Task<Article> ProcessMarkdownFileAsync(string inputDir, string filePath, string baseAbsolutePath)
    {
        var relativePathExcludeFileName = Path.GetRelativePath(inputDir, Path.GetDirectoryName(filePath)!).Replace("\\", "/");
        relativePathExcludeFileName = relativePathExcludeFileName == "." ? string.Empty : relativePathExcludeFileName;
        var routeRelativePath = PageModelBase.CombineUrlPath(baseAbsolutePath, relativePathExcludeFileName);
        var (html, frontMatter) = await ParseMarkdownWithFrontmatterAsync(filePath, routeRelativePath);
        return new Article(Path.ChangeExtension(Path.GetFileNameWithoutExtension(filePath), ".html"), frontMatter.Title, html, frontMatter.Tags ?? [], frontMatter.Published, relativePathExcludeFileName, routeRelativePath, frontMatter.IsFixedPage, frontMatter.Template ?? string.Empty);
    }

    private async Task<(string html, Frontmatter frontMatter)> ParseMarkdownWithFrontmatterAsync(string path, string basePath)
    {
        var markdown = File.ReadAllText(path);
        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        _contentPipeline!.Setup(renderer);
        var document = Markdown.Parse(markdown, _frontMatterPipeline);
        var yamlBlock = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault();
        var frontMatter = yamlBlock is null ? new Frontmatter() : _frontMatterParser.Parse(yamlBlock.Lines.ToString(), path);

        var markdownContent = yamlBlock == null ? markdown : markdown[(yamlBlock.Span.End + 1)..].TrimStart();
        var markdownDocument = Markdown.Parse(markdownContent, _contentPipeline!);
        if (_amazonCardTemplateRenderer is not null && markdownDocument.Descendants<AmazonInline>().Any())
            await AmazonDocumentResolver.ResolveAsync(markdownDocument, _amazonCardTemplateRenderer, _siteOption.AmazonAssociateTag, _amazonProductMetadataResolver);
        foreach (var link in markdownDocument.Descendants<Markdig.Syntax.Inlines.LinkInline>())
            if (link.IsImage && !IsExternalUrl(link.Url!)) link.Url = PageModelBase.CombineUrlPath(basePath, link.Url!);

        var oEmbedInlines = markdownDocument.Descendants<OEmbedInline>().ToArray();
        var amazonFallbackInlines = markdownDocument.Descendants<AmazonInline>().Where(x => !string.IsNullOrEmpty(x.OEmbedFallbackUrl)).ToArray();
        if (oEmbedInlines.Length != 0 || amazonFallbackInlines.Length != 0)
        {
            await EnsureOEmbedProviderCatalogLoadedAsync();
            if (oEmbedInlines.Length != 0) await OEmbedDocumentResolver.ResolveAsync(markdownDocument, _oEmbedResolver);
            foreach (var amazonInline in amazonFallbackInlines)
            {
                var canonicalUrl = amazonInline.OEmbedFallbackUrl!;
                var fallbackHtml = await _oEmbedResolver.GetOEmbedHtmlAsync(canonicalUrl);
                amazonInline.HtmlContent = fallbackHtml == OEmbedHtmlFactory.CreateStandardLink(canonicalUrl) && !string.IsNullOrEmpty(amazonInline.FallbackLinkUrl) ? OEmbedHtmlFactory.CreateStandardLink(amazonInline.FallbackLinkUrl, canonicalUrl) : fallbackHtml;
            }
        }
        writer.GetStringBuilder().Clear();
        renderer.Render(markdownDocument);
        writer.Flush();
        return (writer.ToString(), frontMatter);
    }

    private static bool IsExternalUrl(string url) => url.StartsWith("/", StringComparison.Ordinal) || url.StartsWith("//", StringComparison.Ordinal) || Uri.TryCreate(url, UriKind.Absolute, out _);

    private async Task EnsureInitializedAsync()
    {
        if (_isInitialized) return;
        await _initializationSemaphore.WaitAsync();
        try
        {
            if (_isInitialized) return;
            if (!string.IsNullOrEmpty(_oEmbedDir)) await OEmbedCacheStore.LoadAsync(_oEmbedDir, _oEmbedResolver.OEmbedCache);
            if (_amazonProductMetadataResolver is not null && !string.IsNullOrEmpty(_amazonCacheSettings?.FilePath)) await AmazonProductMetadataCacheStore.LoadAsync(_amazonCacheSettings.FilePath, _amazonProductMetadataResolver.Cache);
            _contentPipeline = new MarkdownPipelineBuilder().UseYamlFrontMatter().Use(new AmazonAssociateExtension(_siteOption.AmazonAssociateTag)).Use(new OEmbedCardExtension(_oEmbedParser)).UseAdvancedExtensions().Build();
            _isInitialized = true;
        }
        finally { _initializationSemaphore.Release(); }
    }

    private static HttpClient CreateOEmbedHttpClient() { var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) }; client.DefaultRequestHeaders.UserAgent.ParseAdd("BlogGenerator"); return client; }
    private static OEmbedResolver CreateDefaultResolver() => new(new OEmbedProviderCatalog([]), CreateOEmbedHttpClient());
    private async Task EnsureOEmbedProviderCatalogLoadedAsync()
    {
        if (_oEmbedProviderCatalogLoaded) return;
        await _oEmbedProviderSemaphore.WaitAsync();
        try { if (!_oEmbedProviderCatalogLoaded) { _oEmbedResolver.SetProviderCatalog(await _oEmbedProviderCatalogLoader()); _oEmbedProviderCatalogLoaded = true; } }
        finally { _oEmbedProviderSemaphore.Release(); }
    }
    private static async Task<OEmbedProviderCatalog> LoadDefaultProviderCatalogAsync() => await new OEmbedProviderCatalogLoader(new OEmbedHttpFetcher(CreateOEmbedHttpClient())).LoadAsync();
}
