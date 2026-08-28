using BlogGenerator.Core.Interfaces;
using BlogGenerator.MarkdigExtension;
using BlogGenerator.Models;
using Microsoft.Extensions.DependencyInjection;
using RazorLight;

namespace BlogGenerator.Core;

internal static class BlogServiceProviderFactory
{
    public static ServiceProvider Create(
        SiteOption siteOption,
        FeedOption feedOption,
        string themePath,
        string? oEmbedCachePath,
        string? amazonCachePath,
        TimeZoneInfo timeZone)
    {
        var services = new ServiceCollection();

        services.AddSingleton<RazorLightEngine>(_ => new RazorLightEngineBuilder()
            .UseFileSystemProject(themePath)
            .UseMemoryCachingProvider()
            .Build());
        services.AddSingleton<IAmazonCardTemplateRenderer>(sp =>
            new AmazonCardTemplateRenderer(sp.GetRequiredService<RazorLightEngine>(), themePath));
        services.AddSingleton(_ => new AmazonProductMetadataResolver(
            new AmazonProductHttpFetcher(AmazonProductHttpFetcher.CreateHttpClient()),
            new AmazonProductPageParser()));

        services.AddSingleton(siteOption);
        services.AddSingleton(feedOption);
        services.AddSingleton(new ThemeSettings(themePath));
        services.AddSingleton(_ => oEmbedCachePath ?? string.Empty);
        services.AddSingleton(new AmazonProductMetadataCacheSettings(amazonCachePath ?? string.Empty));
        services.AddSingleton(timeZone);

        services.AddSingleton<IFileSystemHelper, FileSystemHelper>();
        services.AddSingleton<IThemeProcessor, ThemeProcessor>();
        services.AddSingleton<IMarkdownProcessor, MarkdownProcessor>();
        services.AddSingleton<IPageGenerator, PageGenerator>();
        services.AddSingleton<IRssFeedGenerator, RssFeedGenerator>();

        return services.BuildServiceProvider();
    }
}
