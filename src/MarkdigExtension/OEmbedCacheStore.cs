using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace BlogGenerator.MarkdigExtension;

public static class OEmbedCacheStore
{
    public static readonly TimeSpan DefaultSuccessTtl = TimeSpan.FromDays(180);

    /// <summary>
    /// キャッシュをJSONファイルに保存する
    /// </summary>
    public static async Task SaveAsync(string filePath, ConcurrentDictionary<string, OEmbedCacheEntry> cache)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var cacheFile = new OEmbedCacheFile
            {
                Entries = cache.ToDictionary(pair => pair.Key, pair => pair.Value)
            };
            var json = JsonSerializer.Serialize(cacheFile, options);
            await File.WriteAllTextAsync(filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving OEmbed cache: {ex.Message}");
        }
    }

    /// <summary>
    /// JSONファイルからキャッシュを読み込む
    /// </summary>
    public static async Task LoadAsync(
        string filePath,
        ConcurrentDictionary<string, OEmbedCacheEntry> cache,
        Func<DateTimeOffset>? utcNowProvider = null)
    {
        if (!File.Exists(filePath))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var now = (utcNowProvider ?? (() => DateTimeOffset.UtcNow))();
            var loadedCache = DeserializeEntries(json, now);

            if (loadedCache != null)
            {
                foreach (var item in loadedCache)
                {
                    cache.TryAdd(item.Key, item.Value);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading OEmbed cache: {ex.Message}");
        }
    }

    private static Dictionary<string, OEmbedCacheEntry>? DeserializeEntries(string json, DateTimeOffset now)
    {
        var rootNode = JsonNode.Parse(json);
        if (rootNode is JsonObject rootObject && rootObject.ContainsKey("Entries"))
        {
            return JsonSerializer.Deserialize<OEmbedCacheFile>(json)?.Entries;
        }

        var legacyEntries = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        // 旧形式は取得時刻を持たないため、互換性維持を優先して取込時点から成功TTLを与える
        return legacyEntries?.ToDictionary(
            pair => pair.Key,
            pair => OEmbedCacheEntry.CreateLegacySuccess(pair.Value, now, DefaultSuccessTtl));
    }
}
