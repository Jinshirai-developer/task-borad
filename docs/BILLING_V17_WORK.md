# チーム人数制限とStripeテスト契約 v17

購入前の比較・確認画面と結果画面は後続の [v18](PURCHASE_V18_WORK.md) で追加し、ローカルへ反映済み。現在は「チームProのプランを見る → テスト決済へ進む」から開始する。以下は基盤実装・接続作業の記録。

## 実装範囲と現在の状態

無料はオーナー込み3人、チームProは対象の1チームだけプラン上の人数制限を解除。送った招待数ではなく現在の所属数を数える。既存の超過チーム、タスク、担当者、XP、ごほうびを維持し、無料枠以上では新規参加だけ拒否する。サービス全体の登録上限（設定の初期値100人）、1人10チームまで、レート制限は別に残る。

Stripe.net 52.4.1によるCheckout・契約同期・解約予約／取消・即時終了・未完了決済の中止・署名付きWebhookを実装。新規環境の標準設定は連携無効。2026-09-09にローカル5097へ月額500円のStripe Sandbox接続設定を反映し、価格の読み取り、CLI認証、通知転送の接続とAPI版一致を確認した。実際のCheckout・購入・Pro反映・更新・解約のE2E試験はまだ行っていない。登録不要デモのプラン変更はネットワークを使わない画面シミュレーションであり、Stripeテスト決済の成功を意味しない。

今回はチーム管理内のプラン表示を整えた。前に提案したアプリ全体のサイドパネル化・日常操作の導線刷新は別作業。ペット画像の生成／透過も変更していない。

## 画面

「設定 → タグ・チーム」で対象チームを選び「チームの管理」を開く。参加人数、無料／Pro、テスト価格（月額500円）、契約状態と確認済み期限、テスト決済、状態再確認をまとめている。Proには期間末解約／予約取消、折りたたみ内にテストを片付けるための即時終了がある。メンバーは状態だけ閲覧し、購入・契約操作は所有者限定。

決済の戻り先に`?billing=return&team=...`が付いていても、それを購入証明にしない。所属を再確認し、対象チームの管理を開くだけ。ユーザーがサーバー上の最新状態を確認する。Stripe未接続時は購入ボタンを無効にして理由を表示する。デモではStripe未接続のシミュレーションであることを明示し、見本メンバーの参加、更新失敗・期限切れを体験できる。

## 保存と安全

- 追加Migration `20260908140702_AddTeamTestBilling` は `team_billing` と `billing_event_receipts` の2表だけを追加。旧データの書換え・削除・自動契約はない。
- サーバーの固定価格IDを使い、テストモード、JPY、月額、設定金額（現在500円）、数量1をStripeオブジェクトで照合する。ブラウザーが価格・契約IDを選べない。
- 本番キーを無効設定時も拒否。使用可能なキーは標準の`sk_test_`／`rk_test_`。本番のイベント・Session・Subscription・Price・Invoiceも拒否し、本番モードへ切り替えるコードはない。
- 支払い済みInvoiceとactive契約の確認で期限を保存。期限内はPro、更新失敗時も支払い済み期間は保護し、期限後は新規参加のみ無料枠へ戻す。期間末解約は期限まで利用可能。即時終了は返金ではない。
- 参加処理は既存のDBトランザクション／advisory lock内で人数と有効なPro状態を判定。同時参加で無料の最後の枠が重複しない。
- StripeへのI/O中はDBのスレッド依存ロックを保持しない。操作予約・2分のリース・比較更新で並行操作と古い応答を防止する。
- Checkoutの試行ID・価格・開始時刻・戻りoriginを先に保存し、応答喪失時も同じ冪等キーとパラメーターで再確認。Stripeの冪等保持期間を超える可能性がある23時間超の未確認試行は自動で新契約を作らない。
- 署名検証には公式SDKと未加工のHTTP本文を使用。WebhookだけCookie/CSRF検証を除外し、他の更新APIは既存認証・CSRFを維持。Webhookは本番モード・古い署名・偽造署名を拒否する。
- 通知の内容から直接Proを付与せず、保存済みのSessionからStripeの現在状態を再取得。処理済みイベントIDを同じDBトランザクションで保存し、重複や順序逆転でも過去状態を再適用しない。失敗時は503で再送対象にする。
- 2分間隔で未終了契約の同期を補完（1回20チームまで）。通知IDは30日後に定期削除。カード情報・メール・Stripe通知の本文・秘密鍵をアプリDBに保存しない。
- 未完了決済／契約がある間のチーム削除・所有権移譲を拒否し、前所有者の契約が残ったまま引き継がれないようにする。契約終了後は従来のチーム操作を利用できる。
- 任意のテスト決済の送信項目・保存・削除をプライバシー文書に追記。既存の同意版は変更しておらず、購入前にはテスト専用・外部画面への遷移を明示して確認する。法的審査を行ったという意味ではない。

## Stripe接続の最新状況（2026-09-09）

ユーザーが価格IDと制限付きテストキーを `.local/stripe-test.env` に入力済み。明示的な許可を受け、誤ってWebhook欄に貼られた `rk_test_` キーを `Billing__SecretKey` へ移動し、Webhook欄を空欄に戻した。ファイルは権限600、Git／Dockerビルド対象外。秘密値はチャット・ログ・文書へ出力していない。

Stripe公式APIへの `GET /v1/prices/{保存された価格ID}` がHTTP 200で成功。テストモード、有効、JPY、毎月1回、定額、数量変換なしを確認。Stripe側が月額500円で初期設定980円と不一致だったが、ユーザーが **月額500円へ揃える** と明示的に合意し、`.local/stripe-test.env` の `Billing__MonthlyYen` を500に変更した。再度のGETでも500円の設定一致を確認済み。価格の存在と読み取り権限だけの確認で、Checkout／Subscriptionの書き込み権限を検証したという意味ではない。

標準設定・登録不要デモ・ローカル設定の金額を500円に揃えた。`.local/stripe-test.env` は通知署名キーを保存したうえで `Billing__Enabled=true` とし、既存の非課金環境設定・鍵を保持してコンテナへ反映済み。このファイルをASP.NET Coreが自動読込するわけではない。標準の `appsettings.json` は引き続き連携無効で、秘密情報をイメージへ含めない。今回Stripe側への商品／価格／Checkout／契約の作成・更新は行っていない。

Stripe公式CLI 1.50.10（macOS arm64）を `.local/stripe-cli/` へ導入。公式GitHub ReleaseのアーカイブSHA-256 `29f7d7ead27625a04bd860aceb5fbdbee93a30b8c5338deba2952a821ac1b8fe` と公式チェックサムの一致を確認。グローバルインストールや既存のStripe設定変更はしていない。

`scripts/stripe-local.rb` は500円の価格検証、秘密値を出さない署名キー準備／通知転送、専用設定を使うCLI認証を補助する。CLIへのキーは子プロセス環境だけで渡し、コマンド引数・標準出力へ出さない。テレメトリーは無効。アプリ用の制限付きキーではCLI通知準備が権限拒否となったため、権限を広げずブラウザー認証へ切り替えた。導入版の実際のOAuth方式（`access.stripe.com` と `--complete-device`）に公式ソースを確認して対応した。`ruby tests/stripe-local.test.rb` で形式、価格、認証URL／固定コマンドの検証を行う。

**ブラウザーでのSandbox認証は完了。** 認証直後の選択先には保存済み価格がなかったため、許可済みSandboxだけを列挙し、`select-sandbox` で同一の500円テスト価格が存在する接続先を選択した。本番コンテキストへの切替は行っていない。`prepare-webhook-login` で署名キーを非公開ファイルへ保存し、`listen-login` の接続完了とSDKと同じ `2026-08-26.dahlia` を確認した。CLIの秘密設定・認証ファイル・認証ストアをチャットやログへ出さない。

通知転送は現在起動中。PC再起動やプロセス終了後はプロジェクト直下で以下を実行する。既存の転送が動いている間は重複起動しない。API版や署名キーが設定と異なる場合、補助スクリプトは転送を止める。SDKの版チェックを無効にせず、接続先と設定を確認する。

```sh
ruby scripts/stripe-local.rb listen-login
```

### 接続反映の検証記録

- `scripts/connect-stripe-preview.py` で `task-board:billing-v17` から `task-board:billing-v17-500` へ更新し、`http://localhost:5097/` で稼働。これは旧イメージ・無効設定を前提にした一度限りの反映スクリプトで、現在の環境へそのまま再実行しない。
- バックエンド243件、JavaScript113件、Ruby補助スクリプト20件（22 assertions）成功。Stripe業務APIはバックエンド試験ではテスト用通信に置換。
- 反映後の配信画面で課金98項目（月額500円表示含む）、相棒186項目、使いやすさ73項目が成功。これらは実API・Stripeへアクセスしないデモ試験。記録は `/private/tmp/task-billing-browser-lwa5y2_b`、`/private/tmp/task-companion-browser-3_jl2ph8`、`/private/tmp/task-usability-browser-08nbc4at`。
- readiness 200、匿名プラン取得401、配信29ファイルのSHA-256一致。16永続テーブルの全行ハッシュ、既存Migration、非課金の環境設定、ログイン鍵を保持。画像は変更していない。
- ローカルで生成した業務処理対象外の署名付き検査通知は200、偽造署名と本番モード通知は400。これは受信口の局所的検査で、Stripeが発行した決済イベントの配信・Pro反映のE2E確認ではない。
- バックアップは `.local/backups/stripe-connect-20260909.qs6l28c1/`（700/600、DB・鍵・秘密設定を含むため共有・commit禁止）。安全な結果の要約は同フォルダーの `verification.json`。旧コンテナ `task-board-preview-before-stripe-connect` と以前のバックアップも停止・保持した。
- 実ユーザーのタスクや契約を使ったテスト操作、実決済、公開、commit、pushは行っていない。次にチーム所有者によるCheckoutのテスト購入が必要。
- 今回作成したJavaScript検証専用コンテナ `task-stripe-js-check` は停止・削除済み（マウントなし、ネットワークなし、同じテストから再作成可能）。実アプリ・DB・メール・鍵・通知転送・復旧用コンテナは保持した。

## 別環境での接続手順と残る決済試験

ローカル5097は接続設定済み。以下の1〜5は別環境の準備手順であり、現在残っている試験は6〜7。新規のサーバー用キーは、このアプリ専用の制限付きテストキー `rk_test_` を優先する。Stripeも制限付きキーを推奨している。[公式のキー権限ガイド](https://docs.stripe.com/keys/restricted-api-keys)。接続時は実装のSession作成／取得／失効、Subscription取得／更新／終了、Price取得、Invoice展開取得に必要な権限を確認し、関係ないAPIへの全権限は付与しない。キーはファイルへ直接入力し、チャット・ログへ出力しない。

1. StripeのSandboxを用意する。専用の制限付きテストキーを取得する。**秘密鍵をチャット・ソースコード・Gitへ貼らない。** 別アプリの設定を流用せず、専用テスト環境を推奨。[公式Sandbox案内](https://docs.stripe.com/sandboxes)
2. Sandbox内にチームProの月額商品価格を用意する。JPY、500円、毎月、数量1、割引・トライアルなし。`price_...`を控える。
3. WebhookのAPI版をSDKに合わせる。現在のSDKは`2026-08-26.dahlia`。[SDKの定義](https://github.com/stripe/stripe-dotnet/blob/v52.4.1/src/Stripe.net/Constants/ApiVersion.cs)
4. ローカルはStripe CLIをSandboxへ接続し、下記で通知を転送する。導入／認証の最新状況は上記参照。`--live`は使用しない。CLIが表示した`whsec_...`はダッシュボードの別エンドポイントの鍵と混同せず、秘密としてローカル保管する。CLIはアカウントのデフォルトAPI版で通知するため、選択したSandboxの版も確認する。[CLIの公式手順](https://docs.stripe.com/cli/listen)

```sh
stripe listen --forward-to http://localhost:5097/api/billing/stripe-webhook --events checkout.session.completed,checkout.session.expired,customer.subscription.created,customer.subscription.updated,customer.subscription.deleted,invoice.paid,invoice.payment_failed,invoice.payment_action_required
```

5. `.env.example`末尾を参考に、Git対象外・権限600のファイルまたはホスティングの秘密設定へ以下を保存する。ASP.NET Coreは`.env`を自動読込しない。既存のDB接続・ログイン鍵・メール設定を維持して環境変数を渡す。**`docker compose up`だけで現在の5097プレビューが更新されるわけではない。** ローカル5097への今回の反映は上記の専用スクリプトで完了済み。

```dotenv
Billing__Enabled=true
Billing__MonthlyYen=500
Billing__SecretKey=rk_test_REPLACE_LOCALLY
Billing__WebhookSecret=whsec_REPLACE_LOCALLY
Billing__PriceId=price_REPLACE_LOCALLY
```

6. ログインしたチーム所有者から実際にCheckoutを開き、公式テストカードで支払う。実カードを入力しない。例：`4242 4242 4242 4242`、未来の期限、任意の3桁CVC。成功画面だけでなくWebhook受信・DBのPro状態・4人目の参加を確認する。[公式テストカード](https://docs.stripe.com/testing)
7. 失敗・追加認証・キャンセル・更新失敗・期間末解約をSandboxで確認する。更新日時はStripeのシミュレーション／test clocksを使って検証可能だが、今回この外部検証は未実施。[時間のシミュレーション](https://docs.stripe.com/billing/testing/test-clocks)

## API

全てチームを明示し、通常の更新には認証CookieとCSRFが必要。

| Method | Path | 内容 |
| --- | --- | --- |
| GET | `/api/teams/{id}/billing` | メンバー向けプラン・人数・契約状態。秘密IDを返さない |
| POST | `/api/teams/{id}/billing/checkout` | 所有者が同一チームのテストCheckoutを開始／再開 |
| POST | `/api/teams/{id}/billing/sync` | 所有者による最新状態の照合 |
| POST | `/api/teams/{id}/billing/cancel` | 期間末解約を予約 |
| POST | `/api/teams/{id}/billing/resume` | 期間末解約の予約を取り消す |
| POST | `/api/teams/{id}/billing/end_now` | テスト契約を即時終了。返金操作ではない |
| POST | `/api/teams/{id}/billing/abandon` | 未完了Checkoutを失効させる |
| POST | `/api/billing/stripe-webhook` | 公式SDKで署名を検証する専用受信口 |

未接続503、所属外404、所有者以外の契約操作403、同時操作409、無料上限での参加409。契約操作の応答が不明なら、別チームで作り直したりDBを手編集したりせず、同じ試行を再確認する。

## 残る検証と運用上の制限

- **接続設定は完了したが、実Checkoutによる購入・Pro反映・4人目参加・失敗・更新・解約・決済通知のE2E確認は未実施。** 価格GETやCLI接続、ローカル署名検査の成功と、決済フロー全体の成功を混同しない。制限付きキーの業務API書き込み権限も実操作では未検証。
- Customer Portal、請求書UI、返金、日割りプラン変更、支払い方法変更、契約移管、座席数課金は初版に含めない。返金が必要な検証はStripe Sandbox側で別途行い、アプリの返金連動は未実装として扱う。
- Session IDを保存できないまま長時間経過した試行は、Stripe側で試行ID（metadataの`taskboard_attempt`）に一致するSessionを確認して復旧する。冪等キーの保存期限後に盲目的な再作成はしない。キー変更・Price変更・Sandbox切替も未確認契約を整理してから行う。
- チーム削除でアプリ内の課金対応表は消えるが、Stripe側のテストCustomer／履歴は自動削除しない。
- 一般公開、本番決済、法的な課金サービス運用の準備完了を意味しない。

## 初回v17実装時の検証記録（接続設定前）

- バックエンド242件、JavaScript113件、PNG4件成功。StripeのHTTP通信はテスト用ハンドラー／Gatewayに置換し、署名生成・偽造／期限切れ／本番イベント拒否、支払い確認、冪等キー、失敗時再試行、チーム権限を確認。
- 新画面97項目（320/375/1440px×classic/retro/dark、未接続・所有者／メンバー・再試行・戻りURL改変）、既存の相棒186項目・使いやすさ73項目のChrome試験成功。実APIへのアクセス0件。
- PostgreSQL 16上の旧v16→v17移行で、4人チームとチェックリスト付き共有タスクを含む既存15表の全行ハッシュが一致。pending model changesなし。
- 実PostgreSQL／Mailpitで34検証・45 APIリクエスト成功。同時参加の1成功／1拒否、再参加、退出で空いた枠の再利用、未接続503、権限・XP不変を確認。テスト4アカウント・チーム・タスクは片付け済み。Stripeへは接続していない。
- 302 PNG・実配信111素材のバイト列不変。

## 初回ローカル反映の履歴（2026-09-09 JST、接続設定前）

`scripts/update-billing-preview.py` で `task-board:companion-v16` から `task-board:billing-v17` へ更新済み。アプリは `http://localhost:5097/`、登録不要デモは `http://localhost:5097/index.html?demo=1`。Stripe接続情報は追加せず、未接続のまま反映した。

- 追加型Migrationの2表のみ作成。更新前後の既存14永続テーブルの全行ハッシュ一致、環境設定とData Protection鍵を保持。旧データへのテスト操作なし。
- 配信29ファイルがソースと一致。readiness 200、匿名プラン取得401、未接続Webhook 503。
- 実配信デモでもChrome97／186／73項目が成功し、実APIアクセスは0件。新プラン画面のPC／スマホを目視確認。記録は `/private/tmp/task-billing-browser-yjiw3gbu`、`/private/tmp/task-companion-browser-8f_er7g4`、`/private/tmp/task-usability-browser-blxxknch`。
- DB・鍵・環境設定のバックアップは `.local/backups/billing-20260908.2kqpl8en/`（700/600、秘密を含む、共有・commit禁止）。確認結果は同フォルダーの `verification.json`。旧アプリは `task-board-preview-before-billing-v17` として停止保持。これ以前のバックアップも維持。
- 移行SQL生成の初期2回はツール用イメージのentrypoint問題で事前検査中に停止し、アプリ・DBは未変更。共通デプロイヤーで `--entrypoint dotnet` を明示して解消し、その後の反映は成功した。
- 公開、commit、push、本番請求は行っていない。アプリ全体のレイアウト刷新・新ペット一体絵の透過は未実施。
- 検証専用コンテナ6個（`task-v17-sdk`、`task-v17-js`、`task-v17-qa-db`、`task-v17-qa-mail`、`task-v17-qa-web`、`task-v17-qa-runner`）、専用DBの匿名ボリューム、内部ネットワーク `task-v17-qa` は削除済み。合成テストデータだけを削除し、同じテストから再作成可能。実アプリ・実DB・メール・鍵・復旧用コンテナ・バックアップは保持した。
