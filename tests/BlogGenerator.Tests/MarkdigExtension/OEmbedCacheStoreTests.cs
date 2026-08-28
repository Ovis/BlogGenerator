using System.Collections.Concurrent;
using BlogGenerator.MarkdigExtension;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class OEmbedCacheStoreTests
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
    public async Task キャッシュを保存して再読込できる()
    {
        var filePath = Path.Combine(_testRootPath, "oembed-cache.json");
        var sourceCache = new ConcurrentDictionary<string, OEmbedCacheEntry>();
        sourceCache["https://example.com/post"] = OEmbedCacheEntry.CreateSuccess(
            "<p>cached</p>",
            new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero),
            TimeSpan.FromDays(180));

        await OEmbedCacheStore.SaveAsync(filePath, sourceCache);

        var loadedCache = new ConcurrentDictionary<string, OEmbedCacheEntry>();
        await OEmbedCacheStore.LoadAsync(filePath, loadedCache);

        Assert.That(loadedCache, Contains.Key("https://example.com/post"));
        Assert.Multiple(() =>
        {
            Assert.That(loadedCache["https://example.com/post"].HtmlContent, Is.EqualTo("<p>cached</p>"));
            Assert.That(loadedCache["https://example.com/post"].Status, Is.EqualTo(OEmbedCacheEntryStatus.Success));
        });
    }

    [Test]
    public async Task OGP表示モデルを保存して再読込できる()
    {
        const string url = "https://example.com/post";
        var filePath = Path.Combine(_testRootPath, "oembed-ogp-cache.json");
        var sourceCache = new ConcurrentDictionary<string, OEmbedCacheEntry>();
        var model = new OgpCardModel(
            url,
            "example.com/post",
            "タイトル",
            "説明",
            "Example",
            "https://example.com/image.png",
            "https://example.com/favicon.ico");
        sourceCache[url] = OEmbedCacheEntry.CreateOgpSuccess(
            model,
            new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero),
            TimeSpan.FromDays(180));

        await OEmbedCacheStore.SaveAsync(filePath, sourceCache);

        var loadedCache = new ConcurrentDictionary<string, OEmbedCacheEntry>();
        await OEmbedCacheStore.LoadAsync(filePath, loadedCache);

        Assert.Multiple(() =>
        {
            Assert.That(loadedCache[url].OgpCard, Is.EqualTo(model));
            Assert.That(loadedCache[url].HtmlContent, Is.Empty);
            Assert.That(loadedCache[url].Status, Is.EqualTo(OEmbedCacheEntryStatus.Success));
        });
    }

    [Test]
    public async Task 旧形式キャッシュは成功エントリとして読める()
    {
        var filePath = Path.Combine(_testRootPath, "oembed-cache-legacy.json");
        await File.WriteAllTextAsync(filePath, """{"https://example.com/post":"<p>cached</p>"}""");

        var loadedCache = new ConcurrentDictionary<string, OEmbedCacheEntry>();
        var now = new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero);
        await OEmbedCacheStore.LoadAsync(filePath, loadedCache, () => now);

        Assert.That(loadedCache, Contains.Key("https://example.com/post"));
        Assert.Multiple(() =>
        {
            Assert.That(loadedCache["https://example.com/post"].HtmlContent, Is.EqualTo("<p>cached</p>"));
            Assert.That(loadedCache["https://example.com/post"].Status, Is.EqualTo(OEmbedCacheEntryStatus.Success));
            Assert.That(loadedCache["https://example.com/post"].FreshUntil, Is.EqualTo(now.AddDays(180)));
        });
    }
}
