using BlogGenerator.Core.Interfaces;

namespace BlogGenerator.Core;

public class FileSystemHelper : IFileSystemHelper
{
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

    public void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    public string CombineFilePath(string outputDir, string relativePath, string? extension = null)
    {
        var combinedPath = Path.Combine(outputDir, relativePath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
        return extension == null ? combinedPath : Path.ChangeExtension(combinedPath, extension);
    }

    public void CopyContentFiles(string inputDir, string outputDir)
    {
        foreach (var filePath in Directory.GetFiles(inputDir, "*", SearchOption.AllDirectories))
        {
            if (Path.GetExtension(filePath) == ".md" || Path.GetFileName(filePath).StartsWith("."))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(inputDir, filePath);
            var targetFilePath = Path.Combine(outputDir, relativePath);
            EnsureDirectoryExists(Path.GetDirectoryName(targetFilePath)!);

            CopyFileWithRetry(filePath, targetFilePath);
        }
    }

    private static void CopyFileWithRetry(string sourceFilePath, string targetFilePath)
    {
        const int maxRetries = 3;
        const int delayMilliseconds = 3000;
        var attempt = 0;
        var success = false;

        while (!success)
        {
            try
            {
                File.Copy(sourceFilePath, targetFilePath, true);
                success = true;
            }
            catch (IOException ex) when (IsRetryableCopyIOException(ex))
            {
                attempt++;
                if (attempt < maxRetries)
                {
                    Thread.Sleep(delayMilliseconds);
                }
                else
                {
                    throw;
                }
            }
        }
    }

    private static bool IsRetryableCopyIOException(IOException ex)
    {
        var win32Error = ex.HResult & 0xFFFF;
        return win32Error is ErrorSharingViolation or ErrorLockViolation;
    }
}
