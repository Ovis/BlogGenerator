using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;
using BlogGenerator.Core.Interfaces;
using BlogGenerator.Models;

namespace BlogGenerator.Core;

public class RssFeedGenerator(SiteOption siteOption, IFileSystemHelper fileSystemHelper) : IRssFeedGenerator
{
    public async Task GenerateRssAndAtomFeedsAsync(List<Article> articles, string outputDir)
    {
        var rssFeed = new SyndicationFeed(
            title: siteOption.SiteName,
            description: siteOption.SiteDescription,
            feedAlternateLink: new Uri(siteOption.SiteUrl),
            id: siteOption.SiteUrl,
            lastUpdatedTime: new DateTimeOffset(DateTime.UtcNow.AddHours(9).Ticks, TimeSpan.FromHours(9)))
        {
            Language = "ja-JP",
            Items = articles.Take(10).Select(article => new SyndicationItem(
                title: article.Title,
                content: article.ExcerptHtml,
                itemAlternateLink: new Uri($"{siteOption.SiteUrl.TrimEnd('/')}/{article.RelativeDirectoryPath.TrimEnd('/')}/{article.FileName}"),
                id: new Uri($"{siteOption.SiteUrl.TrimEnd('/')}/{article.RelativeDirectoryPath.TrimEnd('/')}/{article.FileName}").ToString(),
                lastUpdatedTime: article.Published
            ))
        };

        var writerRss20 = new Rss20FeedFormatter(rssFeed);
        var writerAtom10 = new Atom10FeedFormatter(rssFeed);

        await using var rssFile = File.Create(fileSystemHelper.CombineFilePath(outputDir, "feed.rss"));
        await using var atomFile = File.Create(fileSystemHelper.CombineFilePath(outputDir, "feed.atom"));

        await using (var rssWriter = XmlWriter.Create(rssFile, new XmlWriterSettings { Async = true, Indent = true, Encoding = new UTF8Encoding(false) }))
        {
            writerRss20.WriteTo(rssWriter);
        }

        await using (var atomWriter = XmlWriter.Create(atomFile, new XmlWriterSettings { Async = true, Indent = true, Encoding = new UTF8Encoding(false) }))
        {
            writerAtom10.WriteTo(atomWriter);
        }
    }
}
