using BlogGenerator.Models;

namespace BlogGenerator.Core.Interfaces;

public interface IMarkdownProcessor
{
    Task InitializeAsync();
    Task<List<Article>> ProcessMarkdownFilesAsync(string inputDir, string outputDir, string baseAbsolutePath);
}
