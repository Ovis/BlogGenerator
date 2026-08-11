# Markdig And oEmbed Constraints

この文書は、`BlogGenerator` が Markdown 処理に依存している `Markdig` を前提とした場合に、
oEmbed 系処理へ手を入れる際の技術的制約と注意点を整理したものです。

## スコープ

- 確認日: 2026-08-11
- 対象:
  - `src/BlogGenerator.csproj`
  - `src/Core/MarkdownProcessor.cs`
  - `src/MarkdigExtension/OEmbedExtension.cs`
  - `src/MarkdigExtension/AmazonAssociateExtension.cs`
- 主眼:
  - Markdig 依存が改修に与える制約
  - 現行実装で起きている構造上の問題
  - 改修時に先に整理すべきポイント

## 結論

`BlogGenerator` の oEmbed 改修で本当に厄介なのは、oEmbed の仕様そのものより、
`Markdig` の拡張ポイントにネットワーク I/O を直接持ち込んでいる現状の構造です。

特に問題になるのは次です。

- `InlineParser.Match` が同期 API である
- provider 取得と埋め込み解決を同期ブロックで実行している
- frontmatter 抽出のために Markdig パースを 2 回実行している
- Markdown 処理は並列だが、oEmbed 側は static 状態を広く共有している
- HTML 生成が `HtmlInline` へ直書きされ、テーマやレンダラ差し替えに弱い

## 現在の依存関係

`BlogGenerator` は `Markdig 0.41.0` に依存しています。

根拠:

- [src/BlogGenerator.csproj](/F:/_Git/Blog/BlogGenerator/src/BlogGenerator.csproj:23)

## BlogGenerator における Markdig の使い方

Markdown パイプラインは `MarkdownProcessor` で構築されています。

構成:

- `UseYamlFrontMatter()`
- `AmazonAssociateExtension`
- `OEmbedCardExtension`
- `UseAdvancedExtensions()`

根拠:

- [src/Core/MarkdownProcessor.cs](/F:/_Git/Blog/BlogGenerator/src/Core/MarkdownProcessor.cs:15)
- [src/Core/MarkdownProcessor.cs](/F:/_Git/Blog/BlogGenerator/src/Core/MarkdownProcessor.cs:18)

この設計により、oEmbed は Markdown パースの最中にインラインとして評価されます。

## 制約 1: InlineParser は同期 API

`OEmbedCardParser` は `InlineParser` を継承し、`Match` を実装しています。

根拠:

- [src/MarkdigExtension/OEmbedExtension.cs](/F:/_Git/Blog/BlogGenerator/src/MarkdigExtension/OEmbedExtension.cs:189)
- [src/MarkdigExtension/OEmbedExtension.cs](/F:/_Git/Blog/BlogGenerator/src/MarkdigExtension/OEmbedExtension.cs:211)

この `Match` は同期メソッドです。
したがって、本来非同期であるべき HTTP 取得を素直には書けません。

現状は次のように同期ブロックで逃がしています。

- `GetOEmbedHtml(url).GetAwaiter().GetResult()`

根拠:

- [src/MarkdigExtension/OEmbedExtension.cs](/F:/_Git/Blog/BlogGenerator/src/MarkdigExtension/OEmbedExtension.cs:228)

### この制約が意味すること

- oEmbed 解決を重くすると Markdown パース自体が詰まる
- タイムアウトや HTTP 遅延が、そのままパース遅延になる
- 非同期キャンセル制御を組み込みにくい
- テストで非同期境界を分離しにくい

## 制約 2: pipeline 初期化時にも同期ネットワークを行っている

`OEmbedCardExtension.Setup(MarkdownPipelineBuilder)` 内で、初回だけ provider 一覧を取りに行っています。

根拠:

- [src/MarkdigExtension/OEmbedExtension.cs](/F:/_Git/Blog/BlogGenerator/src/MarkdigExtension/OEmbedExtension.cs:33)
- [src/MarkdigExtension/OEmbedExtension.cs](/F:/_Git/Blog/BlogGenerator/src/MarkdigExtension/OEmbedExtension.cs:48)

ここでも `GetAwaiter().GetResult()` が使われています。

### この制約が意味すること

- Markdig パイプライン構築がネットワーク依存になる
- 起動直後の UX が不安定になる
- provider 一覧取得失敗時の扱いが拡張初期化ロジックに閉じる
- テストの際に「パース前に何を準備すべきか」が不透明になる

## 制約 3: frontmatter 抽出でも同じ pipeline を通している

`MarkdownProcessor.ParseMarkdownWithFrontmatter` では `Markdown.Parse` を 2 回呼んでいます。

1. frontmatter を見つけるために全文パース
2. frontmatter 除去後の本文を再パース

根拠:

- [src/Core/MarkdownProcessor.cs](/F:/_Git/Blog/BlogGenerator/src/Core/MarkdownProcessor.cs:74)
- [src/Core/MarkdownProcessor.cs](/F:/_Git/Blog/BlogGenerator/src/Core/MarkdownProcessor.cs:92)

しかも、どちらも同じ `_markdownPipeline` を使っています。

### この制約が意味すること

- oEmbed タグが frontmatter 抽出フェーズでも評価対象になる
- 本文に `[oembed:"..."]` が多いほど、パース 2 回の負荷が効く
- oEmbed 改修の影響範囲が「本文レンダリングだけ」に閉じない

## 制約 4: Markdown 処理は並列だが、oEmbed 側は static 状態が多い

Markdown ファイル処理は `AsParallel()` で並列実行されています。

根拠:

- [src/Core/MarkdownProcessor.cs](/F:/_Git/Blog/BlogGenerator/src/Core/MarkdownProcessor.cs:33)

一方で `OEmbedExtension` 側には static 状態が多数あります。

- `HttpClient`
- `_oEmbedProvidersJson`
- `OembedProviderDic`
- `OEmbedCardParser`
- `_oEmbedCache`

根拠:

- [src/MarkdigExtension/OEmbedExtension.cs](/F:/_Git/Blog/BlogGenerator/src/MarkdigExtension/OEmbedExtension.cs:21)
- [src/MarkdigExtension/OEmbedExtension.cs](/F:/_Git/Blog/BlogGenerator/src/MarkdigExtension/OEmbedExtension.cs:27)
- [src/MarkdigExtension/OEmbedExtension.cs](/F:/_Git/Blog/BlogGenerator/src/MarkdigExtension/OEmbedExtension.cs:30)
- [src/MarkdigExtension/OEmbedExtension.cs](/F:/_Git/Blog/BlogGenerator/src/MarkdigExtension/OEmbedExtension.cs:194)

### この制約が意味すること

- 並列パース時の副作用を追いにくい
- テスト間で状態が残留しやすい
- キャッシュや provider 情報の初期化順序に依存しやすい
- 将来 DI による差し替えやモックがしにくい

## 制約 5: HTML 出力がパーサ内で直組みされている

oEmbed 成功時も OGP fallback 時も、最終的な HTML は `HtmlInline` または文字列連結で生成しています。

根拠:

- [src/MarkdigExtension/OEmbedExtension.cs](/F:/_Git/Blog/BlogGenerator/src/MarkdigExtension/OEmbedExtension.cs:231)
- [src/MarkdigExtension/OEmbedExtension.cs](/F:/_Git/Blog/BlogGenerator/src/MarkdigExtension/OEmbedExtension.cs:281)
- [src/MarkdigExtension/OEmbedExtension.cs](/F:/_Git/Blog/BlogGenerator/src/MarkdigExtension/OEmbedExtension.cs:494)

### この制約が意味すること

- provider ごとの差し替えが難しい
- テーマ側へ責務を寄せにくい
- レスポンシブ整形やアクセシビリティ対応を共通化しにくい
- HTML 検証と取得ロジックが混ざる

## 制約 6: Amazon 拡張と同じ挿入点を使っている

Amazon 拡張も `InlineParser` を使って `HtmlInline` を返す構造です。

根拠:

- [src/MarkdigExtension/AmazonAssociateExtension.cs](/F:/_Git/Blog/BlogGenerator/src/MarkdigExtension/AmazonAssociateExtension.cs:18)
- [src/MarkdigExtension/AmazonAssociateExtension.cs](/F:/_Git/Blog/BlogGenerator/src/MarkdigExtension/AmazonAssociateExtension.cs:28)

これは「今の拡張の作り方は一貫している」という意味ではよいですが、
oEmbed だけより高度な非同期処理やテンプレート連携を持ち込みたい場合は、同じ手法では限界があります。

## 改修時の具体的な課題

## 課題 1: Markdig 内で完結させようとすると設計が苦しくなる

`InlineParser.Match` で全部やろうとすると、次をすべて同期化する必要があります。

- provider 一覧取得
- キャッシュ確認
- URL 展開
- endpoint 解決
- discovery
- OGP 取得
- HTML 組み立て

結果として、現在のような `GetAwaiter().GetResult()` 連鎖になりやすいです。

## 課題 2: 改修の副作用が frontmatter にも波及する

frontmatter 取得と本文レンダリングが同じ pipeline に乗っているため、
oEmbed を触ると「frontmatter 解析だけしたい場面」まで重くなる可能性があります。

## 課題 3: テスト導入の難易度が上がる

Markdig 拡張内に static 状態と HTTP ロジックがあるため、次が難しくなります。

- URL ごとの応答差し替え
- provider list の差し替え
- キャッシュ初期状態の制御
- テストケースの独立性確保

## 課題 4: 将来の renderer 差し替え余地が小さい

例えば次のような要求が出た時に苦しくなります。

- 動画だけレスポンシブラッパに変換したい
- provider 別にテンプレートを変えたい
- カードの見た目を Razor テーマ側で持ちたい
- サーバー不要のまま遅延ロード風にしたい

## 推奨方針

## 1. Markdig は「記法検出」に寄せる

Markdig 側の責務は次だけに縮めるのが安全です。

- `[oembed:"..."]` を見つける
- URL を抽出する
- 既に解決済みの HTML を差し込む

つまり、Markdig 内では「HTTP 解決そのもの」はやらない方向がよいです。

## 2. oEmbed 解決を前処理または別 service に逃がす

候補:

- 前処理で本文中の oEmbed URL を列挙して先に解決
- `IOEmbedResolver` のような service へ委譲
- parser は resolver の結果を見るだけにする

## 3. frontmatter 抽出経路を分離する

少なくとも次のどちらかが必要です。

- frontmatter 用の軽量 pipeline を別に持つ
- YAML 部分だけ別手段で先に抽出する

## 4. static 状態を減らす

理想的には次の単位で instance 化する方がよいです。

- provider cache
- response cache
- HTTP accessor
- fallback strategy

## 5. テスト可能な境界を作る

最低限、次は interface 化したいです。

- `IOEmbedFetcher`
- `IOEmbedProviderCatalog`
- `IOEmbedCache`
- `IOgpMetadataExtractor`
- `IOEmbedHtmlRenderer`

## 改修順序の提案

### Step 1

設計分離だけ先に行う。

- `OEmbedCardExtension` から HTTP / provider / cache / OGP / HTML 生成を外へ追い出す

### Step 2

frontmatter 用の軽量経路を作る。

### Step 3

Markdig parser は resolver 結果を埋め込むだけに寄せる。

### Step 4

キャッシュ方式とテストを整備する。

## まとめ

Markdig 依存そのものが悪いわけではありません。
問題は、Markdig の同期インライン拡張へ「非同期ネットワーク解決」と「HTML 表示ロジック」と「キャッシュ管理」を同時に詰め込んでいることです。

したがって、oEmbed 改修で最初にやるべきことは機能追加ではなく、構造分離です。

最初に整理すべきポイントは次です。

1. frontmatter 解析と本文レンダリングの経路分離
2. Markdig 拡張からネットワーク処理を逃がす
3. static 状態を減らす
4. テスト可能な service 境界を作る

## 関連資料

- [docs/oembed-comparison.md](/F:/_Git/Blog/BlogGenerator/docs/oembed-comparison.md)
- [docs/issue-list.md](/F:/_Git/Blog/BlogGenerator/docs/issue-list.md)
- [docs/code-map.md](/F:/_Git/Blog/BlogGenerator/docs/code-map.md)
