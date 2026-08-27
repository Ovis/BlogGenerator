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

        var commandLineSetup = new CommandLineSetup();
        var rootCommand = commandLineSetup.CreateRootCommand();

        rootCommand.SetAction(async (parseResult, _) =>
        {
            var input = parseResult.GetRequiredValue(commandLineSetup.InputOption);
            var output = parseResult.GetRequiredValue(commandLineSetup.OutputOption);
            var theme = parseResult.GetRequiredValue(commandLineSetup.ThemeOption);
            var oEmbedDir = parseResult.GetValue(commandLineSetup.OEmbedOption);
            var amazonCachePath = parseResult.GetValue(commandLineSetup.AmazonCacheOption);
            var configFile = parseResult.GetValue(commandLineSetup.ConfigOption);

            Console.WriteLine($"[Start] Command Line Setup: {sw.Elapsed}");

            ThrowIfOutputDirectoryIsInputSubdirectory(input.FullName, output.FullName);

            var configBuilder = new ConfigurationBuilder();

            var userConfigPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".bloggen",
                "config.json");

            if (File.Exists(userConfigPath))
            {
                configBuilder.AddJsonFile(userConfigPath, optional: true, reloadOnChange: true);
                Console.WriteLine($"User config loaded from: {userConfigPath}");
            }

            if (File.Exists("appsettings.json"))
            {
                configBuilder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            }

            if (File.Exists("appsettings.Development.json"))
            {
                configBuilder.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true);
            }

            if (configFile is { Exists: true })
            {
                configBuilder.AddJsonFile(configFile.FullName, optional: false, reloadOnChange: true);
                Console.WriteLine($"Config file loaded from: {configFile.FullName}");
            }

            configBuilder.AddEnvironmentVariables("BLOGGEN_");

            var configuration = configBuilder.Build();
            var siteOption = configuration.GetSection("SiteOption").Get<SiteOption>() ?? new SiteOption();
            var feedOption = configuration.GetSection("FeedOption").Get<FeedOption>() ?? new FeedOption();

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

            var useRss2Str = Environment.GetEnvironmentVariable("BLOGGEN_FEED_USERSS2");
            if (!string.IsNullOrEmpty(useRss2Str) && bool.TryParse(useRss2Str, out bool useRss2))
                feedOption.UseRss2 = useRss2;
            var useAtomStr = Environment.GetEnvironmentVariable("BLOGGEN_FEED_USEATOM");
            if (!string.IsNullOrEmpty(useAtomStr) && bool.TryParse(useAtomStr, out bool useAtom))
                feedOption.UseAtom = useAtom;
            var rssFileName = Environment.GetEnvironmentVariable("BLOGGEN_FEED_RSSFILENAME");
            if (!string.IsNullOrEmpty(rssFileName))
                feedOption.RssFileName = rssFileName;
            var atomFileName = Environment.GetEnvironmentVariable("BLOGGEN_FEED_ATOMFILENAME");
            if (!string.IsNullOrEmpty(atomFileName))
                feedOption.AtomFileName = atomFileName;
            var maxItemsStr = Environment.GetEnvironmentVariable("BLOGGEN_FEED_MAXITEMS");
            if (!string.IsNullOrEmpty(maxItemsStr) && int.TryParse(maxItemsStr, out int maxItems))
                feedOption.MaxFeedItems = maxItems;
            var language = Environment.GetEnvironmentVariable("BLOGGEN_FEED_LANGUAGE");
            if (!string.IsNullOrEmpty(language))
                feedOption.Language = language;

            if (string.IsNullOrEmpty(siteOption.SiteUrl))
            {
                throw new ArgumentException("SiteUrl is a required field. Please specify it via environment variables or a configuration file.");
            }

            Console.WriteLine($"[Completed] Configuration Loading: {sw.Elapsed}");

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var serviceProvider = ConfigureServices(siteOption, feedOption, theme.FullName, oEmbedDir, amazonCachePath);

            Console.WriteLine($"[Completed] Dependency Injection Setup: {sw.Elapsed}");

            var markdownProcessor = serviceProvider.GetRequiredService<IMarkdownProcessor>();
            var themeProcessor = serviceProvider.GetRequiredService<IThemeProcessor>();
            var fileSystemHelper = serviceProvider.GetRequiredService<IFileSystemHelper>();
            var pageGenerator = serviceProvider.GetRequiredService<IPageGenerator>();
            var rssFeedGenerator = serviceProvider.GetRequiredService<IRssFeedGenerator>();

            await markdownProcessor.InitializeAsync();

            Console.WriteLine($"[Completed] Service Initialization: {sw.Elapsed}");

            fileSystemHelper.EnsureDirectoryExists(output.FullName);
            Console.WriteLine($"[Completed] Output Directory Preparation: {sw.Elapsed}");

            themeProcessor.CopyThemeFilesToOutput(theme.FullName, output.FullName);
            Console.WriteLine($"[Completed] Theme Files Copy: {sw.Elapsed}");

            fileSystemHelper.CopyContentFiles(input.FullName, output.FullName);
            Console.WriteLine($"[Completed] Content Files Copy: {sw.Elapsed}");

            var articles = await markdownProcessor.ProcessMarkdownFilesAsync(
                input.FullName,
                output.FullName,
                siteOption.BaseAbsolutePath);
            Console.WriteLine($"[Completed] Markdown Processing: {sw.Elapsed}");

            var sideBarHtml = await pageGenerator.GenerateSideBarHtmlAsync(articles);
            Console.WriteLine($"[Completed] Sidebar HTML Generation: {sw.Elapsed}");

            await pageGenerator.GenerateArticlePagesAsync(articles, output.FullName, sideBarHtml);
            await pageGenerator.GenerateIndexPagesAsync(articles, output.FullName, sideBarHtml);
            await pageGenerator.GenerateTagPagesAsync(articles, output.FullName, sideBarHtml);
            await pageGenerator.GenerateArchivePagesAsync(articles, output.FullName, sideBarHtml);
            Console.WriteLine($"[Completed] HTML Generation: {sw.Elapsed}");

            await rssFeedGenerator.GenerateRssAndAtomFeedsAsync(articles, output.FullName);
            Console.WriteLine($"[Completed] RSS Feed Generation: {sw.Elapsed}");

            if (!string.IsNullOrEmpty(oEmbedDir))
            {
                await OEmbedCacheStore.SaveAsync(oEmbedDir, markdownProcessor.OEmbedCache);
            }

            if (!string.IsNullOrEmpty(amazonCachePath))
            {
                await AmazonProductMetadataCacheStore.SaveAsync(amazonCachePath, markdownProcessor.AmazonProductMetadataCache);
            }

            Console.WriteLine($"[Completed] oEmbed Cache Save: {sw.Elapsed}");
            Console.WriteLine("Completed: " + sw.Elapsed);
            return 0;
        });

        var invocationConfiguration = new InvocationConfiguration
        {
            Output = Console.Out,
            Error = Console.Error
        };

        return await rootCommand.Parse(args, new ParserConfiguration())
            .InvokeAsync(invocationConfiguration);
    }

    private static IServiceProvider ConfigureServices(SiteOption siteOption, FeedOption feedOption, string themePath, string? oEmbedDir, string? amazonCachePath)
    {
        var services = new ServiceCollection();

        services.AddSingleton<RazorLightEngine>(_ => new RazorLightEngineBuilder()
            .UseFileSystemProject(themePath)
            .UseMemoryCachingProvider()
            .Build());

        services.AddSingleton<IAmazonCardTemplateRenderer>(serviceProvider => new AmazonCardTemplateRenderer(
            serviceProvider.GetRequiredService<RazorLightEngine>(),
            themePath));
        services.AddSingleton(_ => new AmazonProductMetadataResolver(
            new AmazonProductHttpFetcher(AmazonProductHttpFetcher.CreateHttpClient()),
            new AmazonProductPageParser()));

        services.AddSingleton(siteOption);
        services.AddSingleton(feedOption);
        services.AddSingleton(new ThemeSettings(themePath));
        services.AddSingleton(_ => oEmbedDir ?? string.Empty);
        services.AddSingleton(new AmazonProductMetadataCacheSettings(amazonCachePath ?? string.Empty));

        services.AddSingleton<IFileSystemHelper, FileSystemHelper>();
        services.AddSingleton<IThemeProcessor, ThemeProcessor>();
        services.AddSingleton<IMarkdownProcessor, MarkdownProcessor>();
        services.AddSingleton<IPageGenerator, PageGenerator>();
        services.AddSingleton<IRssFeedGenerator, RssFeedGenerator>();

        return services.BuildServiceProvider();
    }

    private static void ThrowIfOutputDirectoryIsInputSubdirectory(string inputDir, string outputDir)
    {
        var normalizedInputDir = NormalizeDirectoryPath(inputDir);
        var normalizedOutputDir = NormalizeDirectoryPath(outputDir);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (string.Equals(normalizedInputDir, normalizedOutputDir, comparison) ||
            normalizedOutputDir.StartsWith(normalizedInputDir + Path.DirectorySeparatorChar, comparison))
        {
            throw new ArgumentException("Output directory must not be the same as or a subdirectory of the input directory.");
        }
    }

    private static string NormalizeDirectoryPath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
