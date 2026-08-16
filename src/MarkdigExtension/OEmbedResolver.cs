using System.Collections.Concurrent;
using BlogGenerator.MarkdigExtension.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace BlogGenerator.MarkdigExtension;

public class OEmbedResolver
{
    private static readonly TimeSpan SuccessTtl = TimeSpan.FromDays(180);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromHours(6);

    private OEmbedProviderCatalog _oEmbedProviderCatalog;
    private readonly OEmbedEndpointResolver _oEmbedEndpointResolver;
    private readonly OEmbedSiteMetaDataExtractor _oEmbedSiteMetaDataExtractor;
    private readonly Func<DateTimeOffset> _utcNowProvider;

    public OEmbedResolver(
        OEmbedProviderCatalog oEmbedProviderCatalog,
        HttpClient httpClient,
        ConcurrentDictionary<string, OEmbedCacheEntry>? oEmbedCache = null,
        Func<DateTimeOffset>? utcNowProvider = null)
        : this(
            oEmbedProviderCatalog,
            new OEmbedHttpFetcher(httpClient),
            oEmbedCache,
            utcNowProvider)
    {
    }

    public OEmbedResolver(
        OEmbedProviderCatalog oEmbedProviderCatalog,
        OEmbedHttpFetcher fetcher,
        ConcurrentDictionary<string, OEmbedCacheEntry>? oEmbedCache = null,
        Func<DateTimeOffset>? utcNowProvider = null)
        : this(
            oEmbedProviderCatalog,
            new OEmbedEndpointResolver(fetcher),
            new OEmbedSiteMetaDataExtractor(fetcher),
            oEmbedCache,
            utcNowProvider)
    {
    }

    public OEmbedResolver(
        OEmbedProviderCatalog oEmbedProviderCatalog,
        OEmbedEndpointResolver oEmbedEndpointResolver,
        OEmbedSiteMetaDataExtractor oEmbedSiteMetaDataExtractor,
        ConcurrentDictionary<string, OEmbedCacheEntry>? oEmbedCache = null,
        Func<DateTimeOffset>? utcNowProvider = null)
    {
        _oEmbedProviderCatalog = oEmbedProviderCatalog;
        _oEmbedEndpointResolver = oEmbedEndpointResolver;
        _oEmbedSiteMetaDataExtractor = oEmbedSiteMetaDataExtractor;
        _utcNowProvider = utcNowProvider ?? (() => DateTimeOffset.UtcNow);
        OEmbedCache = oEmbedCache ?? [];
    }

    public ConcurrentDictionary<string, OEmbedCacheEntry> OEmbedCache { get; }

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

        if (OEmbedCache.TryGetValue(url, out var cachedResult))
        {
            if (cachedResult.IsFresh(now) || cachedResult.ShouldSkipRetry(now))
            {
                return cachedResult.HtmlContent;
            }
        }

        var resolution = await ResolveOEmbedAsync(url);
        if (resolution.IsSuccess)
        {
            var refreshedEntry = OEmbedCacheEntry.CreateSuccess(resolution.HtmlContent, now, SuccessTtl);
            OEmbedCache[url] = refreshedEntry;
            return refreshedEntry.HtmlContent;
        }

        if (cachedResult is not null && cachedResult.Status == OEmbedCacheEntryStatus.Success)
        {
            // 期限切れ後の再取得に失敗しても、古い成功結果を捨てると既存記事の表示が崩れるため保持する
            OEmbedCache[url] = cachedResult.MarkRefreshFailure(now, FailureTtl, resolution.ErrorSummary);
            return cachedResult.HtmlContent;
        }

        var failedEntry = OEmbedCacheEntry.CreateFailure(resolution.HtmlContent, now, FailureTtl, resolution.ErrorSummary);
        OEmbedCache[url] = failedEntry;
        return failedEntry.HtmlContent;
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

        if (!string.IsNullOrEmpty(metaData.OgTitle))
        {
            return new OEmbedResolutionResult
            {
                HtmlContent = OEmbedHtmlFactory.WrapInContainer(OEmbedHtmlFactory.CreateOgpCard(url, metaData)),
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
