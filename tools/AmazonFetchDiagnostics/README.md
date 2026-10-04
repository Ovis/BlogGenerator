# Amazon取得診断

通常モードは本番の `AmazonProductHttpFetcher`（同じヘッダー、圧縮展開、タイムアウト）、
`AmazonProductPageParser`、`AmazonProductMetadataResolver` を使い、商品ページ取得だけを検証します。
ブログ生成・公開や本番キャッシュの読書きは行いません。
各試行で空のメモリキャッシュから開始するため、失敗キャッシュによる再試行抑止の影響も受けません。

## GitHub Actions

workflowがデフォルトブランチに入った後、Actions の **Amazon Fetch Diagnostics** → **Run workflow** から実行します。
ASINの初期値は `B0CDWSWLWV`。3つの新しい `ubuntu-latest` runnerを順番に使用し、
各runnerで1〜3回、10秒間隔で取得します（最大9リクエスト）。
取得失敗はジョブ失敗として表示されますが、他のrunnerの診断は継続します。
JSON結果、各試行の応答HTML（`amazon-response-1.html`など）、比較用の出口IPを7日間のartifactとして保存します。
出口IPの確認に使う `checkip.amazonaws.com` が失敗しても商品取得の診断は継続します。

結果にはUTC時刻、ASIN、HTTPステータス、例外型、本文長・SHA-256、
本番ロジックの判定（Success / Blocked / NetworkError / UnexpectedResponse / ParseMiss / NotFound）、
取得できた商品名・画像URL、commit、HTMLファイル名、ページタイトル、ブロック判定に一致した文字列を記録します。
HTMLはfetcherが圧縮展開・文字列化した応答本文をUTF-8で保存します。Cookie・リクエストヘッダーは保存しません。
HTMLはブラウザで実行せず、まずテキストとして確認してください。
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

カレントディレクトリに `amazon-diagnostics.json` と各試行の `amazon-response-N.html` を出力します。
終了コードは全試行成功で0、1件以上取得失敗で1、引数不正で2です。
ASINは英数字10文字、回数は1〜3のみ受け付けます。

## Playwrightとの比較

手動workflowの `engine` を `playwright` にすると、通常のheadless Chromiumで商品ページを開きます。
User-Agentの偽装や確認画面のボタン操作は行いません。各試行で新しいブラウザコンテキストを作り、Cookieを引き継ぎません。
HTTPモードと同じASIN・回数で比較してください。別workflow実行では出口IPと時刻も変わるため、ブラウザだけが差の原因とは断定できません。

- `amazon-browser-N-initial.html`: メインページのHTTP応答本文
- `amazon-browser-N-dom.html`: JavaScript実行・load待ち後のDOM
- `amazon-browser-N.png`: 画面のスクリーンショット
- `amazon-response-N.html`: 商品情報抽出に渡したDOM

JSONの `engine`、`browserHttpStatus`（実際のメイン応答ステータス）、`finalUrl` も確認できます。
本番と同じ商品情報パーサー・ブロック判定を使用します。ブラウザ版のブロック判定はDOMに対して行います。

ローカルではツールをビルド後、生成された `playwright.ps1` をPowerShellで実行してChromiumをインストールしてください。

```bash
dotnet build tools/AmazonFetchDiagnostics/AmazonFetchDiagnostics.csproj -c Release
pwsh tools/AmazonFetchDiagnostics/bin/Release/net10.0/playwright.ps1 install chromium
dotnet run --project tools/AmazonFetchDiagnostics/AmazonFetchDiagnostics.csproj -c Release -- B0CDWSWLWV 1 playwright
```

Linuxでブラウザのシステム依存が不足する場合は、Playwrightの公式手順に従い `install --with-deps chromium` を使います。
