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
    public static Task<int> Main(string[] args) => MainAsync(args, TimeProvider.System);

    internal static async Task<int> MainAsync(string[] args, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var sw = Stopwatch.StartNew();
        Console.WriteLine($"[Start] Total Execution Time: {sw.Elapsed}");

        var commandLineSetup = new CommandLineSetup();
        var rootCommand = commandLineSetup.CreateRootCommand();
        rootCommand.SetAction(async (parseResult, _) =>
        {
            var buildContext = new BuildContext(timeProvider.GetLocalNow());
            Console.WriteLine($"Build time: {buildContext.BuildTime:yyyy-MM-dd HH:mm:ss zzz}");
            Console.WriteLine($"Time zone: {timeProvider.LocalTimeZone.Id}");

            var input = parseResult.GetRequiredValue(commandLineSetup.InputOption);
            var output = parseResult.GetRequiredValue(commandLineSetup.OutputOption);
            var theme = parseResult.GetRequiredValue(commandLineSetup.ThemeOption);
            var oEmbedDir = parseResult.GetValue(commandLineSetup.OEmbedOption);
            var amazonCachePath = parseResult.GetValue(commandLineSetup.AmazonCacheOption);
            var configFile = parseResult.GetValue(commandLineSetup.ConfigOption);
            ThrowIfOutputDirectoryIsInputSubdirectory(input.FullName, output.FullName);

            var configBuilder = new ConfigurationBuilder();
            var userConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".bloggen", "config.json");
            if (File.Exists(userConfigPath)) configBuilder.AddJsonFile(userConfigPath, optional: true, reloadOnChange: true);
            if (File.Exists("appsettings.json")) configBuilder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            if (File.Exists("appsettings.Development.json")) configBuilder.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true);
            if (configFile is { Exists: true }) configBuilder.AddJsonFile(configFile.FullName, optional: false, reloadOnChange: true);
            configBuilder.AddEnvironmentVariables("BLOGGEN_");

            var configuration = configBuilder.Build();
            var siteOption = configuration.GetSection("SiteOption").Get<SiteOption>() ?? new SiteOption();
            var feedOption = configuration.GetSection("FeedOption").Get<FeedOption>() ?? new FeedOption();
            siteOption.SiteName = string.IsNullOrEmpty(siteOption.SiteName) ? Environment.GetEnvironmentVariable("BLOGGEN_SITENAME") ?? string.Empty : siteOption.SiteName;
            siteOption.SiteUrl = string.IsNullOrEmpty(siteOption.SiteUrl) ? Environment.GetEnvironmentVariable("BLOGGEN_SITEURL") ?? string.Empty : siteOption.SiteUrl;
            siteOption.SiteDescription = string.IsNullOrEmpty(siteOption.SiteDescription) ? Environment.GetEnvironmentVariable("BLOGGEN_SITEDESCRIPTION") ?? string.Empty : siteOption.SiteDescription;
            siteOption.SiteAuthor = string.IsNullOrEmpty(siteOption.SiteAuthor) ? Environment.GetEnvironmentVariable("BLOGGEN_SITEAUTHOR") ?? string.Empty : siteOption.SiteAuthor;
            siteOption.SiteAuthorDescription = string.IsNullOrEmpty(siteOption.SiteAuthorDescription) ? Environment.GetEnvironmentVariable("BLOGGEN_SITEAUTHORDESCRIPTION") ?? string.Empty : siteOption.SiteAuthorDescription;
            siteOption.AmazonAssociateTag = string.IsNullOrEmpty(siteOption.AmazonAssociateTag) ? Environment.GetEnvironmentVariable("BLOGGEN_AMAZONTAG") ?? string.Empty : siteOption.AmazonAssociateTag;
            if (string.IsNullOrEmpty(siteOption.SiteUrl)) throw new ArgumentException("SiteUrl is a required field. Please specify it via environment variables or a configuration file.");

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var serviceProvider = ConfigureServices(siteOption, feedOption, theme.FullName, oEmbedDir, amazonCachePath, timeProvider.LocalTimeZone);
            var markdownProcessor = serviceProvider.GetRequiredService<IMarkdownProcessor>();
            var themeProcessor = serviceProvider.GetRequiredService<IThemeProcessor>();
            var fileSystemHelper = serviceProvider.GetRequiredService<IFileSystemHelper>();
            var pageGenerator = serviceProvider.GetRequiredService<IPageGenerator>();
            var rssFeedGenerator = serviceProvider.GetRequiredService<IRssFeedGenerator>();
            await markdownProcessor.InitializeAsync();

            // Read and validate all Markdown before touching output.
            var articles = await markdownProcessor.ProcessMarkdownFilesAsync(input.FullName, output.FullName, siteOption.BaseAbsolutePath);
            var publicationSet = PublicationSet.Create(articles, buildContext.BuildTime);
            var published = publicationSet.PublishedContents.ToList();
            var validationSideBar = await pageGenerator.GenerateSideBarHtmlAsync(published);
            var validationErrors = new List<Exception>();
            foreach (var scheduled in publicationSet.ScheduledContents)
            {
                try
                {
                    await pageGenerator.ValidateArticlePageAsync(scheduled, published, validationSideBar);
                    Console.WriteLine($"Scheduled content validated: {scheduled.RootRelativePath} (Published: {scheduled.Published:yyyy-MM-dd HH:mm:ss zzz})");
                }
                catch (Exception ex)
                {
                    validationErrors.Add(new InvalidOperationException($"Failed to validate scheduled content '{scheduled.RootRelativePath}' (Published: {scheduled.Published:yyyy-MM-dd HH:mm:ss zzz}).", ex));
                }
            }
            if (validationErrors.Count != 0) throw new AggregateException("One or more scheduled contents failed validation.", validationErrors);

            fileSystemHelper.EnsureDirectoryExists(output.FullName);
            themeProcessor.CopyThemeFilesToOutput(theme.FullName, output.FullName);
            fileSystemHelper.CopyContentFiles(input.FullName, output.FullName);

            var sideBarHtml = await pageGenerator.GenerateSideBarHtmlAsync(published);
            await pageGenerator.GenerateArticlePagesAsync(published, output.FullName, sideBarHtml);
            await pageGenerator.GenerateIndexPagesAsync(published, output.FullName, sideBarHtml);
            await pageGenerator.GenerateTagPagesAsync(published, output.FullName, sideBarHtml);
            await pageGenerator.GenerateArchivePagesAsync(published, output.FullName, sideBarHtml);
            await rssFeedGenerator.GenerateRssAndAtomFeedsAsync(published, output.FullName);

            if (!string.IsNullOrEmpty(oEmbedDir)) await OEmbedCacheStore.SaveAsync(oEmbedDir, markdownProcessor.OEmbedCache);
            if (!string.IsNullOrEmpty(amazonCachePath)) await AmazonProductMetadataCacheStore.SaveAsync(amazonCachePath, markdownProcessor.AmazonProductMetadataCache);
            Console.WriteLine("Completed: " + sw.Elapsed);
            return 0;
        });

        return await rootCommand.Parse(args, new ParserConfiguration()).InvokeAsync(new InvocationConfiguration { Output = Console.Out, Error = Console.Error });
    }

    private static IServiceProvider ConfigureServices(SiteOption siteOption, FeedOption feedOption, string themePath, string? oEmbedDir, string? amazonCachePath, TimeZoneInfo timeZone)
    {
        var services = new ServiceCollection();
        services.AddSingleton<RazorLightEngine>(_ => new RazorLightEngineBuilder().UseFileSystemProject(themePath).UseMemoryCachingProvider().Build());
        services.AddSingleton<IAmazonCardTemplateRenderer>(sp => new AmazonCardTemplateRenderer(sp.GetRequiredService<RazorLightEngine>(), themePath));
        services.AddSingleton(_ => new AmazonProductMetadataResolver(new AmazonProductHttpFetcher(AmazonProductHttpFetcher.CreateHttpClient()), new AmazonProductPageParser()));
        services.AddSingleton(siteOption);
        services.AddSingleton(feedOption);
        services.AddSingleton(new ThemeSettings(themePath));
        services.AddSingleton(_ => oEmbedDir ?? string.Empty);
        services.AddSingleton(new AmazonProductMetadataCacheSettings(amazonCachePath ?? string.Empty));
        services.AddSingleton(timeZone);
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
        if (string.Equals(normalizedInputDir, normalizedOutputDir, comparison) || normalizedOutputDir.StartsWith(normalizedInputDir + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("Output directory must not be the same as or a subdirectory of the input directory.");
    }

    private static string NormalizeDirectoryPath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
