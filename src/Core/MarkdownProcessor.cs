using System.Collections.Concurrent;
using System.Globalization;
using BlogGenerator.Core.Interfaces;
using BlogGenerator.MarkdigExtension;
using BlogGenerator.Models;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Renderers;
using Markdig.Syntax;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace BlogGenerator.Core;

public class MarkdownProcessor : IMarkdownProcessor
{
    private readonly SiteOption _siteOption;
    private readonly string _oEmbedDir;
    private readonly TimeZoneInfo _timeZone;
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
        _timeZone = timeZone;
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
        var frontMatter = new Frontmatter();
        if (yamlBlock != null)
        {
            var yaml = yamlBlock.Lines.ToString();
            frontMatter = new DeserializerBuilder().IgnoreUnmatchedProperties().Build().Deserialize<Frontmatter>(yaml);
            frontMatter.Published = ParsePublished(yaml, path);
        }

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

    private DateTimeOffset? ParsePublished(string yaml, string path)
    {
        var stream = new YamlStream();
        try { stream.Load(new StringReader(yaml)); }
        catch (Exception ex) { throw PublishedError(path, null, "Front Matter YAML could not be parsed.", ex); }
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode mapping) return null;

        var matches = mapping.Children.Where(x => x.Key is YamlScalarNode key && string.Equals(key.Value, "Published", StringComparison.Ordinal)).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length > 1) throw PublishedError(path, null, "Published is defined more than once.");
        if (matches[0].Value is not YamlScalarNode scalar) throw PublishedError(path, null, "Published must be a scalar value.");
        var value = scalar.Value?.Trim();
        if (string.IsNullOrEmpty(value)) throw PublishedError(path, value, "Published must contain a valid date and time.");

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var withOffset) && HasExplicitOffset(value)) return withOffset;
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local)) throw PublishedError(path, value, "Published could not be parsed as a date and time.");
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (_timeZone.IsInvalidTime(local)) throw PublishedError(path, value, $"The local time does not exist in time zone '{_timeZone.Id}'.");
        if (_timeZone.IsAmbiguousTime(local)) throw PublishedError(path, value, $"The local time is ambiguous in time zone '{_timeZone.Id}'. Specify an explicit UTC offset.");
        return new DateTimeOffset(local, _timeZone.GetUtcOffset(local));
    }

    private InvalidDataException PublishedError(string path, string? value, string reason, Exception? inner = null) =>
        new($"Invalid Published value in '{path}': '{value ?? "<null>"}'. Time zone: '{_timeZone.Id}'. Reason: {reason}", inner);

    private static bool HasExplicitOffset(string value) => value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) || System.Text.RegularExpressions.Regex.IsMatch(value, @"[+-]\d{2}:?\d{2}$");
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
