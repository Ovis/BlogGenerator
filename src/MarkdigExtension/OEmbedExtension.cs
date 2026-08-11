using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlogGenerator.MarkdigExtension.Models;
using Hnx8.ReadJEnc;
using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Syntax.Inlines;

namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// Markdigにoembedカード機能を提供する拡張
/// </summary>
public class OEmbedCardExtension : IMarkdownExtension
{
    private static bool _isFirstCall = true;
    private static readonly object LockObject = new();
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15) // 15秒でタイムアウト
    };

    private static OEmbedProviderCatalog OEmbedProviderCatalog = new([]);

    public static OEmbedCardParser OEmbedCardParser { get; private set; } = null!;

    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        lock (LockObject)
        {
            if (_isFirstCall)
            {
                // HttpClientの初期化
                HttpClient.DefaultRequestHeaders.Add("User-Agent", "BlogGenerator");

                // 初回実行時のみoEmbed Provider情報を取得
                GetOEmbedProvidersJsonAsync().GetAwaiter().GetResult();
                _isFirstCall = false;

                OEmbedCardParser = new OEmbedCardParser(OEmbedProviderCatalog, HttpClient);
            }
        }

        if (!pipeline.InlineParsers.Contains<OEmbedCardParser>())
        {
            pipeline.InlineParsers.Insert(0, OEmbedCardParser);
        }
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }

    private async ValueTask GetOEmbedProvidersJsonAsync()
    {
        try
        {
            var (isSuccess, content, _, _) = await GetWebsiteContentAsync("https://oembed.com/providers.json");

            if (!isSuccess || string.IsNullOrEmpty(content))
                return;

            var jsonData = JsonSerializer.Deserialize<List<OEmbedProviderJson>>(content);

            if (jsonData == null)
                return;

            OEmbedProviderCatalog = new OEmbedProviderCatalog(jsonData);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"oEmbed provider json could not be obtained. Error:{ex.Message}");
        }
    }

    private async ValueTask<(bool isSuccess, string content, string mediaType, Exception? error)>
        GetWebsiteContentAsync(string url)
    {
        try
        {
            var response = await HttpClient.GetAsync(url);

            // リダイレクト処理
            if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.MovedPermanently)
            {
                var redirectUrl = response.Headers.Location?.OriginalString;
                if (redirectUrl != null)
                {
                    response = await HttpClient.GetAsync(redirectUrl);
                }
            }

            response.EnsureSuccessStatusCode();

            if (response.IsSuccessStatusCode)
            {
                var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                var byteArray = await response.Content.ReadAsByteArrayAsync();

                ReadJEnc.JP.GetEncoding(byteArray, byteArray.Length, out var content);
                return (true, content, mediaType, null);
            }
        }
        catch (TaskCanceledException e)
        {
            return (false, string.Empty, string.Empty, e);
        }
        catch (Exception e)
        {
            return (false, string.Empty, string.Empty, e);
        }

        return (false, string.Empty, string.Empty, null);
    }

    /// <summary>
    /// キャッシュをJSONファイルに保存する
    /// </summary>
    public static async Task SaveOEmbedCacheAsync(string filePath)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(OEmbedCardParser.OEmbedCache, options);
            await File.WriteAllTextAsync(filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving OEmbed cache: {ex.Message}");
        }
    }

    /// <summary>
    /// JSONファイルからキャッシュを読み込む
    /// </summary>
    public static async Task LoadOEmbedCacheAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var loadedCache = JsonSerializer.Deserialize<ConcurrentDictionary<string, string>>(json);

            if (loadedCache != null)
            {
                foreach (var item in loadedCache)
                {
                    OEmbedCardParser.OEmbedCache.TryAdd(item.Key, item.Value);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading OEmbed cache: {ex.Message}");
        }
    }
}

/// <summary>
/// oembedタグを解析し、HTML化するパーサー
/// </summary>
public class OEmbedCardParser : InlineParser
{
    private static OEmbedProviderCatalog _oEmbedProviderCatalog = new([]);
    private static OEmbedEndpointResolver _oEmbedEndpointResolver = new(new HttpClient());
    private static OEmbedSiteMetaDataExtractor _oEmbedSiteMetaDataExtractor = new(new HttpClient());
    private static readonly ConcurrentDictionary<string, string> _oEmbedCache = new();

    // OEmbedCacheをパブリックプロパティとして公開
    public static ConcurrentDictionary<string, string> OEmbedCache => _oEmbedCache;

    private static readonly Regex OEmbedTagRegex = new(@"\[oembed:""(?<url>https?:\/\/[^""]+)""\]");

    public OEmbedCardParser(OEmbedProviderCatalog oEmbedProviderCatalog, HttpClient httpClient)
    {
        _oEmbedProviderCatalog = oEmbedProviderCatalog;
        _oEmbedEndpointResolver = new OEmbedEndpointResolver(httpClient);
        _oEmbedSiteMetaDataExtractor = new OEmbedSiteMetaDataExtractor(httpClient);
        OpeningCharacters = ['['];
    }

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        // 先頭文字チェック - 空白でなければ不一致
        var precedingCharacter = slice.PeekCharExtra(-1);
        if (!precedingCharacter.IsWhiteSpaceOrZero())
        {
            return false;
        }

        // 正規表現によるマッチング
        var match = OEmbedTagRegex.Match(slice.ToString());
        if (!match.Success)
        {
            return false;
        }

        var url = match.Groups["url"].Value;
        var htmlContent = GetOEmbedHtml(url).GetAwaiter().GetResult();

        // インラインとして処理
        processor.Inline = new HtmlInline(htmlContent)
        {
            Span =
                {
                    Start = processor.GetSourcePosition(slice.Start, out var line, out var column)
                },
            Line = line,
            Column = column,
            IsClosed = true
        };
        processor.Inline.Span.End = processor.Inline.Span.Start + match.Length - 1;
        slice.Start += match.Length;
        return true;
    }

    private async ValueTask<string> GetOEmbedHtml(string url)
    {
        // キャッシュ検索
        if (_oEmbedCache.TryGetValue(url, out var cachedResult))
        {
            return cachedResult;
        }

        // URLに応じた処理ルート選択
        string html;

        // GitHub Gist特別処理
        if (url.Contains("gist.github.com"))
        {
            html = OEmbedHtmlFactory.WrapInParagraph(OEmbedHtmlFactory.CreateGistEmbed(url));
            _oEmbedCache[url] = html;
            return html;
        }

        // 1. oEmbed Provider対応チェック
        var (isProviderSupported, richLinkHtml, isVideo) = await GetRichLinkByOEmbedProviderAsync(url);
        if (isProviderSupported)
        {
            html = OEmbedHtmlFactory.WrapInParagraph(richLinkHtml ?? string.Empty, isVideo);
            _oEmbedCache[url] = html;
            return html;
        }

        // 2. サイトメタデータ取得
        var (isMetaDataSuccess, metaData) = await _oEmbedSiteMetaDataExtractor.GetSiteMetaDataAsync(url);
        if (!isMetaDataSuccess)
        {
            html = OEmbedHtmlFactory.WrapInParagraph(OEmbedHtmlFactory.CreateStandardLink(url));
            _oEmbedCache[url] = html;
            return html;
        }

        // 3. oEmbed Discovery
        var oEmbedEndpoint = OEmbedSiteMetaDataExtractor.GetOEmbedEndpoint(metaData);
        if (!string.IsNullOrEmpty(oEmbedEndpoint))
        {
            var (isSuccess, embedHtml, _, _) = await _oEmbedEndpointResolver.GetEmbedResultAsync(oEmbedEndpoint, string.Empty);
            if (isSuccess && !string.IsNullOrEmpty(embedHtml))
            {
                html = OEmbedHtmlFactory.WrapInParagraph(embedHtml);
                _oEmbedCache[url] = html;
                return html;
            }
        }

        // 4. OGP情報による生成
        if (!string.IsNullOrEmpty(metaData.OgTitle) && !string.IsNullOrEmpty(metaData.OgUrl))
        {
            html = OEmbedHtmlFactory.WrapInParagraph(OEmbedHtmlFactory.CreateOgpCard(url, metaData));
            _oEmbedCache[url] = html;
            return html;
        }

        // 5. 標準リンク
        html = OEmbedHtmlFactory.WrapInParagraph(OEmbedHtmlFactory.CreateStandardLink(url));
        _oEmbedCache[url] = html;
        return html;
    }

    /// <summary>
    /// oEmbedプロバイダからリッチリンクHTMLを取得
    /// </summary>
    private async Task<(bool IsSuccess, string? RichLinkHtml, bool IsVideo)> GetRichLinkByOEmbedProviderAsync(string url)
    {
        // プロバイダURLの検索
        var existProviderUrl = _oEmbedProviderCatalog.FindMatchingProviderUrl(url);
        if (string.IsNullOrEmpty(existProviderUrl))
        {
            return (false, null, false);
        }

        // エンドポイントURL取得
        var endpointUrl = _oEmbedProviderCatalog.GetProviderEndpointUrl(existProviderUrl, url);
        if (string.IsNullOrEmpty(endpointUrl))
        {
            return (false, null, false);
        }

        // WordPress.com向け特殊処理
        if (existProviderUrl.Contains("wordpress.com"))
        {
            endpointUrl = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(endpointUrl, new Dictionary<string, string?>
                {
                    { "for", "BlogGenerator" }
                });
        }

        // oEmbedレスポンス取得
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
}
