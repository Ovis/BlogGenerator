using System.Collections.Concurrent;
using System.Text.Json;

namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// Amazon商品メタデータキャッシュをJSONファイルへ保存する
/// </summary>
public static class AmazonProductMetadataCacheStore
{
    public static async Task LoadAsync(string filePath, ConcurrentDictionary<string, AmazonProductMetadataCacheEntry> cache)
    {
        if (!File.Exists(filePath)) return;
        try
        {
            var entries = JsonSerializer.Deserialize<Dictionary<string, AmazonProductMetadataCacheEntry>>(await File.ReadAllTextAsync(filePath));
            if (entries is not null)
                foreach (var entry in entries) cache.TryAdd(entry.Key, entry.Value);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Error loading Amazon metadata cache: {exception.Message}");
        }
    }

    public static async Task SaveAsync(string filePath, ConcurrentDictionary<string, AmazonProductMetadataCacheEntry> cache)
    {
        try
        {
            var directoryPath = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (!string.IsNullOrEmpty(directoryPath)) Directory.CreateDirectory(directoryPath);
            await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Error saving Amazon metadata cache: {exception.Message}");
        }
    }
}
