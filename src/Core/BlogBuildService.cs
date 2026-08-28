using System.Diagnostics;
using System.Text;
using BlogGenerator.Core.Interfaces;
using BlogGenerator.MarkdigExtension;
using BlogGenerator.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BlogGenerator.Core;

internal sealed class BlogBuildService(TimeProvider timeProvider)
{
    public async Task<int> BuildAsync(BuildOptions options)
    {
        var sw = Stopwatch.StartNew();
        Console.WriteLine($"[Start] Total Execution Time: {sw.Elapsed}");

        var buildContext = new BuildContext(timeProvider.GetLocalNow());
        Console.WriteLine($"Build time: {buildContext.BuildTime:yyyy-MM-dd HH:mm:ss zzz}");
        Console.WriteLine($"Time zone: {timeProvider.LocalTimeZone.Id}");

        DirectoryPathValidator.ThrowIfOutputDirectoryIsInputSubdirectory(options.InputPath, options.OutputPath);
        var (siteOption, feedOption) = BlogConfigurationLoader.Load(options.ConfigFile);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        await using var serviceProvider = BlogServiceProviderFactory.Create(
            siteOption,
            feedOption,
            options.ThemePath,
            options.OEmbedCachePath,
            options.AmazonCachePath,
            timeProvider.LocalTimeZone);

        var markdownProcessor = serviceProvider.GetRequiredService<IMarkdownProcessor>();
        var themeProcessor = serviceProvider.GetRequiredService<IThemeProcessor>();
        var fileSystemHelper = serviceProvider.GetRequiredService<IFileSystemHelper>();
        var pageGenerator = serviceProvider.GetRequiredService<IPageGenerator>();
        var rssFeedGenerator = serviceProvider.GetRequiredService<IRssFeedGenerator>();

        await markdownProcessor.InitializeAsync();
        var articles = await markdownProcessor.ProcessMarkdownFilesAsync(options.InputPath, options.OutputPath, siteOption.BaseAbsolutePath);
        var publicationSet = PublicationSet.Create(articles, buildContext.BuildTime);
        var published = publicationSet.PublishedContents.ToList();

        // 出力を開始する前に予約コンテンツをすべて検証し、途中まで生成された成果物が残ることを防ぐ
        await ValidateScheduledContentsAsync(publicationSet.ScheduledContents, published, pageGenerator);

        fileSystemHelper.EnsureDirectoryExists(options.OutputPath);
        themeProcessor.CopyThemeFilesToOutput(options.ThemePath, options.OutputPath);
        fileSystemHelper.CopyContentFiles(options.InputPath, options.OutputPath);

        var sideBarHtml = await pageGenerator.GenerateSideBarHtmlAsync(published);
        await pageGenerator.GenerateArticlePagesAsync(published, options.OutputPath, sideBarHtml);
        await pageGenerator.GenerateIndexPagesAsync(published, options.OutputPath, sideBarHtml);
        await pageGenerator.GenerateTagPagesAsync(published, options.OutputPath, sideBarHtml);
        await pageGenerator.GenerateArchivePagesAsync(published, options.OutputPath, sideBarHtml);
        await rssFeedGenerator.GenerateRssAndAtomFeedsAsync(published, options.OutputPath);

        if (!string.IsNullOrEmpty(options.OEmbedCachePath))
            await OEmbedCacheStore.SaveAsync(options.OEmbedCachePath, markdownProcessor.OEmbedCache);
        if (!string.IsNullOrEmpty(options.AmazonCachePath))
            await AmazonProductMetadataCacheStore.SaveAsync(options.AmazonCachePath, markdownProcessor.AmazonProductMetadataCache);

        Console.WriteLine("Completed: " + sw.Elapsed);
        return 0;
    }

    private static async Task ValidateScheduledContentsAsync(
        IReadOnlyList<Article> scheduledContents,
        List<Article> published,
        IPageGenerator pageGenerator)
    {
        var validationSideBar = await pageGenerator.GenerateSideBarHtmlAsync(published);
        var validationErrors = new List<Exception>();

        foreach (var scheduled in scheduledContents)
        {
            try
            {
                await pageGenerator.ValidateArticlePageAsync(scheduled, published, validationSideBar);
                Console.WriteLine($"Scheduled content validated: {scheduled.RootRelativePath} (Published: {scheduled.Published:yyyy-MM-dd HH:mm:ss zzz})");
            }
            catch (Exception ex)
            {
                validationErrors.Add(new InvalidOperationException(
                    $"Failed to validate scheduled content '{scheduled.RootRelativePath}' (Published: {scheduled.Published:yyyy-MM-dd HH:mm:ss zzz}).",
                    ex));
            }
        }

        if (validationErrors.Count != 0)
            throw new AggregateException("One or more scheduled contents failed validation.", validationErrors);
    }
}
