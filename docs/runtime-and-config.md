# Runtime And Config

## 必要環境

- .NET 8

## 実際の CLI オプション

実装上のオプションは次の通りです。

- `-i`, `--input`, `/input`
  - 入力ディレクトリ
- `-o`, `--output`, `/output`
  - 出力ディレクトリ
- `--theme`, `/theme`
  - テーマディレクトリ
- `--oembed`, `/oembed`
  - oEmbed キャッシュファイル
- `-c`, `--config`, `/config`
  - 設定ファイル

注意:

- README では `-t` が紹介されているが、実装には存在しない

## 実行例

```powershell
dotnet src/bin/Debug/net8.0/BlogGenerator.dll `
  -i C:\path\to\input `
  -o C:\path\to\output `
  --theme F:\_Git\Blog\BlogGenerator\src\TemplateSample `
  -c C:\path\to\config.json `
  --oembed C:\path\to\oembed-cache.json
```

`dotnet tool` として使う前提もあり、`BlogGenerator.csproj` には次が設定されています。

- `PackAsTool=true`
- `ToolCommandName=bloggen`
- `PackageId=eSheepDev.BlogGenerator`

## 設定読込の優先順位

低い順に次が読まれます。

1. `%USERPROFILE%\.bloggen\config.json`
2. カレントディレクトリの `appsettings.json`
3. カレントディレクトリの `appsettings.Development.json`
4. `--config` で指定した JSON
5. `BLOGGEN_` プレフィックスの環境変数

補足:

- `SiteUrl` は必須
- 実装では、設定バインド後に一部の環境変数を手動で再適用している

## 設定モデル

### SiteOption

- `SiteName`
- `SiteDescription`
- `SiteUrl`
- `SiteAuthor`
- `SiteAuthorDescription`
- `AmazonAssociateTag`

`BaseAbsolutePath` は `SiteUrl` から計算される派生値です。
例: `https://example.com/blog/` の場合は `/blog/`

### FeedOption

- `UseRss2`
- `RssFileName`
- `UseAtom`
- `AtomFileName`
- `MaxFeedItems`
- `Language`

## Frontmatter

実装で受ける項目は次の通りです。

```yaml
---
Title: Title text
Published: 2026-08-11 12:00:00
Tags:
  - tag1
  - tag2
Eyecatch: /img/sample.png
IsFixedPage: false
---
```

注意:

- `Eyecatch` は現状どこからも参照されていない
- `Published` 未設定時は `DateTimeOffset.MinValue`
- `IsFixedPage=true` のとき、記事ページでメタ情報の一部表示が省略される

## フィード生成

- RSS と Atom は同じ記事集合から生成
- 対象件数は `MaxFeedItems`
- リンクは `SiteUrl` と記事相対パスから構築
- コンテンツには `ExcerptHtml` が使われる

## oEmbed の動作

- 初回セットアップ時に `https://oembed.com/providers.json` を取得する
- 対応プロバイダが見つからないときは OGP や通常リンクへフォールバックする
- `--oembed` 指定時のみキャッシュファイルをロード/保存する
- Gist は専用処理で `<script src="...js">` を生成する

## 現時点のビルド確認

2026-08-11 時点で次を確認済みです。

```powershell
dotnet build BlogGenerator.sln -v minimal
```

結果:

- ビルド成功
- `AngleSharp 1.3.0` に対する `NU1902` 警告あり
