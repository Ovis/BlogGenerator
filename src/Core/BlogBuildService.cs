using System.Diagnostics;
using System.Text;
using BlogGenerator.Core.Interfaces;
using BlogGenerator.MarkdigExtension;
using BlogGenerator.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BlogGenerator.Core;

/// <summary>
/// BlogGeneratorの通常ビルド処理全体を順序立てて実行する
/// </summary>
/// <remarks>
/// 個々の変換や生成処理は各サービスへ委譲し、このクラスではビルド全体の処理順序と成果物生成の境界を管理する
/// </remarks>
internal sealed class BlogBuildService(TimeProvider timeProvider)
{
    /// <summary>
    /// 指定されたビルドオプションを使用してサイト全体を生成する
    /// </summary>
    /// <param name="options">入力、出力、テーマ、キャッシュなどのビルド設定</param>
    /// <returns>正常終了時は0</returns>
    public async Task<int> BuildAsync(BuildOptions options)
    {
        var sw = Stopwatch.StartNew();
        Console.WriteLine($"[Start] Total Execution Time: {sw.Elapsed}");

        // ビルド中に現在時刻が変化しても公開判定が揺れないよう、開始時刻を1度だけ確定する
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
        var scheduledContentValidator = new ScheduledContentValidator(pageGenerator);

        // Markdownの解析と公開状態の分類までは、出力ディレクトリへ変更を加えない
        await markdownProcessor.InitializeAsync();
        var articles = await markdownProcessor.ProcessMarkdownFilesAsync(options.InputPath, options.OutputPath, siteOption.BaseAbsolutePath);
        var publicationSet = PublicationSet.Create(articles, buildContext.BuildTime);
        var published = publicationSet.PublishedContents.ToList();

        // 出力を開始する前に予約コンテンツをすべて検証し、途中まで生成された成果物が残ることを防ぐ
        await scheduledContentValidator.ValidateAsync(publicationSet.ScheduledContents, publicationSet.PublishedContents);

        // ここから先を成果物生成フェーズとし、テーマ、コンテンツ、各ページ、フィードの順に出力する
        fileSystemHelper.EnsureDirectoryExists(options.OutputPath);
        themeProcessor.CopyThemeFilesToOutput(options.ThemePath, options.OutputPath);
        fileSystemHelper.CopyContentFiles(options.InputPath, options.OutputPath);

        var sideBarHtml = await pageGenerator.GenerateSideBarHtmlAsync(published);
        await pageGenerator.GenerateArticlePagesAsync(published, options.OutputPath, sideBarHtml);
        await pageGenerator.GenerateIndexPagesAsync(published, options.OutputPath, sideBarHtml);
        await pageGenerator.GenerateTagPagesAsync(published, options.OutputPath, sideBarHtml);
        await pageGenerator.GenerateArchivePagesAsync(published, options.OutputPath, sideBarHtml);
        await rssFeedGenerator.GenerateRssAndAtomFeedsAsync(published, options.OutputPath);

        // キャッシュはサイト生成が完了した場合だけ保存し、失敗したビルドの途中状態を永続化しない
        if (!string.IsNullOrEmpty(options.OEmbedCachePath))
            await OEmbedCacheStore.SaveAsync(options.OEmbedCachePath, markdownProcessor.OEmbedCache);
        if (!string.IsNullOrEmpty(options.AmazonCachePath))
            await AmazonProductMetadataCacheStore.SaveAsync(options.AmazonCachePath, markdownProcessor.AmazonProductMetadataCache);

        Console.WriteLine("Completed: " + sw.Elapsed);
        return 0;
    }
}
