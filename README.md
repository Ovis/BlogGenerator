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

`--output`で指定したディレクトリは、成果物生成を開始する直前に削除して空の状態で再作成します。前回のビルドで生成された記事、タグ、ページネーションなどが今回の生成対象から外れた場合も古いファイルは残りません。出力ディレクトリには手動で管理するファイルを置かないでください。また、入力と出力には同一ディレクトリや親子関係にあるディレクトリを指定できません。

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

## ライセンス

MIT License
