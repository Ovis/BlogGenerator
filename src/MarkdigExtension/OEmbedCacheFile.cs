namespace BlogGenerator.MarkdigExtension;

public sealed class OEmbedCacheFile
{
    public int Version { get; init; } = 2;

    public Dictionary<string, OEmbedCacheEntry> Entries { get; init; } = [];
}
