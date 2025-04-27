namespace BlogGenerator.Core;

public class FileSystemHelper
{
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

    public void CopyContentFile(string inputDir, string outputDir, string filePath)
    {
        var relativePath = Path.GetRelativePath(inputDir, filePath);
        var outputPath = Path.Combine(outputDir, relativePath);

        // 出力フォルダパス
        var outputDirPath = Path.GetDirectoryName(outputPath);
        if (!Directory.Exists(outputDirPath))
        {
            Directory.CreateDirectory(outputDirPath!);
        }

        var directoryInfo = new DirectoryInfo(Path.GetDirectoryName(filePath)!);
        foreach (var dir in directoryInfo.GetDirectories("*", SearchOption.AllDirectories))
        {
            var targetDir = dir.FullName.Replace(inputDir, outputDir);
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }
        }

        foreach (var fileInfo in directoryInfo.GetFiles("*", SearchOption.AllDirectories))
        {
            if (fileInfo.FullName != filePath && Path.GetExtension(fileInfo.FullName) != ".md" && !Path.GetFileName(fileInfo.FullName).StartsWith("."))
            {
                var targetFile = fileInfo.FullName.Replace(inputDir, outputDir);

                const int maxRetries = 3;
                const int delayMilliseconds = 3000;
                var attempt = 0;
                var success = false;

                while (!success)
                {
                    try
                    {
                        File.Copy(fileInfo.FullName, targetFile, true);
                        success = true;
                    }
                    catch (IOException ex) when (ex.Message.Contains("being used by another process"))
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
        }
    }
}
