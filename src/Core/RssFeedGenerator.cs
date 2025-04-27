using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;
using BlogGenerator.Models;

namespace BlogGenerator.Core;

public class RssFeedGenerator
{
    private readonly SiteOption _siteOption;
    private readonly FileSystemHelper _fileSystemHelper = new();

    public RssFeedGenerator(SiteOption siteOption)
    {
        _siteOption = siteOption;
    }

    public async Task GenerateRssAndAtomFeedsAsync(List<Article> articles, string outputDir)
    {
        var rssFeed = new SyndicationFeed(
            title: _siteOption.SiteName,
            description: _siteOption.SiteDescription,
            feedAlternateLink: new Uri(_siteOption.SiteUrl),
            id: _siteOption.SiteUrl,
            lastUpdatedTime: new DateTimeOffset(DateTime.UtcNow.AddHours(9).Ticks, TimeSpan.FromHours(9)))
        {
            Language = "ja-JP",
            Items = articles.Take(10).Select(article => new SyndicationItem(
                title: article.Title,
                content: article.ExcerptHtml,
                itemAlternateLink: new Uri($"{_siteOption.SiteUrl.TrimEnd('/')}/{article.RelativeDirectoryPath.TrimEnd('/')}/{article.FileName}"),
                id: new Uri($"{_siteOption.SiteUrl.TrimEnd('/')}/{article.RelativeDirectoryPath.TrimEnd('/')}/{article.FileName}").ToString(),
                lastUpdatedTime: article.Published
            ))
        };

        var writerRss20 = new Rss20FeedFormatter(rssFeed);
        var writerAtom10 = new Atom10FeedFormatter(rssFeed);

        await using var rssFile = File.Create(_fileSystemHelper.CombineFilePath(outputDir, "feed.rss"));
        await using var atomFile = File.Create(_fileSystemHelper.CombineFilePath(outputDir, "feed.atom"));

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
