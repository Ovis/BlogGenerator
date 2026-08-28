using BlogGenerator.Core.Interfaces;

namespace BlogGenerator.Core;

/// <summary>
/// ビルドで使用するファイルシステム操作を提供する
/// </summary>
public class FileSystemHelper : IFileSystemHelper
{
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2)
    ];

    /// <summary>
    /// 指定されたディレクトリを存在する状態にする
    /// </summary>
    public void EnsureDirectoryExists(string path) => Directory.CreateDirectory(path);

    public string CombineFilePath(string outputDir, string relativePath, string? extension = null)
    {
        var combinedPath = Path.Combine(outputDir, relativePath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
        return extension == null ? combinedPath : Path.ChangeExtension(combinedPath, extension);
    }

    /// <summary>
    /// 従来の同期APIを維持しつつ、内部では非同期コピー処理を使用する
    /// </summary>
    public void CopyContentFiles(string inputDir, string outputDir) =>
        CopyContentFilesAsync(inputDir, outputDir).GetAwaiter().GetResult();

    /// <summary>
    /// Markdown以外のコンテンツファイルを上限付きで並列コピーする
    /// </summary>
    public async Task CopyContentFilesAsync(string inputDir, string outputDir)
    {
        var filePaths = Directory.EnumerateFiles(inputDir, "*", SearchOption.AllDirectories)
            .Where(filePath =>
                !string.Equals(Path.GetExtension(filePath), ".md", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(filePath).StartsWith("."))
            .ToArray();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = BuildConcurrency.FileCopyDegreeOfParallelism
        };

        await Parallel.ForEachAsync(filePaths, parallelOptions, async (filePath, cancellationToken) =>
        {
            var relativePath = Path.GetRelativePath(inputDir, filePath);
            var targetFilePath = Path.Combine(outputDir, relativePath);
            EnsureDirectoryExists(Path.GetDirectoryName(targetFilePath)!);
            await CopyFileAsync(filePath, targetFilePath, overwrite: true, cancellationToken);
        });
    }

    /// <summary>
    /// 一時的な共有違反・ロック違反だけを待機付きで再試行してファイルをコピーする
    /// </summary>
    /// <remarks>
    /// ウイルス対策ソフト、インデクサー、同期ソフトなどが短時間ファイルを開く場合があるため、
    /// 共有違反自体を完全に防ぐことはできない。同期Thread.Sleepは使用せず、他のコピー処理を塞がない形で再試行する
    /// </remarks>
    public Task CopyFileAsync(string sourceFilePath, string targetFilePath, bool overwrite = true) =>
        CopyFileAsync(sourceFilePath, targetFilePath, overwrite, CancellationToken.None);

    /// <summary>
    /// キャンセル可能なファイルコピーを実行する
    /// </summary>
    private static async Task CopyFileAsync(
        string sourceFilePath,
        string targetFilePath,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Copy(sourceFilePath, targetFilePath, overwrite);
                return;
            }
            catch (IOException ex) when (IsRetryableCopyIOException(ex) && attempt < RetryDelays.Length)
            {
                await Task.Delay(RetryDelays[attempt], cancellationToken);
            }
        }
    }

    /// <summary>
    /// Windowsの共有違反またはロック違反として再試行可能なIOExceptionか判定する
    /// </summary>
    private static bool IsRetryableCopyIOException(IOException ex)
    {
        var win32Error = ex.HResult & 0xFFFF;
        return win32Error is ErrorSharingViolation or ErrorLockViolation;
    }
}
