using Markdig.Renderers;
using Markdig.Renderers.Html;

namespace BlogGenerator.MarkdigExtension;

public class OEmbedInlineRenderer : HtmlObjectRenderer<OEmbedInline>
{
    protected override void Write(HtmlRenderer renderer, OEmbedInline obj)
    {
        renderer.Write(obj.HtmlContent);
    }
}
