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

- .NET 8.0以上

## 使い方

### コマンドライン引数

基本的な使用方法：

```bash
dotnet BlogGenerator.dll -i /path/to/input -o /path/to/output -t /path/to/theme
```

dotnet toolを使用してインストールすることもできます：  
```bash
dotnet tool install -g eSheepDev.BlogGenerator
```

dotnet toolを使用して実行する場合：

```bash
bloggen -i /path/to/input -o /path/to/output -t /path/to/theme
```

必須オプション：

- `-i, --input, /input` - Markdownファイルを含む入力フォルダーを指定します
- `-o, --output, /output` - HTMLファイルを出力するフォルダーを指定します
- `-t, --theme, /theme` - テーマフォルダーを指定します

オプション引数：

- `-c, --config, /config` - 設定ファイルのパスを指定します
- `--oembed, /oembed` - oEmbedキャッシュファイルのパスを指定します

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

`IsFiexedPage` は固定ページの場合に `true` に設定します。通常時は省略可能です。


### ページ分割
ページ分割は `<!-- more -->` で行います。これにより、インデックスページやアーカイブページでの表示を制御できます。


### oEmbedによるリッチコンテンツ埋め込み
oEmbedに対応したサイトのコンテンツを埋め込むことができます：

```
[oembed:"https://www.example.com/page1"]
```

`--oembed` のオプションでキャッシュファイルを指定することで、oEmbedのキャッシュを保存できます。これにより、次回以降同じURLをキャッシュのデータで処理して生成時間を短縮できます。  
キャッシュファイルはJSON形式で保存されます。

### Amazonリンクの拡張

Amazonの商品リンクを自動的にアソシエイトタグ付きの形式に変換します。

```
[amazon:XXXXXXXXXX]
```


## ライセンス

MIT License