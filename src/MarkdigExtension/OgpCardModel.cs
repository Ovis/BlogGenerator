namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// OGPカードテンプレートへ渡す表示モデル
/// </summary>
/// <param name="Url">カード全体のリンク先として使用する安全なHTTP(S) URL</param>
/// <param name="DisplayUrl">カード上に表示するURL</param>
/// <param name="Title">ページタイトル</param>
/// <param name="Description">ページ概要</param>
/// <param name="SiteName">サイト名</param>
/// <param name="ImageUrl">OGP画像の安全なHTTP(S) URL。画像がない場合はnull</param>
/// <param name="FaviconUrl">サイトアイコンの取得URL</param>
public sealed record OgpCardModel(
    string Url,
    string DisplayUrl,
    string Title,
    string Description,
    string SiteName,
    string? ImageUrl,
    string FaviconUrl);
