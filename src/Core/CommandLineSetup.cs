using System.CommandLine;

namespace BlogGenerator.Core;

public class CommandLineSetup
{
    public Option<DirectoryInfo> InputOption { get; } = new(
            "--input",
            ["/input", "-i"])
    {
        Description = "入力フォルダー",
        Required = true
    };

    public Option<DirectoryInfo> OutputOption { get; } = new(
            "--output",
            ["/output", "-o"])
    {
        Description = "出力フォルダー",
        Required = true
    };

    public Option<DirectoryInfo> ThemeOption { get; } = new(
            "--theme",
            ["/theme"])
    {
        Description = "テーマフォルダー",
        Required = true
    };

    public Option<string> OEmbedOption { get; } = new(
            "--oembed",
            ["/oembed"])
    {
        Description = "oEmbedキャッシュファイル"
    };

    // 設定ファイル指定オプション
    public Option<FileInfo> ConfigOption { get; } = new(
            "--config",
            ["/config", "-c"])
    {
        Description = "設定ファイルのパス"
    };

    public RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand("Markdown to HTML generator");

        // 既存オプション
        rootCommand.Add(InputOption);
        rootCommand.Add(OutputOption);
        rootCommand.Add(ThemeOption);
        rootCommand.Add(OEmbedOption);

        // 設定ファイルオプション
        rootCommand.Add(ConfigOption);

        return rootCommand;
    }
}
