using System.CommandLine;

namespace BlogGenerator.Core;

public class CommandLineSetup
{
    public Option<DirectoryInfo> InputOption { get; } = new(
            aliases: ["/input", "--input", "-i"],
            description: "入力フォルダー")
    { IsRequired = true };

    public Option<DirectoryInfo> OutputOption { get; } = new(
            aliases: ["/output", "--output", "-o"],
            description: "出力フォルダー")
    { IsRequired = true };

    public Option<DirectoryInfo> ThemeOption { get; } = new(
            aliases: ["/theme", "--theme"],
            description: "出力フォルダー")
    { IsRequired = true };

    public Option<string> OEmbedOption { get; } = new(
            aliases: ["/oembed", "--oembed"],
            description: "oEmbedキャッシュファイル")
    { IsRequired = false };

    public RootCommand CreateRootCommand()
    {
        return new RootCommand("Markdown to HTML generator")
        {
            InputOption,
            OutputOption,
            ThemeOption,
            OEmbedOption
        };
    }
}
