using System.Collections.Concurrent;
using BlogGenerator.MarkdigExtension.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace BlogGenerator.MarkdigExtension;

public class OEmbedResolver
{
    private OEmbedProviderCatalog _oEmbedProviderCatalog;
    private readonly OEmbedEndpointResolver _oEmbedEndpointResolver;
    private readonly OEmbedSiteMetaDataExtractor _oEmbedSiteMetaDataExtractor;

    public OEmbedResolver(
        OEmbedProviderCatalog oEmbedProviderCatalog,
        HttpClient httpClient,
        ConcurrentDictionary<string, string>? oEmbedCache = null)
        : this(
            oEmbedProviderCatalog,
            new OEmbedEndpointResolver(httpClient),
            new OEmbedSiteMetaDataExtractor(httpClient),
            oEmbedCache)
    {
    }

    public OEmbedResolver(
        OEmbedProviderCatalog oEmbedProviderCatalog,
        OEmbedEndpointResolver oEmbedEndpointResolver,
        OEmbedSiteMetaDataExtractor oEmbedSiteMetaDataExtractor,
        ConcurrentDictionary<string, string>? oEmbedCache = null)
    {
        _oEmbedProviderCatalog = oEmbedProviderCatalog;
        _oEmbedEndpointResolver = oEmbedEndpointResolver;
        _oEmbedSiteMetaDataExtractor = oEmbedSiteMetaDataExtractor;
        OEmbedCache = oEmbedCache ?? [];
    }

    public ConcurrentDictionary<string, string> OEmbedCache { get; }

    public void SetProviderCatalog(OEmbedProviderCatalog oEmbedProviderCatalog)
    {
        _oEmbedProviderCatalog = oEmbedProviderCatalog;
    }

    /// <summary>
    /// URLからoEmbed HTMLを解決する
    /// </summary>
    public async ValueTask<string> GetOEmbedHtmlAsync(string url)
    {
        if (OEmbedCache.TryGetValue(url, out var cachedResult))
        {
            return cachedResult;
        }

        string html;

        if (IsGistUrl(url))
        {
            html = OEmbedHtmlFactory.WrapInContainer(OEmbedHtmlFactory.CreateGistEmbed(url));
            OEmbedCache[url] = html;
            return html;
        }

        var (isProviderSupported, richLinkHtml, isVideo) = await GetRichLinkByOEmbedProviderAsync(url);
        if (isProviderSupported)
        {
            html = OEmbedHtmlFactory.WrapInContainer(richLinkHtml ?? string.Empty, isVideo);
            OEmbedCache[url] = html;
            return html;
        }

        var (isMetaDataSuccess, metaData) = await _oEmbedSiteMetaDataExtractor.GetSiteMetaDataAsync(url);
        if (!isMetaDataSuccess)
        {
            html = OEmbedHtmlFactory.WrapInContainer(OEmbedHtmlFactory.CreateStandardLink(url));
            OEmbedCache[url] = html;
            return html;
        }

        var oEmbedEndpoint = OEmbedSiteMetaDataExtractor.GetOEmbedEndpoint(metaData);
        if (!string.IsNullOrEmpty(oEmbedEndpoint))
        {
            var (isSuccess, embedHtml, discoveryIsVideo, _) = await _oEmbedEndpointResolver.GetEmbedResultAsync(oEmbedEndpoint, url);
            if (isSuccess && !string.IsNullOrEmpty(embedHtml))
            {
                html = OEmbedHtmlFactory.WrapInContainer(embedHtml, discoveryIsVideo);
                OEmbedCache[url] = html;
                return html;
            }
        }

        if (!string.IsNullOrEmpty(metaData.OgTitle))
        {
            html = OEmbedHtmlFactory.WrapInContainer(OEmbedHtmlFactory.CreateOgpCard(url, metaData));
            OEmbedCache[url] = html;
            return html;
        }

        html = OEmbedHtmlFactory.WrapInContainer(OEmbedHtmlFactory.CreateStandardLink(url));
        OEmbedCache[url] = html;
        return html;
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
