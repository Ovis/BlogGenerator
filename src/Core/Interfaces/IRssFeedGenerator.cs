using BlogGenerator.Models;

namespace BlogGenerator.Core.Interfaces;

public interface IRssFeedGenerator
{
    Task GenerateRssAndAtomFeedsAsync(List<Article> articles, string outputDir);
}
