namespace BlogGenerator.Core.Interfaces;

public interface IFileSystemHelper
{
    void EnsureDirectoryExists(string path);
    string CombineFilePath(string outputDir, string relativePath, string? extension = null);
    void CopyContentFiles(string inputDir, string outputDir);
}
