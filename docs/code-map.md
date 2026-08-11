# Code Map

## 入口

- `src/Program.cs`
  - CLI ハンドラ
  - 設定読込
  - DI 構築
  - 全体処理フローの制御

- `src/Core/CommandLineSetup.cs`
  - コマンドラインオプション定義

## コア処理

- `src/Core/MarkdownProcessor.cs`
  - Markdown 再帰読込
  - Frontmatter 解析
  - Markdig パイプライン構築
  - 画像 URL の書換え
  - `Article` への変換

- `src/Core/PageGenerator.cs`
  - サイドバー生成
  - 記事ページ生成
  - トップ一覧生成
  - タグ一覧/タグ別一覧生成
  - 月別アーカイブ生成

- `src/Core/RssFeedGenerator.cs`
  - RSS 2.0 / Atom 生成

- `src/Core/ThemeProcessor.cs`
  - テーマ静的ファイルのコピー

- `src/Core/FileSystemHelper.cs`
  - ディレクトリ作成
  - 出力パス結合
  - 入力配下の非 Markdown ファイルコピー

## モデル

- `src/Models/Article.cs`
  - 記事データ
  - `ExcerptHtml`
  - `RemainingHtml`
  - `Description`
  - `RootRelativePath`

- `src/Models/FrontMatter.cs`
  - Frontmatter 入力モデル

- `src/Models/SiteOption.cs`
  - サイト設定

- `src/Models/FeedOption.cs`
  - フィード設定

- `src/Models/PageModel.cs`
  - レイアウト描画用モデル
  - ページネーション情報を含む

- `src/Models/PageModelBase.cs`
  - 共通ベース
  - `GeneratePath` でパス整形

- `src/Models/SideBarModel.cs`
  - サイドバー描画用モデル

## Markdig 拡張

- `src/MarkdigExtension/AmazonAssociateExtension.cs`
  - `[amazon:ASIN]` の処理

- `src/MarkdigExtension/OEmbedExtension.cs`
  - `[oembed:"URL"]` の処理
  - oEmbed provider 読込
  - discovery
  - OGP フォールバック
  - キャッシュ保存/読込

- `src/Converters/AutoNumberToStringConverter.cs`
  - oEmbed JSON の数値/文字列差異吸収

## テーマ

- `src/TemplateSample/Layout.cshtml`
  - 共通レイアウト
  - Head、ナビ、メイン、サイドバー、フッター

- `src/TemplateSample/Content.cshtml`
  - 単一記事ページ

- `src/TemplateSample/PageList.cshtml`
  - 一覧ページ

- `src/TemplateSample/Tag.cshtml`
  - タグ一覧ページ

- `src/TemplateSample/SideBar.cshtml`
  - サイドバー

- `src/TemplateSample/_pagination.cshtml`
  - ページネーション UI

- `src/TemplateSample/css/blog.css`
  - サンプルテーマ用 CSS

## 依存パッケージ

主要なパッケージは次の通りです。

- `Markdig`
- `RazorLight`
- `YamlDotNet`
- `AngleSharp`
- `System.CommandLine`
- `System.ServiceModel.Syndication`
- `ReadJEnc`

## 改修で最初に読むべき順番

1. `src/Program.cs`
2. `src/Core/MarkdownProcessor.cs`
3. `src/Core/PageGenerator.cs`
4. `src/TemplateSample/Layout.cshtml`
5. 対象機能に応じて `src/MarkdigExtension/*` または `src/Models/*`
