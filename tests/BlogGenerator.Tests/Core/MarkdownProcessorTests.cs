using System.Net.Http;
using BlogGenerator.Core;
using BlogGenerator.MarkdigExtension;
using BlogGenerator.Models;
using Markdig.Helpers;
using Markdig.Parsers;
using NUnit.Framework;

namespace BlogGenerator.Tests.Core;

[TestFixture]
[NonParallelizable]
public class MarkdownProcessorTests
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
    public async Task FrontmatterありのMarkdownをArticleへ変換できる()
    {
        var (inputDir, outputDir) = CreateInputAndOutputDirectories();
        var articlePath = Path.Combine(inputDir, "posts", "hello.md");
        Directory.CreateDirectory(Path.GetDirectoryName(articlePath)!);
        Directory.CreateDirectory(Path.Combine(inputDir, "posts", "img"));

        await File.WriteAllTextAsync(articlePath, """
            ---
            Title: Frontmatter title
            Published: 2026-08-10T12:34:56+09:00
            Tags:
              - csharp
              - blog
            IsFixedPage: true
            ---
            ![sample](img/sample.png)
            """);
        await File.WriteAllBytesAsync(Path.Combine(inputDir, "posts", "img", "sample.png"), [1, 2, 3]);

        var processor = CreateProcessor();

        var articles = await processor.ProcessMarkdownFilesAsync(inputDir, outputDir, "/blog/");

        Assert.That(articles, Has.Count.EqualTo(1));

        var article = articles[0];
        Assert.Multiple(() =>
        {
            Assert.That(article.FileName, Is.EqualTo("hello.html"));
            Assert.That(article.Title, Is.EqualTo("Frontmatter title"));
            Assert.That(article.Published, Is.EqualTo(DateTimeOffset.Parse("2026-08-10T12:34:56+09:00")));
            Assert.That(article.Tags, Is.EqualTo(new[] { "csharp", "blog" }));
            Assert.That(article.IsFixedPage, Is.True);
            Assert.That(article.RelativeDirectoryPath, Is.EqualTo("posts"));
            Assert.That(article.RootRelativeDirectoryPath, Is.EqualTo("/blog/posts"));
            Assert.That(article.Body, Does.Contain("/blog/posts/img/sample.png"));
        });
    }

    [Test]
    public async Task FrontmatterなしのMarkdownは既定値で変換できる()
    {
        var (inputDir, outputDir) = CreateInputAndOutputDirectories();
        var articlePath = Path.Combine(inputDir, "plain.md");
        await File.WriteAllTextAsync(articlePath, "本文だけです");

        var processor = CreateProcessor();

        var articles = await processor.ProcessMarkdownFilesAsync(inputDir, outputDir, "/blog/");

        Assert.That(articles, Has.Count.EqualTo(1));

        var article = articles[0];
        Assert.Multiple(() =>
        {
            Assert.That(article.Title, Is.EqualTo(string.Empty));
            Assert.That(article.Published, Is.EqualTo(DateTimeOffset.MinValue));
            Assert.That(article.Tags, Is.Empty);
            Assert.That(article.IsFixedPage, Is.False);
            Assert.That(article.RelativeDirectoryPath, Is.EqualTo(string.Empty));
            Assert.That(article.Body, Does.Contain("<p>本文だけです</p>"));
        });
    }

    [Test]
    public async Task 大文字拡張子のMarkdownも記事として変換できる()
    {
        var (inputDir, outputDir) = CreateInputAndOutputDirectories();
        var articlePath = Path.Combine(inputDir, "HELLO.MD");
        await File.WriteAllTextAsync(articlePath, "大文字拡張子です");

        var processor = CreateProcessor();

        var articles = await processor.ProcessMarkdownFilesAsync(inputDir, outputDir, "/blog/");

        Assert.That(articles, Has.Count.EqualTo(1));

        var article = articles[0];
        Assert.Multiple(() =>
        {
            Assert.That(article.FileName, Is.EqualTo("HELLO.html"));
            Assert.That(article.RelativeDirectoryPath, Is.EqualTo(string.Empty));
            Assert.That(article.Body, Does.Contain("<p>大文字拡張子です</p>"));
        });
    }

    [Test]
    public async Task 外部画像URLは絶対パス化せずそのまま出力できる()
    {
        var (inputDir, outputDir) = CreateInputAndOutputDirectories();
        var articlePath = Path.Combine(inputDir, "external-image.md");
        await File.WriteAllTextAsync(articlePath, "![sample](https://cdn.example.com/sample.png)");

        var processor = CreateProcessor();

        var articles = await processor.ProcessMarkdownFilesAsync(inputDir, outputDir, "/blog/");

        Assert.That(articles, Has.Count.EqualTo(1));

        var article = articles[0];
        Assert.Multiple(() =>
        {
            Assert.That(article.RelativeDirectoryPath, Is.EqualTo(string.Empty));
            Assert.That(article.Body, Does.Contain("https://cdn.example.com/sample.png"));
            Assert.That(article.Body, Does.Not.Contain("/blog/https://cdn.example.com/sample.png"));
            Assert.That(article.Body, Does.Not.Contain("\\https://cdn.example.com/sample.png"));
        });
    }

    [Test]
    public async Task ルート相対画像URLは記事ディレクトリ配下へ再解決しない()
    {
        var (inputDir, outputDir) = CreateInputAndOutputDirectories();
        var articlePath = Path.Combine(inputDir, "posts", "root-relative-image.md");
        Directory.CreateDirectory(Path.GetDirectoryName(articlePath)!);
        await File.WriteAllTextAsync(articlePath, "![sample](/img/sample.png)");

        var processor = CreateProcessor();

        var articles = await processor.ProcessMarkdownFilesAsync(inputDir, outputDir, "/blog/");

        Assert.That(articles, Has.Count.EqualTo(1));

        var article = articles[0];
        Assert.Multiple(() =>
        {
            Assert.That(article.RelativeDirectoryPath, Is.EqualTo("posts"));
            Assert.That(article.Body, Does.Contain("/img/sample.png"));
            Assert.That(article.Body, Does.Not.Contain("/blog/posts/img/sample.png"));
        });
    }

    [Test]
    public async Task oEmbedキャッシュ済みURLを本文へ展開できる()
    {
        const string targetUrl = "https://example.com/post";
        const string expectedHtml = "<div class='oembed-card'>cached content</div>";

        var (inputDir, outputDir) = CreateInputAndOutputDirectories();
        var articlePath = Path.Combine(inputDir, "oembed.md");
        await File.WriteAllTextAsync(articlePath, $"""
            [oembed:"{targetUrl}"]
            """);

        var processor = CreateProcessor(new Dictionary<string, string>
        {
            [targetUrl] = expectedHtml
        });

        var articles = await processor.ProcessMarkdownFilesAsync(inputDir, outputDir, "/blog/");

        Assert.That(articles, Has.Count.EqualTo(1));
        Assert.That(articles[0].Body, Does.Contain(expectedHtml));
    }

    [Test]
    public async Task oEmbed記法を含む本文は1回だけ評価される()
    {
        var countingParser = new CountingOEmbedCardParser();

        var (inputDir, outputDir) = CreateInputAndOutputDirectories();
        var articlePath = Path.Combine(inputDir, "post.md");
        await File.WriteAllTextAsync(articlePath, """
            ---
            Title: Count parser
            ---
            [oembed:"https://example.com/post"]
            """);

        var processor = CreateProcessor(parser: countingParser);

        await processor.ProcessMarkdownFilesAsync(inputDir, outputDir, "/blog/");

        Assert.That(countingParser.MatchCallCount, Is.EqualTo(1));
    }

    private MarkdownProcessor CreateProcessor(
        IDictionary<string, string>? cachedEntries = null,
        OEmbedCardParser? parser = null)
    {
        var resolver = new OEmbedResolver(
            new OEmbedProviderCatalog([]),
            new HttpClient(new ThrowIfCalledHandler()));

        if (cachedEntries != null)
        {
            foreach (var cachedEntry in cachedEntries)
            {
                resolver.OEmbedCache[cachedEntry.Key] = cachedEntry.Value;
            }
        }

        return new MarkdownProcessor(
            new SiteOption
            {
                SiteUrl = "https://example.com/blog/"
            },
            string.Empty,
            resolver,
            parser);
    }

    private (string inputDir, string outputDir) CreateInputAndOutputDirectories()
    {
        var inputDir = Path.Combine(_testRootPath, "input");
        var outputDir = Path.Combine(_testRootPath, "output");
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outputDir);
        return (inputDir, outputDir);
    }

    private sealed class CountingOEmbedCardParser()
        : OEmbedCardParser()
    {
        public int MatchCallCount { get; private set; }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            MatchCallCount++;
            return false;
        }
    }

    private sealed class ThrowIfCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new AssertionException($"HTTP should not be called. URL: {request.RequestUri}");
        }
    }
}
