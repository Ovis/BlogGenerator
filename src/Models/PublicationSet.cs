namespace BlogGenerator.Models;

internal sealed record PublicationSet(
    IReadOnlyList<Article> PublishedContents,
    IReadOnlyList<Article> ScheduledContents,
    IReadOnlyList<Article> DraftContents)
{
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
