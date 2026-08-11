# Markdig oEmbed Migration Plan

この文書は、`BlogGenerator` の Markdown / oEmbed 改修について、2026-08-11 時点の判断を単独で参照できるようにまとめた一次資料です。
実装方針、採用理由、最低限の実施順序、設計上の注意点をここに固定します。

## スコープ

- 対象:
  - `src/BlogGenerator.csproj`
  - `src/Core/MarkdownProcessor.cs`
  - `src/MarkdigExtension/OEmbedExtension.cs`
  - `src/MarkdigExtension/AmazonAssociateExtension.cs`
- 関連調査:
  - `docs/oembed-comparison.md`
  - `docs/markdig-oembed-constraints.md`
- 参照した外部情報:
  - Markdig 公式 GitHub
  - Markdig 公式ドキュメント
  - NuGet の Markdig パッケージ情報

## 決定事項

今回の方針は次で確定します。

1. Markdown エンジンは当面 `Markdig` を使い続ける
2. `Markdig` は最新系へ更新する
3. oEmbed は `HtmlInline` 直書きではなく、独自 AST ノード + 独自 renderer で扱う
4. frontmatter 抽出と本文レンダリングは同じ pipeline を使わない
5. oEmbed の HTTP 解決、provider 解決、discovery、OGP、キャッシュは Markdig parser から分離する

## 現在の把握

現行実装は `Markdig 0.41.0` に依存しています。

- 根拠:
  - [src/BlogGenerator.csproj](/F:/_Git/Blog/BlogGenerator/src/BlogGenerator.csproj:23)

2026-08-11 時点で確認した NuGet 上の最新は `Markdig 1.3.2` です。

- 参照:
  - [NuGet: Markdig](https://www.nuget.org/packages/Markdig)
  - [Markdig GitHub](https://github.com/xoofx/markdig)

また、現在の Markdown パイプラインは `MarkdownProcessor` で 1 本だけ構築されており、
その中に `UseYamlFrontMatter()` と `OEmbedCardExtension` の両方が入っています。

- 根拠:
  - [src/Core/MarkdownProcessor.cs](/F:/_Git/Blog/BlogGenerator/src/Core/MarkdownProcessor.cs:14)

このため、frontmatter 抽出でも本文レンダリングでも同じ oEmbed 拡張が評価されます。

## 現行構造の問題

現状の `OEmbedCardExtension` / `OEmbedCardParser` は、次を 1 つの拡張に抱えています。

- provider 一覧取得
- provider マッチング
- oEmbed API 呼び出し
- discovery
- OGP 抽出
- HTML 組み立て
- キャッシュ保存 / 読込

さらに `InlineParser.Match()` の中で `GetOEmbedHtml(url).GetAwaiter().GetResult()` を呼び、
`HtmlInline` を直接差し込んでいます。

- 根拠:
  - [src/MarkdigExtension/OEmbedExtension.cs](/F:/_Git/Blog/BlogGenerator/src/MarkdigExtension/OEmbedExtension.cs:189)
  - [src/MarkdigExtension/OEmbedExtension.cs](/F:/_Git/Blog/BlogGenerator/src/MarkdigExtension/OEmbedExtension.cs:228)
  - [src/MarkdigExtension/OEmbedExtension.cs](/F:/_Git/Blog/BlogGenerator/src/MarkdigExtension/OEmbedExtension.cs:231)

この構造だと次が同時に起きます。

- parser の責務にネットワーク I/O が混ざる
- 同期パース中に外部通信待ちが発生する
- frontmatter 抽出側にも oEmbed 改修の影響が波及する
- HTML 出力が parser に固定され、renderer / theme へ責務を逃がしにくい
- テスト境界を作りにくい

## 採用する設計方針

### 方針 1: Markdig は使い続ける

Markdown 本体の置き換えは今はやりません。
まず解くべき問題は Markdown エンジンの選定ではなく、`Markdig` 拡張の責務過多です。

### 方針 2: parser は記法検出に寄せる

`[oembed:"..."]` を見つけた時に、parser は HTML を作らず、
`OEmbedInline` のような独自ノードを AST に置くだけにします。

つまり parser の責務は次に限定します。

- oEmbed 記法を認識する
- URL を抽出する
- 独自 AST ノードを生成する

### 方針 3: renderer が最終 HTML を出す

HTML への変換は `HtmlObjectRenderer<T>` 側で行います。

これにより、次が可能になります。

- provider ごとの描画分岐
- 動画向けのレスポンシブ整形
- デフォルトカードと通常リンク fallback の整理
- 将来の theme / template 側連携

### 方針 4: 非同期解決は parse と render の外に置く

HTTP を伴う処理は Markdig parser / renderer に直接持ち込まず、
アプリ側 service として分離します。

想定責務:

- `IOEmbedProviderCatalog`
- `IOEmbedResolver`
- `IOEmbedDiscoveryResolver`
- `IOgpMetadataExtractor`
- `IOEmbedCache`
- `IOEmbedHtmlFactory`

## AST + renderer 方式の具体像

この方式での処理イメージは次です。

1. Markdown parser が `[oembed:"url"]` を見つける
2. parser は `OEmbedInline` ノードを作る
3. Markdown 全体を AST 化する
4. AST から `OEmbedInline` を集める
5. URL 群をアプリ側 service が非同期に解決する
6. 解決結果をノードまたは解決テーブルへ載せる
7. renderer が `OEmbedInline` を HTML に変換する

この方式で重要なのは、Markdig の parse phase と render phase を正しく分けることです。

- 参照:
  - [Markdig Pipeline Architecture](https://xoofx.github.io/markdig/docs/advanced/pipeline/)

## frontmatter についての合意事項

frontmatter 抽出でも同じ pipeline を使っている現状は見直します。

この判断は確定です。

やるべきこと:

1. frontmatter 用の軽量 pipeline を別に持つ
2. oEmbed 拡張は本文用 pipeline にだけ載せる

これにより、oEmbed 改修の影響範囲を本文レンダリング側へ閉じ込めやすくなります。

## 今回の最低限の実施範囲

今回の最低ラインは次です。

1. `Markdig` を最新系へ更新する
2. parser から HTTP / provider / OGP / cache を分離する
3. `HtmlInline` 直書きをやめ、独自 AST ノード + 独自 renderer へ移す
4. frontmatter 用 pipeline と本文用 pipeline を分離する

ここまでは実施対象です。

一方で、次は今回の最低限スコープには含めません。

- Markdown エンジン自体の全面置き換え
- provider 別テンプレートの大規模拡張
- Amazon URL 対応の拡張仕様化
- cache directory / TTL 方式への全面移行

## 難易度感

### Markdig 更新

- 難易度: 中
- 主な理由:
  - `0.41.0` から `1.3.2` への API 差分確認が必要
  - 既存の `InlineParser` / renderer 周辺の互換性確認が必要

### AST + renderer 化

- 難易度: 中
- 主な理由:
  - `MarkdownProcessor` の処理順を少し組み替える必要がある
  - parser / resolver / renderer の 3 層へ責務を割る必要がある
  - frontmatter 用 pipeline 分離を同時にやる方が安全

### Markdown エンジン置き換え

- 難易度: 高
- 今回やらない理由:
  - 問題の本質に対して費用対効果が低い
  - 既存拡張との互換性コストが大きい

## この方針のメリット

- oEmbed のネットワーク処理を parser から外せる
- frontmatter 側を軽く保てる
- HTML 組み立てを renderer 側へ寄せられる
- provider 差分や動画整形を後から拡張しやすい
- テストを parser / resolver / renderer で分割できる
- `MarkTheRipper` の段階的 fallback 設計や `a-terra-forge` の責務分離を取り込みやすい

## 実装時の注意点

- renderer 側も同期 API 前提なので、HTTP は renderer 内で直接行わない
- `OEmbedInline` の生成と、oEmbed 解決は別段階に分ける
- static 状態は減らし、できるだけ instance / service 境界へ寄せる
- 既存の `[amazon:ASIN]` 拡張との責務境界は後続で整理する
- provider 一覧取得失敗時の fallback は通常リンクまで含めて明文化する

## 参照順

この文書を起点にする場合の読み順は次です。

1. `docs/markdig-oembed-migration-plan.md`
2. `docs/markdig-oembed-constraints.md`
3. `docs/oembed-comparison.md`
4. `src/Core/MarkdownProcessor.cs`
5. `src/MarkdigExtension/OEmbedExtension.cs`

## 関連資料

- [docs/markdig-oembed-constraints.md](/F:/_Git/Blog/BlogGenerator/docs/markdig-oembed-constraints.md)
- [docs/oembed-comparison.md](/F:/_Git/Blog/BlogGenerator/docs/oembed-comparison.md)
- [docs/issue-list.md](/F:/_Git/Blog/BlogGenerator/docs/issue-list.md)
