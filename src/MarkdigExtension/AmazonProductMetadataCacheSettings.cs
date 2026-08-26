namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// Amazon商品メタデータキャッシュの設定
/// </summary>
public sealed class AmazonProductMetadataCacheSettings(string filePath)
{
    public string FilePath { get; } = filePath;
}
