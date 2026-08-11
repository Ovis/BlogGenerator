using System.Collections.Concurrent;
using System.Text.Json;

namespace BlogGenerator.MarkdigExtension;

public static class OEmbedCacheStore
{
    /// <summary>
    /// キャッシュをJSONファイルに保存する
    /// </summary>
    public static async Task SaveAsync(string filePath, ConcurrentDictionary<string, string> cache)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(cache, options);
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
    public static async Task LoadAsync(string filePath, ConcurrentDictionary<string, string> cache)
    {
        if (!File.Exists(filePath))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var loadedCache = JsonSerializer.Deserialize<ConcurrentDictionary<string, string>>(json);

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
}
