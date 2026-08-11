using System.Net.Http;
using System.Reflection;
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
        OEmbedTestState.Prepare();

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
        OEmbedTestState.Prepare();

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
    public async Task oEmbedキャッシュ済みURLを本文へ展開できる()
    {
        const string targetUrl = "https://example.com/post";
        const string expectedHtml = "<div class='oembed-card'>cached content</div>";
        OEmbedTestState.Prepare(new Dictionary<string, string>
        {
            [targetUrl] = expectedHtml
        });

        var (inputDir, outputDir) = CreateInputAndOutputDirectories();
        var articlePath = Path.Combine(inputDir, "oembed.md");
        await File.WriteAllTextAsync(articlePath, $"""
            [oembed:"{targetUrl}"]
            """);

        var processor = CreateProcessor();

        var articles = await processor.ProcessMarkdownFilesAsync(inputDir, outputDir, "/blog/");

        Assert.That(articles, Has.Count.EqualTo(1));
        Assert.That(articles[0].Body, Does.Contain(expectedHtml));
    }

    [Test]
    public async Task oEmbed記法を含む本文は1回だけ評価される()
    {
        var countingParser = new CountingOEmbedCardParser();
        OEmbedTestState.Prepare(parser: countingParser);

        var (inputDir, outputDir) = CreateInputAndOutputDirectories();
        var articlePath = Path.Combine(inputDir, "post.md");
        await File.WriteAllTextAsync(articlePath, """
            ---
            Title: Count parser
            ---
            [oembed:"https://example.com/post"]
            """);

        var processor = CreateProcessor();

        await processor.ProcessMarkdownFilesAsync(inputDir, outputDir, "/blog/");

        Assert.That(countingParser.MatchCallCount, Is.EqualTo(1));
    }

    private MarkdownProcessor CreateProcessor()
    {
        return new MarkdownProcessor(
            new SiteOption
            {
                SiteUrl = "https://example.com/blog/"
            },
            string.Empty);
    }

    private (string inputDir, string outputDir) CreateInputAndOutputDirectories()
    {
        var inputDir = Path.Combine(_testRootPath, "input");
        var outputDir = Path.Combine(_testRootPath, "output");
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outputDir);
        return (inputDir, outputDir);
    }

    private static class OEmbedTestState
    {
        public static void Prepare(
            IDictionary<string, string>? cachedEntries = null,
            OEmbedCardParser? parser = null)
        {
            OEmbedCardParser.OEmbedCache.Clear();

            if (cachedEntries != null)
            {
                foreach (var cachedEntry in cachedEntries)
                {
                    OEmbedCardParser.OEmbedCache[cachedEntry.Key] = cachedEntry.Value;
                }
            }

            parser ??= new OEmbedCardParser(new OEmbedProviderCatalog([]), new HttpClient());

            // MarkdownProcessor生成時の初回プロバイダ取得を避け、テストを外部通信から切り離す
            typeof(OEmbedCardExtension)
                .GetField("_isFirstCall", BindingFlags.Static | BindingFlags.NonPublic)!
                .SetValue(null, false);

            typeof(OEmbedCardExtension)
                .GetProperty(nameof(OEmbedCardExtension.OEmbedCardParser), BindingFlags.Static | BindingFlags.Public)!
                .GetSetMethod(nonPublic: true)!
                .Invoke(null, [parser]);
        }
    }

    private sealed class CountingOEmbedCardParser()
        : OEmbedCardParser(new OEmbedProviderCatalog([]), new HttpClient())
    {
        public int MatchCallCount { get; private set; }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            MatchCallCount++;
            return false;
        }
    }
}
