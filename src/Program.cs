using System.CommandLine;
using System.Diagnostics;
using System.Text;
using BlogGenerator.Core;
using BlogGenerator.MarkdigExtension;
using BlogGenerator.Models;
using Microsoft.Extensions.Configuration;
using RazorLight;

namespace BlogGenerator;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var sw = Stopwatch.StartNew();

        // コマンドライン設定の作成
        var commandLineSetup = new CommandLineSetup();
        var rootCommand = commandLineSetup.CreateRootCommand();

        rootCommand.SetHandler(async (input, output, theme, oEmbedDir) =>
        {
            // 設定の読み込み
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
                .Build();

            var siteOption = configuration.GetSection("SiteOption").Get<SiteOption>();

            if (siteOption == null)
            {
                throw new ArgumentNullException($"{nameof(SiteOption)} is not found");
            }

            // 文字エンコーディング
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // RazorLightエンジンの初期化
            var razorLightEngine = new RazorLightEngineBuilder()
                .UseFileSystemProject(theme.FullName)
                .UseMemoryCachingProvider()
                .DisableEncoding()
                .Build();

            // Markdownプロセッサーの初期化
            var markdownProcessor = new MarkdownProcessor(siteOption, oEmbedDir);
            await markdownProcessor.InitializeAsync();

            // テーマプロセッサーの初期化
            var themeProcessor = new ThemeProcessor();

            // ファイルシステムヘルパーの初期化
            var fileSystemHelper = new FileSystemHelper();

            // 出力先の準備
            fileSystemHelper.EnsureDirectoryExists(output.FullName);

            // テーマファイルのコピー
            themeProcessor.CopyThemeFilesToOutput(theme.FullName, output.FullName);

            // 記事の処理
            var articles = await markdownProcessor.ProcessMarkdownFilesAsync(
                input.FullName,
                output.FullName,
                siteOption.BaseAbsolutePath);

            // ページ生成機能の初期化
            var pageGenerator = new PageGenerator(razorLightEngine, siteOption, fileSystemHelper);

            // サイドバーのHTML生成
            var sideBarHtml = await pageGenerator.GenerateSideBarHtmlAsync(articles);

            // HTML生成
            await pageGenerator.GenerateArticlePagesAsync(articles, output.FullName, sideBarHtml);
            await pageGenerator.GenerateIndexPagesAsync(articles, output.FullName, sideBarHtml);
            await pageGenerator.GenerateTagPagesAsync(articles, output.FullName, sideBarHtml);
            await pageGenerator.GenerateArchivePagesAsync(articles, output.FullName, sideBarHtml);

            // RSSフィード生成
            var rssFeedGenerator = new RssFeedGenerator(siteOption);
            await rssFeedGenerator.GenerateRssAndAtomFeedsAsync(articles, output.FullName);

            // oEmbedキャッシュの保存
            if (!string.IsNullOrEmpty(oEmbedDir))
            {
                await OEmbedCardExtension.SaveOEmbedCacheAsync(oEmbedDir);
            }

            Console.WriteLine("Completed: " + sw.Elapsed);
        }, commandLineSetup.InputOption, commandLineSetup.OutputOption, commandLineSetup.ThemeOption, commandLineSetup.OEmbedOption);

        return await rootCommand.InvokeAsync(args);
    }
}
