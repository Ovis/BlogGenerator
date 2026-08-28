using System.CommandLine;

namespace BlogGenerator.Core;

public class CommandLineSetup
{
    public Option<DirectoryInfo> InputOption { get; } = new("--input", ["/input", "-i"]) { Description = "入力フォルダー", Required = true };
    public Option<DirectoryInfo> OutputOption { get; } = new("--output", ["/output", "-o"]) { Description = "出力フォルダー", Required = true };
    public Option<DirectoryInfo> ThemeOption { get; } = new("--theme", ["/theme"]) { Description = "テーマフォルダー", Required = true };
    public Option<string> OEmbedOption { get; } = new("--oembed", ["/oembed"]) { Description = "oEmbedキャッシュファイル" };
    public Option<string> AmazonCacheOption { get; } = new("--amazon-cache") { Description = "Amazon商品メタデータキャッシュファイル" };
    public Option<FileInfo> ConfigOption { get; } = new("--config", ["/config", "-c"]) { Description = "設定ファイルのパス" };

    public Option<DirectoryInfo> ScheduledInputOption { get; } = new("--input", ["-i"]) { Description = "入力フォルダー", Required = true };
    public Option<string> AfterOption { get; } = new("--after") { Description = "判定開始日時（ISO 8601、オフセット必須）", Required = true };
    public Option<string> UntilOption { get; } = new("--until") { Description = "判定終了日時（ISO 8601、オフセット必須）", Required = true };
    public Option<string> TimeZoneOption { get; } = new("--time-zone") { Description = "オフセットなしPublishedの解釈に使用するタイムゾーンID" };

    public Command ScheduledCommand { get; }

    public CommandLineSetup()
    {
        ScheduledCommand = new Command("scheduled", "指定期間に公開時刻を迎えたコンテンツを検出します");
        ScheduledCommand.Add(ScheduledInputOption);
        ScheduledCommand.Add(AfterOption);
        ScheduledCommand.Add(UntilOption);
        ScheduledCommand.Add(TimeZoneOption);
    }

    public RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand("Markdown to HTML generator");
        rootCommand.Add(InputOption);
        rootCommand.Add(OutputOption);
        rootCommand.Add(ThemeOption);
        rootCommand.Add(OEmbedOption);
        rootCommand.Add(AmazonCacheOption);
        rootCommand.Add(ConfigOption);
        rootCommand.Add(ScheduledCommand);

        // System.CommandLine treats a command that only has subcommands and no action as
        // requiring one of those subcommands. BlogGenerator historically executes directly
        // from the root command, so keep the root executable. Program replaces this action.
        rootCommand.SetAction(_ => 0);
        return rootCommand;
    }
}
