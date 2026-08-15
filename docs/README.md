# BlogGenerator Docs

この `docs` フォルダは、`BlogGenerator` を今後改修するときの一次資料です。
主な読者は Codex を含む実装支援エージェントで、コンテキスト圧縮後や別チャットへ切り替えた後でも、ここを起点に再度コード修正へ入れる状態を目指します。

## この資料の目的

- リポジトリの目的と責務を短時間で再把握できるようにする
- 実行方法、設定方法、入出力、主要コード配置を実装ベースで残す
- README と実装の差分、未使用要素、改修時の注意点を明示する
- 次回以降の Codex 作業で「どこを読めばよいか」を固定化する

## スナップショット

- 作成日: 2026-08-11
- 確認対象:
  - `README.md`
  - `src/Program.cs`
  - `src/Core/*`
  - `src/Models/*`
  - `src/MarkdigExtension/*`
  - `src/TemplateSample/*`
- 実施済み確認:
  - `dotnet build BlogGenerator.sln -v minimal` は成功
  - `dotnet src/bin/Debug/net8.0/BlogGenerator.dll --help` で CLI ヘルプ確認
- 未実施確認:
  - 実データを使ったサイト生成結果の目視確認
  - テーマ差し替え時の互換性確認

## 読み順

1. `project-overview.md`
2. `runtime-and-config.md`
3. `code-map.md`
4. `modification-playbook.md`

## すぐに使うための要点

- これは .NET 8 の静的ブログジェネレーターで、Markdown を HTML と RSS/Atom に変換する CLI ツール
- エントリポイントは `src/Program.cs`
- 改修で最初に読むべき実装は `src/Core/MarkdownProcessor.cs` と `src/Core/PageGenerator.cs`
- テーマは RazorLight 前提で `src/TemplateSample` が最小の参考実装
- テストプロジェクトは現時点で存在しない

## 改修前の最小確認コマンド

```powershell
dotnet build BlogGenerator.sln -v minimal
dotnet src/bin/Debug/net8.0/BlogGenerator.dll --help
```

## 関連資料の位置づけ

- `README.md`:
  - 利用者向けの概要
  - 実装との差分があるため、そのまま改修判断の根拠にはしない
- `docs/*.md`:
  - 改修用の一次資料
  - 実装読解とビルド確認を踏まえて整理した内容
