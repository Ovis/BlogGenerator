using System.Xml.Linq;
using BlogGenerator.Core;
using BlogGenerator.Models;
using NUnit.Framework;

namespace BlogGenerator.Tests.Core;

[TestFixture]
[NonParallelizable]
public class RssFeedGeneratorTests
{
    private string _testRootPath = null!;

    [SetUp]
    public void SetUp()
    {
        _testRootPath = Path.Combine(Path.GetTempPath(), "BlogGenerator.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRootPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testRootPath))
        {
            Directory.Delete(_testRootPath, recursive: true);
        }
    }

    [Test]
    public async Task 固定ページと未公開記事はフィードから除外する()
    {
        var generator = new RssFeedGenerator(
            new SiteOption
            {
                SiteName = "BlogGenerator Test",
                SiteDescription = "Test Description",
                SiteUrl = "https://example.com/blog/"
            },
            new FeedOption(),
            new FileSystemHelper());

        var outputDir = Path.Combine(_testRootPath, "output");
        Directory.CreateDirectory(outputDir);

        await generator.GenerateRssAndAtomFeedsAsync(
            [
                new Article(
                    FileName: "article.html",
                    Title: "Published article",
                    Body: "<p>Published body</p>",
                    Tags: ["normal"],
                    Published: DateTimeOffset.Parse("2026-08-12T10:00:00+09:00"),
                    RelativeDirectoryPath: "entry",
                    RootRelativeDirectoryPath: "/blog/entry",
                    IsFixedPage: false),
                new Article(
                    FileName: "about.html",
                    Title: "About page",
                    Body: "<p>Fixed body</p>",
                    Tags: ["profile"],
                    Published: DateTimeOffset.Parse("2026-08-11T10:00:00+09:00"),
                    RelativeDirectoryPath: string.Empty,
                    RootRelativeDirectoryPath: "/blog",
                    IsFixedPage: true),
                new Article(
                    FileName: "draft.html",
                    Title: "Draft article",
                    Body: "<p>Draft body</p>",
                    Tags: ["draft"],
                    Published: DateTimeOffset.MinValue,
                    RelativeDirectoryPath: "drafts",
                    RootRelativeDirectoryPath: "/blog/drafts",
                    IsFixedPage: false)
            ],
            outputDir);

        var rssXml = XDocument.Load(Path.Combine(outputDir, "feed.rss")).ToString();
        var atomXml = XDocument.Load(Path.Combine(outputDir, "feed.atom")).ToString();

        Assert.Multiple(() =>
        {
            Assert.That(rssXml, Does.Contain("Published article"));
            Assert.That(rssXml, Does.Contain("https://example.com/blog/entry/article.html"));
            Assert.That(rssXml, Does.Not.Contain("About page"));
            Assert.That(rssXml, Does.Not.Contain("Draft article"));
            Assert.That(atomXml, Does.Contain("Published article"));
            Assert.That(atomXml, Does.Contain("https://example.com/blog/entry/article.html"));
            Assert.That(atomXml, Does.Not.Contain("About page"));
            Assert.That(atomXml, Does.Not.Contain("Draft article"));
        });
    }
}
