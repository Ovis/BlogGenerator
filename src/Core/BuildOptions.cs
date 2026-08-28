namespace BlogGenerator.Core;

/// <summary>
/// 通常ビルドコマンドからビルド処理へ渡す入力値をまとめたオプション
/// </summary>
/// <param name="InputPath">Markdownや静的コンテンツを読み込む入力ディレクトリ</param>
/// <param name="OutputPath">生成したサイトを出力するディレクトリ</param>
/// <param name="ThemePath">Razorテンプレートやテーマファイルを格納したディレクトリ</param>
/// <param name="OEmbedCachePath">oEmbedキャッシュファイルのパス</param>
/// <param name="AmazonCachePath">Amazon商品メタデータキャッシュファイルのパス</param>
/// <param name="ConfigFile">コマンドラインから明示指定された設定ファイル</param>
internal sealed record BuildOptions(
    string InputPath,
    string OutputPath,
    string ThemePath,
    string? OEmbedCachePath,
    string? AmazonCachePath,
    FileInfo? ConfigFile);
