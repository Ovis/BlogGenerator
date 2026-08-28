namespace BlogGenerator.Models;

/// <summary>
/// ビルド対象コンテンツを、公開済み、予約公開、下書きの状態別に分類した結果
/// </summary>
internal sealed record PublicationSet(
    IReadOnlyList<Article> PublishedContents,
    IReadOnlyList<Article> ScheduledContents,
    IReadOnlyList<Article> DraftContents)
{
    /// <summary>
    /// 指定されたビルド時刻を基準として、すべての記事を公開状態別に分類する
    /// </summary>
    /// <param name="articles">分類対象の記事</param>
    /// <param name="buildTime">公開状態を判定する基準時刻</param>
    /// <returns>公開状態別に分類された記事集合</returns>
    internal static PublicationSet Create(IEnumerable<Article> articles, DateTimeOffset buildTime)
    {
        ArgumentNullException.ThrowIfNull(articles);

        var published = new List<Article>();
        var scheduled = new List<Article>();
        var drafts = new List<Article>();

        foreach (var article in articles)
        {
            ArgumentNullException.ThrowIfNull(article);

            switch (article.GetPublicationState(buildTime))
            {
                case PublicationState.Draft:
                    drafts.Add(article);
                    break;
                case PublicationState.Scheduled:
                    scheduled.Add(article);
                    break;
                case PublicationState.Published:
                    published.Add(article);
                    break;
                case PublicationState.Undefined:
                default:
                    throw new InvalidOperationException($"Unexpected publication state for '{article.RootRelativePath}'.");
            }
        }

        // 予約コンテンツは公開予定時刻、同時刻ならパスの順に固定し、検証結果を再現可能にする
        scheduled.Sort((left, right) =>
        {
            var publishedComparison = Nullable.Compare(left.Published, right.Published);
            return publishedComparison != 0
                ? publishedComparison
                : StringComparer.Ordinal.Compare(left.RootRelativePath, right.RootRelativePath);
        });

        return new PublicationSet(published, scheduled, drafts);
    }
}
