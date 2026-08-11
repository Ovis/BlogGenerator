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
        var sourceCache = new ConcurrentDictionary<string, string>();
        sourceCache["https://example.com/post"] = "<p>cached</p>";

        await OEmbedCacheStore.SaveAsync(filePath, sourceCache);

        var loadedCache = new ConcurrentDictionary<string, string>();
        await OEmbedCacheStore.LoadAsync(filePath, loadedCache);

        Assert.That(loadedCache, Contains.Key("https://example.com/post"));
        Assert.That(loadedCache["https://example.com/post"], Is.EqualTo("<p>cached</p>"));
    }
}
