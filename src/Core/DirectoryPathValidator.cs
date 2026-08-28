namespace BlogGenerator.Core;

internal static class DirectoryPathValidator
{
    public static void ThrowIfOutputDirectoryIsInputSubdirectory(string inputDir, string outputDir)
    {
        var normalizedInputDir = NormalizeDirectoryPath(inputDir);
        var normalizedOutputDir = NormalizeDirectoryPath(outputDir);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (string.Equals(normalizedInputDir, normalizedOutputDir, comparison) ||
            normalizedOutputDir.StartsWith(normalizedInputDir + Path.DirectorySeparatorChar, comparison))
        {
            throw new ArgumentException("Output directory must not be the same as or a subdirectory of the input directory.");
        }
    }

    private static string NormalizeDirectoryPath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
