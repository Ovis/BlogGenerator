using Markdig.Syntax.Inlines;

namespace BlogGenerator.MarkdigExtension;

public class OEmbedInline(string htmlContent) : LeafInline
{
    public string HtmlContent { get; } = htmlContent;
}
