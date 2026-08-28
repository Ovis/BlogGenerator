using System.Collections.Concurrent;
using BlogGenerator.Core.Interfaces;
using BlogGenerator.MarkdigExtension;
using BlogGenerator.Models;
using Markdig;
using Markdig.Extensions.Yaml;

namespace BlogGenerator.Core;

/// <summary>
/// Markdownファイル群の列挙、初期化、Articleへの変換順序を管理する
/// </summary>
public class MarkdownProcessor : IMarkdownProcessor
{
    private readonly SiteOption _siteOption;
    private readonly string _oEmbedDir;
    private readonly FrontMatterParser _frontMatterParser;
    private readonly OEmbedCardParser _oEmbedParser;
    private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);
    private readonly MarkdownPipeline _frontMatterPipeline = new MarkdownPipelineBuilder().UseYamlFrontMatter().Build();
    private readonly ExternalRequestMetrics _oEmbedRequestMetrics = new();
    private readonly Func<Task<OEmbedProviderCatalog>> _oEmbedProviderCatalogLoader;
    private readonly IAmazonCardTemplateRenderer? _amazonCardTemplateRenderer;
    private readonly AmazonProductMetadataResolver? _amazonProductMetadataResolver;
    private readonly AmazonProductMetadataCacheSettings? _amazonCacheSettings;
    private readonly OEmbedResolver _oEmbedResolver;
    private readonly bool _oEmbedProviderCatalogInitiallyLoaded;
    private MarkdownContentProcessor? _contentProcessor;
    private bool _isInitialized;

    /// <summary>
    /// ローカルタイムゾーンを使用してMarkdownProcessorを生成する
    /// </summary>
    public MarkdownProcessor(
        SiteOption siteOption,
        string oEmbedDir,
        OEmbedResolver? oEmbedResolver = null,
        OEmbedCardParser? oEmbedParser = null,
        Func<Task<OEmbedProviderCatalog>>? oEmbedProviderCatalogLoader = null,
        IAmazonCardTemplateRenderer? amazonCardTemplateRenderer = null,
        AmazonProductMetadataResolver? amazonProductMetadataResolver = null,
        AmazonProductMetadataCacheSettings? amazonCacheSettings = null)
        : this(
            siteOption,
            oEmbedDir,
            TimeZoneInfo.Local,
            oEmbedResolver,
            oEmbedParser,
            oEmbedProviderCatalogLoader,
            amazonCardTemplateRenderer,
            amazonProductMetadataResolver,
            amazonCacheSettings)
    {
    }

    /// <summary>
    /// 公開日時の解釈に使用するタイムゾーンを指定してMarkdownProcessorを生成する
    /// </summary>
    public MarkdownProcessor(
        SiteOption siteOption,
        string oEmbedDir,
        TimeZoneInfo timeZone,
        OEmbedResolver? oEmbedResolver = null,
        OEmbedCardParser? oEmbedParser = null,
        Func<Task<OEmbedProviderCatalog>>? oEmbedProviderCatalogLoader = null,
        IAmazonCardTemplateRenderer? amazonCardTemplateRenderer = null,
        AmazonProductMetadataResolver? amazonProductMetadataResolver = null,
        AmazonProductMetadataCacheSettings? amazonCacheSettings = null)
    {
        _siteOption = siteOption;
        _oEmbedDir = oEmbedDir;
        _frontMatterParser = new FrontMatterParser(timeZone);
        _oEmbedResolver = oEmbedResolver ?? CreateDefaultResolver(_oEmbedRequestMetrics);
        _oEmbedParser = oEmbedParser ?? new OEmbedCardParser();
        _oEmbedProviderCatalogLoader = oEmbedProviderCatalogLoader ?? (() => LoadDefaultProviderCatalogAsync(_oEmbedRequestMetrics));
        _amazonCardTemplateRenderer = amazonCardTemplateRenderer;
        _amazonProductMetadataResolver = amazonProductMetadataResolver;
        _amazonCacheSettings = amazonCacheSettings;

        // Resolverを外部から受け取った場合はprovider一覧も設定済みという従来の契約を維持する
        _oEmbedProviderCatalogInitiallyLoaded = oEmbedResolver is not null;
    }

    /// <summary>
    /// 現在のoEmbedキャッシュを取得する
    /// </summary>
    public ConcurrentDictionary<string, OEmbedCacheEntry> OEmbedCache => _oEmbedResolver.OEmbedCache;

    /// <summary>
    /// 現在のAmazon商品メタデータキャッシュを取得する
    /// </summary>
    public ConcurrentDictionary<string, AmazonProductMetadataCacheEntry> AmazonProductMetadataCache =>
        _amazonProductMetadataResolver?.Cache ?? [];

    /// <summary>
    /// Markdown処理中に発生した外部埋め込み解決の計測結果を取得する
    /// </summary>
    public ExternalResolutionMetrics ExternalResolutionMetrics
    {
        get
        {
            var (oEmbedCacheHits, oEmbedCacheMisses) = _oEmbedResolver.GetCacheMetrics();
            var oEmbedRequests = _oEmbedRequestMetrics.GetSnapshot();
            var amazonMetrics = _amazonProductMetadataResolver?.GetMetrics();

            return new ExternalResolutionMetrics(
                oEmbedCacheHits,
                oEmbedCacheMisses,
                oEmbedRequests.RequestCount,
                oEmbedRequests.Elapsed,
                amazonMetrics?.CacheHits ?? 0,
                amazonMetrics?.CacheMisses ?? 0,
                amazonMetrics?.HttpRequests ?? 0,
                amazonMetrics?.FetchElapsed ?? TimeSpan.Zero);
        }
    }

    /// <summary>
    /// キャッシュとMarkdown変換パイプラインを初期化する
    /// </summary>
    public Task InitializeAsync() => EnsureInitializedAsync();

    /// <summary>
    /// 入力ディレクトリ配下のMarkdownファイルをすべてArticleへ変換する
    /// </summary>
    public async Task<List<Article>> ProcessMarkdownFilesAsync(string inputDir, string outputDir, string baseAbsolutePath)
    {
        await EnsureInitializedAsync();

        // 拡張子比較は大文字小文字を区別せず、従来どおり全サブディレクトリを対象にする
        var filePaths = Directory.GetFiles(inputDir, "*", SearchOption.AllDirectories)
            .Where(filePath => string.Equals(Path.GetExtension(filePath), ".md", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var articles = new ConcurrentBag<Article>();
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = BuildConcurrency.MarkdownDegreeOfParallelism
        };

        // 全Markdownを一度にTask化せず、CPU・ファイルI/O・外部HTTP待機が過剰に積み上がらない範囲で処理する
        await Parallel.ForEachAsync(filePaths, parallelOptions, async (filePath, _) =>
        {
            articles.Add(await ProcessMarkdownFileAsync(inputDir, filePath, baseAbsolutePath));
        });

        return articles.OrderByDescending(x => x.Published).ToList();
    }

    /// <summary>
    /// 1つのMarkdownファイルから公開パスを組み立て、Articleへ変換する
    /// </summary>
    private async Task<Article> ProcessMarkdownFileAsync(string inputDir, string filePath, string baseAbsolutePath)
    {
        var relativeDirectoryPath = Path.GetRelativePath(inputDir, Path.GetDirectoryName(filePath)!)
            .Replace("\\", "/");
        relativeDirectoryPath = relativeDirectoryPath == "." ? string.Empty : relativeDirectoryPath;

        var routeRelativePath = PageModelBase.CombineUrlPath(baseAbsolutePath, relativeDirectoryPath);
        var (html, frontMatter) = await _contentProcessor!.ProcessAsync(filePath, routeRelativePath);

        return new Article(
            Path.ChangeExtension(Path.GetFileNameWithoutExtension(filePath), ".html"),
            frontMatter.Title,
            html,
            frontMatter.Tags ?? [],
            frontMatter.Published,
            relativeDirectoryPath,
            routeRelativePath,
            frontMatter.IsFixedPage,
            frontMatter.Template ?? string.Empty);
    }

    /// <summary>
    /// キャッシュを読み込み、本文変換に必要なパイプラインと内部サービスを1回だけ構築する
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        if (_isInitialized) return;

        await _initializationSemaphore.WaitAsync();
        try
        {
            if (_isInitialized) return;

            if (!string.IsNullOrEmpty(_oEmbedDir))
                await OEmbedCacheStore.LoadAsync(_oEmbedDir, _oEmbedResolver.OEmbedCache);

            if (_amazonProductMetadataResolver is not null && !string.IsNullOrEmpty(_amazonCacheSettings?.FilePath))
                await AmazonProductMetadataCacheStore.LoadAsync(_amazonCacheSettings.FilePath, _amazonProductMetadataResolver.Cache);

            var contentPipeline = new MarkdownPipelineBuilder()
                .UseYamlFrontMatter()
                .Use(new AmazonAssociateExtension(_siteOption.AmazonAssociateTag))
                .Use(new OEmbedCardExtension(_oEmbedParser))
                .UseAdvancedExtensions()
                .Build();

            var embedResolver = new MarkdownEmbedResolver(
                _oEmbedResolver,
                _oEmbedProviderCatalogLoader,
                _oEmbedProviderCatalogInitiallyLoaded);

            _contentProcessor = new MarkdownContentProcessor(
                _siteOption,
                _frontMatterParser,
                _frontMatterPipeline,
                contentPipeline,
                _amazonCardTemplateRenderer,
                _amazonProductMetadataResolver,
                embedResolver);

            _isInitialized = true;
        }
        finally
        {
            _initializationSemaphore.Release();
        }
    }

    /// <summary>
    /// oEmbed取得用の既定HttpClientを生成する
    /// </summary>
    private static HttpClient CreateOEmbedHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BlogGenerator");
        return client;
    }

    /// <summary>
    /// provider一覧を空の状態で開始する既定oEmbed resolverを生成する
    /// </summary>
    private static OEmbedResolver CreateDefaultResolver(ExternalRequestMetrics requestMetrics)
    {
        var fetcher = new OEmbedHttpFetcher(CreateOEmbedHttpClient(), requestMetrics);
        return new OEmbedResolver(new OEmbedProviderCatalog([]), fetcher);
    }

    /// <summary>
    /// 公開oEmbed provider一覧を取得する
    /// </summary>
    private static async Task<OEmbedProviderCatalog> LoadDefaultProviderCatalogAsync(ExternalRequestMetrics requestMetrics) =>
        await new OEmbedProviderCatalogLoader(new OEmbedHttpFetcher(CreateOEmbedHttpClient(), requestMetrics)).LoadAsync();
}
