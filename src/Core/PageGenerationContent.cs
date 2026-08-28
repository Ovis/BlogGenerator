using BlogGenerator.Models;

namespace BlogGenerator.Core;

/// <summary>
/// ページ生成で共通して使用する記事選別とタグカタログ生成を提供する
/// </summary>
internal static class PageGenerationContent
{
    /// <summary>
    /// 固定ページと未公開コンテンツを除外し、記事一覧に表示できる通常記事だけを返す
    /// </summary>
    public static IEnumerable<Article> GetRegularArticles(IEnumerable<Article> articles) =>
        articles.Where(article => !article.IsFixedPage && IsRenderableContent(article));

    /// <summary>
    /// 公開日時が設定され、ページ生成対象として扱えるコンテンツか判定する
    /// </summary>
    public static bool IsRenderableContent(Article article) =>
        article.Published is { } published && published != DateTimeOffset.MinValue;

    /// <summary>
    /// 公開済み通常記事からタグカタログを生成する
    /// </summary>
    public static TagCatalog CreateTagCatalog(IEnumerable<Article> articles) =>
        TagCatalog.Build(articles, message => Console.Error.WriteLine($"[tag warning] {message}"));
}
