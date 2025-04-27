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

        rootCommand.SetHandler(async (input, output, theme, oEmbedDir, configFile) =>
        {
            Console.WriteLine($"[Start] Command Line Setup: {sw.Elapsed}");

            // 設定の読み込み（優先度順に適用）
            var configBuilder = new ConfigurationBuilder();

            // 1. ユーザーホームディレクトリの設定ファイル（最も低い優先度）
            var userConfigPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".bloggen",
                "config.json");

            if (File.Exists(userConfigPath))
            {
                configBuilder.AddJsonFile(userConfigPath, optional: true, reloadOnChange: true);
                Console.WriteLine($"User config loaded from: {userConfigPath}");
            }

            // 2. カレントディレクトリのappsettings.json
            if (File.Exists("appsettings.json"))
            {
                configBuilder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            }

            if (File.Exists("appsettings.Development.json"))
            {
                configBuilder.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true);
            }

            // 3. 指定された設定ファイル（configオプションで指定）
            if (configFile is { Exists: true })
            {
                configBuilder.AddJsonFile(configFile.FullName, optional: false, reloadOnChange: true);
                Console.WriteLine($"Config file loaded from: {configFile.FullName}");
            }

            // 4. 環境変数（最も高い優先度）
            configBuilder.AddEnvironmentVariables("BLOGGEN_");

            var configuration = configBuilder.Build();

            // サイトオプションの作成と優先順位付き初期化
            var siteOption = configuration.GetSection("SiteOption").Get<SiteOption>() ?? new SiteOption();

            // 設定ファイルから取得できなかった場合に個別の環境変数から直接取得
            if (string.IsNullOrEmpty(siteOption.SiteName))
                siteOption.SiteName = Environment.GetEnvironmentVariable("BLOGGEN_SITENAME") ?? string.Empty;

            if (string.IsNullOrEmpty(siteOption.SiteUrl))
                siteOption.SiteUrl = Environment.GetEnvironmentVariable("BLOGGEN_SITEURL") ?? string.Empty;

            if (string.IsNullOrEmpty(siteOption.SiteDescription))
                siteOption.SiteDescription = Environment.GetEnvironmentVariable("BLOGGEN_SITEDESCRIPTION") ?? string.Empty;

            if (string.IsNullOrEmpty(siteOption.SiteAuthor))
                siteOption.SiteAuthor = Environment.GetEnvironmentVariable("BLOGGEN_SITEAUTHOR") ?? string.Empty;

            if (string.IsNullOrEmpty(siteOption.SiteAuthorDescription))
                siteOption.SiteAuthorDescription = Environment.GetEnvironmentVariable("BLOGGEN_SITEAUTHORDESCRIPTION") ?? string.Empty;

            if (string.IsNullOrEmpty(siteOption.AmazonAssociateTag))
                siteOption.AmazonAssociateTag = Environment.GetEnvironmentVariable("BLOGGEN_AMAZONTAG") ?? string.Empty;

            // 必須項目のバリデーション
            if (string.IsNullOrEmpty(siteOption.SiteUrl))
            {
                throw new ArgumentException("SiteUrl is a required field. Please specify it via environment variables or a configuration file.");
            }

            Console.WriteLine($"[Completed] Configuration Loading: {sw.Elapsed}");


            // 文字エンコーディング
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // DIコンテナの設定
            var serviceProvider = ConfigureServices(siteOption, theme.FullName, oEmbedDir);

            Console.WriteLine($"[Completed] Dependency Injection Setup: {sw.Elapsed}");

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
        },
        commandLineSetup.InputOption,
        commandLineSetup.OutputOption,
        commandLineSetup.ThemeOption,
        commandLineSetup.OEmbedOption,
        commandLineSetup.ConfigOption);

        return await rootCommand.InvokeAsync(args);
    }

    private static IServiceProvider ConfigureServices(SiteOption siteOption, string themePath, string? oEmbedDir)
    {
        var services = new ServiceCollection();

        // RazorLightEngineの登録
        services.AddSingleton<RazorLightEngine>(_ => new RazorLightEngineBuilder()
            .UseFileSystemProject(themePath)
            .UseMemoryCachingProvider()
            .DisableEncoding()
            .Build());

        // サイトオプションの登録
        services.AddSingleton(siteOption);

        // oEmbedDirの登録
        services.AddSingleton(_ => oEmbedDir ?? string.Empty);

        // 各サービスの登録
        services.AddSingleton<IFileSystemHelper, FileSystemHelper>();
        services.AddSingleton<IThemeProcessor, ThemeProcessor>();
        services.AddSingleton<IMarkdownProcessor, MarkdownProcessor>();
        services.AddSingleton<IPageGenerator, PageGenerator>();
        services.AddSingleton<IRssFeedGenerator, RssFeedGenerator>();

        return services.BuildServiceProvider();
    }
}
