namespace BlogGenerator.Models;

public class FeedOption
{
    /// <summary>
    /// RSS2.0フィードを生成するかどうか
    /// </summary>
    public bool UseRss2 { get; set; } = true;

    /// <summary>
    /// フィードのファイル名（RSS）
    /// </summary>
    public string RssFileName { get; set; } = "feed.rss";

    /// <summary>
    /// Atomフィードを生成するかどうか
    /// </summary>
    public bool UseAtom { get; set; } = true;

    /// <summary>
    /// フィードのファイル名（Atom）
    /// </summary>
    public string AtomFileName { get; set; } = "feed.atom";

    /// <summary>
    /// フィードに含める記事の最大数
    /// </summary>
    public int MaxFeedItems { get; set; } = 10;

    /// <summary>
    /// フィードの言語
    /// </summary>
    public string Language { get; set; } = "ja-JP";
}
