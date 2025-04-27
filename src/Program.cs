using System.CommandLine;
using System.Diagnostics;
using System.Text;
using BlogGenerator.Core;
using BlogGenerator.Core.Interfaces;
using BlogGenerator.MarkdigExtension;
using BlogGenerator.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RazorLight;

namespace BlogGenerator;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var sw = Stopwatch.StartNew();

        Console.WriteLine($"[Start] Total Execution Time: {sw.Elapsed}");

        // コマンドライン設定の作成
        var commandLineSetup = new CommandLineSetup();
        var rootCommand = commandLineSetup.CreateRootCommand();

        rootCommand.SetHandler(async (input, output, theme, oEmbedDir) =>
        {
            Console.WriteLine($"[Start] Command Line Setup: {sw.Elapsed}");

            // 設定の読み込み
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
                .Build();

            Console.WriteLine($"[Completed] Configuration Loading: {sw.Elapsed}");

            var siteOption = configuration.GetSection("SiteOption").Get<SiteOption>();

            if (siteOption == null)
            {
                throw new ArgumentNullException($"{nameof(SiteOption)} is not found");
            }

            // 文字エンコーディング
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // DIコンテナの設定
            var serviceProvider = ConfigureServices(siteOption, theme.FullName, oEmbedDir);

            Console.WriteLine($"[Completed] Dependency Injection Setup: {sw.Elapsed}");

            // RazorLightエンジンの取得
            var razorLightEngine = serviceProvider.GetRequiredService<RazorLightEngine>();

            // 各種サービスの取得
            var markdownProcessor = serviceProvider.GetRequiredService<IMarkdownProcessor>();
            var themeProcessor = serviceProvider.GetRequiredService<IThemeProcessor>();
            var fileSystemHelper = serviceProvider.GetRequiredService<IFileSystemHelper>();
            var pageGenerator = serviceProvider.GetRequiredService<IPageGenerator>();
            var rssFeedGenerator = serviceProvider.GetRequiredService<IRssFeedGenerator>();

            await markdownProcessor.InitializeAsync();

            Console.WriteLine($"[Completed] Service Initialization: {sw.Elapsed}");

            // 出力先の準備
            fileSystemHelper.EnsureDirectoryExists(output.FullName);

            Console.WriteLine($"[Completed] Output Directory Preparation: {sw.Elapsed}");

            // テーマファイルのコピー
            themeProcessor.CopyThemeFilesToOutput(theme.FullName, output.FullName);

            Console.WriteLine($"[Completed] Theme Files Copy: {sw.Elapsed}");

            // 記事の処理
            var articles = await markdownProcessor.ProcessMarkdownFilesAsync(
                input.FullName,
                output.FullName,
                siteOption.BaseAbsolutePath);

            Console.WriteLine($"[Completed] Markdown Processing: {sw.Elapsed}");

            // サイドバーのHTML生成
            var sideBarHtml = await pageGenerator.GenerateSideBarHtmlAsync(articles);

            Console.WriteLine($"[Completed] Sidebar HTML Generation: {sw.Elapsed}");

            // HTML生成
            await pageGenerator.GenerateArticlePagesAsync(articles, output.FullName, sideBarHtml);
            await pageGenerator.GenerateIndexPagesAsync(articles, output.FullName, sideBarHtml);
            await pageGenerator.GenerateTagPagesAsync(articles, output.FullName, sideBarHtml);
            await pageGenerator.GenerateArchivePagesAsync(articles, output.FullName, sideBarHtml);

            Console.WriteLine($"[Completed] HTML Generation: {sw.Elapsed}");

            // RSSフィード生成
            await rssFeedGenerator.GenerateRssAndAtomFeedsAsync(articles, output.FullName);

            Console.WriteLine($"[Completed] RSS Feed Generation: {sw.Elapsed}");

            // oEmbedキャッシュの保存
            if (!string.IsNullOrEmpty(oEmbedDir))
            {
                await OEmbedCardExtension.SaveOEmbedCacheAsync(oEmbedDir);
            }

            Console.WriteLine($"[Completed] oEmbed Cache Save: {sw.Elapsed}");

            Console.WriteLine("Completed: " + sw.Elapsed);
        }, commandLineSetup.InputOption, commandLineSetup.OutputOption, commandLineSetup.ThemeOption, commandLineSetup.OEmbedOption);

        return await rootCommand.InvokeAsync(args);
    }

    private static IServiceProvider ConfigureServices(SiteOption siteOption, string themePath, string? oEmbedDir)
    {
        var services = new ServiceCollection();

        // RazorLightEngineの登録
        services.AddSingleton<RazorLightEngine>(sp =>
        {
            return new RazorLightEngineBuilder()
                .UseFileSystemProject(themePath)
                .UseMemoryCachingProvider()
                .DisableEncoding()
                .Build();
        });

        // サイトオプションの登録
        services.AddSingleton(siteOption);

        // oEmbedDirの登録
        services.AddSingleton(provider => oEmbedDir);

        // 各サービスの登録
        services.AddSingleton<IFileSystemHelper, FileSystemHelper>();
        services.AddSingleton<IThemeProcessor, ThemeProcessor>();
        services.AddSingleton<IMarkdownProcessor, MarkdownProcessor>();
        services.AddSingleton<IPageGenerator, PageGenerator>();
        services.AddSingleton<IRssFeedGenerator, RssFeedGenerator>();

        return services.BuildServiceProvider();
    }
}
