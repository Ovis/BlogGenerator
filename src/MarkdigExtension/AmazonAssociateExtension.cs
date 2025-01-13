using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Syntax.Inlines;

namespace BlogGenerator.MarkdigExtension;

public class AmazonAssociateExtension(string affiliateId) : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.InlineParsers.Contains<AmazonAssociateParser>())
        {
            pipeline.InlineParsers.Insert(0, new AmazonAssociateParser(affiliateId));
        }
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }
}

public partial class AmazonAssociateParser : InlineParser
{
    private readonly string _associateId;

    public AmazonAssociateParser(string associateId)
    {
        _associateId = associateId;
        OpeningCharacters = ['['];
    }

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        var precedingCharacter = slice.PeekCharExtra(-1);
        if (!precedingCharacter.IsWhiteSpaceOrZero())
        {
            return false;
        }

        var regex = AmazonAffiliateTagRegex();
        var match = regex.Match(slice.ToString());

        if (!match.Success)
        {
            return false;
        }

        var itemCode = match.Groups["itemCode"].Value;

        var sb = new StringBuilder();
        sb.AppendLine($"<iframe style=\"width: 120px; height: 240px;\" marginwidth=\"0\" marginheight=\"0\" scrolling=\"no\" frameborder=\"0\"")
            .AppendLine($"src=\"")
            .AppendLine($"https://rcm-fe.amazon-adsystem.com/e/cm?ref=qf_sp_asin_til&t={_associateId}&m=amazon&o=9&p=8&l=as1&IS2=1&detail=1&asins={itemCode}&bc1=000000&amp;lt1=_blank&fc1=333333&lc1=0066c0&bg1=ffffff&f=ifr")
            .AppendLine("\">")
            .AppendLine("</iframe>");

        processor.Inline = new HtmlInline(sb.ToString())
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

    [GeneratedRegex(@"\[amazon:(?<itemCode>\w+)]")]
    private static partial Regex AmazonAffiliateTagRegex();
}
