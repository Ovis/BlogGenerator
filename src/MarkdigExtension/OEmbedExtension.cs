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
    private static OEmbedResolver _oEmbedResolver = new(new OEmbedProviderCatalog([]), HttpClient);

    public static OEmbedCardParser OEmbedCardParser { get; private set; } = null!;
    public static OEmbedResolver OEmbedResolver => _oEmbedResolver;

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

                _oEmbedResolver = new OEmbedResolver(OEmbedProviderCatalog, HttpClient);
                OEmbedCardParser = new OEmbedCardParser(_oEmbedResolver);
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

    internal static void SetResolver(OEmbedResolver oEmbedResolver)
    {
        _oEmbedResolver = oEmbedResolver;
    }
}

/// <summary>
/// oembedタグを解析し、HTML化するパーサー
/// </summary>
public class OEmbedCardParser : InlineParser
{
    private static readonly Regex OEmbedTagRegex = new(@"\[oembed:""(?<url>https?:\/\/[^""]+)""\]");

    public OEmbedCardParser(OEmbedResolver oEmbedResolver)
    {
        OEmbedCardExtension.SetResolver(oEmbedResolver);
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
