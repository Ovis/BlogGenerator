using BlogGenerator.Core.Interfaces;

namespace BlogGenerator.Core;

/// <summary>
/// テーマ内の静的ファイルを出力ディレクトリへ配置する
/// </summary>
public class ThemeProcessor(IFileSystemHelper fileSystemHelper) : IThemeProcessor
{
    /// <summary>
    /// 従来の同期APIを維持しつつ、内部では非同期コピー処理を使用する
    /// </summary>
    public void CopyThemeFilesToOutput(string themeDir, string outputDir) =>
        CopyThemeFilesToOutputAsync(themeDir, outputDir).GetAwaiter().GetResult();

    /// <summary>
    /// Razorテンプレート以外のテーマファイルを上限付きで並列コピーする
    /// </summary>
    public async Task CopyThemeFilesToOutputAsync(string themeDir, string outputDir)
    {
        var themeFiles = Directory.EnumerateFiles(themeDir, "*", SearchOption.AllDirectories)
            .Where(path =>
                !string.Equals(Path.GetExtension(path), ".cshtml", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(path).StartsWith("."))
            .ToArray();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = BuildConcurrency.FileCopyDegreeOfParallelism
        };

        await Parallel.ForEachAsync(themeFiles, parallelOptions, async (themeFile, _) =>
        {
            var relativePath = Path.GetRelativePath(themeDir, themeFile);
            var outputPath = Path.Combine(outputDir, relativePath);
            fileSystemHelper.EnsureDirectoryExists(Path.GetDirectoryName(outputPath)!);
            await fileSystemHelper.CopyFileAsync(themeFile, outputPath, overwrite: true);
        });
    }
}
