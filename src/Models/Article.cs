namespace BlogGenerator.Models;

public record Article(
    string FileName,
    string Title,
    string Body,
    List<string> Tags,
    DateTimeOffset Published,
    string RelativeDirectoryPath,
    string RootRelativeDirectoryPath,
    bool IsFixedPage)
{
    public string ExcerptHtml => Body.SplitHtml().excerptHtml;
    public string RemainingHtml => Body.SplitHtml().remainingHtml;

    public string RootRelativePath => Path.Combine(RootRelativeDirectoryPath, FileName).Replace("\\", "/");
}

public static class ArticleExtensions
{
    /// <summary>
    /// 分割した2つのHtmlを返す
    /// </summary>
    /// <param name="html"></param>
    /// <returns></returns>
    public static (string excerptHtml, string remainingHtml) SplitHtml(this string html)
    {
        const string moreTag = "<!-- more -->";
        var excerptHtml = html;
        var remainingHtml = string.Empty;
        var moreIndex = html.IndexOf(moreTag, StringComparison.Ordinal);

        if (moreIndex >= 0)
        {
            excerptHtml = html[..moreIndex];
            remainingHtml = html[(moreIndex + moreTag.Length)..];
        }

        return (excerptHtml, remainingHtml);
    }

}
