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
/// <param name="Webp">生成した画像をWebPへ最適化するか</param>
/// <param name="WebpParallelism">WebP変換の並列数。未指定ならCPU数に応じて自動設定</param>
internal sealed record BuildOptions(
    string InputPath,
    string OutputPath,
    string ThemePath,
    string? OEmbedCachePath,
    string? AmazonCachePath,
    FileInfo? ConfigFile,
    bool Webp = false,
    int? WebpParallelism = null);
