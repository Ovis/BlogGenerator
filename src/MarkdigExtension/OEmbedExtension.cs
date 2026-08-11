using System.Collections.Concurrent;
using System.Net;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using AngleSharp.Html.Parser;
using BlogGenerator.Converters;
using BlogGenerator.MarkdigExtension.Models;
using Hnx8.ReadJEnc;
using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.WebUtilities;

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
    private static HttpClient _httpClient = new();
    private static readonly ConcurrentDictionary<string, string> _oEmbedCache = new();

    // OEmbedCacheをパブリックプロパティとして公開
    public static ConcurrentDictionary<string, string> OEmbedCache => _oEmbedCache;

    private static readonly Regex OEmbedTagRegex = new(@"\[oembed:""(?<url>https?:\/\/[^""]+)""\]");

    public OEmbedCardParser(OEmbedProviderCatalog oEmbedProviderCatalog, HttpClient httpClient)
    {
        _oEmbedProviderCatalog = oEmbedProviderCatalog;
        _httpClient = httpClient;
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
        var (isMetaDataSuccess, metaData) = await GetSiteMetaDataAsync(url);
        if (!isMetaDataSuccess)
        {
            html = OEmbedHtmlFactory.WrapInParagraph(OEmbedHtmlFactory.CreateStandardLink(url));
            _oEmbedCache[url] = html;
            return html;
        }

        // 3. oEmbed Discovery
        var oEmbedEndpoint = GetOEmbedEndpoint(metaData);
        if (!string.IsNullOrEmpty(oEmbedEndpoint))
        {
            var (isSuccess, embedHtml, _, _) = await GetEmbedResultAsync(oEmbedEndpoint, string.Empty);
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
    /// メタデータからoEmbedエンドポイントを取得
    /// </summary>
    private static string GetOEmbedEndpoint(SiteMetaData metaData)
    {
        if (!string.IsNullOrEmpty(metaData.OembedJson))
            return metaData.OembedJson;

        if (!string.IsNullOrEmpty(metaData.OembedXml))
            return metaData.OembedXml;

        return string.Empty;
    }

    /// <summary>
    /// サイトメタデータを取得
    /// </summary>
    private async Task<(bool IsSuccess, SiteMetaData Data)> GetSiteMetaDataAsync(string url)
    {
        // Webサイトコンテンツ取得
        var (isSuccess, contentHtml, _, _) = await GetWebsiteContentAsync(url);
        if (!isSuccess || string.IsNullOrEmpty(contentHtml))
        {
            return (false, new SiteMetaData());
        }

        try
        {
            // HTMLパース
            var document = new HtmlParser().ParseDocument(contentHtml);

            // メタデータ抽出
            var metaData = new SiteMetaData
            {
                Url = url,
                Title = document.QuerySelector("title")?.TextContent ?? string.Empty,
                OgTitle = document.QuerySelector("meta[property='og:title']")?.GetAttribute("content") ?? string.Empty,
                OgImage = document.QuerySelector("meta[property='og:image']")?.GetAttribute("content") ?? string.Empty,
                OgDescription = document.QuerySelector("meta[property='og:description']")?.GetAttribute("content") ?? string.Empty,
                OgType = document.QuerySelector("meta[property='og:type']")?.GetAttribute("content") ?? string.Empty,
                OgUrl = document.QuerySelector("meta[property='og:url']")?.GetAttribute("content") ?? string.Empty,
                OgSiteName = document.QuerySelector("meta[property='og:site_name']")?.GetAttribute("content") ?? string.Empty,
                OembedJson = document.QuerySelector("link[type='application/json+oembed']")?.GetAttribute("href") ?? string.Empty,
                OembedXml = GetXmlOembedLink(document)
            };

            return (true, metaData);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error parsing HTML: {e.Message}, URL: {url}");
            return (false, new SiteMetaData());
        }
    }

    /// <summary>
    /// XMLのoEmbedリンクを取得（複数の可能性に対応）
    /// </summary>
    private static string GetXmlOembedLink(AngleSharp.Html.Dom.IHtmlDocument document)
    {
        var xmlLink = document.QuerySelector("link[type='application/xml+oembed']")?.GetAttribute("href");
        if (!string.IsNullOrEmpty(xmlLink))
            return xmlLink;

        return document.QuerySelector("link[type='text/xml+oembed']")?.GetAttribute("href") ?? string.Empty;
    }

    /// <summary>
    /// Webサイトコンテンツを取得
    /// </summary>
    private async Task<(bool IsSuccess, string? Content, string? MediaType, Exception? Error)> GetWebsiteContentAsync(string url)
    {
        try
        {
            var response = await _httpClient.GetAsync(url);

            // リダイレクト処理
            if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.MovedPermanently)
            {
                var redirectUrl = response.Headers.Location?.OriginalString ?? string.Empty;
                if (!string.IsNullOrEmpty(redirectUrl))
                {
                    response = await _httpClient.GetAsync(redirectUrl);
                }
            }

            response.EnsureSuccessStatusCode();

            if (response.IsSuccessStatusCode)
            {
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                var byteArray = await response.Content.ReadAsByteArrayAsync();
                ReadJEnc.JP.GetEncoding(byteArray, byteArray.Length, out var content);
                return (true, content, mediaType, null);
            }
        }
        catch (TaskCanceledException e)
        {
            Console.WriteLine($"Request timeout: {url}");
            return (false, null, null, e);
        }
        catch (HttpRequestException ex)
        {
            LogHttpRequestError(ex, url);
            return (false, null, null, ex);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error fetching content: {e.Message}, URL: {url}");
            return (false, null, null, e);
        }

        return (false, null, null, null);
    }

    /// <summary>
    /// HTTPリクエストエラーのログ出力
    /// </summary>
    private static void LogHttpRequestError(HttpRequestException ex, string url)
    {
        if (ex.HttpRequestError == HttpRequestError.Unknown)
        {
            Console.WriteLine($"HTTP error: {ex.StatusCode}, URL: {url}");
        }
        else
        {
            Console.WriteLine($"HTTP request error: {ex.HttpRequestError}, URL: {url}");
        }
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
            endpointUrl = QueryHelpers.AddQueryString(endpointUrl, new Dictionary<string, string?>
                {
                    { "for", "BlogGenerator" }
                });
        }

        // oEmbedレスポンス取得
        var (isSuccess, richLinkString, isVideo, error) = await GetEmbedResultAsync(endpointUrl, url);
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

    /// <summary>
    /// oEmbedエンドポイントからEmbedレスポンスを取得
    /// </summary>
    private async Task<(bool IsSuccess, string? RichLinkString, bool IsVideo, Exception? Error)> GetEmbedResultAsync(string endpoint, string url)
    {
        // リクエストURLの構築
        string requestUrl = BuildRequestUrl(endpoint, url);

        try
        {
            // コンテンツ取得
            var (isSuccess, content, mediaType, error) = await GetWebsiteContentAsync(requestUrl);
            if (!isSuccess || string.IsNullOrEmpty(content))
            {
                return (false, null, false, error);
            }

            // メディアタイプに応じたデシリアライズ
            var embedResponse = DeserializeEmbedResponse(content, mediaType);

            // HTML応答の処理
            if (!string.IsNullOrEmpty(embedResponse.Html))
            {
                return (true, embedResponse.Html, embedResponse.Type == "video", null);
            }

            // 画像タイプの処理
            if (embedResponse.Type == "photo")
            {
                // 必須要素チェック
                if (string.IsNullOrEmpty(embedResponse.Url) ||
                    string.IsNullOrEmpty(embedResponse.Width) ||
                    string.IsNullOrEmpty(embedResponse.Height))
                {
                    throw new InvalidDataException("Missing required oEmbed values for image type");
                }

                var imgHtml = $"<img src=\"{embedResponse.Url}\" width=\"{embedResponse.Width}\" height=\"{embedResponse.Height}\" />";
                return (true, imgHtml, false, null);
            }

            if (embedResponse.Type == "link")
            {
                return (false, null, false, null);
            }

            return (false, null, false, new InvalidDataException("Unsupported oEmbed content type"));
        }
        catch (Exception e)
        {
            return (false, null, false, e);
        }
    }

    /// <summary>
    /// リクエストURLを構築
    /// </summary>
    private string BuildRequestUrl(string endpoint, string url)
    {
        if (string.IsNullOrEmpty(url))
            return endpoint;

        return QueryHelpers.AddQueryString(endpoint, new Dictionary<string, string?>
            {
                { "url", url }
            });
    }

    /// <summary>
    /// メディアタイプに応じたEmbedResponseのデシリアライズ
    /// </summary>
    private EmbedResponse DeserializeEmbedResponse(string content, string? mediaType)
    {
        switch (mediaType)
        {
            case MediaTypeNames.Application.Json:
            case MediaTypeNames.Text.Plain:
            case MediaTypeNames.Text.Html:
                var options = new JsonSerializerOptions();
                options.Converters.Add(new AutoNumberToStringConverter());
                return JsonSerializer.Deserialize<EmbedResponse>(content, options)
                    ?? new EmbedResponse();

            case MediaTypeNames.Application.Xml:
            case MediaTypeNames.Text.Xml:
                {
                    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
                    return (EmbedResponse)new XmlSerializer(typeof(EmbedResponse)).Deserialize(stream)!
                           ?? new EmbedResponse();
                }

            default:
                throw new InvalidDataException($"Unsupported media type: {mediaType}");
        }
    }

}
