using BlogGenerator.Core.Interfaces;

namespace BlogGenerator.Core;

public class ThemeProcessor(IFileSystemHelper fileSystemHelper) : IThemeProcessor
{
    public void CopyThemeFilesToOutput(string themeDir, string outputDir)
    {
        // themeDirに渡されたフォルダパスから、cshtmlファイル以外のファイル、フォルダをoutputDirにコピー
        foreach (var themeFile in Directory.GetFiles(themeDir, "*", SearchOption.AllDirectories)
                     .Where(x => !x.EndsWith(".cshtml") && !Path.GetFileName(x).StartsWith(".")))
        {
            var relativePath = Path.GetRelativePath(themeDir, themeFile);
            var outputPath = Path.Combine(outputDir, relativePath);

            var outputDirPath = Path.GetDirectoryName(outputPath);
            fileSystemHelper.EnsureDirectoryExists(outputDirPath!);

            File.Copy(themeFile, outputPath, true);
        }
    }
}
