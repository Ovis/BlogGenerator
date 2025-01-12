namespace BlogGenerator.Models;

public class Frontmatter
{
    public string Title { get; set; } = string.Empty;

    public DateTimeOffset Published { get; set; } = DateTimeOffset.MinValue;

    public List<string> Tags { get; set; } = [];

    public string Eyecatch { get; set; } = string.Empty;

    public bool IsFixedPage { get; set; } = false;
}
