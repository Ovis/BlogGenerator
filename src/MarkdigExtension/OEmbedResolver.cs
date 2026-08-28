using System.Collections.Concurrent;
using BlogGenerator.MarkdigExtension.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// URLをoEmbed、discovery、OGPの順で解決し、埋め込みHTMLを返す
/// </summary>
public class OEmbedResolver
{
    private static readonly TimeSpan SuccessTtl = TimeSpan.FromDays(180);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromHours(6);

    private OEmbedProviderCatalog _oEmbedProviderCatalog;
    private readonly OEmbedEndpointResolver _oEmbedEndpointResolver;
    private readonly OEmbedSiteMetaDataExtractor _oEmbedSiteMetaDataExtractor;
    private readonly IOgpCardTemplateRenderer _ogpCardTemplateRenderer;
    private readonly Func<DateTimeOffset> _utcNowProvider;
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _inFlightResolutions = [];
    private long _cacheHits;
    private long _cacheMisses;

    public OEmbedResolver(
        OEmbedProviderCatalog oEmbedProviderCatalog,
        HttpClient httpClient,
        ConcurrentDictionary<string, OEmbedCacheEntry>? oEmbedCache = null,
        Func<DateTimeOffset>? utcNowProvider = null,
        IOgpCardTemplateRenderer? ogpCardTemplateRenderer = null)
        : this(
            oEmbedProviderCatalog,
            new OEmbedHttpFetcher(httpClient),
            oEmbedCache,
            utcNowProvider,
            ogpCardTemplateRenderer)
    {
    }

    public OEmbedResolver(
        OEmbedProviderCatalog oEmbedProviderCatalog,
        OEmbedHttpFetcher fetcher,
        ConcurrentDictionary<string, OEmbedCacheEntry>? oEmbedCache = null,
        Func<DateTimeOffset>? utcNowProvider = null,
        IOgpCardTemplateRenderer? ogpCardTemplateRenderer = null)
        : this(
            oEmbedProviderCatalog,
            new OEmbedEndpointResolver(fetcher),
            new OEmbedSiteMetaDataExtractor(fetcher),
            oEmbedCache,
            utcNowProvider,
            ogpCardTemplateRenderer)
    {
    }

    public OEmbedResolver(
        OEmbedProviderCatalog oEmbedProviderCatalog,
        OEmbedEndpointResolver oEmbedEndpointResolver,
        OEmbedSiteMetaDataExtractor oEmbedSiteMetaDataExtractor,
        ConcurrentDictionary<string, OEmbedCacheEntry>? oEmbedCache = null,
        Func<DateTimeOffset>? utcNowProvider = null,
        IOgpCardTemplateRenderer? ogpCardTemplateRenderer = null)
    {
        _oEmbedProviderCatalog = oEmbedProviderCatalog;
        _oEmbedEndpointResolver = oEmbedEndpointResolver;
        _oEmbedSiteMetaDataExtractor = oEmbedSiteMetaDataExtractor;
        _utcNowProvider = utcNowProvider ?? (() => DateTimeOffset.UtcNow);
        _ogpCardTemplateRenderer = ogpCardTemplateRenderer ?? new OgpCardTemplateRenderer();
        OEmbedCache = oEmbedCache ?? [];
    }

    public ConcurrentDictionary<string, OEmbedCacheEntry> OEmbedCache { get; }

    /// <summary>
    /// 現在までのキャッシュhit/miss件数を取得する
    /// </summary>
    internal (long CacheHits, long CacheMisses) GetCacheMetrics() =>
        (Interlocked.Read(ref _cacheHits), Interlocked.Read(ref _cacheMisses));

    public void SetProviderCatalog(OEmbedProviderCatalog oEmbedProviderCatalog)
    {
        _oEmbedProviderCatalog = oEmbedProviderCatalog;
    }

    /// <summary>
    /// URLからoEmbed HTMLを解決する
    /// </summary>
    public async ValueTask<string> GetOEmbedHtmlAsync(string url)
    {
        var now = _utcNowProvider();
        var cached = await TryGetCachedHtmlAsync(url, now);
        if (cached.Found)
        {
            Interlocked.Increment(ref _cacheHits);
            return cached.Html;
        }

        // 期限切れエントリと旧形式OGPカードは再取得が必要なためmissとして扱う
        Interlocked.Increment(ref _cacheMisses);

        // Markdownは複数ファイルを並列処理するため、同じURLが同時に現れると全呼び出しがcache missになり得る
        // URL単位で解決Taskを共有し、provider APIや対象ページへの不要な重複アクセスを防ぐ
        var candidate = new Lazy<Task<string>>(
            () => ResolveAndCacheAsync(url),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var resolution = _inFlightResolutions.GetOrAdd(url, candidate);

        try
        {
            return await resolution.Value;
        }
        finally
        {
            // 完了したTaskは保持せず、以後は通常のTTLキャッシュ判定へ戻す
            _inFlightResolutions.TryRemove(new KeyValuePair<string, Lazy<Task<string>>>(url, resolution));
        }
    }

    /// <summary>
    /// キャッシュを再確認したうえでURLを解決し、結果をキャッシュへ保存する
    /// </summary>
    private async Task<string> ResolveAndCacheAsync(string url)
    {
        var now = _utcNowProvider();

        // 最初のcache missからin-flight登録までの間に別Taskが解決を完了した場合は外部アクセスを省略する
        var cached = await TryGetCachedHtmlAsync(url, now);
        if (cached.Found)
        {
            return cached.Html;
        }

        OEmbedCache.TryGetValue(url, out var cachedResult);
        var resolution = await ResolveOEmbedAsync(url);
        if (resolution.IsSuccess)
        {
            OEmbedCacheEntry refreshedEntry;
            if (resolution.OgpCard is not null)
            {
                // OGP fallbackは最終HTMLではなく表示モデルを保存し、テーマ変更時にも再描画できるようにする
                refreshedEntry = OEmbedCacheEntry.CreateOgpSuccess(resolution.OgpCard, now, SuccessTtl);
            }
            else
            {
                refreshedEntry = OEmbedCacheEntry.CreateSuccess(resolution.HtmlContent, now, SuccessTtl);
            }

            OEmbedCache[url] = refreshedEntry;
            return await RenderCacheEntryAsync(refreshedEntry);
        }

        if (cachedResult is not null && cachedResult.Status == OEmbedCacheEntryStatus.Success)
        {
            // 期限切れ後の再取得に失敗しても、古い成功結果を捨てると既存記事の表示が崩れるため保持する
            OEmbedCache[url] = cachedResult.MarkRefreshFailure(now, FailureTtl, resolution.ErrorSummary);
            return await RenderCacheEntryAsync(cachedResult);
        }

        var failedEntry = OEmbedCacheEntry.CreateFailure(resolution.HtmlContent, now, FailureTtl, resolution.ErrorSummary);
        OEmbedCache[url] = failedEntry;
        return failedEntry.HtmlContent;
    }

    /// <summary>
    /// TTLまたは再試行抑止期間が有効なキャッシュからHTMLを取得する
    /// </summary>
    private async Task<(bool Found, string Html)> TryGetCachedHtmlAsync(string url, DateTimeOffset now)
    {
        if (!OEmbedCache.TryGetValue(url, out var cachedResult))
        {
            return (false, string.Empty);
        }

        if (cachedResult.IsFresh(now))
        {
            // 旧bcard HTMLは一度だけ再取得し、テーマで再描画できるモデル形式へ移行する
            if (cachedResult.IsLegacyOgpHtml)
            {
                return (false, string.Empty);
            }

            return (true, await RenderCacheEntryAsync(cachedResult));
        }

        if (cachedResult.ShouldSkipRetry(now))
        {
            return (true, await RenderCacheEntryAsync(cachedResult));
        }

        return (false, string.Empty);
    }

    /// <summary>
    /// キャッシュエントリを現在のテーマに応じた最終HTMLへ変換する
    /// </summary>
    private async Task<string> RenderCacheEntryAsync(OEmbedCacheEntry entry)
    {
        if (entry.OgpCard is null)
        {
            return entry.HtmlContent;
        }

        var cardHtml = await _ogpCardTemplateRenderer.RenderAsync(entry.OgpCard);
        return OEmbedHtmlFactory.WrapInContainer(cardHtml);
    }

    private async Task<OEmbedResolutionResult> ResolveOEmbedAsync(string url)
    {
        if (IsGistUrl(url))
        {
            return new OEmbedResolutionResult
            {
                HtmlContent = OEmbedHtmlFactory.WrapInContainer(OEmbedHtmlFactory.CreateGistEmbed(url)),
                IsSuccess = true
            };
        }

        var (isProviderSupported, richLinkHtml, isVideo) = await GetRichLinkByOEmbedProviderAsync(url);
        if (isProviderSupported)
        {
            return new OEmbedResolutionResult
            {
                HtmlContent = OEmbedHtmlFactory.WrapInContainer(richLinkHtml ?? string.Empty, isVideo),
                IsSuccess = true
            };
        }

        var (isMetaDataSuccess, metaData) = await _oEmbedSiteMetaDataExtractor.GetSiteMetaDataAsync(url);
        if (!isMetaDataSuccess)
        {
            return new OEmbedResolutionResult
            {
                HtmlContent = OEmbedHtmlFactory.WrapInContainer(OEmbedHtmlFactory.CreateStandardLink(url)),
                IsSuccess = false,
                ErrorSummary = "Failed to fetch metadata"
            };
        }

        var oEmbedEndpoint = OEmbedSiteMetaDataExtractor.GetOEmbedEndpoint(metaData);
        if (!string.IsNullOrEmpty(oEmbedEndpoint))
        {
            var (isSuccess, embedHtml, discoveryIsVideo, _) = await _oEmbedEndpointResolver.GetEmbedResultAsync(oEmbedEndpoint, url);
            if (isSuccess && !string.IsNullOrEmpty(embedHtml))
            {
                return new OEmbedResolutionResult
                {
                    HtmlContent = OEmbedHtmlFactory.WrapInContainer(embedHtml, discoveryIsVideo),
                    IsSuccess = true
                };
            }
        }

        if (!string.IsNullOrEmpty(metaData.OgTitle) && OgpCardModelFactory.TryCreate(url, metaData, out var ogpCard))
        {
            return new OEmbedResolutionResult
            {
                OgpCard = ogpCard,
                IsSuccess = true
            };
        }

        return new OEmbedResolutionResult
        {
            HtmlContent = OEmbedHtmlFactory.WrapInContainer(OEmbedHtmlFactory.CreateStandardLink(url)),
            IsSuccess = true
        };
    }

    /// <summary>
    /// oEmbedプロバイダからリッチリンクHTMLを取得する
    /// </summary>
    private async Task<(bool IsSuccess, string? RichLinkHtml, bool IsVideo)> GetRichLinkByOEmbedProviderAsync(string url)
    {
        var existProviderUrl = _oEmbedProviderCatalog.FindMatchingProviderUrl(url);
        if (string.IsNullOrEmpty(existProviderUrl))
        {
            return (false, null, false);
        }

        var endpointUrl = _oEmbedProviderCatalog.GetProviderEndpointUrl(existProviderUrl, url);
        if (string.IsNullOrEmpty(endpointUrl))
        {
            return (false, null, false);
        }

        if (IsWordPressProviderUrl(existProviderUrl))
        {
            endpointUrl = QueryHelpers.AddQueryString(endpointUrl, new Dictionary<string, string?>
            {
                { "for", "BlogGenerator" }
            });
        }

        var (isSuccess, richLinkString, isVideo, error) = await _oEmbedEndpointResolver.GetEmbedResultAsync(endpointUrl, url);
        if (!isSuccess)
        {
            if (error != null)
            {
                Console.WriteLine($"oEmbed error: {error.Message}, URL: {url}, Endpoint: {endpointUrl}");
            }
            return (false, null, false);
        }

        return (true, richLinkString, isVideo);
    }

    // gist 判定は部分一致ではなくホスト一致に寄せ、誤検出を防ぐ
    private static bool IsGistUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        string.Equals(uri.Host, "gist.github.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsWordPressProviderUrl(string providerUrl)
    {
        if (!Uri.TryCreate(providerUrl, UriKind.Absolute, out var providerUri))
        {
            return false;
        }

        return string.Equals(providerUri.Host, "wordpress.com", StringComparison.OrdinalIgnoreCase) ||
            providerUri.Host.EndsWith(".wordpress.com", StringComparison.OrdinalIgnoreCase);
    }
}
