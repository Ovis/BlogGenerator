using BlogGenerator.MarkdigExtension;
using Markdig.Syntax;

namespace BlogGenerator.Core.Content;

/// <summary>
/// Markdown本文中のoEmbed記法とAmazonフォールバックを解決する
/// </summary>
internal sealed class MarkdownEmbedResolver(
    OEmbedResolver oEmbedResolver,
    Func<Task<OEmbedProviderCatalog>> providerCatalogLoader,
    bool providerCatalogLoaded)
{
    private readonly SemaphoreSlim _providerSemaphore = new(1, 1);
    private bool _providerCatalogLoaded = providerCatalogLoaded;

    /// <summary>
    /// 文書中に埋め込み対象が存在する場合だけprovider一覧を読み込み、HTMLへ解決する
    /// </summary>
    public async Task ResolveAsync(MarkdownDocument document)
    {
        var oEmbedInlines = document.Descendants<OEmbedInline>().ToArray();
        var amazonFallbackInlines = document.Descendants<AmazonInline>()
            .Where(x => !string.IsNullOrEmpty(x.OEmbedFallbackUrl))
            .ToArray();

        if (oEmbedInlines.Length == 0 && amazonFallbackInlines.Length == 0)
            return;

        await EnsureProviderCatalogLoadedAsync();

        if (oEmbedInlines.Length != 0)
            await OEmbedDocumentResolver.ResolveAsync(document, oEmbedResolver);

        foreach (var amazonInline in amazonFallbackInlines)
        {
            var canonicalUrl = amazonInline.OEmbedFallbackUrl!;
            var fallbackHtml = await oEmbedResolver.GetOEmbedHtmlAsync(canonicalUrl);

            // Amazon側のURLで通常リンクへフォールバックした場合は、可能なら元リンクを表示先として維持する
            amazonInline.HtmlContent = fallbackHtml == OEmbedHtmlFactory.CreateStandardLink(canonicalUrl)
                && !string.IsNullOrEmpty(amazonInline.FallbackLinkUrl)
                    ? OEmbedHtmlFactory.CreateStandardLink(amazonInline.FallbackLinkUrl, canonicalUrl)
                    : fallbackHtml;
        }
    }

    /// <summary>
    /// oEmbed provider一覧を必要になった時点で1回だけ読み込む
    /// </summary>
    private async Task EnsureProviderCatalogLoadedAsync()
    {
        if (_providerCatalogLoaded) return;

        await _providerSemaphore.WaitAsync();
        try
        {
            if (_providerCatalogLoaded) return;

            oEmbedResolver.SetProviderCatalog(await providerCatalogLoader());
            _providerCatalogLoaded = true;
        }
        finally
        {
            _providerSemaphore.Release();
        }
    }
}
