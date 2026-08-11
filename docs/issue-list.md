# Issue List

この文書は、`BlogGenerator` が現時点で抱えている課題点の一覧です。
改修計画、優先順位付け、別チャットでの引き継ぎ時に、そのまま一次資料として使うことを想定しています。

## スコープ

- 確認日: 2026-08-11
- 根拠:
  - `src/*` 実装読解
  - `README.md`
  - `dotnet build BlogGenerator.sln -v minimal`
- 対象:
  - 挙動上の不具合
  - 保守性上の問題
  - 性能・運用上の懸念

## 優先度の見方

- `High`
  - 実際の出力不具合や欠損につながる
- `Medium`
  - すぐ壊れなくても、運用上の不安定さや改修コストを増やす
- `Low`
  - 直近の致命傷ではないが、整理しておきたい

## 課題一覧

### 1. 入力配下の静的ファイルコピーが不完全

- Priority: `High`
- 概要:
  - 非 Markdown ファイルのコピー対象が「各 Markdown ファイルの属するディレクトリ配下」に限定されている
  - 入力ルート配下でも、Markdown を含まない別系統ディレクトリの静的ファイルは出力されない可能性がある
- 根拠:
  - `MarkdownProcessor` は Markdown ごとに `CopyContentFile` を呼ぶ
  - `CopyContentFile` は `filePath` の親ディレクトリから再帰コピーする
- 該当箇所:
  - [src/Core/MarkdownProcessor.cs](/F:/_Git/Blog/BlogGenerator/src/Core/MarkdownProcessor.cs:30)
  - [src/Core/MarkdownProcessor.cs](/F:/_Git/Blog/BlogGenerator/src/Core/MarkdownProcessor.cs:51)
  - [src/Core/FileSystemHelper.cs](/F:/_Git/Blog/BlogGenerator/src/Core/FileSystemHelper.cs:21)
  - [src/Core/FileSystemHelper.cs](/F:/_Git/Blog/BlogGenerator/src/Core/FileSystemHelper.cs:33)
- 影響:
  - 画像や添付ファイルが欠落する
  - 入力構成によっては生成物が壊れる
- 補足:
  - 同じ subtree を Markdown 件数分だけ重複コピーする設計でもあり、性能面でも不利

### 2. 公開日未設定の記事で存在しないアーカイブリンクが出る

- Priority: `High`
- 概要:
  - アーカイブページ生成時は `Published == DateTimeOffset.MinValue` を除外しているが、サイドバーのアーカイブ一覧では除外していない
- 根拠:
  - アーカイブページ生成時には未設定日付を除外
  - サイドバー側は `Year` と `Month` をそのまま集計
- 該当箇所:
  - [src/Core/PageGenerator.cs](/F:/_Git/Blog/BlogGenerator/src/Core/PageGenerator.cs:176)
  - [src/Core/PageGenerator.cs](/F:/_Git/Blog/BlogGenerator/src/Core/PageGenerator.cs:164)
  - [src/TemplateSample/SideBar.cshtml](/F:/_Git/Blog/BlogGenerator/src/TemplateSample/SideBar.cshtml:98)
  - [src/TemplateSample/SideBar.cshtml](/F:/_Git/Blog/BlogGenerator/src/TemplateSample/SideBar.cshtml:109)
- 影響:
  - `0001/01` のような壊れたアーカイブリンクが出る可能性がある
  - サイドバーの表示と実際の生成物が一致しない

### 3. タグ名をそのままディレクトリ名・URL に使っている

- Priority: `High`
- 概要:
  - タグ名の slug 化や URL エンコードを行わず、そのままタグ別ページの出力先とリンクに使っている
- 根拠:
  - タグ別出力先が `tags/<tag>/...`
  - サイドバーや一覧からもそのままリンクしている
- 該当箇所:
  - [src/Core/PageGenerator.cs](/F:/_Git/Blog/BlogGenerator/src/Core/PageGenerator.cs:133)
  - [src/Core/PageGenerator.cs](/F:/_Git/Blog/BlogGenerator/src/Core/PageGenerator.cs:150)
  - [src/TemplateSample/SideBar.cshtml](/F:/_Git/Blog/BlogGenerator/src/TemplateSample/SideBar.cshtml:84)
- 影響:
  - `/`, `?`, `#`, `:` などを含むタグで出力が壊れる
  - ファイルシステム依存のエラーやリンク切れが起きる

### 4. oEmbed が生成処理を強くネットワーク依存にしている

- Priority: `Medium`
- 概要:
  - provider 一覧取得と個別埋め込み生成で同期ブロッキングを行っている
- 根拠:
  - provider 一覧読込で `GetAwaiter().GetResult()`
  - 各 `[oembed:"..."]` 展開でも `GetAwaiter().GetResult()`
- 該当箇所:
  - [src/MarkdigExtension/OEmbedExtension.cs](/F:/_Git/Blog/BlogGenerator/src/MarkdigExtension/OEmbedExtension.cs:48)
  - [src/MarkdigExtension/OEmbedExtension.cs](/F:/_Git/Blog/BlogGenerator/src/MarkdigExtension/OEmbedExtension.cs:228)
- 影響:
  - ネットワーク遅延で生成時間が大きくぶれる
  - オフラインや制限環境で不安定になる
  - 記事数や埋め込み数によって待ち時間が読みにくい

### 5. コピー時のリトライ条件が英語例外メッセージ依存

- Priority: `Medium`
- 概要:
  - `IOException` のリトライ条件が `"being used by another process"` 文字列に依存している
- 該当箇所:
  - [src/Core/FileSystemHelper.cs](/F:/_Git/Blog/BlogGenerator/src/Core/FileSystemHelper.cs:61)
- 影響:
  - ロケールやランタイム差でリトライされずに失敗する可能性がある
  - Windows 日本語環境での安定性に不安がある

### 6. README と実装が一致していない

- Priority: `Medium`
- 概要:
  - README の CLI 説明と実装がずれている
  - Frontmatter 項目名の誤記もある
- 根拠:
  - README は `-t` を案内しているが、実装には存在しない
  - README は `IsFiexedPage` と誤記している
- 該当箇所:
  - [README.md](/F:/_Git/Blog/BlogGenerator/README.md:31)
  - [README.md](/F:/_Git/Blog/BlogGenerator/README.md:49)
  - [README.md](/F:/_Git/Blog/BlogGenerator/README.md:124)
  - [src/Core/CommandLineSetup.cs](/F:/_Git/Blog/BlogGenerator/src/Core/CommandLineSetup.cs:17)
- 影響:
  - 利用者が誤ったコマンドで実行する
  - 次回改修時の調査コストが増える

### 7. 自動テストが存在しない

- Priority: `Medium`
- 概要:
  - ソリューションに本体プロジェクトしかなく、回帰確認を自動化できていない
- 根拠:
  - `BlogGenerator.sln` に 1 プロジェクトしか含まれていない
- 該当箇所:
  - [BlogGenerator.sln](/F:/_Git/Blog/BlogGenerator/BlogGenerator.sln:6)
- 影響:
  - ページ生成、タグ、アーカイブ、oEmbed、固定ページなどの退行を検知しにくい
  - 改修のたびに手動確認コストが増える

### 8. 既知の脆弱性警告を含む依存パッケージが残っている

- Priority: `Medium`
- 概要:
  - `AngleSharp 1.3.0` に対して `NU1902` 警告が出ている
- 根拠:
  - `dotnet build BlogGenerator.sln -v minimal` 実行時に警告を確認
- 該当箇所:
  - [src/BlogGenerator.csproj](/F:/_Git/Blog/BlogGenerator/src/BlogGenerator.csproj:22)
- 影響:
  - セキュリティ上の懸念が残る
  - CI や依存監査の導入時に障害になる可能性がある

### 9. Markdown 処理と静的ファイルコピーが強く結合している

- Priority: `Low`
- 概要:
  - 本文解析とファイルコピーが `MarkdownProcessor` で同時に進む構造になっている
- 根拠:
  - 記事変換処理の途中でコピーを呼んでいる
- 該当箇所:
  - [src/Core/MarkdownProcessor.cs](/F:/_Git/Blog/BlogGenerator/src/Core/MarkdownProcessor.cs:48)
  - [src/Core/MarkdownProcessor.cs](/F:/_Git/Blog/BlogGenerator/src/Core/MarkdownProcessor.cs:51)
- 影響:
  - 責務分離が弱く、静的資産まわりの改善がしづらい
  - テストもしづらい

### 10. サンプルテーマに運用前提のハードコードが残っている

- Priority: `Low`
- 概要:
  - サンプルテーマ内に固定リンクや外部 CDN 依存がある
- 根拠:
  - `About` リンクが `/about` 固定
  - SNS リンクがダミー
  - Bulma / Prism / FontAwesome などを CDN から直接参照
- 該当箇所:
  - [src/TemplateSample/Layout.cshtml](/F:/_Git/Blog/BlogGenerator/src/TemplateSample/Layout.cshtml:51)
  - [src/TemplateSample/Layout.cshtml](/F:/_Git/Blog/BlogGenerator/src/TemplateSample/Layout.cshtml:89)
  - [src/TemplateSample/SideBar.cshtml](/F:/_Git/Blog/BlogGenerator/src/TemplateSample/SideBar.cshtml:22)
  - [src/TemplateSample/SideBar.cshtml](/F:/_Git/Blog/BlogGenerator/src/TemplateSample/SideBar.cshtml:29)
  - [src/TemplateSample/SideBar.cshtml](/F:/_Git/Blog/BlogGenerator/src/TemplateSample/SideBar.cshtml:36)
- 影響:
  - サンプルをそのまま本番テーマに近いものとして使うとリンク不整合が出やすい
  - オフライン配布や依存削減の妨げになる

## 優先対応案

まずは次の順で着手するのが妥当です。

1. 静的ファイルコピーの見直し
2. 公開日未設定記事とアーカイブ表示の整合化
3. タグの slug 化または安全な URL 生成
4. テスト基盤の追加
5. README と docs の整合化
6. 依存パッケージの更新検討
7. oEmbed のネットワーク依存と同期処理の整理

## 関連資料

- [docs/README.md](/F:/_Git/Blog/BlogGenerator/docs/README.md)
- [docs/project-overview.md](/F:/_Git/Blog/BlogGenerator/docs/project-overview.md)
- [docs/runtime-and-config.md](/F:/_Git/Blog/BlogGenerator/docs/runtime-and-config.md)
- [docs/code-map.md](/F:/_Git/Blog/BlogGenerator/docs/code-map.md)
- [docs/modification-playbook.md](/F:/_Git/Blog/BlogGenerator/docs/modification-playbook.md)
