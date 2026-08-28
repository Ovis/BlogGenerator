using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;
using BlogGenerator.Core.Interfaces;
using BlogGenerator.Models;

namespace BlogGenerator.Core;

/// <summary>
/// 公開済み記事からRSS 2.0とAtomフィードを生成する
/// </summary>
public class RssFeedGenerator : IRssFeedGenerator
{
    private readonly SiteOption _siteOption;
    private readonly FeedOption _feedOption;
    private readonly IFileSystemHelper _fileSystemHelper;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// システム時刻を使用してフィードジェネレーターを生成する
    /// </summary>
    /// <remarks>
    /// 既存の直接生成コードとの互換性を維持するためのコンストラクター。通常ビルドではビルド時刻を固定したTimeProviderがDIされる
    /// </remarks>
    public RssFeedGenerator(
        SiteOption siteOption,
        FeedOption feedOption,
        IFileSystemHelper fileSystemHelper)
        : this(siteOption, feedOption, fileSystemHelper, TimeProvider.System)
    {
    }

    /// <summary>
    /// 更新時刻の取得元を指定してフィードジェネレーターを生成する
    /// </summary>
    public RssFeedGenerator(
        SiteOption siteOption,
        FeedOption feedOption,
        IFileSystemHelper fileSystemHelper,
        TimeProvider timeProvider)
    {
        _siteOption = siteOption;
        _feedOption = feedOption;
        _fileSystemHelper = fileSystemHelper;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task GenerateRssAndAtomFeedsAsync(List<Article> articles, string outputDir)
    {
        if (_feedOption is { UseRss2: false, UseAtom: false }) return;

        // ビルド全体で固定された現在時刻を使い、実行環境やフィード生成タイミングによる揺れを避ける
        var feedUpdatedTime = _timeProvider.GetLocalNow();
        var rssFeed = new SyndicationFeed(
            title: _siteOption.SiteName,
            description: _siteOption.SiteDescription,
            feedAlternateLink: new Uri(_siteOption.SiteUrl),
            id: _siteOption.SiteUrl,
            lastUpdatedTime: feedUpdatedTime)
        {
            Language = _feedOption.Language,
            Items = articles
                .Where(article => !article.IsFixedPage && article.Published is { } published && published != DateTimeOffset.MinValue)
                .Take(_feedOption.MaxFeedItems)
                .Select(article => new SyndicationItem(
                    title: article.Title,
                    content: article.ExcerptHtml,
                    itemAlternateLink: new Uri(new Uri(_siteOption.SiteUrl), article.RootRelativePath),
                    id: new Uri(new Uri(_siteOption.SiteUrl), article.RootRelativePath).ToString(),
                    lastUpdatedTime: article.Published!.Value))
        };

        if (_feedOption.UseRss2)
        {
            var writerRss20 = new Rss20FeedFormatter(rssFeed);
            await using var rssFile = File.Create(_fileSystemHelper.CombineFilePath(outputDir, _feedOption.RssFileName));
            await using var rssWriter = XmlWriter.Create(rssFile, new XmlWriterSettings { Async = true, Indent = true, Encoding = new UTF8Encoding(false) });
            writerRss20.WriteTo(rssWriter);
        }

        if (_feedOption.UseAtom)
        {
            var writerAtom10 = new Atom10FeedFormatter(rssFeed);
            await using var atomFile = File.Create(_fileSystemHelper.CombineFilePath(outputDir, _feedOption.AtomFileName));
            await using var atomWriter = XmlWriter.Create(atomFile, new XmlWriterSettings { Async = true, Indent = true, Encoding = new UTF8Encoding(false) });
            writerAtom10.WriteTo(atomWriter);
        }
    }
}
