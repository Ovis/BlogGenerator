using BlogGenerator.Models;

namespace BlogGenerator.Core.Rendering;

/// <summary>
/// 1回のページ生成で共有する記事集合とタグカタログを保持する
/// </summary>
internal sealed record PageGenerationContext(
    IReadOnlyList<Article> Articles,
    IReadOnlyList<Article> RegularArticles,
    TagCatalog TagCatalog)
{
    /// <summary>
    /// 公開対象の記事集合からページ生成用コンテキストを構築する
    /// </summary>
    public static PageGenerationContext Create(IReadOnlyList<Article> articles)
    {
        var regularArticles = PageGenerationContent.GetRegularArticles(articles).ToArray();
        var tagCatalog = PageGenerationContent.CreateTagCatalog(regularArticles);
        return new PageGenerationContext(articles, regularArticles, tagCatalog);
    }
}
