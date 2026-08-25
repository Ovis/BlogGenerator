# BlogGenerator

マークダウン形式ファイルをHTMLに変換し、ブログとして公開するための静的サイトジェネレーター

## 概要

BlogGeneratorは、Markdownファイルをもとに静的なブログサイトを生成するツールです。  
設定したテーマに基づいて、記事ページ、インデックスページ、タグページ、アーカイブページを生成します。  
また、RSS/Atomフィードの生成もサポートしています。

## 特徴

- Markdownからの静的サイト生成
- Razor構文によるカスタマイズが可能なテーマ
- タグ、アーカイブページの自動生成
- RSS/Atomフィード対応
- oEmbedを使用したリッチコンテンツの埋め込み
- Amazonアソシエイトタグのサポート

## 必要条件

- .NET 10.0以上

## 使い方

### コマンドライン引数

基本的な使用方法：

```bash
dotnet BlogGenerator.dll --input /path/to/input --output /path/to/output --theme /path/to/theme
```

dotnet toolを使用してインストールすることもできます：  
```bash
dotnet tool install -g eSheepDev.BlogGenerator
```

dotnet toolを使用して実行する場合：

```bash
bloggen --input /path/to/input --output /path/to/output --theme /path/to/theme
```

### 生成とプレビュー

次の兄弟ディレクトリ構成になっている場合、同梱のPowerShellスクリプトでビルド、サイト生成、ローカルプレビューをまとめて実行できます。

```text
Blog/
├─ BlogGenerator/
├─ theme/
├─ article/
└─ output/
```

```powershell
.\build-preview.ps1 `
  -ThemeRoot ..\theme `
  -ArticleRoot ..\article
```

生成に成功すると `output` に成果物を作成し、`http://127.0.0.1:8765/` でPythonのWebサーバーを起動します。終了するときは `Ctrl+C` を押します。

`ThemeRoot`には`templates`ディレクトリと`blogconfig.json`を持つテーマルート、`ArticleRoot`にはMarkdown記事と静的ファイルを持つ入力ルートを指定します。相対パスと絶対パスのどちらも使用できます。

ポートを変更する場合：

```powershell
.\build-preview.ps1 `
  -ThemeRoot ..\theme `
  -ArticleRoot ..\article `
  -Port 8080
```

生成だけ行い、Webサーバーを起動しない場合：

```powershell
.\build-preview.ps1 `
  -ThemeRoot ..\theme `
  -ArticleRoot ..\article `
  -NoServer
```

スクリプトは新しい成果物を別ディレクトリへ生成・検証してから `output` と差し替えます。既存の `output` は `output.previous-日時-ID` という名前で残るため、不要になったことを確認してから削除してください。また、記事リポジトリの `.git` などの管理用ディレクトリは一時入力から除外され、成果物にはコピーされません。

必須オプション：

- `-i, --input, /input` - Markdownファイルを含む入力フォルダーを指定します
- `-o, --output, /output` - HTMLファイルを出力するフォルダーを指定します
- `--theme, /theme` - テーマフォルダーを指定します

オプション引数：

- `-c, --config, /config` - 設定ファイルのパスを指定します
- `--oembed, /oembed` - oEmbedキャッシュファイルのパスを指定します
- `--amazon-cache` - Amazon 商品メタデータキャッシュファイルのパスを指定します

### 設定ファイル

設定は以下の優先順位で適用されます（上に行くほど優先度が高い）：  

1. 環境変数（`BLOGGEN_` プレフィックス）  
2. コマンドラインオプションで指定された設定ファイル  
3. カレントディレクトリの `appsettings.json`  
4. ユーザーホームフォルダの `~/.bloggen/config.json`  

設定ファイルの例（JSON形式）：
```json
{
    "SiteOption": {
        "SiteName": "サイト名",
        "SiteDescription": "サイトの説明",
        "SiteUrl": "https://example.com/",
        "SiteAuthor": "サイト運営者名",
        "SiteAuthorDescription": "サイト運営者の説明",
        "AmazonAssociateTag": "amazon-tag"
    },
    "FeedOption": {
        "MaxFeedItems": 10,
        "UseRss2": true,
        "UseAtom": true,
        "RssFileName": "feed.rss",
        "AtomFileName": "feed.atom",
        "Language": "ja-JP"
    }
}
```

### 環境変数

環境変数でも設定可能です（`BLOGGEN_` プレフィックスが必要）：

#### サイト設定
- `BLOGGEN_SITENAME` - サイト名
- `BLOGGEN_SITEURL` - サイトURL（必須）
- `BLOGGEN_SITEDESCRIPTION` - サイトの説明
- `BLOGGEN_SITEAUTHOR` - 著者名
- `BLOGGEN_SITEAUTHORDESCRIPTION` - 著者の説明
- `BLOGGEN_AMAZONTAG` - Amazonアソシエイトタグ

#### フィード設定
- `BLOGGEN_FEED_USERSS2` - RSS2.0フィードを生成するかどうか（true/false デフォルト: true）
- `BLOGGEN_FEED_USEATOM` - Atomフィードを生成するかどうか（true/false デフォルト: true）
- `BLOGGEN_FEED_RSSFILENAME` - RSSフィードのファイル名（デフォルト: feed.rss）
- `BLOGGEN_FEED_ATOMFILENAME` - Atomフィードのファイル名（デフォルト: feed.atom）
- `BLOGGEN_FEED_MAXITEMS` - フィードに含める記事の最大数（デフォルト: 10）
- `BLOGGEN_FEED_LANGUAGE` - フィードの言語（デフォルト: ja-JP）

### Frontmatter

各マークダウンファイルの先頭にYAML形式のFrontmatterを記述できます：

```markdown
---
Title: 記事のタイトル
Tags:
  - "tag1"
  - "tag2"
Published: 2025-01-01 20:00:00
IsFixedPage: false

---
ここから記事の本文...
```

`IsFixedPage` は固定ページの場合に `true` に設定します。通常時は省略可能です。


### ページ分割
ページ分割は
```html
<!-- more -->
```

で行います。これにより、インデックスページやアーカイブページでの表示を制御できます。


### oEmbedによるリッチコンテンツ埋め込み
oEmbedに対応したサイトのコンテンツを埋め込むことができます：

```
[oembed:"https://www.example.com/page1"]
```

`--oembed` のオプションでキャッシュファイルを指定することで、oEmbedのキャッシュを保存できます。これにより、次回以降同じURLをキャッシュのデータで処理して生成時間を短縮できます。  
キャッシュファイルはJSON形式で保存されます。

### Amazon商品カード

Amazon アソシエイトタグを設定している場合、Amazon の商品カードを出力できます。  

```markdown
[amazon:XXXXXXXXXX]
```

商品ページから商品名と画像を取得します。取得に失敗した場合は、同じ商品 URL を通常の oEmbed / リンクカード処理へ渡します。アソシエイトタグ未設定時もカードを出力し、リンク先には `tag` パラメータを付けません。

商品名・画像 ID を手動指定する記法もあります。手動指定は自動取得より優先します。`image` は画像 URL ではなく、Amazon 画像 ID のみを受け付けます。

```markdown
[amazon:4844339648,title="商品名",image="91+IeF0u9eL"]
```

`--amazon-cache` を指定すると、商品名と画像 URL を専用 JSON キャッシュへ保存します。成功結果は 365 日間保持し、期限後の更新が失敗しても最後に成功した情報を使い続けます。

表示はテーマの `.embeds/amazon.cshtml` で上書きできます。未配置時はアプリ同梱の既定テンプレートを使用します。


## ライセンス

MIT License
