# Instruction: BlogGenerator 改修実装計画

## 関連

- 実装課題: `BlogGenerator` の段階的改修
- 前提課題: 2026-08-11 時点までのコード調査、課題整理、oEmbed / Markdig 方針整理
- 関連課題:
  - [docs/issue-list.md](/F:/_Git/Blog/BlogGenerator/docs/issue-list.md)
  - [docs/markdig-oembed-migration-plan.md](/F:/_Git/Blog/BlogGenerator/docs/markdig-oembed-migration-plan.md)
  - [docs/markdig-oembed-constraints.md](/F:/_Git/Blog/BlogGenerator/docs/markdig-oembed-constraints.md)
  - [docs/oembed-comparison.md](/F:/_Git/Blog/BlogGenerator/docs/oembed-comparison.md)
  - [docs/project-overview.md](/F:/_Git/Blog/BlogGenerator/docs/project-overview.md)
  - [docs/runtime-and-config.md](/F:/_Git/Blog/BlogGenerator/docs/runtime-and-config.md)
  - [docs/code-map.md](/F:/_Git/Blog/BlogGenerator/docs/code-map.md)
  - [docs/modification-playbook.md](/F:/_Git/Blog/BlogGenerator/docs/modification-playbook.md)
- 参照手引き:
  - `D:\Users\Desktop\Work\指示書ガイド\Guide_InstructionAuthoring.md`
  - `D:\Users\Desktop\Work\指示書ガイド\Template_Instruction_ImplementationStepDriven.md`

## ゴール

- `issue-list.md` にある主要課題を、依存関係を踏まえた安全な順序で段階的に解消する
- `Markdig` 継続利用を前提に、oEmbed を `HtmlInline` 直書きから独自 AST ノード + 独自 renderer 方式へ移行する
- 次回以降の改修でも壊れにくいよう、最初にテスト基盤を作り、出力破壊系の不具合を優先して直す

## 2026-08-11 時点の進捗

- Step 1 から Step 8 は実装済み
- Step 9 は `README.md` と oEmbed 関連 docs の同期まで実施済み
- 残作業は `docs/issue-list.md` を含む最終同期と、Step 10 の文字列依存リトライ条件の整理
- サンプルテーマのハードコードは今回の対象外として残置する

## 動作等価性 / 基本方針

- 本作業は、出力不具合の修正と保守性改善の両方を含む
- ただし、各コミットは `1コミット = 1論点` を厳守し、フェーズ単位でまとめて変更しない
- 大きい改修は、事前にテストで現状と期待動作を固定してから進める
- oEmbed については `Markdig` 自体を置き換えず、`Markdig` の使い方を見直す方針で進める

## スコープ

- 対象:
  - `src/*`
  - `BlogGenerator.sln`
  - `README.md`
  - `docs/*`
- 主対象機能:
  - Markdown 処理
  - 静的ファイルコピー
  - タグ / アーカイブページ生成
  - oEmbed 処理
  - テスト基盤
  - 依存パッケージ更新
- 非対象:
  - 別リポジトリの改修
  - Markdown エンジンの全面置き換え
  - 大規模なテーマ刷新
  - 仕様未確定の Amazon URL カード化拡張

## 実装時の必須ルール

### コミット単位

- `1コミット = 1論点` とする
- `1フェーズ = 1コミット` にはしない
- 小さくレビューしやすいコミットへ分ける
- コミットメッセージは日本語タイトルにする
- コミット前には原則ビルド成功を確認する
- ただし、分界点維持のために一時的にビルド不能状態でコミットする必要がある場合は許容する
- 各コミット後は次のコミットへ自動で進まず、一旦停止して内容を報告し、次へ進めてよいか確認する

### 停止して質問する条件

以下に当たる場合は推測で進めず停止して質問すること。

- 設計判断が必要
- docs と実コードが矛盾している
- NuGet 追加・更新が必要
- 互換性要件が曖昧

補足:

- 今回の計画には `Markdig` と `AngleSharp` を含む依存更新候補がある
- したがって、実際に NuGet 更新へ入る直前には必ずユーザー確認を取る

### テスト方針

- テストフレームワークは `NUnit` を用いる
- 新規テストは、最終 HTML 断片や出力ファイル構造を確認できる粒度を優先する
- 大規模改修前に、現状固定用の最小テストを先に入れる

### コメント方針

- 実装コメントは日本語で記載する
- 特に「なぜこの実装が必要か」が追えるコメントを優先する
- 冗長な逐語説明は避ける
- メソッドの XML コメントは簡潔に書く
- XML コメントは常体で、文末に句点をつけない

## 前提と事実確認

### 実装から確認できている事実

- 静的ファイルコピーは Markdown ごとに呼ばれ、Markdown を含まないディレクトリ配下の資産が欠落しうる
  - [src/Core/MarkdownProcessor.cs](/F:/_Git/Blog/BlogGenerator/src/Core/MarkdownProcessor.cs:48)
  - [src/Core/FileSystemHelper.cs](/F:/_Git/Blog/BlogGenerator/src/Core/FileSystemHelper.cs:21)
- アーカイブページ生成とサイドバー集計で、公開日未設定記事の扱いが一致していない
  - [src/Core/PageGenerator.cs](/F:/_Git/Blog/BlogGenerator/src/Core/PageGenerator.cs:164)
  - [src/Core/PageGenerator.cs](/F:/_Git/Blog/BlogGenerator/src/Core/PageGenerator.cs:176)
  - [src/TemplateSample/SideBar.cshtml](/F:/_Git/Blog/BlogGenerator/src/TemplateSample/SideBar.cshtml:98)
- タグ名は slug 化や URL エンコードなしでディレクトリ名と URL に使われている
  - [src/Core/PageGenerator.cs](/F:/_Git/Blog/BlogGenerator/src/Core/PageGenerator.cs:133)
  - [src/Core/PageGenerator.cs](/F:/_Git/Blog/BlogGenerator/src/Core/PageGenerator.cs:150)
- `OEmbedCardParser.Match()` は同期メソッドの中で `GetAwaiter().GetResult()` を使っている
  - [src/MarkdigExtension/OEmbedExtension.cs](/F:/_Git/Blog/BlogGenerator/src/MarkdigExtension/OEmbedExtension.cs:228)
- frontmatter 抽出と本文レンダリングで同じ `Markdig` pipeline を使っている
  - [src/Core/MarkdownProcessor.cs](/F:/_Git/Blog/BlogGenerator/src/Core/MarkdownProcessor.cs:74)
  - [src/Core/MarkdownProcessor.cs](/F:/_Git/Blog/BlogGenerator/src/Core/MarkdownProcessor.cs:92)
- 現行依存は `Markdig 0.41.0`、`AngleSharp 1.3.0`
  - [src/BlogGenerator.csproj](/F:/_Git/Blog/BlogGenerator/src/BlogGenerator.csproj:22)
  - [src/BlogGenerator.csproj](/F:/_Git/Blog/BlogGenerator/src/BlogGenerator.csproj:23)
- ソリューションにテストプロジェクトは存在しない
  - [BlogGenerator.sln](/F:/_Git/Blog/BlogGenerator/BlogGenerator.sln:6)

### 方針として確定していること

- oEmbed は `Markdig` を使い続ける前提で改修する
- `HtmlInline` 直書きではなく、独自 AST ノード + 独自 renderer へ移す
- frontmatter 用 pipeline と本文用 pipeline は分離する
- まずテスト基盤を作り、その後に依存更新と構造変更へ進む

## 問題点

- 生成物が壊れる可能性のある課題と、保守性上の課題が混在している
- 自動テストがないため、依存更新や oEmbed 再設計の退行を検知しにくい
- `issue-list.md` の各項目は独立ではなく、静的ファイルコピーと責務分離、`Markdig` 更新と oEmbed 再設計のように依存関係がある
- したがって、優先度だけではなく実装順序を固定しないと安全に進めにくい

## 実装順序

| Step | 内容 |
|---:|---|
| 1 | `NUnit` テスト基盤を追加し、現状固定用の最小テストを作る |
| 2 | 静的ファイルコピー不具合と責務結合を整理する |
| 3 | アーカイブ不整合を直す |
| 4 | タグ URL を安全化する |
| 5 | 依存更新の実施可否を確認し、`Markdig` / `AngleSharp` 更新を行う |
| 6 | frontmatter 用 pipeline と本文用 pipeline を分離する |
| 7 | oEmbed の HTTP / provider / discovery / OGP / cache を parser から分離する |
| 8 | oEmbed を独自 AST ノード + 独自 renderer に移行する |
| 9 | README と docs を実装へ追従させる |
| 10 | ロケール依存のコピーリトライ条件やサンプルテーマ残課題を整理する |

## Step 1: NUnit テスト基盤と現状固定

### 目的

- 以後の改修で挙動退行を検知できる土台を作る
- 出力破壊系の不具合修正と、`Markdig` / oEmbed の構造変更を安全に進める

### 変更対象候補

- `BlogGenerator.sln`
- 新規テストプロジェクト
- `src/Core/MarkdownProcessor.cs`
- `src/Core/PageGenerator.cs`

### 実施内容

- `NUnit` のテストプロジェクトを追加する
- 最初は広げすぎず、次の観点だけ固定する
  - frontmatter あり / なし
  - 固定ページ
  - タグページ生成
  - アーカイブページ生成
  - 画像パス変換
  - `[oembed:"..."]` の基本挙動
- oEmbed はネットワーク実依存を避けられる形を優先する

### コミット粒度の例

- `NUnit テスト基盤を追加`
- `MarkdownProcessor の基本変換テストを追加`
- `PageGenerator のタグとアーカイブ生成テストを追加`

## Step 2: 静的ファイルコピー不具合と責務結合の整理

### 目的

- `issue-list` の `1` と `9` をまとめて解消する
- Markdown 解析の件数に依存しない静的ファイルコピーへ寄せる

### 変更対象候補

- `src/Core/MarkdownProcessor.cs`
- `src/Core/FileSystemHelper.cs`
- `src/Program.cs`

### 実施内容

- Markdown ごとのコピー呼び出しを見直す
- 入力ルート全体を基準に、非 Markdown 資産を 1 回で正しくコピーする方向を検討する
- Markdown 変換と静的ファイルコピーの責務を分ける

### 注意

- コピー仕様は出力互換性に直結するため、既存入力構成との互換性が曖昧なら停止して確認する

## Step 3: アーカイブ不整合の修正

### 目的

- `Published == DateTimeOffset.MinValue` を持つ記事が壊れたアーカイブリンクを生まないようにする

### 変更対象候補

- `src/Core/PageGenerator.cs`
- `src/TemplateSample/SideBar.cshtml`
- 関連テスト

### 実施内容

- アーカイブページ生成とサイドバー集計で、未設定公開日の扱いを統一する
- 統一先は実装確認後に決めるが、設計判断が必要なら停止して確認する

## Step 4: タグ URL の安全化

### 目的

- タグ名をそのままパスに使う現状をやめ、壊れない URL / ディレクトリ名にする

### 変更対象候補

- `src/Core/PageGenerator.cs`
- `src/Models/*`
- `src/TemplateSample/SideBar.cshtml`
- 関連テスト

### 実施内容

- slug 化または安全な URL 生成方式を導入する
- どの変換規則を採るかは互換性要件に関わるため、曖昧なら停止して確認する

## Step 5: 依存更新

### 目的

- `Markdig` 更新と `AngleSharp` 警告解消の入口を作る

### 変更対象候補

- `src/BlogGenerator.csproj`
- `src/MarkdigExtension/*`
- 関連テスト

### 実施内容

- まずユーザーへ NuGet 更新可否を確認する
- 承認後、`Markdig` の最新系更新を行う
- 必要に応じて `AngleSharp` も更新候補として扱う
- この段階では大きい設計変更を混ぜず、互換 API 追従とビルド通過を優先する

### コミット粒度の例

- `Markdig を更新`
- `Markdig 更新に伴う拡張実装を追従`
- `AngleSharp を更新`

## Step 6: frontmatter 用 pipeline と本文用 pipeline の分離

### 目的

- oEmbed 改修の影響を本文レンダリング側へ閉じ込める

### 変更対象候補

- `src/Core/MarkdownProcessor.cs`

### 実施内容

- frontmatter 抽出用の軽量 pipeline を別に持つ
- oEmbed 拡張は本文用 pipeline にだけ載せる
- ここではまだ AST / renderer 化までは行わず、処理経路分離に集中する

## Step 7: oEmbed の責務分離

### 目的

- `issue-list` の `4` を構造から解消する
- parser から HTTP / provider / discovery / OGP / cache を外へ出す

### 変更対象候補

- `src/MarkdigExtension/OEmbedExtension.cs`
- 新規 service / model 群
- `src/Core/MarkdownProcessor.cs`

### 実施内容

- 次の責務を分離する
  - provider catalog
  - oEmbed resolver
  - discovery resolver
  - OGP extractor
  - cache
  - HTML 生成
- parser は記法検出に寄せる

### 注意

- interface 設計や責務境界で迷う場合は停止して確認する

## Step 8: 独自 AST ノード + 独自 renderer への移行

### 目的

- `HtmlInline` 直書きをやめ、`Markdig` の parse / render を正しく分離する

### 変更対象候補

- `src/MarkdigExtension/OEmbedExtension.cs`
- 新規 `Inline` / `Renderer` 実装
- 関連テスト

### 実施内容

- `[oembed:"..."]` から独自ノードを生成する
- renderer が最終 HTML を出力する
- renderer 内で HTTP は行わない
- 動画や通常リンク fallback の描画責務を renderer 側へ寄せる

## Step 9: README と docs の同期

### 目的

- 実装と利用手順の差分を解消する

### 変更対象候補

- `README.md`
- `docs/*`

### 実施内容

- `-t` 誤記や `IsFiexedPage` 誤記を是正する
- 実装変更後の CLI / oEmbed / テスト方針に追従させる
- docs の変更は、可能ならコード変更と別コミットに分ける

### 2026-08-11 時点の反映

- `README.md` の CLI 例と `IsFixedPage` 誤記は修正済み
- `docs/markdig-oembed-migration-plan.md` と `docs/markdig-oembed-constraints.md` は現行実装へ同期済み
- `docs/issue-list.md` などの最終同期が残る

## Step 10: 残課題整理

### 対象

- コピー時の英語例外メッセージ依存
- サンプルテーマ内のハードコード

### 注意

- これらは最後に扱う
- 直近の出力不具合や構造変更へ混ぜない

### 2026-08-11 時点の方針

- `FileSystemHelper` の英語例外メッセージ依存リトライは対応対象
- サンプルテーマのハードコードは今回対応しない

## 残置するもの / 今回やらないもの

- Markdown エンジン全面置き換え
- Amazon URL の自動カード化仕様
- provider 別テンプレートの大規模拡張
- `cacheDir` / TTL 方式への全面移行

## 受入基準

- `NUnit` テストプロジェクトが追加され、主要ケースの退行検知ができる
- 静的ファイルコピー欠落が解消される
- 公開日未設定記事で壊れたアーカイブリンクが出ない
- タグ URL が危険文字で壊れない
- `Markdig` 更新後もビルドと関連テストが通る
- frontmatter と本文で pipeline が分離される
- oEmbed が `HtmlInline` 直書きではなく、独自 AST ノード + 独自 renderer 方式へ移行する
- README と docs が最終実装に追従する

## 検証手順

最低限、各コミットまたはコミット直前に次を行う。

```powershell
dotnet build BlogGenerator.sln -v minimal
dotnet test BlogGenerator.sln -v minimal
```

必要に応じて次も行う。

- テスト入力からの生成結果を一時出力し、タグ / アーカイブ / 静的ファイル / oEmbed の結果を目視確認する
- 依存更新直後はビルド成功だけでなく関連テスト成功を確認する

## 影響・リスク

- `Markdig` 更新は API 差分対応が必要になる可能性が高い
- タグ URL の変換方式は既存リンク互換性へ影響する可能性がある
- 静的ファイルコピー方式の見直しは生成物の配置差分を生む可能性がある
- oEmbed 再設計は広範囲なので、テスト先行なしでは退行リスクが高い

## ロールバック方針

- 各論点を小さい日本語コミットへ分ける
- 問題が出た場合は論点単位で差し戻せる状態を保つ
- 依存更新コミットと構造変更コミットを分離し、原因切り分けをしやすくする

## 実装時の注意

- コード変更前に、関連 `docs` と実コードを両方確認する
- docs と実コードが矛盾していたら停止して確認する
- NuGet 更新は事前承認なしで進めない
- コメントは日本語で、理由が追えるものを優先する
- XML コメントは簡潔にする
- docs だけの修正は、可能ならコード修正と別コミットに分ける
- 実装中に新しい課題を見つけても、今回の論点に直接関係しないなら混ぜない

---

## 次チャットでの開始手順

1. この文書を読む
2. 次の関連文書を読む
   - `docs/issue-list.md`
   - `docs/markdig-oembed-migration-plan.md`
   - `docs/markdig-oembed-constraints.md`
   - `docs/code-map.md`
3. `src/Core/MarkdownProcessor.cs`、`src/Core/PageGenerator.cs`、`src/Core/FileSystemHelper.cs`、`src/MarkdigExtension/OEmbedExtension.cs` を読む
4. まず Step 1 の `NUnit` テスト基盤追加だけに絞って着手する

## 次チャットへの依頼文

```text
docs/Instruction_BlogGenerator_改修実装計画.md を起点に、このリポジトリの改修を進めてください。
まず指示書と関連 docs を読んでください。
今回は Step 1 だけを対象にし、NUnit によるテスト基盤追加と最小限の現状固定テストまで実装してください。
1コミット = 1論点で進め、コミットメッセージは日本語にしてください。
各コミット後は一旦停止し、コミット内容と確認結果を報告して、次へ進めてよいか確認してください。
設計判断が必要、docs と実コードが矛盾、NuGet 追加・更新が必要、互換性要件が曖昧のいずれかに当たる場合は停止して質問してください。
コメントは日本語で、なぜこの実装が必要かが追いやすい形にしてください。
XML コメントは簡潔な常体で、文末に句点をつけないでください。
```
