using static System.Text.RegularExpressions.Regex;

namespace BlogGenerator.Models;

public record Article(
    string FileName,
    string Title,
    string Body,
    List<string> Tags,
    DateTimeOffset? Published,
    string RelativeDirectoryPath,
    string RootRelativeDirectoryPath,
    bool IsFixedPage,
    string Template = "")
{
    // 本文HTMLは Markdown 変換済みなので、テンプレート側でだけ明示的に生出力する
    public TrustedHtml BodyHtml => new(Body);

    public string ExcerptHtml => Body.SplitHtml().excerptHtml;
    public string RemainingHtml => Body.SplitHtml().remainingHtml;

    // 抜粋HTMLも本文と同じく、通常文字列とは分けて扱う
    public TrustedHtml ExcerptHtmlContent => new(ExcerptHtml);

    public string Description
    {
        get
        {
            var plainText = Replace(Body.SplitHtml().excerptHtml, "<.*?>", string.Empty);
            return plainText.Length > 50 ? plainText[..50] + "..." : plainText;
        }
    }

    public string RootRelativePath => PageModelBase.CombineUrlPath(RootRelativeDirectoryPath, FileName);

    internal PublicationState GetPublicationState(DateTimeOffset buildTime)
    {
        if (Published is null)
        {
            return PublicationState.Draft;
        }

        return Published <= buildTime
            ? PublicationState.Published
            : PublicationState.Scheduled;
    }
}

public static class ArticleExtensions
{
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
