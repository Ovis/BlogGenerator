namespace BlogGenerator.Core;

/// <summary>
/// ビルドで使用する入力・出力ディレクトリの安全性を検証する
/// </summary>
internal static class DirectoryPathValidator
{
    /// <summary>
    /// 入力元と出力先が同一、またはいずれかが他方の配下である場合に例外を送出する
    /// </summary>
    /// <remarks>
    /// 出力先は成果物生成前に再作成するため、出力先が入力元の親ディレクトリである場合も
    /// 入力ファイルを削除する危険がある。入出力ディレクトリは完全に分離されている必要がある
    /// </remarks>
    public static void ThrowIfInputAndOutputDirectoriesOverlap(string inputDir, string outputDir)
    {
        var normalizedInputDir = NormalizeDirectoryPath(inputDir);
        var normalizedOutputDir = NormalizeDirectoryPath(outputDir);

        // Windowsのファイルシステムでは通常大文字小文字を区別しないため、OSに合わせて比較方法を切り替える
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (string.Equals(normalizedInputDir, normalizedOutputDir, comparison) ||
            normalizedOutputDir.StartsWith(normalizedInputDir + Path.DirectorySeparatorChar, comparison) ||
            normalizedInputDir.StartsWith(normalizedOutputDir + Path.DirectorySeparatorChar, comparison))
        {
            throw new ArgumentException("Input and output directories must not be the same or nested within each other.");
        }
    }

    /// <summary>
    /// ディレクトリパスを絶対パスへ変換し、末尾の区切り文字を除去する
    /// </summary>
    private static string NormalizeDirectoryPath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
