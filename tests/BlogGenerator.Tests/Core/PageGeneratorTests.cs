using BlogGenerator.Core;
using BlogGenerator.Models;
using NUnit.Framework;
using RazorLight;

namespace BlogGenerator.Tests.Core;

[TestFixture]
[NonParallelizable]
public class PageGeneratorTests
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
    public async Task サイドバーは未設定公開日のアーカイブリンクも出力する()
    {
        var pageGenerator = CreatePageGenerator();
        var articles = CreateArticles();

        var html = await pageGenerator.GenerateSideBarHtmlAsync(articles);

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("/blog/tags/csharp"));
            Assert.That(html, Does.Contain("csharp (2)"));
            Assert.That(html, Does.Contain("/blog/1/01"));
            Assert.That(html, Does.Contain("1-01 (1)"));
        });
    }

    [Test]
    public async Task タグ一覧とタグ別ページを生成できる()
    {
        var pageGenerator = CreatePageGenerator();
        var outputDir = CreateOutputDirectory();
        var articles = CreateArticles();

        await pageGenerator.GenerateTagPagesAsync(articles, outputDir, "<aside>stub</aside>");

        var tagIndexPath = Path.Combine(outputDir, "tags", "index.html");
        var csharpTagPath = Path.Combine(outputDir, "tags", "csharp", "index.html");
        var dotnetTagPath = Path.Combine(outputDir, "tags", "dotnet", "index.html");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(tagIndexPath), Is.True);
            Assert.That(File.Exists(csharpTagPath), Is.True);
            Assert.That(File.Exists(dotnetTagPath), Is.True);
        });

        var tagIndexHtml = await File.ReadAllTextAsync(tagIndexPath);
        var csharpTagHtml = await File.ReadAllTextAsync(csharpTagPath);

        Assert.Multiple(() =>
        {
            Assert.That(tagIndexHtml, Does.Contain("/blog/tags/csharp"));
            Assert.That(tagIndexHtml, Does.Contain("csharp (2)"));
            Assert.That(tagIndexHtml, Does.Contain("dotnet (1)"));
            Assert.That(csharpTagHtml, Does.Contain("First article"));
            Assert.That(csharpTagHtml, Does.Contain("Second article"));
            Assert.That(csharpTagHtml, Does.Contain("/blog/tags/csharp"));
        });
    }

    [Test]
    public async Task アーカイブページ生成は未設定公開日を出力しない()
    {
        var pageGenerator = CreatePageGenerator();
        var outputDir = CreateOutputDirectory();
        var articles = CreateArticles();

        await pageGenerator.GenerateArchivePagesAsync(articles, outputDir, "<aside>stub</aside>");

        var augustArchivePath = Path.Combine(outputDir, "2026", "08", "index.html");
        var undefinedArchivePath = Path.Combine(outputDir, "0001", "01", "index.html");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(augustArchivePath), Is.True);
            Assert.That(File.Exists(undefinedArchivePath), Is.False);
        });

        var archiveHtml = await File.ReadAllTextAsync(augustArchivePath);

        Assert.Multiple(() =>
        {
            Assert.That(archiveHtml, Does.Contain("First article"));
            Assert.That(archiveHtml, Does.Contain("Second article"));
            Assert.That(archiveHtml, Does.Not.Contain("Draft article"));
        });
    }

    [Test]
    public async Task 記事ページは通常記事と固定ページでヘッダー表示を切り替える()
    {
        var pageGenerator = CreatePageGenerator();
        var outputDir = CreateOutputDirectory();
        var articles = new List<Article>
        {
            new(
                FileName: "article.html",
                Title: "Normal article",
                Body: "<p>Normal body</p>",
                Tags: ["csharp"],
                Published: DateTimeOffset.Parse("2026-08-11T10:00:00+09:00"),
                RelativeDirectoryPath: "posts",
                RootRelativeDirectoryPath: "/blog/posts",
                IsFixedPage: false),
            new(
                FileName: "about.html",
                Title: "About page",
                Body: "<p>Fixed body</p>",
                Tags: ["profile"],
                Published: DateTimeOffset.Parse("2026-08-11T10:00:00+09:00"),
                RelativeDirectoryPath: string.Empty,
                RootRelativeDirectoryPath: "/blog",
                IsFixedPage: true)
        };

        await pageGenerator.GenerateArticlePagesAsync(articles, outputDir, "<aside>stub</aside>");

        var normalArticleHtml = await File.ReadAllTextAsync(Path.Combine(outputDir, "posts", "article.html"));
        var fixedPageHtml = await File.ReadAllTextAsync(Path.Combine(outputDir, "about.html"));

        Assert.Multiple(() =>
        {
            Assert.That(normalArticleHtml, Does.Contain("Normal article"));
            Assert.That(normalArticleHtml, Does.Contain("2026/08/11 10:00"));
            Assert.That(normalArticleHtml, Does.Contain("/blog/tags/csharp"));
            Assert.That(normalArticleHtml, Does.Contain("pt-0"));
            Assert.That(fixedPageHtml, Does.Contain("Fixed body"));
            Assert.That(fixedPageHtml, Does.Not.Contain("fa-calendar-alt"));
            Assert.That(fixedPageHtml, Does.Not.Contain("fa-tags"));
            Assert.That(fixedPageHtml, Does.Not.Contain("pt-0"));
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
            .DisableEncoding()
            .Build();

        return new PageGenerator(razorLightEngine, siteOption, new FileSystemHelper());
    }

    private List<Article> CreateArticles()
    {
        return
        [
            new Article(
                FileName: "first.html",
                Title: "First article",
                Body: "<p>First body</p>",
                Tags: ["csharp", "dotnet"],
                Published: DateTimeOffset.Parse("2026-08-10T09:00:00+09:00"),
                RelativeDirectoryPath: "posts",
                RootRelativeDirectoryPath: "/blog/posts",
                IsFixedPage: false),
            new Article(
                FileName: "second.html",
                Title: "Second article",
                Body: "<p>Second body</p>",
                Tags: ["csharp"],
                Published: DateTimeOffset.Parse("2026-08-05T08:00:00+09:00"),
                RelativeDirectoryPath: "posts",
                RootRelativeDirectoryPath: "/blog/posts",
                IsFixedPage: false),
            new Article(
                FileName: "draft.html",
                Title: "Draft article",
                Body: "<p>Draft body</p>",
                Tags: ["draft"],
                Published: DateTimeOffset.MinValue,
                RelativeDirectoryPath: "drafts",
                RootRelativeDirectoryPath: "/blog/drafts",
                IsFixedPage: false)
        ];
    }

    private string CreateOutputDirectory()
    {
        var outputDir = Path.Combine(_testRootPath, "output");
        Directory.CreateDirectory(outputDir);
        return outputDir;
    }

    private static string GetRepositoryRootPath()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BlogGenerator.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("BlogGenerator.sln を基準にしたリポジトリルートを特定できません。");
    }
}
