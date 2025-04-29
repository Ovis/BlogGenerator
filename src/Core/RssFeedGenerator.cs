using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;
using BlogGenerator.Core.Interfaces;
using BlogGenerator.Models;

namespace BlogGenerator.Core;

public class RssFeedGenerator(SiteOption siteOption, FeedOption feedOption, IFileSystemHelper fileSystemHelper) : IRssFeedGenerator
{
    public async Task GenerateRssAndAtomFeedsAsync(List<Article> articles, string outputDir)
    {
        // RSS2フィード、Atomフィードどちらも出力しない場合は何もしない
        if (feedOption is { UseRss2: false, UseAtom: false })
        {
            return;
        }

        var rssFeed = new SyndicationFeed(
            title: siteOption.SiteName,
            description: siteOption.SiteDescription,
            feedAlternateLink: new Uri(siteOption.SiteUrl),
            id: siteOption.SiteUrl,
            lastUpdatedTime: new DateTimeOffset(DateTime.UtcNow.AddHours(9).Ticks, TimeSpan.FromHours(9)))
        {
            Language = feedOption.Language,
            Items = articles.Take(feedOption.MaxFeedItems).Select(article => new SyndicationItem(
                title: article.Title,
                content: article.ExcerptHtml,
                itemAlternateLink: new Uri($"{siteOption.SiteUrl.TrimEnd('/')}/{article.RelativeDirectoryPath.TrimEnd('/')}/{article.FileName}"),
                id: new Uri($"{siteOption.SiteUrl.TrimEnd('/')}/{article.RelativeDirectoryPath.TrimEnd('/')}/{article.FileName}").ToString(),
                lastUpdatedTime: article.Published
            ))
        };

        if (feedOption.UseRss2)
        {
            var writerRss20 = new Rss20FeedFormatter(rssFeed);
            await using var rssFile = File.Create(fileSystemHelper.CombineFilePath(outputDir, feedOption.RssFileName));

            await using var rssWriter = XmlWriter.Create(rssFile, new XmlWriterSettings { Async = true, Indent = true, Encoding = new UTF8Encoding(false) });
            writerRss20.WriteTo(rssWriter);
        }

        if (feedOption.UseAtom)
        {
            var writerAtom10 = new Atom10FeedFormatter(rssFeed);

            await using var atomFile = File.Create(fileSystemHelper.CombineFilePath(outputDir, feedOption.AtomFileName));

            await using var atomWriter = XmlWriter.Create(atomFile, new XmlWriterSettings { Async = true, Indent = true, Encoding = new UTF8Encoding(false) });
            writerAtom10.WriteTo(atomWriter);
        }
    }
}
