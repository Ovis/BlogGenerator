using BlogGenerator.Models;

namespace BlogGenerator.Core.Interfaces;

public interface IMarkdownProcessor
{
    System.Collections.Concurrent.ConcurrentDictionary<string, BlogGenerator.MarkdigExtension.OEmbedCacheEntry> OEmbedCache { get; }
    Task InitializeAsync();
    Task<List<Article>> ProcessMarkdownFilesAsync(string inputDir, string outputDir, string baseAbsolutePath);
}
