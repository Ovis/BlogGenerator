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
        var totalStopwatch = Stopwatch.StartNew();
        var phaseStopwatch = Stopwatch.StartNew();
        var progress = new BuildProgressReporter(Console.Out);

        // ビルド中に現在時刻が変化しても公開判定が揺れないよう、開始時刻を1度だけ確定する
        var buildContext = new BuildContext(timeProvider.GetLocalNow());
        progress.WriteBuildStarted(buildContext.BuildTime, timeProvider.LocalTimeZone.Id);

        // 成果物生成前に出力先を削除するため、入力元と出力先は親子関係も含めて完全に分離する
        DirectoryPathValidator.ThrowIfInputAndOutputDirectoriesOverlap(options.InputPath, options.OutputPath);
        var (siteOption, feedOption) = BlogConfigurationLoader.Load(options.ConfigFile);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        await using var serviceProvider = BlogServiceProviderFactory.Create(
            siteOption,
            feedOption,
            options.ThemePath,
            options.OEmbedCachePath,
            options.AmazonCachePath,
            timeProvider.LocalTimeZone,
            buildContext.BuildTime);

        var markdownProcessor = serviceProvider.GetRequiredService<IMarkdownProcessor>();
        var themeProcessor = serviceProvider.GetRequiredService<IThemeProcessor>();
        var fileSystemHelper = serviceProvider.GetRequiredService<IFileSystemHelper>();
        var pageGenerator = serviceProvider.GetRequiredService<IPageGenerator>();
        var rssFeedGenerator = serviceProvider.GetRequiredService<IRssFeedGenerator>();
        var scheduledContentValidator = new ScheduledContentValidator(pageGenerator);
        progress.WritePhaseCompleted("Setup", phaseStopwatch.Elapsed);

        // Markdownの解析と公開状態の分類までは、出力ディレクトリへ変更を加えない
        phaseStopwatch.Restart();
        await markdownProcessor.InitializeAsync();
        var articles = await markdownProcessor.ProcessMarkdownFilesAsync(options.InputPath, options.OutputPath, siteOption.BaseAbsolutePath);
        var publicationSet = PublicationSet.Create(articles, buildContext.BuildTime);
        var published = publicationSet.PublishedContents.ToList();
        progress.WritePhaseCompleted(
            "Parse",
            phaseStopwatch.Elapsed,
            $"published: {publicationSet.PublishedContents.Count}, scheduled: {publicationSet.ScheduledContents.Count}, drafts: {publicationSet.DraftContents.Count}");

        // Parse時間の大半を外部取得が占めているか判断できるよう、キャッシュ利用状況と取得累積時間を続けて出力する
        progress.WriteExternalResolutionMetrics(markdownProcessor.ExternalResolutionMetrics);

        // 出力を開始する前に予約コンテンツをすべて検証し、途中まで生成された成果物が残ることを防ぐ
        phaseStopwatch.Restart();
        await scheduledContentValidator.ValidateAsync(publicationSet.ScheduledContents, publicationSet.PublishedContents);
        progress.WritePhaseCompleted("Validate", phaseStopwatch.Elapsed, $"scheduled: {publicationSet.ScheduledContents.Count}");

        // ここから先を成果物生成フェーズとする。前回生成された不要ファイルを残さないよう、出力先を空の状態へ戻す
        phaseStopwatch.Restart();
        OutputDirectoryPreparer.Recreate(options.OutputPath);

        // content側でthemeと同名の静的ファイルを上書きできる従来の順序は維持し、各コピー処理の内部だけを並列化する
        await themeProcessor.CopyThemeFilesToOutputAsync(options.ThemePath, options.OutputPath);
        await fileSystemHelper.CopyContentFilesAsync(options.InputPath, options.OutputPath);
        progress.WritePhaseCompleted("Assets", phaseStopwatch.Elapsed);

        phaseStopwatch.Restart();
        await pageGenerator.GenerateSitePagesAsync(published, options.OutputPath);
        progress.WritePhaseCompleted("Render", phaseStopwatch.Elapsed, $"published: {published.Count}");

        phaseStopwatch.Restart();
        await rssFeedGenerator.GenerateRssAndAtomFeedsAsync(published, options.OutputPath);
        progress.WritePhaseCompleted("Feed", phaseStopwatch.Elapsed);

        // キャッシュはサイト生成が完了した場合だけ保存し、失敗したビルドの途中状態を永続化しない
        phaseStopwatch.Restart();
        var cacheFileCount = 0;
        if (!string.IsNullOrEmpty(options.OEmbedCachePath))
        {
            await OEmbedCacheStore.SaveAsync(options.OEmbedCachePath, markdownProcessor.OEmbedCache);
            cacheFileCount++;
        }

        if (!string.IsNullOrEmpty(options.AmazonCachePath))
        {
            await AmazonProductMetadataCacheStore.SaveAsync(options.AmazonCachePath, markdownProcessor.AmazonProductMetadataCache);
            cacheFileCount++;
        }

        progress.WritePhaseCompleted("Cache", phaseStopwatch.Elapsed, $"files: {cacheFileCount}");
        progress.WriteBuildCompleted(totalStopwatch.Elapsed);
        return 0;
    }
}
