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
    public async Task ルート直下固定ページのフィードURLに二重スラッシュを含めない()
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
            Assert.That(rssXml, Does.Contain("https://example.com/blog/about.html"));
            Assert.That(rssXml, Does.Not.Contain("https://example.com/blog//about.html"));
            Assert.That(rssXml, Does.Not.Contain("Draft article"));
            Assert.That(atomXml, Does.Contain("https://example.com/blog/about.html"));
            Assert.That(atomXml, Does.Not.Contain("https://example.com/blog//about.html"));
            Assert.That(atomXml, Does.Not.Contain("Draft article"));
        });
    }
}
