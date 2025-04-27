using BlogGenerator.Models;

namespace BlogGenerator.Core.Interfaces;

public interface IPageGenerator
{
    Task<string> GenerateSideBarHtmlAsync(List<Article> articles);
    Task GenerateArticlePagesAsync(List<Article> articles, string outputDir, string sideBarHtml);
    Task GenerateIndexPagesAsync(List<Article> articles, string outputDir, string sideBarHtml);
    Task GenerateTagPagesAsync(List<Article> articles, string outputDir, string sideBarHtml);
    Task GenerateArchivePagesAsync(List<Article> articles, string outputDir, string sideBarHtml);
}
