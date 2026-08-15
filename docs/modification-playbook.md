# Modification Playbook

## Codex が最初に確認すべきこと

1. `docs/README.md` を読む
2. 対象変更がどの責務かを決める
3. 入口として `src/Program.cs` を確認する
4. 出力に関わる変更なら `src/Core/PageGenerator.cs` と `src/TemplateSample/*` を読む
5. Markdown 記法や Frontmatter に関わる変更なら `src/Core/MarkdownProcessor.cs` と `src/MarkdigExtension/*` を読む

## 機能別の主担当ファイル

### CLI 変更

- `src/Core/CommandLineSetup.cs`
- `src/Program.cs`

### 設定追加

- `src/Models/SiteOption.cs` または `src/Models/FeedOption.cs`
- `src/Program.cs`
- 必要に応じて `README.md`

### Frontmatter 追加

- `src/Models/FrontMatter.cs`
- `src/Core/MarkdownProcessor.cs`
- 必要に応じてテーマファイル

### 記事 HTML 変更

- `src/TemplateSample/Content.cshtml`
- `src/TemplateSample/Layout.cshtml`
- 必要に応じて `src/Models/Article.cs`

### 一覧ページ変更

- `src/Core/PageGenerator.cs`
- `src/TemplateSample/PageList.cshtml`
- `src/TemplateSample/_pagination.cshtml`

### タグ/アーカイブ変更

- `src/Core/PageGenerator.cs`
- `src/TemplateSample/Tag.cshtml`
- `src/TemplateSample/SideBar.cshtml`

### oEmbed 変更

- `src/MarkdigExtension/OEmbedExtension.cs`
- `src/MarkdigExtension/Models/*`
- `src/Converters/AutoNumberToStringConverter.cs`

## 実装上の注意点

### README と実装の差分

- README の `-t` は実装されていない
- README には `IsFiexedPage` という誤記があるが、実装は `IsFixedPage`

### 未使用または実質未接続の要素

- `Frontmatter.Eyecatch` は未使用
- `PageType.Index` はレイアウト分岐で使われていない
- `PageType.Archive` もレイアウト分岐で使われていない

### URL とファイル名

- タグ名は slug 化されず、そのままディレクトリ名になる
- 記事の出力は `about.md -> about.html`
- サンプルテーマのナビは `/about` を向いており、静的ホスティング条件によっては `about.html` とずれる可能性がある

### HTML 生成

- RazorLight で `DisableEncoding()` を使っている
- `Article.Body` は HTML としてそのまま描画される
- 安全性やエスケープ方針を変えるときは出力互換性に注意する

### oEmbed

- 初回に外部ネットワークへアクセスする
- キャッシュは任意指定
- ネットワーク不通時も通常リンクへフォールバックする実装がある

### ファイルコピー

- 入力配下の非 Markdown ファイルは、各 Markdown の処理中に同一ディレクトリ単位でコピーされる
- コピーの責務が `MarkdownProcessor` から `FileSystemHelper` に委譲されているため、性能改善や重複コピー削減はこの境界を意識する

## 改修時の最小確認

### 毎回

```powershell
dotnet build BlogGenerator.sln -v minimal
dotnet src/bin/Debug/net8.0/BlogGenerator.dll --help
```

### 出力系を触ったとき

少なくとも次を確認する。

- 記事ページが生成される
- `index.html` が生成される
- タグページが生成される
- 月別アーカイブが生成される
- `feed.rss` と `feed.atom` が生成される

### テーマや URL を触ったとき

少なくとも次を確認する。

- 画像パスが壊れない
- タグリンクとアーカイブリンクが壊れない
- ページネーションリンクが壊れない
- `SiteUrl` にサブパスがある場合でもリンクが壊れない

## 現時点の技術的負債候補

- 自動テストがない
- README と実装の乖離がある
- `Eyecatch` のような未使用入力が残っている
- タグ slug 化や URL 正規化の設計が未整理
- サンプルテーマにハードコードされた外部 CDN 依存がある
- `AngleSharp 1.3.0` に脆弱性警告がある

## 変更時のドキュメント更新方針

仕様や実装責務が変わった場合は、少なくとも次を更新対象にする。

- `docs/project-overview.md`
- `docs/runtime-and-config.md`
- `docs/code-map.md`
- `docs/modification-playbook.md`
- 必要に応じて `README.md`
