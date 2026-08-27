namespace BlogGenerator.Models;

public class Frontmatter
{
    public string Title { get; set; } = string.Empty;

    // Front Matter自体がない既存入力との互換性のため、DTOの初期値のみMinValueを維持する。
    // Publishedキーを持つFront MatterはMarkdownProcessorで明示的にnull/日時へ正規化する。
    public DateTimeOffset? Published { get; set; } = DateTimeOffset.MinValue;

    public List<string> Tags { get; set; } = [];

    public string Eyecatch { get; set; } = string.Empty;

    public bool IsFixedPage { get; set; } = false;

    public string Template { get; set; } = string.Empty;
}
