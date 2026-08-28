using BlogGenerator.Core.Interfaces;
using BlogGenerator.Models;

namespace BlogGenerator.Core;

/// <summary>
/// 予約公開されるコンテンツが、公開時に正常にページ生成できることを事前検証する
/// </summary>
internal sealed class ScheduledContentValidator(IPageGenerator pageGenerator)
{
    /// <summary>
    /// 予約コンテンツをすべて検証し、検証エラーがある場合はまとめて例外として通知する
    /// </summary>
    /// <param name="scheduledContents">予約公開待ちのコンテンツ</param>
    /// <param name="publishedContents">現在公開済みのコンテンツ</param>
    public async Task ValidateAsync(
        IReadOnlyList<Article> scheduledContents,
        IReadOnlyList<Article> publishedContents)
    {
        // 実際の公開ページと同じ条件で検証するため、現在公開済みの記事からサイドバーを生成する
        var published = publishedContents.ToList();
        var validationSideBar = await pageGenerator.GenerateSideBarHtmlAsync(published);
        var validationErrors = new List<Exception>();

        // 1件のエラーで検証を打ち切らず、修正対象を一度に確認できるよう全件を検証する
        foreach (var scheduled in scheduledContents)
        {
            try
            {
                await pageGenerator.ValidateArticlePageAsync(scheduled, published, validationSideBar);
                Console.WriteLine($"Scheduled content validated: {scheduled.RootRelativePath} (Published: {scheduled.Published:yyyy-MM-dd HH:mm:ss zzz})");
            }
            catch (Exception ex)
            {
                validationErrors.Add(new InvalidOperationException(
                    $"Failed to validate scheduled content '{scheduled.RootRelativePath}' (Published: {scheduled.Published:yyyy-MM-dd HH:mm:ss zzz}).",
                    ex));
            }
        }

        if (validationErrors.Count != 0)
            throw new AggregateException("One or more scheduled contents failed validation.", validationErrors);
    }
}
