# Project Overview

## 何をするプログラムか

`BlogGenerator` は、入力ディレクトリ配下の Markdown ファイル群を静的ブログサイトへ変換する .NET 8 の CLI ツールです。

生成対象は次の通りです。

- 記事 HTML
- トップ一覧ページ
- タグ一覧ページ
- タグ別一覧ページ
- 月別アーカイブページ
- RSS 2.0 フィード
- Atom フィード
- テーマ配下の静的ファイル
- 入力ディレクトリ配下の非 Markdown ファイル

## 実行時の大まかな処理フロー

1. コマンドライン引数を解釈する
2. 設定を読み込む
3. DI コンテナを組み立てる
4. 出力先ディレクトリを作成する
5. テーマ配下の `.cshtml` 以外を出力先へコピーする
6. 入力配下の `.md` を再帰的に読み込んで Article モデルへ変換する
7. サイドバー HTML を生成する
8. 記事ページ、一覧ページ、タグページ、アーカイブページを生成する
9. RSS/Atom フィードを生成する
10. 指定時のみ oEmbed キャッシュを保存する

この流れは `src/Program.cs` に集約されています。

## データの基本単位

Markdown 1 ファイルは最終的に `Article` 1 件になります。

`Article` には次の主要情報が載ります。

- 出力ファイル名
- タイトル
- 本文 HTML
- タグ
- 公開日時
- 入力相対ディレクトリ
- ルート相対 URL
- 固定ページかどうか

`<!-- more -->` が本文中にある場合、抜粋と残り本文に分割されます。
一覧ページやフィードでは抜粋側が使われます。

## ルーティングと出力構造

入力例:

```text
input/
  about.md
  posts/2026/hello.md
  posts/2026/img/sample.png
```

出力例:

```text
output/
  about.html
  posts/2026/hello.html
  posts/2026/img/sample.png
  index.html
  2.html
  tags/index.html
  tags/<tag>/index.html
  2026/08/index.html
  feed.rss
  feed.atom
```

## Markdown 拡張

標準 Markdown に加えて次を処理します。

- YAML Frontmatter
- `[amazon:ASIN]`
  - Amazon アソシエイト用 iframe に展開
- `[oembed:"https://..."]`
  - oEmbed Provider、oEmbed discovery、OGP の順で埋め込み HTML を生成

## テーマの責務

テーマは Razor テンプレートと静的資産のセットです。

- `.cshtml`:
  - RazorLight でコンパイル・描画される
- それ以外:
  - そのまま出力先へコピーされる

サンプルテーマは `src/TemplateSample` にあります。

## 現在の非機能的特徴

- 並列で Markdown を読む
- oEmbed はネットワークアクセスを使う
- oEmbed キャッシュは任意指定
- HTML エンコードは RazorLight 側で無効化している
- テストコードは現時点で存在しない
