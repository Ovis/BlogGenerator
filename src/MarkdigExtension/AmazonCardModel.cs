namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// Amazonカードテンプレートへ渡す表示モデル
/// </summary>
public sealed record AmazonCardModel(string Asin, string ProductUrl, string Title, string? ImageUrl);
