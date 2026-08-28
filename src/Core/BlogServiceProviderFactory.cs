using BlogGenerator.Core.Interfaces;
using BlogGenerator.MarkdigExtension;
using BlogGenerator.Models;
using Microsoft.Extensions.DependencyInjection;
using RazorLight;

namespace BlogGenerator.Core;

/// <summary>
/// 1回のサイトビルドで使用する依存関係を登録し、サービスプロバイダーを構築する
/// </summary>
internal static class BlogServiceProviderFactory
{
    /// <summary>
    /// テーマやサイト設定、キャッシュ設定を含むビルド用サービスプロバイダーを生成する
    /// </summary>
    public static ServiceProvider Create(
        SiteOption siteOption,
        FeedOption feedOption,
        string themePath,
        string? oEmbedCachePath,
        string? amazonCachePath,
        TimeZoneInfo timeZone,
        DateTimeOffset buildTime)
    {
        var services = new ServiceCollection();

        // RazorLightはテーマディレクトリをテンプレートのルートとして扱い、ビルド中はコンパイル結果を再利用する
        services.AddSingleton<RazorLightEngine>(_ => new RazorLightEngineBuilder()
            .UseFileSystemProject(themePath)
            .UseMemoryCachingProvider()
            .Build());
        services.AddSingleton<IAmazonCardTemplateRenderer>(sp =>
            new AmazonCardTemplateRenderer(sp.GetRequiredService<RazorLightEngine>(), themePath));
        services.AddSingleton<IOgpCardTemplateRenderer>(sp =>
            new OgpCardTemplateRenderer(sp.GetRequiredService<RazorLightEngine>(), themePath));
        services.AddSingleton(_ => new AmazonProductMetadataResolver(
            new AmazonProductHttpFetcher(AmazonProductHttpFetcher.CreateHttpClient()),
            new AmazonProductPageParser()));

        // コマンドラインと設定ファイルから確定した値は、ビルド全体で同一インスタンスを共有する
        services.AddSingleton(siteOption);
        services.AddSingleton(feedOption);
        services.AddSingleton(new ThemeSettings(themePath));
        services.AddSingleton(_ => oEmbedCachePath ?? string.Empty);
        services.AddSingleton(new AmazonProductMetadataCacheSettings(amazonCachePath ?? string.Empty));
        services.AddSingleton(timeZone);

        // 現在時刻へ依存する処理は、ビルド開始時に固定した同一時刻を参照する
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(buildTime, timeZone));

        services.AddSingleton<IFileSystemHelper, FileSystemHelper>();
        services.AddSingleton<IThemeProcessor, ThemeProcessor>();
        services.AddSingleton<IMarkdownProcessor, MarkdownProcessor>();
        services.AddSingleton<IPageGenerator, PageGenerator>();
        services.AddSingleton<IRssFeedGenerator, RssFeedGenerator>();

        return services.BuildServiceProvider();
    }
}
