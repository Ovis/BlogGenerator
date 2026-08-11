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
using Markdig.Renderers.Html;
using Markdig.Syntax;
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

                OEmbedCardParser = new OEmbedCardParser(new OEmbedResolver(OEmbedProviderCatalog, HttpClient));
            }
        }

        if (!pipeline.InlineParsers.Contains<OEmbedCardParser>())
        {
            pipeline.InlineParsers.Insert(0, OEmbedCardParser);
        }
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer htmlRenderer && !htmlRenderer.ObjectRenderers.Contains<OEmbedInlineRenderer>())
        {
            htmlRenderer.ObjectRenderers.Insert(0, new OEmbedInlineRenderer());
        }
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
    private static OEmbedResolver _oEmbedResolver = new(new OEmbedProviderCatalog([]), new HttpClient());

    // OEmbedCacheをパブリックプロパティとして公開
    public static ConcurrentDictionary<string, string> OEmbedCache => _oEmbedResolver.OEmbedCache;

    private static readonly Regex OEmbedTagRegex = new(@"\[oembed:""(?<url>https?:\/\/[^""]+)""\]");

    public OEmbedCardParser(OEmbedResolver oEmbedResolver)
    {
        _oEmbedResolver = oEmbedResolver;
        OpeningCharacters = ['['];
    }

    public static ValueTask<string> ResolveHtmlAsync(string url)
    {
        return _oEmbedResolver.GetOEmbedHtmlAsync(url);
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

        // インラインとして処理
        processor.Inline = new OEmbedInline(url)
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
}

public static class OEmbedDocumentResolver
{
    /// <summary>
    /// Markdown文書内のoEmbedノードへ解決済みHTMLを設定する
    /// </summary>
    public static async Task ResolveAsync(MarkdownDocument markdownDocument)
    {
        foreach (var oEmbedInline in markdownDocument.Descendants<OEmbedInline>())
        {
            oEmbedInline.HtmlContent = await OEmbedCardParser.ResolveHtmlAsync(oEmbedInline.Url);
        }
    }
}
