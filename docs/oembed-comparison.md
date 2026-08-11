# oEmbed Comparison

この文書は、`BlogGenerator` の oEmbed 系処理を改善するために、類似プロジェクトの実装を比較調査した結果をまとめたものです。
`a-terra-forge` と `MarkTheRipper` を対象に、`BlogGenerator` へ取り込み可能な設計・実装パターンを整理します。

## スコープ

- 確認日: 2026-08-11
- 対象:
  - `F:\_Git\Blog\kekyo\a-terra-forge`
  - `F:\_Git\Blog\kekyo\MarkTheRipper`
  - `F:\_Git\Blog\BlogGenerator`
- 主眼:
  - oEmbed の取得方式
  - キャッシュ
  - フォールバック戦略
  - テンプレート連携
  - テストの厚さ

## 要約

- `a-terra-forge`
  - oEmbed 本体は自前実装せず、`mark-deco` に委譲している
  - 代わりに、キャッシュ、renderer 組み立て、設定注入、テストを周辺設計として丁寧に整えている
- `MarkTheRipper`
  - oEmbed 処理を自前で実装しており、段階的フォールバックが明確
  - 短縮 URL 展開、Amazon 特別処理、provider 一覧、discovery、OGP、最低限カードまでを順序立てて処理している
- `BlogGenerator`
  - 現在は `OEmbedExtension.cs` に処理が集中しており、ネットワーク、provider 解決、discovery、OGP、HTML 生成、キャッシュ保存が一体化している

結論として、`BlogGenerator` に取り込むべきなのは次の組み合わせです。

- `a-terra-forge` 由来:
  - キャッシュ層の分離
  - 設定可能な cache directory
  - テストしやすい構造
- `MarkTheRipper` 由来:
  - 段階的フォールバックの明文化
  - 短縮 URL 展開
  - provider / discovery / OGP / 最低限カードの順序制御
  - provider 固有テンプレート差し替えの考え方

## BlogGenerator の現状

`BlogGenerator` の oEmbed 処理は `src/MarkdigExtension/OEmbedExtension.cs` に集中しています。

- provider 一覧取得
- provider マッチング
- oEmbed API 呼び出し
- discovery
- OGP 取得
- 標準リンク生成
- キャッシュ読込/保存

この構造では、責務が集中しすぎており、次の課題があります。

- テストしにくい
- キャッシュ戦略を差し替えにくい
- HTTP 依存を局所化できていない
- provider ごとの描画差分を扱いにくい
- フォールバック順序の仕様がコード上は読めても、設計単位として分かれていない

## a-terra-forge の oEmbed 実装

## 方針

`a-terra-forge` は oEmbed を自前で実装せず、`mark-deco` の Markdown processor plugin に委譲しています。

根拠:

- [src/renderPipeline.ts](/F:/_Git/Blog/kekyo/a-terra-forge/src/renderPipeline.ts:84)
- [src/renderPipeline.ts](/F:/_Git/Blog/kekyo/a-terra-forge/src/renderPipeline.ts:93)
- [src/renderPipeline.ts](/F:/_Git/Blog/kekyo/a-terra-forge/src/renderPipeline.ts:107)
- [README_ja.md](/F:/_Git/Blog/kekyo/a-terra-forge/README_ja.md:923)

## 具体的な実装ポイント

### 1. キャッシュ付き fetcher を使う

`createCachedFetcher` を使い、ファイルシステムキャッシュを前提としています。

- キャッシュストレージ: `createFileSystemCacheStorage`
- TTL: `864000000` ミリ秒
  - 10日

根拠:

- [src/renderPipeline.ts](/F:/_Git/Blog/kekyo/a-terra-forge/src/renderPipeline.ts:84)
- [src/renderPipeline.ts](/F:/_Git/Blog/kekyo/a-terra-forge/src/renderPipeline.ts:89)

これは `BlogGenerator` に対して次の示唆があります。

- 現在の「JSON 一括保存型キャッシュ」だけでなく、取得単位キャッシュへ分離できる
- provider list、oEmbed response、OGP HTML 取得を同じ fetch 層で扱える
- キャッシュ無効期間を設計に組み込める

### 2. provider fallback を plugin 側の責務として分離

`createCardPlugin` に対して `createCardOEmbedFallback(defaultProviderList)` を渡しています。

根拠:

- [src/renderPipeline.ts](/F:/_Git/Blog/kekyo/a-terra-forge/src/renderPipeline.ts:93)
- [src/renderPipeline.ts](/F:/_Git/Blog/kekyo/a-terra-forge/src/renderPipeline.ts:94)

`BlogGenerator` への示唆:

- provider 解決ロジックを `MarkdownExtension` から独立させられる
- embed 本体と fallback 戦略を別オブジェクトに分けられる

### 3. 設定として `cacheDir` を持つ

README に `cacheDir` が明示されています。

根拠:

- [README_ja.md](/F:/_Git/Blog/kekyo/a-terra-forge/README_ja.md:986)

`BlogGenerator` への示唆:

- 現在の `--oembed` を「単一 JSON ファイル」ではなく「キャッシュディレクトリ」へ見直す余地がある
- CLI と設定ファイルの両方から指定できるようにすると運用しやすい

### 4. テストが実装に密着している

oEmbed fallback を使うケースが generator test に含まれています。

根拠:

- [tests/generator.test.ts](/F:/_Git/Blog/kekyo/a-terra-forge/tests/generator.test.ts:4244)
- [tests/generator.test.ts](/F:/_Git/Blog/kekyo/a-terra-forge/tests/generator.test.ts:4331)

見ておくべき点:

- provider endpoint をモックしている
- HTML 側の最終出力で期待値を見ている
- ネットワーク依存を本番 fetch から切り離している

`BlogGenerator` への示唆:

- oEmbed の単体テストだけでなく、最終 HTML 生成テストも追加した方がよい
- HTTP 層を抽象化すればモックしやすくなる

## MarkTheRipper の oEmbed 実装

## 方針

`MarkTheRipper` は oEmbed を自前で実装しています。
処理順がコード上で非常に明確で、フォールバック段階が設計として読み取れます。

根拠:

- [MarkTheRipper.Engine/Functions/oEmbed.cs](/F:/_Git/Blog/kekyo/MarkTheRipper/MarkTheRipper.Engine/Functions/oEmbed.cs:28)

## 処理フロー

`ProcessPermaLinkAsync` は概ね次の順で動きます。

1. HTTP accessor を取得
2. 短縮 URL を展開
3. Amazon 特別処理を試す
4. provider 一覧ベースの oEmbed 解決を試す
5. 失敗したら HTML 本体を取得
6. discovery の `application/json+oembed` を試す
7. それも失敗したら OGP / title / favicon からカードメタデータを作る
8. 最後に最低限のカードレイアウトで出す

根拠:

- [MarkTheRipper.Engine/Functions/oEmbed.cs](/F:/_Git/Blog/kekyo/MarkTheRipper/MarkTheRipper.Engine/Functions/oEmbed.cs:52)
- [MarkTheRipper.Engine/Functions/oEmbed.cs](/F:/_Git/Blog/kekyo/MarkTheRipper/MarkTheRipper.Engine/Functions/oEmbed.cs:59)
- [MarkTheRipper.Engine/Functions/oEmbed.cs](/F:/_Git/Blog/kekyo/MarkTheRipper/MarkTheRipper.Engine/Functions/oEmbed.cs:71)
- [MarkTheRipper.Engine/Functions/oEmbed.cs](/F:/_Git/Blog/kekyo/MarkTheRipper/MarkTheRipper.Engine/Functions/oEmbed.cs:99)
- [MarkTheRipper.Engine/Functions/oEmbed.cs](/F:/_Git/Blog/kekyo/MarkTheRipper/MarkTheRipper.Engine/Functions/oEmbed.cs:111)
- [MarkTheRipper.Engine/Functions/oEmbed.cs](/F:/_Git/Blog/kekyo/MarkTheRipper/MarkTheRipper.Engine/Functions/oEmbed.cs:117)

この流れは `BlogGenerator` にかなり相性がよく、そのまま設計分割の下敷きにできます。

## 補助実装

### 1. HTML メタデータ抽出が独立している

`CreateHtmlMetadata` が JSON oEmbed と HTML OGP の双方から `HtmlMetadata` を組み立てます。

根拠:

- [MarkTheRipper.Engine/Functions/Internal/oEmbedUtilities.cs](/F:/_Git/Blog/kekyo/MarkTheRipper/MarkTheRipper.Engine/Functions/Internal/oEmbedUtilities.cs:52)
- [MarkTheRipper.Engine/Functions/Internal/oEmbedUtilities.cs](/F:/_Git/Blog/kekyo/MarkTheRipper/MarkTheRipper.Engine/Functions/Internal/oEmbedUtilities.cs:63)

`BlogGenerator` への示唆:

- OGP 抽出結果を専用 DTO にまとめる
- provider 応答と OGP 応答の差を吸収する変換層を作る

### 2. provider 固有テンプレートを選べる

`embed-YouTube` や `card-Amazon` のような名前でレイアウト差し替えができます。

根拠:

- [MarkTheRipper.Engine/Functions/Internal/oEmbedUtilities.cs](/F:/_Git/Blog/kekyo/MarkTheRipper/MarkTheRipper.Engine/Functions/Internal/oEmbedUtilities.cs:18)

`BlogGenerator` への示唆:

- すべての埋め込みを同じ HTML 断片で包むだけではなく、provider 別の描画 hook を持てる
- テーマ側で見た目の自由度を上げられる

### 3. iframe をレスポンシブ化している

`ConvertToResponsiveBlockAsync` で iframe の width/height を取り除き、比率維持コンテナに入れています。

根拠:

- [MarkTheRipper.Engine/Functions/Internal/oEmbedUtilities.cs](/F:/_Git/Blog/kekyo/MarkTheRipper/MarkTheRipper.Engine/Functions/Internal/oEmbedUtilities.cs:153)

`BlogGenerator` への示唆:

- 現状の `class='oembed-video'` だけではなく、中身の iframe も整形した方が安定する
- 埋め込み提供側 HTML をそのまま置くより、見た目崩れに強い

## MarkTheRipper のテスト

oEmbed テストはかなり広いです。

カバーされている主なケース:

- provider 一覧ベースで成功する
- provider が HTML を返さない
- HTML の title / OGP だけでカード化する
- discovery で成功する
- discovery で HTML 付き oEmbed を返す
- 完全 fallback
- Amazon 本体 URL
- Amazon カード
- Amazon 短縮 URL
- 一般短縮 URL

根拠:

- [MarkTheRipper.Engine.Tests/oEmbedTests.cs](/F:/_Git/Blog/kekyo/MarkTheRipper/MarkTheRipper.Engine.Tests/oEmbedTests.cs:109)
- [MarkTheRipper.Engine.Tests/oEmbedTests.cs](/F:/_Git/Blog/kekyo/MarkTheRipper/MarkTheRipper.Engine.Tests/oEmbedTests.cs:568)
- [MarkTheRipper.Engine.Tests/oEmbedTests.cs](/F:/_Git/Blog/kekyo/MarkTheRipper/MarkTheRipper.Engine.Tests/oEmbedTests.cs:612)
- [MarkTheRipper.Engine.Tests/oEmbedTests.cs](/F:/_Git/Blog/kekyo/MarkTheRipper/MarkTheRipper.Engine.Tests/oEmbedTests.cs:738)
- [MarkTheRipper.Engine.Tests/oEmbedTests.cs](/F:/_Git/Blog/kekyo/MarkTheRipper/MarkTheRipper.Engine.Tests/oEmbedTests.cs:952)

`BlogGenerator` に足りないのは、まさにこの粒度のテストです。

## BlogGenerator への取り込み候補

## 優先度 High

### 1. oEmbed 処理を責務分離する

現在の `OEmbedExtension.cs` を少なくとも次に分けるべきです。

- HTTP / fetch 層
- provider 一覧解決層
- discovery 層
- OGP 抽出層
- HTML 生成層
- キャッシュ層

これは `a-terra-forge` の「周辺設計の分離」と、`MarkTheRipper` の「段階的処理分離」の両方を取り込む形です。

### 2. フォールバック順序を明文化する

推奨順序:

1. キャッシュ
2. 短縮 URL 展開
3. provider 一覧ベースの oEmbed
4. discovery
5. OGP / title / favicon
6. 最低限の通常リンク

これは `MarkTheRipper` 方式をそのまま設計に落としたものです。

### 3. キャッシュ方式を見直す

現状:

- `--oembed` で JSON ファイル全体を保存・読込

見直し候補:

- `cacheDir` 指定方式
- URL 単位キャッシュ
- TTL 付き
- provider list も同じキャッシュ層に載せる

これは `a-terra-forge` 方式が参考になります。

## 優先度 Medium

### 4. provider 固有テンプレート hook を持つ

例えば次のような差し替え点です。

- `Embed.YouTube`
- `Embed.Amazon`
- `Card.Default`
- `Card.ProviderName`

BlogGenerator は現在、HTML を extension 側でかなり決め打ちしているため、テーマ側へ逃がせる余地があります。

### 5. iframe のレスポンシブ化を自動化する

`MarkTheRipper` の `ConvertToResponsiveBlockAsync` に近い考え方を取り込むと、YouTube 等の埋め込みの崩れに強くなります。

### 6. Amazon 特別処理をどうするか明示する

`BlogGenerator` は `[amazon:ASIN]` を独立 shortcode として持っています。
一方で `MarkTheRipper` は通常 URL や短縮 URL から Amazon を判別して oEmbed/card 系の処理へ流しています。

方針はどちらかに寄せる必要があります。

- 現状維持:
  - Amazon は専用 shortcode のみ
- 拡張:
  - Amazon URL もカード化対象に含める

## 優先度 Medium

### 7. テストを追加する

最低限必要なテストケース:

- provider 成功
- provider 応答に `html` が無い
- discovery 成功
- OGP フォールバック
- 取得失敗時に通常リンクへ落ちる
- 短縮 URL 展開
- キャッシュヒット
- 動画 iframe のレスポンシブ整形

## BlogGenerator に対する具体的な提案

段階的に進めるなら次が妥当です。

### Step 1

設計分離だけ先に行う。

- `IOEmbedFetcher`
- `IOEmbedProviderResolver`
- `IOEmbedDiscoveryResolver`
- `IOgpMetadataExtractor`
- `IOEmbedCache`

### Step 2

現状ロジックをその新構造へ移植する。

この時点では仕様変更を最小にする。

### Step 3

`MarkTheRipper` 方式のフォールバック順序へ整理する。

### Step 4

`cacheDir` と TTL を導入する。

### Step 5

テスト追加後に、必要なら provider 別テンプレートや Amazon URL 対応を広げる。

## 今回の調査から見た判断

- すぐ真似しやすいのは `MarkTheRipper` の処理段階設計
- 中長期で価値が高いのは `a-terra-forge` のキャッシュ・テスト・委譲設計
- `BlogGenerator` は現状、自前実装を完全に捨てるよりも、段階的分離の方が安全

つまり、現実的な移行方針は次です。

1. `MarkTheRipper` 的に責務分離して読みやすくする
2. `a-terra-forge` 的にキャッシュ層とテストを整備する
3. 必要なら将来的に外部ライブラリ委譲を検討する

## 関連資料

- [docs/project-overview.md](/F:/_Git/Blog/BlogGenerator/docs/project-overview.md)
- [docs/code-map.md](/F:/_Git/Blog/BlogGenerator/docs/code-map.md)
- [docs/issue-list.md](/F:/_Git/Blog/BlogGenerator/docs/issue-list.md)
