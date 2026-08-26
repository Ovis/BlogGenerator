namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// Amazon商品ページから抽出したカード用メタデータ
/// </summary>
public sealed record AmazonProductMetadata(string? Title, string? ImageUrl);
