using System.Collections.Concurrent;
using BlogGenerator.MarkdigExtension;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class AmazonProductMetadataCacheStoreTests
{
    [Test]
    public async Task キャッシュを保存して再読込できる()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "BlogGeneratorTests", Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(rootPath, "cache", "amazon.json");
        try
        {
            var source = new ConcurrentDictionary<string, AmazonProductMetadataCacheEntry>();
            source["B0ABC12345"] = AmazonProductMetadataCacheEntry.CreateSuccess(
                "B0ABC12345", new AmazonProductMetadata("商品名", null), DateTimeOffset.UtcNow, TimeSpan.FromDays(365));

            await AmazonProductMetadataCacheStore.SaveAsync(filePath, source);
            var loaded = new ConcurrentDictionary<string, AmazonProductMetadataCacheEntry>();
            await AmazonProductMetadataCacheStore.LoadAsync(filePath, loaded);

            Assert.That(loaded["B0ABC12345"].Title, Is.EqualTo("商品名"));
        }
        finally
        {
            if (Directory.Exists(rootPath)) Directory.Delete(rootPath, true);
        }
    }
}
