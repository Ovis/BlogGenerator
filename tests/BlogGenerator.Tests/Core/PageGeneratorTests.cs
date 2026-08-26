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
    public async Task サイドバーのアーカイブ一覧は未設定公開日を除外する()
    {
        var pageGenerator = CreatePageGenerator();
        var articles = CreateArticles();

        var html = await pageGenerator.GenerateSideBarHtmlAsync(articles);

        Assert.Multiple(() =>
        {
            Assert.That(html.Value, Does.Not.Contain("Draft article"));
            Assert.That(html.Value, Does.Contain("/blog/tags/csharp"));
            Assert.That(html.Value, Does.Contain("csharp (2)"));
            Assert.That(html.Value, Does.Not.Contain("draft (1)"));
            Assert.That(html.Value, Does.Contain("/blog/2026/08"));
            Assert.That(html.Value, Does.Not.Contain("/blog/1/01"));
            Assert.That(html.Value, Does.Not.Contain("1-01 (1)"));
        });
    }

    [Test]
    public async Task タグ一覧とタグ別ページを生成できる()
    {
        var pageGenerator = CreatePageGenerator();
        var outputDir = CreateOutputDirectory();
        var articles = CreateArticles();

        await pageGenerator.GenerateTagPagesAsync(articles, outputDir, new TrustedHtml("<aside>stub</aside>"));

        var tagIndexPath = Path.Combine(outputDir, "tags", "index.html");
        var csharpTagPath = Path.Combine(outputDir, "tags", "csharp", "index.html");
        var dotnetTagPath = Path.Combine(outputDir, "tags", "dotnet", "index.html");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(tagIndexPath), Is.True);
            Assert.That(File.Exists(csharpTagPath), Is.True);
            Assert.That(File.Exists(dotnetTagPath), Is.True);
            Assert.That(File.Exists(Path.Combine(outputDir, "tags", "draft", "index.html")), Is.False);
        });

        var tagIndexHtml = await File.ReadAllTextAsync(tagIndexPath);
        var csharpTagHtml = await File.ReadAllTextAsync(csharpTagPath);

        Assert.Multiple(() =>
        {
            Assert.That(tagIndexHtml, Does.Contain("/blog/tags/csharp"));
            Assert.That(tagIndexHtml, Does.Contain("csharp (2)"));
            Assert.That(tagIndexHtml, Does.Contain("dotnet (1)"));
            Assert.That(tagIndexHtml, Does.Not.Contain("draft (1)"));
            Assert.That(csharpTagHtml, Does.Contain("First article"));
            Assert.That(csharpTagHtml, Does.Contain("Second article"));
            Assert.That(csharpTagHtml, Does.Not.Contain("Draft article"));
            Assert.That(csharpTagHtml, Does.Contain("/blog/tags/csharp"));
        });
    }

    [Test]
    public async Task アーカイブページ生成は未設定公開日を出力しない()
    {
        var pageGenerator = CreatePageGenerator();
        var outputDir = CreateOutputDirectory();
        var articles = CreateArticles();

        await pageGenerator.GenerateArchivePagesAsync(articles, outputDir, new TrustedHtml("<aside>stub</aside>"));

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

        await pageGenerator.GenerateArticlePagesAsync(articles, outputDir, new TrustedHtml("<aside>stub</aside>"));

        var normalArticleHtml = await File.ReadAllTextAsync(Path.Combine(outputDir, "posts", "article.html"));
        var fixedPageHtml = await File.ReadAllTextAsync(Path.Combine(outputDir, "about.html"));

        Assert.Multiple(() =>
        {
            Assert.That(normalArticleHtml, Does.Contain("Normal article"));
            Assert.That(normalArticleHtml, Does.Contain("2026/08/11 10:00"));
            Assert.That(normalArticleHtml, Does.Contain("/blog/tags/csharp"));
            Assert.That(normalArticleHtml, Does.Contain("href=\"/blog/posts/article.html\""));
            Assert.That(normalArticleHtml, Does.Contain("content=\"https://example.com/blog/posts/article.html\""));
            Assert.That(normalArticleHtml, Does.Not.Contain("/blog/blog/posts/article.html"));
            Assert.That(normalArticleHtml, Does.Contain("pt-0"));
            Assert.That(fixedPageHtml, Does.Contain("Fixed body"));
            Assert.That(fixedPageHtml, Does.Not.Contain("fa-calendar-alt"));
            Assert.That(fixedPageHtml, Does.Not.Contain("fa-tags"));
            Assert.That(fixedPageHtml, Does.Not.Contain("pt-0"));
        });
    }

    [Test]
    public async Task 未公開記事の個別ページは生成しない()
    {
        var pageGenerator = CreatePageGenerator();
        var outputDir = CreateOutputDirectory();
        var articles = CreateArticles();

        await pageGenerator.GenerateArticlePagesAsync(articles, outputDir, new TrustedHtml("<aside>stub</aside>"));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(outputDir, "posts", "first.html")), Is.True);
            Assert.That(File.Exists(Path.Combine(outputDir, "posts", "second.html")), Is.True);
            Assert.That(File.Exists(Path.Combine(outputDir, "drafts", "draft.html")), Is.False);
        });
    }

    [Test]
    public async Task 日本語や空白や記号を含むタグは共通slugでURLと物理パスを生成する()
    {
        var pageGenerator = CreatePageGenerator();
        var outputDir = CreateOutputDirectory();
        const string tagName = "C#/日本語 fizz buzz?";
        const string slug = "c~23~2F日本語-fizz-buzz~3F";
        var articles = new List<Article>
        {
            new(
                FileName: "tagged.html",
                Title: "Tagged article",
                Body: "<p>Tagged body</p>",
                Tags: [tagName],
                Published: DateTimeOffset.Parse("2026-08-11T10:00:00+09:00"),
                RelativeDirectoryPath: "posts",
                RootRelativeDirectoryPath: "/blog/posts",
                IsFixedPage: false)
        };

        await pageGenerator.GenerateTagPagesAsync(articles, outputDir, new TrustedHtml("<aside>stub</aside>"));
        await pageGenerator.GenerateArticlePagesAsync(articles, outputDir, new TrustedHtml("<aside>stub</aside>"));

        var tagIndexHtml = await File.ReadAllTextAsync(Path.Combine(outputDir, "tags", "index.html"));
        var tagPagePath = Path.Combine(outputDir, "tags", slug, "index.html");
        var tagPageHtml = await File.ReadAllTextAsync(tagPagePath);
        var articleHtml = await File.ReadAllTextAsync(Path.Combine(outputDir, "posts", "tagged.html"));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(tagPagePath), Is.True);
            Assert.That(tagIndexHtml, Does.Contain($"/blog/tags/{slug}"));
            Assert.That(tagPageHtml, Does.Contain($"/blog/tags/{slug}"));
            Assert.That(articleHtml, Does.Contain($"/blog/tags/{slug}"));
            Assert.That(tagIndexHtml, Does.Contain("C#/日本語 fizz buzz? (1)"));
        });
    }

    [Test]
    public async Task 複数ページのページネーションURLはスラッシュ区切りで生成する()
    {
        var pageGenerator = CreatePageGenerator();
        var outputDir = CreateOutputDirectory();
        var articles = CreatePagedArticles();

        await pageGenerator.GenerateIndexPagesAsync(articles, outputDir, new TrustedHtml("<aside>stub</aside>"));
        await pageGenerator.GenerateTagPagesAsync(articles, outputDir, new TrustedHtml("<aside>stub</aside>"));
        await pageGenerator.GenerateArchivePagesAsync(articles, outputDir, new TrustedHtml("<aside>stub</aside>"));

        var indexPageHtml = await File.ReadAllTextAsync(Path.Combine(outputDir, "index.html"));
        var csharpTagHtml = await File.ReadAllTextAsync(Path.Combine(outputDir, "tags", "csharp", "index.html"));
        var archiveHtml = await File.ReadAllTextAsync(Path.Combine(outputDir, "2026", "08", "index.html"));

        Assert.Multiple(() =>
        {
            Assert.That(indexPageHtml, Does.Contain("href=\"/blog/2\""));
            Assert.That(indexPageHtml, Does.Not.Contain("/blog\\2"));
            Assert.That(csharpTagHtml, Does.Contain("href=\"/blog/tags/csharp/2\""));
            Assert.That(csharpTagHtml, Does.Not.Contain("/blog/tags/csharp\\2"));
            Assert.That(archiveHtml, Does.Contain("href=\"/blog/2026/08/2\""));
            Assert.That(archiveHtml, Does.Not.Contain("/blog/2026/08\\2"));
        });
    }

    [Test]
    public async Task 通常文字列はエスケープし信頼済みHTMLだけを生出力する()
    {
        var siteOption = new SiteOption
        {
            SiteName = "Blog <Generator> & Test",
            SiteDescription = "Description <b>unsafe</b>",
            SiteUrl = "https://example.com/blog/",
            SiteAuthor = "Test Author",
            SiteAuthorDescription = "Author Description"
        };
        var pageGenerator = CreatePageGenerator(siteOption);
        var outputDir = CreateOutputDirectory();
        var articles = new List<Article>
        {
            new(
                FileName: "encoded.html",
                Title: "Title <unsafe> & text",
                Body: "<p><strong>Trusted body</strong></p>",
                Tags: ["csharp"],
                Published: DateTimeOffset.Parse("2026-08-11T10:00:00+09:00"),
                RelativeDirectoryPath: "posts",
                RootRelativeDirectoryPath: "/blog/posts",
                IsFixedPage: false)
        };

        await pageGenerator.GenerateArticlePagesAsync(
            articles,
            outputDir,
            new TrustedHtml("<aside><strong>Trusted sidebar</strong></aside>"));

        var html = await File.ReadAllTextAsync(Path.Combine(outputDir, "posts", "encoded.html"));

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("Title &lt;unsafe&gt; &amp; text"));
            Assert.That(html, Does.Contain("Blog &lt;Generator&gt; &amp; Test"));
            Assert.That(html, Does.Contain("<strong>Trusted body</strong>"));
            Assert.That(html, Does.Not.Contain("&lt;strong&gt;Trusted body&lt;/strong&gt;"));
            Assert.That(html, Does.Contain("<aside><strong>Trusted sidebar</strong></aside>"));
            Assert.That(html, Does.Not.Contain("&lt;aside&gt;&lt;strong&gt;Trusted sidebar&lt;/strong&gt;&lt;/aside&gt;"));
        });
    }

    private PageGenerator CreatePageGenerator(SiteOption? siteOption = null)
    {
        var templatePath = Path.Combine(GetRepositoryRootPath(), "src", "TemplateSample");
        siteOption ??= new SiteOption
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

    private List<Article> CreatePagedArticles()
    {
        return Enumerable.Range(1, 11)
            .Select(index => new Article(
                FileName: $"article-{index}.html",
                Title: $"Paged article {index}",
                Body: $"<p>Paged body {index}</p>",
                Tags: ["csharp"],
                Published: DateTimeOffset.Parse($"2026-08-{index:00}T09:00:00+09:00"),
                RelativeDirectoryPath: "posts",
                RootRelativeDirectoryPath: "/blog/posts",
                IsFixedPage: false))
            .ToList();
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
            if (File.Exists(Path.Combine(current.FullName, "BlogGenerator.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("BlogGenerator.slnx を基準にしたリポジトリルートを特定できません。");
    }
}