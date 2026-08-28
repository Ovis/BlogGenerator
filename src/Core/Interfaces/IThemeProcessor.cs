namespace BlogGenerator.Core.Interfaces;

public interface IThemeProcessor
{
    void CopyThemeFilesToOutput(string themeDir, string outputDir);
    Task CopyThemeFilesToOutputAsync(string themeDir, string outputDir);
}
