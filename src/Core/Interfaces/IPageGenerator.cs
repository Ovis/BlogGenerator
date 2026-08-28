using BlogGenerator.Models;

namespace BlogGenerator.Core.Interfaces;

/// <summary>
/// サイトを構成する各種HTMLページの生成機能を提供する
/// </summary>
public interface IPageGenerator
{
    /// <summary>
    /// 公開済み通常記事からサイドバーHTMLを生成する
    /// </summary>
    Task<TrustedHtml> GenerateSideBarHtmlAsync(List<Article> articles);

    /// <summary>
    /// 指定された記事をファイルへ書き出さずにレンダリングし、生成可能か検証する
    /// </summary>
    Task ValidateArticlePageAsync(Article article, List<Article> publishedArticles, TrustedHtml sideBarHtml);

    /// <summary>
    /// 公開対象の記事と固定ページの個別HTMLファイルを生成する
    /// </summary>
    Task GenerateArticlePagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml);

    /// <summary>
    /// サイトトップの記事一覧ページを生成する
    /// </summary>
    Task GenerateIndexPagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml);

    /// <summary>
    /// タグ一覧ページとタグ別の記事一覧ページを生成する
    /// </summary>
    Task GenerateTagPagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml);

    /// <summary>
    /// 年月別の記事アーカイブページを生成する
    /// </summary>
    Task GenerateArchivePagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml);
}
