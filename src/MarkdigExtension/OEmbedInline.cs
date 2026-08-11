using Markdig.Syntax.Inlines;

namespace BlogGenerator.MarkdigExtension;

public class OEmbedInline(string url) : LeafInline
{
    public string Url { get; } = url;

    public string HtmlContent { get; set; } = string.Empty;
}
