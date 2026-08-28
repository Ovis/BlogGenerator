namespace BlogGenerator.Core;

/// <summary>
/// ビルドで使用する入力・出力ディレクトリの安全性を検証する
/// </summary>
internal static class DirectoryPathValidator
{
    /// <summary>
    /// 出力先が入力元と同一、または入力元の配下である場合に例外を送出する
    /// </summary>
    /// <remarks>
    /// 出力したファイルを次回以降の入力として再帰的に読み込むことを防止するための検証
    /// </remarks>
    public static void ThrowIfOutputDirectoryIsInputSubdirectory(string inputDir, string outputDir)
    {
        var normalizedInputDir = NormalizeDirectoryPath(inputDir);
        var normalizedOutputDir = NormalizeDirectoryPath(outputDir);

        // Windowsのファイルシステムでは通常大文字小文字を区別しないため、OSに合わせて比較方法を切り替える
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (string.Equals(normalizedInputDir, normalizedOutputDir, comparison) ||
            normalizedOutputDir.StartsWith(normalizedInputDir + Path.DirectorySeparatorChar, comparison))
        {
            throw new ArgumentException("Output directory must not be the same as or a subdirectory of the input directory.");
        }
    }

    /// <summary>
    /// ディレクトリパスを絶対パスへ変換し、末尾の区切り文字を除去する
    /// </summary>
    private static string NormalizeDirectoryPath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
