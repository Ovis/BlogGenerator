using BlogGenerator.Core;
using BlogGenerator.Models;
using NUnit.Framework;
using RazorLight;

namespace BlogGenerator.Tests.Core;

[TestFixture]
[NonParallelizable]
public class PageGeneratorPaginationTests
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
            Directory.Delete(_testRootPath, recursive: true);
    }

    [Test]
    public async Task 一覧ページは10件ごとに分割して同じページ番号規則で出力する()
    {
        var pageGenerator = CreatePageGenerator();
        var outputDir = Path.Combine(_testRootPath, "output");
        Directory.CreateDirectory(outputDir);
        var articles = CreateArticles(21);
        var sideBar = new TrustedHtml("<aside>stub</aside>");

        await pageGenerator.GenerateIndexPagesAsync(articles, outputDir, sideBar);
        await pageGenerator.GenerateTagPagesAsync(articles, outputDir, sideBar);
        await pageGenerator.GenerateArchivePagesAsync(articles, outputDir, sideBar);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(outputDir, "index.html")), Is.True);
            Assert.That(File.Exists(Path.Combine(outputDir, "2.html")), Is.True);
            Assert.That(File.Exists(Path.Combine(outputDir, "3.html")), Is.True);

            Assert.That(File.Exists(Path.Combine(outputDir, "tags", "csharp", "index.html")), Is.True);
            Assert.That(File.Exists(Path.Combine(outputDir, "tags", "csharp", "2.html")), Is.True);
            Assert.That(File.Exists(Path.Combine(outputDir, "tags", "csharp", "3.html")), Is.True);

            Assert.That(File.Exists(Path.Combine(outputDir, "2026", "08", "index.html")), Is.True);
            Assert.That(File.Exists(Path.Combine(outputDir, "2026", "08", "2.html")), Is.True);
            Assert.That(File.Exists(Path.Combine(outputDir, "2026", "08", "3.html")), Is.True);
        });
    }

    private PageGenerator CreatePageGenerator()
    {
        var templatePath = Path.Combine(GetRepositoryRootPath(), "src", "TemplateSample");
        var siteOption = new SiteOption
        {
            SiteName = "BlogGenerator Test",
            SiteDescription = "Test Description",
            SiteUrl = "https://example.com/blog/",
            SiteAuthor = "Test Author",
            SiteAuthorDescription = "Author Description"
        };

        var razorLightEngine = new RazorLightEngineBuilder()
            .UseFileSystemProject(templatePath)
            .UseMemoryCachingProvider()
            .Build();

        return new PageGenerator(razorLightEngine, siteOption, new FileSystemHelper());
    }

    private static List<Article> CreateArticles(int count) =>
        Enumerable.Range(1, count)
            .Select(index => new Article(
                FileName: $"article-{index}.html",
                Title: $"Article {index}",
                Body: "<p>body</p>",
                Tags: ["csharp"],
                Published: DateTimeOffset.Parse($"2026-08-{index:00}T09:00:00+09:00"),
                RelativeDirectoryPath: "posts",
                RootRelativeDirectoryPath: "/blog/posts",
                IsFixedPage: false))
            .ToList();

    private static string GetRepositoryRootPath()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BlogGenerator.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("BlogGenerator.slnx を基準にしたリポジトリルートを特定できません。");
    }
}
