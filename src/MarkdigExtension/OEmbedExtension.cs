using System.Text.RegularExpressions;
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
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15) // 15秒でタイムアウト
    };
    private static readonly SemaphoreSlim InitializationSemaphore = new(1, 1);

    private static OEmbedResolver _oEmbedResolver = new(new OEmbedProviderCatalog([]), HttpClient);
    private static bool _isInitialized;

    public static OEmbedCardParser OEmbedCardParser { get; private set; } = new(_oEmbedResolver);
    public static OEmbedResolver OEmbedResolver => _oEmbedResolver;

    public void Setup(MarkdownPipelineBuilder pipeline)
    {
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

    public static async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        await InitializationSemaphore.WaitAsync();
        try
        {
            if (_isInitialized)
                return;

            if (!HttpClient.DefaultRequestHeaders.UserAgent.Any())
            {
                HttpClient.DefaultRequestHeaders.Add("User-Agent", "BlogGenerator");
            }

            var providerCatalog = await new OEmbedProviderCatalogLoader(HttpClient).LoadAsync();
            _oEmbedResolver = new OEmbedResolver(providerCatalog, HttpClient);
            OEmbedCardParser = new OEmbedCardParser(_oEmbedResolver);
            _isInitialized = true;
        }
        finally
        {
            InitializationSemaphore.Release();
        }
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
