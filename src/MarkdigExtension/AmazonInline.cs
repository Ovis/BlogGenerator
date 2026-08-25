using Markdig.Syntax.Inlines;

namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// Amazon shortcodeの入力値を保持するインラインノード
/// </summary>
public sealed class AmazonInline(string asin, string? manualTitle, string? manualImageId) : LeafInline
{
    public string Asin { get; } = asin;

    public string? ManualTitle { get; } = manualTitle;

    public string? ManualImageId { get; } = manualImageId;

    public string HtmlContent { get; set; } = string.Empty;
}
