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
            description: "テーマフォルダー")
    { IsRequired = true };

    public Option<string> OEmbedOption { get; } = new(
            aliases: ["/oembed", "--oembed"],
            description: "oEmbedキャッシュファイル")
    { IsRequired = false };

    // 設定ファイル指定オプション
    public Option<FileInfo> ConfigOption { get; } = new(
            aliases: ["/config", "--config", "-c"],
            description: "設定ファイルのパス")
    { IsRequired = false };

    public RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand("Markdown to HTML generator");

        // 既存オプション
        rootCommand.AddOption(InputOption);
        rootCommand.AddOption(OutputOption);
        rootCommand.AddOption(ThemeOption);
        rootCommand.AddOption(OEmbedOption);

        // 設定ファイルオプション
        rootCommand.AddOption(ConfigOption);

        return rootCommand;
    }
}
