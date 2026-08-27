using BlogGenerator.Models;

namespace BlogGenerator.Core.Interfaces;

public interface IPageGenerator
{
    Task<TrustedHtml> GenerateSideBarHtmlAsync(List<Article> articles);
    Task ValidateArticlePageAsync(Article article, List<Article> publishedArticles, TrustedHtml sideBarHtml);
    Task GenerateArticlePagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml);
    Task GenerateIndexPagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml);
    Task GenerateTagPagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml);
    Task GenerateArchivePagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml);
}
