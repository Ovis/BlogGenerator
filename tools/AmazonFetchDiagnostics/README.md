# Amazon取得診断

本番の `AmazonProductHttpFetcher`（同じヘッダー、圧縮展開、タイムアウト）、
`AmazonProductPageParser`、`AmazonProductMetadataResolver` を使い、商品ページ取得だけを検証します。
ブログ生成・公開や本番キャッシュの読書きは行いません。
各試行で空のメモリキャッシュから開始するため、失敗キャッシュによる再試行抑止の影響も受けません。

## GitHub Actions

workflowがデフォルトブランチに入った後、Actions の **Amazon Fetch Diagnostics** → **Run workflow** から実行します。
ASINの初期値は `B0CDWSWLWV`。3つの新しい `ubuntu-latest` runnerを順番に使用し、
各runnerで1〜3回、10秒間隔で取得します（最大9リクエスト）。
取得失敗はジョブ失敗として表示されますが、他のrunnerの診断は継続します。
JSON結果と、比較用の出口IPを7日間のartifactとして保存します。
出口IPの確認に使う `checkip.amazonaws.com` が失敗しても商品取得の診断は継続します。

結果にはUTC時刻、ASIN、HTTPステータス、例外型、本文長・SHA-256、
本番ロジックの判定（Success / Blocked / NetworkError / UnexpectedResponse / ParseMiss / NotFound）、
取得できた商品名・画像URL、commitを記録します。HTML本文・Cookieは保存しません。
HTTPステータスはfetcherの結果値です。現実装は成功応答を200へ正規化し、通信例外ではnullです。

## 結果の読み方

- 同じrunnerで失敗後に成功: 少なくともその実行では恒常的に取得不能ではない。
- runner間で結果が異なる: ネットワークや時間、Amazon側の判定差が疑われる。出口IPと時刻を比較する。
- 複数runner・別時間帯でBlockedが続く: GitHub Actions環境に対する継続的な制限が疑われる。
- NetworkErrorやタイムアウト: Amazonのボット判定とは分けて、通信失敗として調べる。
- NotFound: ASIN・商品の公開状態を確認する。

**全試行が失敗してもIPブロックとは断定できません。** ヘッダー、TLS、リクエスト頻度、
セッションなども判定に影響し得ます。3つのrunnerが必ず別の出口IPになる保証や、
IP確認サービスとAmazonへの通信が必ず同じ出口を使う保証もありません。
時刻を変えて再実行し、必要なら手元の別回線で同じコマンドを実行して比較してください。
CAPTCHAやアクセス制限を回避する処理は含めていません。

## ローカル実行

.NET 10 SDKでリポジトリルートから実行します。

```bash
dotnet run --project tools/AmazonFetchDiagnostics/AmazonFetchDiagnostics.csproj \
  -c Release /p:CodexSkipRelaxVersioner=true -- B0CDWSWLWV 3
```

カレントディレクトリに `amazon-diagnostics.json` を出力します。
終了コードは全試行成功で0、1件以上取得失敗で1、引数不正で2です。
ASINは英数字10文字、回数は1〜3のみ受け付けます。
