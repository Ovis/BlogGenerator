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
    public Option<bool> WebpOption { get; } = new("--webp") { Description = "出力のJPEG・PNGを小さいWebPへ変換し、画像への参照を更新します" };
    public Option<int?> WebpParallelismOption { get; } = new("--webp-parallelism")
    {
        Description = "WebP変換の並列数（1以上、省略時はCPU数に応じて最大4）",
        Arity = ArgumentArity.ExactlyOne
    };

    public Option<DirectoryInfo> ScheduledInputOption { get; } = new("--input", ["-i"]) { Description = "入力フォルダー", Required = true };
    public Option<string> AfterOption { get; } = new("--after") { Description = "判定開始日時（ISO 8601、オフセット必須）", Required = true };
    public Option<string> UntilOption { get; } = new("--until") { Description = "判定終了日時（ISO 8601、オフセット必須）", Required = true };
    public Option<string> TimeZoneOption { get; } = new("--time-zone") { Description = "オフセットなしPublishedの解釈に使用するタイムゾーンID" };

    public Command ScheduledCommand { get; }

    public CommandLineSetup()
    {
        WebpParallelismOption.Validators.Add(result =>
        {
            if (result.Tokens.Any(token => int.TryParse(token.Value, out var value) && value <= 0))
                result.AddError("--webp-parallelismには1以上の整数を指定してください。");
        });
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
        rootCommand.Add(WebpOption);
        rootCommand.Add(WebpParallelismOption);
        rootCommand.Add(ConfigOption);
        rootCommand.Add(ScheduledCommand);
        rootCommand.Validators.Add(result =>
        {
            if (result.GetResult(WebpParallelismOption) is { Implicit: false } && !result.GetValue(WebpOption))
                result.AddError("--webp-parallelismを指定する場合は--webpも指定してください。");
        });

        // System.CommandLine treats a command that only has subcommands and no action as
        // requiring one of those subcommands. BlogGenerator historically executes directly
        // from the root command, so keep the root executable. Program replaces this action.
        rootCommand.SetAction(_ => 0);
        return rootCommand;
    }
}
