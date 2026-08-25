using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;

namespace BlogGenerator.MarkdigExtension;

public class AmazonAssociateExtension(string affiliateId) : IMarkdownExtension
{
    private readonly AmazonAssociateParser _parser = new();
    private readonly AmazonInlineRenderer _renderer = new(affiliateId);

    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.InlineParsers.Contains(_parser))
        {
            pipeline.InlineParsers.Insert(0, _parser);
        }
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer htmlRenderer && !htmlRenderer.ObjectRenderers.Contains<AmazonInlineRenderer>())
        {
            htmlRenderer.ObjectRenderers.Insert(0, _renderer);
        }
    }
}

public partial class AmazonAssociateParser : InlineParser
{
    public AmazonAssociateParser()
    {
        OpeningCharacters = ['['];
    }

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        var precedingCharacter = slice.PeekCharExtra(-1);
        if (!precedingCharacter.IsWhiteSpaceOrZero())
        {
            return false;
        }

        var match = AmazonShortcodeRegex().Match(slice.ToString());
        if (!match.Success || !TryCreateInline(match, out var amazonInline))
        {
            return false;
        }

        // パース段階では外部アクセスやHTML生成を行わず、後続の解決・描画処理へ必要な値だけを渡す
        processor.Inline = amazonInline;
        processor.Inline.Span.Start = processor.GetSourcePosition(slice.Start, out var line, out var column);
        processor.Inline.Line = line;
        processor.Inline.Column = column;
        processor.Inline.IsClosed = true;
        processor.Inline.Span.End = processor.Inline.Span.Start + match.Length - 1;
        slice.Start += match.Length;
        return true;
    }

    private static bool TryCreateInline(Match match, out AmazonInline amazonInline)
    {
        string? manualTitle = null;
        string? manualImageId = null;
        var seenAttributes = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < match.Groups["name"].Captures.Count; index++)
        {
            var name = match.Groups["name"].Captures[index].Value;
            var value = match.Groups["value"].Captures[index].Value;

            // 属性の見落としや上書きを防ぐため、明示的に許可した属性だけを一度ずつ受理する
            if (!seenAttributes.Add(name))
            {
                amazonInline = null!;
                return false;
            }

            switch (name)
            {
                case "title":
                    manualTitle = value;
                    break;
                case "image":
                    manualImageId = value;
                    break;
                default:
                    amazonInline = null!;
                    return false;
            }
        }

        amazonInline = new AmazonInline(
            match.Groups["asin"].Value.ToUpperInvariant(),
            manualTitle,
            manualImageId);
        return true;
    }

    [GeneratedRegex(@"^\[amazon:(?<asin>[A-Za-z0-9]{10})(?:,(?<name>[a-z]+)=""(?<value>[^""]*)"")*\]")]
    private static partial Regex AmazonShortcodeRegex();
}
