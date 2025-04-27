namespace BlogGenerator.Core.Interfaces;

public interface IThemeProcessor
{
    void CopyThemeFilesToOutput(string themeDir, string outputDir);
}
