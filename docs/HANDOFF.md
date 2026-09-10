# Task Board Handoff

## 現行: v23（SOS宛先指定、2026-09-09）

ローカル5097は task-board:sos-v23。「チームのタスク → メモ → 相談・SOS」で全員向け／特定メンバー1人宛てを選択する。特定宛てへの応答は本人のみ、内容はチーム内共有。宛先の退出・退会で未解決SOSを終了し、退会時は履歴・Undo内の宛先IDも除去する。担当者・経験値は変更しない。

17テーブル・ログイン鍵・既存テスト契約1件を保全して更新済み。[v23の仕様・検証・保全記録](SOS_V23_WORK.md)。v22の整理内容も維持している。

## v22（2026-09-09）の整理

ローカル5097は task-board:review-v22。表示・種類のレベル制限と再ロック、未利用日による元気減少を廃止。取得済み装備・成長姿を保持。新規完了の報酬は担当者へ、未割り当ては操作した人へ。引き継ぎは明示指定時だけ受領で担当変更する。

記録への入口はカードの「メモ」とボードの「作業メモ／相談・メモ」。主操作はメモ・相談・確認／引き継ぎ、続きメモと保存済み作品は副操作へ。タグ・チームの管理は設定に残す。

既存17テーブル・ログイン鍵・有効なテスト契約1件を保全して更新済み。詳細とバックアップ・未実施事項は [v22作業記録](REVIEW_V22_WORK.md)。以下の旧版説明と食い違う場合は、この節とv22記録を優先する。

## 目的と今回の範囲

フリーランス案件の獲得に使う無料のポートフォリオデモです。実販売・本番課金は行わず、Stripe Sandboxでのテスト課金のみを実装しています。Identity・メール確認・復旧に加え、チーム共有、設定画面、6テーマ・5レイアウト・6種類のペットを用意しました。チーム作成・参加とタグ登録は「設定 → タグ・チーム」へ集約しています。左側はタグの絞り込みだけにし、プルダウン末尾の「＋ 新しいタグを追加…」で設定の追加フォームを直接開けます。タグ追加先を個人／選択中チームで明示し、作成／編集のタグは複数選択プルダウンのままです。ボード上部のコンパクトな個人／参加中チーム名タブから表示先を切り替え、既存の件数表示でも非公開／共有を示します。管理項目は引き続き設定内です。Windows風テーマとシンプル版は引き続き選択できます。完了取消時の経験値返却とレベル解放も維持しています。v15で登録不要デモ、担当者、15秒自動更新、Undo、チェックリスト、タグ整理、週間振り返りを追加。外部IdP、MFA、確認済みメールの変更、リアルタイム配信は対象外です。

## 構成

- .NET 10 / ASP.NET Core Web API、EF Core 10、PostgreSQL 16
- 同一originのHTML / CSS / JavaScriptによるカンバンUI
- Identity、HttpOnly Cookie、CSRF検証、アカウント設定完了チェック
- MailKitによるSMTP配信、Data Protectionで暗号化するDB送信キュー
- xUnitのサービス・認証・HTTP統合テストとNode.jsのフロントエンド認証テスト
- Dockerによるビルド・publish、GitHub ActionsのCI設定

## 認証の引き継ぎで重要なこと

- `Models/UserProfile.cs` は `IdentityUser<int>` を継承し、既存の `user_profiles.id` とタスク・ペットの関連を保持します。
- `Authentication/LegacyPasswordHasher.cs` は旧PBKDF2を検証し、ログイン成功時だけIdentity形式へ更新します。
- 旧Bearerは廃止です。旧セッションは再ログインが必要です。メールがない旧ユーザーには限定セッションで追加登録を案内します。
- `RequireAuthenticationAttribute` が未認証を401、メール・規約未確認を403にします。追加設定・セッション確認・ログアウト・退会だけは限定セッションでも可能です。
- 認証Cookieは最長8時間で自動延長なし。公開環境はSecureで、秘密をJavaScriptやStorageに渡しません。
- `ValidateApiAntiforgeryAttribute` は匿名の登録・ログインを含むブラウザー向け更新系APIに適用します。v17のStripe専用Webhookのみ公式SDKの署名検証に置き換えます。認証変更後のCSRF再取得は `frontend/auth-client.js` が管理します。
- ログアウトは `SessionVersion` により全Cookieを失効。メール確認・再設定はSecurityStampも更新します。ログアウトだけで送信済みメールリンクは失効させません。
- 新規登録・再設定のパスワードは12〜100文字。旧パスワードは移行時ログインで使用できます。5回失敗すると15分間ロックアウトします。

## メールと公開文書

- `Services/AuthenticationService.cs` が登録、追加設定、メール確認、再設定を管理します。
- 確認リンク24時間・再設定リンク30分。リンクは `Authentication:PublicBaseUrl` の固定originから生成し、秘密情報をfragmentに置きます。
- `EmailOutboxService` と `EmailOutboxWorker` が予約・再試行・削除を担当します。合計5回まで送信し、成功時または期限切れ後に行を削除します。実到達や完全な重複配送防止の保証ではありません。
- `frontend/terms.html` / `privacy.html` が無料デモ向けの文書です。運営者・連絡先・利用先・保存方針はconfigから表示し、未設定を捏造で埋めません。
- 文書版は `Configuration/LegalOptions.cs` と `frontend/legal.js`、HTML本文の `2026-09-07-teams`。共有範囲・退会時の共有タスク保持に合わせて改定しています。既存ユーザーも次回アクセス時に再確認が必要です。メール確認状態は維持します。
- `publicReleaseReady` は6項目の入力状態であり、法的審査や公開試験の合格フラグではありません。

## ローカル起動

前提は.NET 10 SDK、Docker、フロントエンドテスト用のNode.js 22以降です。

```bash
dotnet tool restore
docker compose up -d
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update
dotnet run --urls http://localhost:5095
```

アプリは `http://localhost:5095/`、Mailpit受信箱は `http://localhost:8025/`、SMTPは1025番です。Mailpitは本番宛先へ配信しません。Live Serverや別origin配信は利用しません。

現在の作業ホストには.NET 10 SDKが未導入のため、今回のバックエンド検証にはDockerの.NET 10環境を使用しています。ホストの `dotnet` には対応SDKの導入が必要です。既存本番DBへのMigration適用、公開・commit・pushは今回行っていません。

## 検証と残作業

### 最新: 2026-09-09 お助けのHTTP 415修正・横断確認（v21）

ローカル http://localhost:5097/ は task-board:transport-v21 に更新済み。companion-work.jsの共通POSTにapplication/jsonを明示し、お助け・ノート・受け渡しなど全16操作のUnsupported Media Typeを修正した。index.htmlのJS読み込み版も更新。キャッシュ更新のため、既に開いている画面は未保存の入力を控えて再読み込みする。

248バックエンド・118フロントエンド、実Chrome→HTTP→PostgreSQLで82確認（全16操作）、相棒実API94確認、使いやすさ実API52確認、Identity/SMTPの登録・確認・復旧・旧Cookie失効・退会が成功。デモChromeは相棒186・購入221・使いやすさ73・ペット396確認、画像4件、Ruby20件成功。実画面送信コードを実行する回帰2件を既存Node CI対象へ追加した。リモートCIは未実行。

同種のJSON送信漏れは今回確認した他の経路には見つからなかった。課金はテスト専用であり実販売・本番課金はしない。既存Sandboxの有効契約1件を保持し、今回新しいStripeの購入・支払い・契約変更は実行していない。外部IdP/MFA/確認済みメール変更は従来どおり対象外。公開HTTPS・実メール到達・運営情報/保存方針の確認などは別作業であり、一般公開の完了判定ではない。

scripts/update-transport-preview.pyは実行済みで再実行しない。全17永続表の行ハッシュ・Migration・環境・ログイン鍵を維持、配信29ファイル一致、readiness 200と匿名API 401を確認。復旧用task-board-preview-before-transport-v21、私有バックアップ.local/backups/transport-v21-ediw57yq/を保持（秘密を含むため共有・commit禁止）。DB移行・画像変更・公開・commit・pushなし。詳細: [TRANSPORT_V21_WORK.md](TRANSPORT_V21_WORK.md)。

### 以前の記録: 2026-09-09 完了画面の閉じ方・アカウント単位のPro（v20）

ローカル http://localhost:5097/ は task-board:account-v20 に更新済み。チーム作成／参加完了の×・Esc・外側クリック・OKは前の設定画面へ戻らず、選択中のボードへ閉じる。旧「共有ボードを開く」は「OK」。未完了フォームは従来どおり設定へ戻れる。

課金はアカウント単位。設定 → アカウント → プラン・契約管理から、チームが0件でも購入・同期・解約ができる。所有する全チームと今後作るチームに適用し、他人所有チームはその所有者のプランに従う。チーム削除で契約を消さず、所有権変更で契約を譲渡しない。期限切れ後もメンバー・タスク・経験値は保持。アカウント削除は決済／契約を終了してから。

ユーザー操作後に有効なテスト契約1件があることを確認し、元の契約番号・利用期限・価格を保持して所有者アカウントへ移行。移行後はProのアカウント1件、対象の所有チーム3件。再購入不要。今回こちらからStripeの契約・Checkout・商品・価格の作成／変更・支払いは実行していない。

20260908183357_AccountBilling は team_billing → account_billing とし、主キー／FKをアカウントへ変更。旧TeamIdは照合用スナップショットとして保持。既存契約のmetadata・idempotencyは旧形式を維持し、新規契約だけアカウント形式。Webhook・自動同期もアカウントを参照。同じ所有者に複数の旧契約行がある場合は削除や自動解約をせず、移行全体を拒否する。

246バックエンド・116フロントエンド、Chromeソース配信221購入／完了／アカウント・73使いやすさ・186相棒のチェック成功。実API・Stripeを遮断したデモ検証。PostgreSQL複製でUp/Down、既存17表の元の値保持、所有者対応、重複契約の原子的拒否を確認した。モデル差分なし。CIに新旧の移行テストを分けて追加したがリモートCIは未実行。

反映スクリプト scripts/update-account-preview.py --deploy は実行済み、再実行しない。既存環境・ログイン鍵保持、配信29ファイル一致、readiness 200、匿名のチーム／アカウント課金API 401。復旧用 task-board-preview-before-account-v20 と .local/backups/account-v20-958cwzwf/ を保持。秘密を含むため共有・commit禁止。単純に旧コンテナを起動してはいけない。Downは新規アカウント契約や対応不能な所有権変更の後は拒否するため、保護バックアップ内のスクリプトと状態を確認して復旧する。旧バックアップも保持。

最終の実配信Chromeでも221項目成功（/private/tmp/task-billing-browser-7bb4b9a_/、実APIを使わないデモ検証）。検証用JS／ビルドの一時コンテナ2個は削除済み・再作成可。アプリ／DB／受信箱、通知転送、旧コンテナ、バックアップは保持。詳細: [ACCOUNT_BILLING_V20_WORK.md](ACCOUNT_BILLING_V20_WORK.md)。メイン画面に管理欄追加なし、画像変更・公開・commit・pushなし。

### 以前の記録: 2026-09-09 購入画面のリッチ化・Stripeカード入力の案内（v19）

`task-board:purchase-v19` を `http://localhost:5097/` に反映済み。購入確認・結果画面をブルーグリーンの見出し、比較カード、購入内容サマリー、既存猫イラストで整理。PCは左右分割、スマホは縦配置と固定操作。メインのタスクボードは変更していない。

通常ボタンを「Stripeでカード入力へ」に変更し、「プラン確認 → Stripeでカード入力 → 結果確認」を明記。デモではカード入力画面が開かないことを強調し、上部にも通常アプリの `login.html` への案内を追加。独自のカードフォーム・偽フォーム・iframeは追加しない。

同日の再確認で保存済み制限付きキーのStripe価格GETが200、テストJPY月額500円の一致と稼働 `Billing__Enabled=true` を確認。**カード画面の実表示・実Checkout作成権限・購入からPro反映までのE2Eは未確認**。一時テストCheckoutを作成し、支払わずに確認後失効させる試験をユーザーへ質問済みだが、まだ承認の回答はない。先に外部の決済オブジェクトを作らない。

バックエンド243件・JavaScript115件、ソース配信の購入193／相棒186／使いやすさ73項目成功。画面テストは実APIとStripeを遮断したデモ。16永続表・Migration・全環境設定・ログイン鍵を保持し、配信29ファイル一致、readiness 200、匿名プラン401。

反映後の実配信も購入193項目成功（`/private/tmp/task-billing-browser-yyj1cd_8`）。検証用コンテナ `task-rich-purchase-js` 1個だけ停止・削除済みで再作成可能。実アプリ・DB・メール・鍵・通知転送・バックアップは保持。

反映用 `scripts/update-rich-purchase-preview.py` は実行済みで再実行しない。復旧用 `task-board-preview-before-purchase-v19` と `.local/backups/purchase-20260909.mn7uetw_/` を保持（700/600、秘密を含むため共有・commit禁止）。通知転送は既存プロセスを継続。実データでのテスト操作、実決済、Stripeの商品・Checkout・契約作成／変更、画像編集、公開・commit・pushはなし。詳細は [RICH_PURCHASE_V19_WORK.md](RICH_PURCHASE_V19_WORK.md)。

### 以前の記録: 2026-09-09 購入確認・結果画面（v18）

`task-board:purchase-v18-1` を `http://localhost:5097/` に反映。チーム管理の「チームProのプランを見る」から専用の比較・購入確認画面へ進む。無料3人／月額500円のPro、対象チーム、テスト専用・継続契約・解約後のデータ保持を明記。メイン画面に欄は増やしていない。初回v18の配信確認後に、エラー案内がスクロール下に隠れないようフォーカス・スクロール処理を追加した。

戻りURLだけで成功とせず、対象チームのAPI応答で確認待ち・中断・終了・有効Proを区別する。「メンバーを招待する」は既存の招待ボタンへの移動のみで、招待コードを自動失効させない。所有者限定・二重操作防止・不正遷移先拒否・スコープ変更時の遅い応答無効化を維持。カード入力はStripeの画面で行う。購入画面見本は `http://localhost:5097/index.html?demo=1&billing=plans&team=1`（実Stripeに接続しない）。

バックエンド243件・JavaScript115件、ソース配信Chrome182／186／73項目成功。初回反映後の配信でも180／186／73項目成功。16永続表・Migration・全環境設定・ログイン鍵を保持し、配信29ファイル一致、readiness 200、匿名プラン取得401。DB移行・秘密設定変更・画像変更なし。Stripe通知転送は既存プロセスを継続する。

最終 `purchase-v18-1` の実配信Chromeも購入関連182項目が成功（`/private/tmp/task-billing-browser-rbxdrdqt`）。実API・Stripeへアクセスしないデモ試験。最終readiness 200、通知転送継続、`git diff --check` 成功。

最終調整前の復旧用 `task-board-preview-before-purchase-v18-1`、バックアップ `.local/backups/purchase-20260909.ancc8_at/` を保持（700/600、秘密を含むため共有・commit禁止）。初回の `task-board-preview-before-purchase-v18` と `.local/backups/purchase-20260909.jkvxqimz/` も保持。反映スクリプト `scripts/update-purchase-preview.py` は引数なし／`--focus-refinement` とも完了済みで再実行しない。検証用コンテナ1個だけ削除済み（再作成可能）。**実Checkout購入・決済通知・Pro反映・更新・解約のE2Eは未実施**。外部のStripe商品・契約などの作成・変更、実ユーザーデータでのテスト操作、公開・commit・pushなし。詳細は [PURCHASE_V18_WORK.md](PURCHASE_V18_WORK.md)。

### 以前の記録: 2026-09-09 Stripe Sandbox接続・月額500円

`task-board:billing-v17-500` を `http://localhost:5097/` に反映済み。ユーザー合意の月額500円へ標準設定・デモ・ローカル設定を統一し、ローカルのみStripe Sandbox連携を有効化した。無料は所有者込み3人、Proは対象の1チームのプラン人数制限を解除する。登録不要デモは引き続きStripeへ接続しないシミュレーション。

ユーザーのブラウザー認証が完了し、許可済みSandboxから保存された500円テスト価格のある接続先を選択。アプリ用 `rk_test_` とCLI認証それぞれで価格GETに成功し、通知署名キーを私有ファイルへ保存、通知転送の接続完了・API版 `2026-08-26.dahlia` の一致を確認した。`.local/stripe-test.env` は600／Git・Docker対象外で `Billing__Enabled=true`。秘密値・CLI認証ストア・秘密設定を表示しない。新規環境の標準設定は連携無効のまま。

**実Checkoutの購入・Pro反映・4人目参加・更新・解約・決済通知のE2E試験は未実施。** Stripeの商品・価格・Checkout・契約をこちらで作成／更新していない。価格GETは業務APIの書き込み権限の証明ではない。次はチーム所有者が「設定 → タグ・チーム → チームの管理 → テスト決済を試す」からテスト購入する。実カード・本番モードは使用しない。

通知転送は現在起動中。終了・PC再起動後はプロジェクト直下で `ruby scripts/stripe-local.rb listen-login` を実行する（重複起動しない）。補助スクリプトは専用の公式CLI 1.50.10を使用し、署名キーとAPI版不一致で停止する。CLI認証は公式の認証ストアを利用。期限切れ時は認証状態を確認し、秘密ファイルの内容を出力しない。実装・接続経緯・運用制限は [BILLING_V17_WORK.md](BILLING_V17_WORK.md) を参照。

バックエンド243件、JavaScript113件、Ruby20件（22 assertions）成功。反映後のChromeは課金98項目（500円表示含む）・相棒186項目・使いやすさ73項目成功（実APIへアクセスしないデモ試験）。記録は `/private/tmp/task-billing-browser-lwa5y2_b`、`/private/tmp/task-companion-browser-3_jl2ph8`、`/private/tmp/task-usability-browser-08nbc4at`。

readiness 200、匿名プランAPI 401、配信29ファイル一致。ローカルの業務対象外の署名検査通知200、偽造署名・本番モード400。実Stripeの決済通知による業務E2Eではない。16永続テーブルの全行ハッシュ・Migration・非課金設定・ログイン鍵を保持した。画像変更、実ユーザーのデータでのテスト操作、公開、commit、pushなし。

反映用は `scripts/connect-stripe-preview.py`（旧イメージ・無効設定を前提とした一度限りのスクリプトなので現在の環境へ再実行しない）。復旧用に `task-board-preview-before-stripe-connect` と `.local/backups/stripe-connect-20260909.qs6l28c1/` を保持。バックアップは700/600で秘密を含むため共有・commit禁止。以前のバックアップも保持した。

### 以前の記録: 2026-09-09 チーム人数制限とStripeテスト契約（v17初回・接続設定前）

`task-board:billing-v17` を `http://localhost:5097/` に反映済み。無料は所有者込み3人、有効なチームProはその1チームだけプラン上の人数制限を解除する。既存の超過メンバーは残し、無料枠を超える新規参加だけ拒否する。設定のチーム管理内に人数・プラン・テスト決済・契約確認・解約を追加。日常のメイン画面に常設欄は増やしていない。アプリ全体のレイアウト刷新は今回の対象外。

Stripe.net 52.4.1で固定価格のCheckout、署名付きWebhook、最新状態の同期、解約予約／取消、即時終了、未完了決済中止を実装。テストキーとテストオブジェクトのみ許可し、本番請求の切替はない。購入／契約操作は所有者限定。Webhookだけ専用署名検証でCookie/CSRFを除外し、通常APIは既存認証を維持する。支払い済み期間でProを判定し、戻りURLやブラウザーの自己申告から解放しない。未完了決済／契約があるチームは削除・所有者移譲を止める。

初回v17反映時点ではStripe接続は無効で、標準価格は980円だった。同日の後続作業で500円への合意・CLI認証・接続反映を完了したため、現在の状態は上の最新記録を優先する。

バックエンド242件、JavaScript113件、PNG4件成功。実PostgreSQL／Mailpitで34検証・45 APIリクエスト、同時の最終枠参加が1成功／1拒否、退出後の枠再利用と権限・XP保持を確認。Stripe通信はGateway／HTTPハンドラーのテスト置換。独立DBのv16→v17移行は4人チームと共有タスクを含む既存15表の全行ハッシュ一致、pending model changesなし。

ローカルDBは追加型Migration `20260908140702_AddTeamTestBilling` の2表追加のみ。更新前後の既存14永続表の全内容、環境設定、ログイン鍵を保持。配信29ファイル一致、readiness 200、匿名プランAPI 401、未接続Webhook 503。実ユーザーデータでテスト操作していない。反映後のChromeも新画面97項目・相棒186項目・使いやすさ73項目成功（全て実APIへのアクセス0件）。記録は `/private/tmp/task-billing-browser-yjiw3gbu`、`/private/tmp/task-companion-browser-8f_er7g4`、`/private/tmp/task-usability-browser-blxxknch`。

復旧用コンテナ `task-board-preview-before-billing-v17` と `.local/backups/billing-20260908.2kqpl8en/` を保持（700/600、秘密を含むため共有・commit禁止）。検証専用コンテナ6個・専用DBの匿名ボリューム・内部ネットワークだけを削除し、実アプリ／DB／メール／鍵は保持した。公開・commit・pushなし。既存302 PNGは不変、一体型90枚の透過・反映は別の残作業。接続手順、正確な検証範囲、未確認決済の手動復旧などの制限は [BILLING_V17_WORK.md](BILLING_V17_WORK.md) を優先する。

### 以前の記録: 2026-09-08 相棒と進める5機能（v16）

`task-board:companion-v16` を `http://localhost:5097/` に反映済み。個人セーブポイント、チームのお助けサインとお礼、受け渡し便、再利用できる攻略ノート、DONEの作品棚を追加。「タスク編集 → 相棒と進める」「設定 → ペット → 相棒と作業の続きを開く」に集約し、常設のメイン欄は増やしていない。デモも5機能に対応。作品棚は説明・外部リンクを飾る初版で、画像アップロード・自動取得・公開はしない。

記録追加でXP・担当者・タスク状態は変えない。チームのセーブポイントも本人限定、それ以外はチーム内共有。更新は所属／認証／CSRF／versionと更新時刻を検証。退出／退会時は本人のしおりをUndoスナップショットからも削除し、未処理の依頼を解除。共有の本文は残して退会者IDを匿名化する。プライバシー文書には具体的な保存範囲の技術説明を追記したが、同意版の変更や法的審査は行っていない。

バックエンド219件、JavaScript108件、PNG検査4件、新機能のChrome186項目・v15回帰73項目、実PostgreSQL/Mailpit94検証・108 APIリクエスト成功。Identity→報酬→タグ→v15→v16移行試験で既存15表保持、pending model changesなし。実DBは追加型Migration `20260908124231_AddCompanionWork`（companion_json追加とUndo保存上限拡張）のみ適用し、既存14永続テーブル・環境・ログイン鍵を保持。配信27ファイルが一致。

更新後の実配信デモでもChrome186項目・73項目が再成功し、実APIアクセス0件。記録は `/private/tmp/task-companion-browser-6ymnp0mp` と `/private/tmp/task-usability-browser-0u6xt40m`。readiness 200・匿名相棒API 401・302 PNG不変を確認。

更新前コンテナ `task-board-preview-before-companion-v16` と `.local/backups/companion-20260908.bostgt4d/` を保持（秘密を含む、700/600、共有・commit禁止）。実データにテスト操作を行っていない。公開・commit・pushなし。新しい一体型ペット90枚の透過・反映は引き続き別の残作業。詳細は [COMPANION_V16_WORK.md](COMPANION_V16_WORK.md)。

### 以前の記録: 2026-09-08 使いやすさ6機能（v15）

6機能を `task-board:usability-v15` として `http://localhost:5097/` に反映。登録不要のお試しは `http://localhost:5097/index.html?demo=1`（実API・Cookie・実データと分離、再読み込みでリセット）。担当者/チェックリストはタスク詳細、タグ整理は設定、週間振り返りはペット設定内に配置。UndoのUIは15秒、サーバーは30秒。別ユーザー・別チーム・新しい編集の上書きと報酬重複を防止します。

バックエンド197件、JavaScript99件、PNG検査4件、分離/実配信Chrome各73項目、専用PostgreSQL/Mailpitの52検証・66 APIリクエスト成功。CI相当の旧Identity→報酬→タグ→v15移行試験とモデル差分なしを確認。実DBは追加型Migration `20260908100945_AddTaskUsability` のみ適用し、既存14データテーブルの全内容・環境設定・ログイン鍵を保持。配信24ファイルのSHA-256一致。

復旧用の旧アプリは `task-board-preview-before-usability-v15`、バックアップは `.local/backups/usability-20260908.e09ziv84/`（700/600、秘密を含むため共有・commit禁止）。検証専用コンテナ・匿名DBボリューム・ネットワークのみ削除し、実DB/メール・鍵・旧バックアップは保持。公開・commit・pushは未実施。

画像透過は未完了。imagegenで猫Lv.1帽子の背景抽出を試したものの、出力はRGBで格子背景が残り不採用。既存302 PNG・実配信111素材は変更していません。一体型90枚は引き続き `drafts/outfits-v1/` の未透過原本で、本アプリ未反映。Pythonで背景だけを処理する明示的な許可待ち。詳細・送信プロンプトは [USABILITY_V15_WORK.md](USABILITY_V15_WORK.md)。

### 以下は各変更時点の履歴

稼働イメージ・直近のバックアップ・実装済み機能は上のv17記録を優先してください。

2026-09-08 アセット画像の整理（画像加工・再デプロイなし）: 全302枚を保持し、184枚を用途別に移動しました。使用中・互換用111枚はURLとバイト列を維持。未透過の一体型90枚は `frontend/assets/pet/drafts/outfits-v1/`、制作原本17枚は `sources/`、不採用11枚は `archive/`、画面キャプチャ66枚は `docs/screenshots/app/` と `pets/` に分類。確認ページ・README・制作記録・スクリプトの参照先も更新しました。案内と全画像の移動前後パス/SHA-256は `docs/assets/` を参照してください。

整理の検証: 302枚すべてのバイト一致、使用中111枚のパス維持、一体型90枚の参照と生成原本一致、分離Chrome20項目/90画像読み込み、PNG検査4件、バックエンド180件、Docker build/publishが成功。`sources`・`drafts`・`archive` はDockerコンテキストとpublishから除外し、Dockerfile/CIの配布確認も更新。検証用イメージは `task-board:assets-organized-check` のみで、稼働中の `task-board:rewards-v14`・DB・保存記録・鍵は未変更です。新しい一体型90枚は引き続き未透過・未反映。既存の無関係な差分は維持し、公開・commit・pushなし。

2026-09-08 ペット＋装備の一枚絵を90枚生成（画像制作のみ・未反映）: 従来の装備単体90点に対応する、6種×Lv.1〜5×帽子/リボン/クッションの一体型PNGを `frontend/assets/pet/drafts/outfits-v1/` に追加しました。全て通常姿・通常表情・装備1点で、成長姿/表情/複数装備の組み合わせは対象外と制作開始時に明示。imagegenスキルの内蔵ツールで90回個別生成し、元画像を上書きせず保存しています。全90枚が1254×1254、重複なし・原本とのSHA-256一致、分離Chromeで20項目/90画像の読み込み成功、6種各15枚を目視確認。確認ページは `frontend/previews/pet-outfits-v1/index.html`、最終一覧は `docs/screenshots/pets/outfits-v1/final/*.png`、プロンプト/検査記録は `docs/pet-outfits-v1/`。

重要: 生成原本90枚はすべてRGBで、格子柄は実透過ではありません。背景だけをPythonで透明化する処理の確認質問を送信済みですが、現時点で返答がないため画像編集は未実施。透明化・再検証が残っています。今回、本アプリのコード・経験値・受取/装備記録・DB・稼働コンテナは変更していません。稼働中のアプリは下記v14のままで、90枚の一枚絵はまだ組み込んでいません。公開・commit・pushなし。

2026-09-08 ペット別ごほうび90点を本アプリへ反映（v14・当時の記録）: 新規の受取対象をLv.1〜5へ変更し、6種×5レベル×帽子/リボン/クッションの90素材をimagegenスキルで個別生成しました。各レベルで3候補から1点を選ぶ方式と3装備枠を維持。種類変更時は同じ取得品が専用デザインに切り替わり、再取得はできません。Lv.6〜20で受取済みの品は旧ごほうびとして再装備可能です。XP・ペット自体のレベル・Lv.5/10/20の成長姿・保存記録は維持し、DBスキーマ変更なし。

90素材と6アトラスは実alphaのある派生PNGです。過去に許可された背景だけの透明化を再利用し、元画像のRGB不変・原本ハッシュを記録。装備は実際の縦横比を保ち、種別/成長姿ごとの頭・首・足元に配置。背景とクッションは固定し、透過ペットと装備のみ動きます。作業中/横たわりのアトラス姿では帽子とリボンを一時非表示にしますが、保存記録とクッションは保持。猫の通常姿のおやつ/休憩は小さな動きのままで、専用の食事/寝姿の新規絵ではありません。素材と制作記録は `frontend/assets/pet/COLLECTION_ASSET_NOTES.md`、詳細は `docs/REWARDS_V14_WORK.md`。

検証: バックエンド180件、フロントエンド89件、Python PNG検査4件、非root Docker build/publish成功。専用PostgreSQL/SMTPで87 APIリクエスト（上限、受取競合、装備保存、取消/再到達、ユーザー分離、退会削除）成功。静的配信と本アプリ実配信でそれぞれChrome396項目成功（API全モック・実データ不使用）。6種/4成長姿/6表情、90枚の実alpha、旧Lv.6維持、320/375/1440px、3テーマ、4タブ、CSP、縦横比、エラー/競合、背景固定、reduced-motionを確認。最終実配信記録は `/private/tmp/task-pet-browser-03hyqvtw`、主要画面は `docs/screenshots/*-v14.png`、6種一覧は `docs/screenshots/pets/rewards-v14/*.png`。

現在の本アプリ `http://localhost:5097/` は `task-board:rewards-v14`。DB・環境変数・`task-board-preview-keys` を継承し、更新前後15テーブルの全内容ダイジェスト一致、配信106ファイルのSHA-256一致、readinessと匿名collection APIの401を確認。バックアップは `.local/backups/rewards-20260908.vurt4po_/`（700/600、秘密を含むため共有・commit禁止）。停止中の更新前コンテナ `task-board-preview-before-rewards-v14` と以前のバックアップも保持。検証専用3コンテナ・匿名DBボリューム・ネットワークは、専用DBのユーザー0件を確認して削除済み。本アプリのDB/メールは更新せず、公開・commit・pushもしていません。

以下は以前の変更時点の記録です。v13の猫装備方式や稼働イメージは上記v14で更新されています。

2026-09-08 猫の承認済み4案を本アプリへ反映（v13）: 猫の `base` / 「いつもの相棒」に `portfolio-cat-{idle,pet,hat,bow}-v3.png` を使用。4枚は確認ページの透過PNGそのままです。帽子・リボンの同時装備と装備付きの笑顔は、同じキャンバス位置のCSS表示クリップで組み合わせます。背景とクッションは静止し、透過画像の子要素だけに小さな動きを付けました。ごほうび・取得品の猫の見本も同じ画像です。旧アトラス全体の回転・上下動は停止し、他種・成長姿の既存ポーズ切替は維持しています。

今回の範囲は猫のいつもの姿のみです。この姿の帽子・リボンは取得レベルにかかわらず、承認された青い布の各1デザインを表示します（設定内でも明示）。おやつ・休憩は小さな上下動・呼吸で、専用の食事・寝姿の新規画像は未制作です。他の種類・成長段階の新素材や装備デザインの追加は未実施。XP、レベル条件、ごほうび、3装備枠、成長姿の保存、アルバム、API、認証、Migrationは変更していません。元の画像と静的確認ページも残しています。素材記録は `frontend/assets/pet/COLLECTION_ASSET_NOTES.md`。

検証: 非root Docker build/publish成功、バックエンド176件、フロント86件成功。分離Chromeの266チェックで4枚の実alpha、装備・保存後再取得・レベル低下時の解除、3操作の背景/クッション固定とXP不変、全種/成長/表情、エラー/再試行/競合ガード、320/375/1440px、classic/retro/dark、reduced-motionを確認。ブラウザーのAPIはすべてモックで、実利用者のタスク・アカウントは操作していません。

現在の本アプリ `http://localhost:5097/` は `task-board:cat-cutout-v13`。既存環境変数・DB・`task-board-preview-keys` をそのまま引継ぎ。停止中の更新前コンテナ `task-board-preview-before-cat-v13` をロールバック用に保持しています。新バックアップは `.local/backups/cat-ui-20260908.ggu78qoh/`（ディレクトリ700、DB dump/環境設定/鍵は600、秘密を含むため共有・commit禁止）。更新前後の15テーブル全内容ダイジェスト一致、readiness、匿名collection APIの401、配信HTML/JS/CSS/4画像のSHA-256一致を確認済みです。DB/メールコンテナは更新していません。公開・commit・pushはしていません。

実配信での最終確認も成功: `PET_BROWSER_ORIGIN=http://localhost:5097 python3 tests/browser-pet-play.py` の266チェック（APIは全てモック、実データ不使用）。記録は `/private/tmp/task-pet-browser-m0dfu79k`、主要画像は `docs/screenshots/cat-integrated-*-v13.png`。配信CSP下で新しい画像・CSSクリップ・操作が動作し、取得品の見本とスマホ表示も目視確認しました。

以下は以前の変更時点の記録です。現在の稼働イメージと直近のバックアップは上段を参照してください。

2026-09-08 ペットの楽しみ4項目: `PetCollectionService` / `PetCollectionController`、`frontend/pet-play.js` / `pet-play.css` を追加しました。6種類×6表情×4成長段階の144差分、Lv.1〜20で各レベル3候補から1点、帽子・リボン・クッションの3装備枠、Lv.5/10/20の任意の成長姿、なでる/おやつ/休憩、10種の思い出と取得品のアルバムです。新しいメイン欄は追加せず、既存の相棒を押す導線と「設定 → ペット」の4タブへ収めました。名前・種類は同画面の折りたたみです。

ごほうびの選択はペット×レベルの主キーで一度だけ保存。同じ品の再送は200、他の品への変更は409です。タスク更新・アカウント削除と同じadvisory lock/トランザクションを使用。完了取り消しで条件未満になった装備・成長姿だけを外し、再到達時は自動再装備せず、取得品を増やしません。思い出は一度記録すると残ります。初回完了直後にGETなしで取り消しても初回の思い出を残すため、新規ペットIDの確定後も同じタスクトランザクション内で記録します。触れ合いは経験値・元気度・連続日数に影響しません。過去データの思い出は現在の累計から初回アクセス時に補完し、日付は実達成日の推測ではなく「記録された日」です。Lv.21以降の追加ごほうび、未選択品の交換、連続フレームのアニメーションは未実装です。

Migration `20260907195957_AddPetCollections` は既存テーブルを変更せず `pet_collections` / `pet_reward_choices` / `pet_memories` の3テーブルを追加します。全てペット削除にCASCADEし、装備レベル・stage・choiceにCHECKがあります。既存画像は残し、6枚の新規アトラスへ切り替えています。imagegenスキルと組み込み生成ツールで制作し、背景の不良生成は修正。新アトラスは淡いアイボリー背景で、透過素材ではありません。実際の行間隔に合わせたCSS切り出し、帽子等はコード描画です。詳細な生成プロンプトは `frontend/assets/pet/COLLECTION_ASSET_NOTES.md`。

最終確認: .NET 10/Dockerのバックエンド176件、既存フロント86件、分離Chromeの232チェック（3幅・classic/retro/dark・4タブ、全種/全成長/全表情、取消、再試行、遅延応答破棄、キーボード、reduced-motion、ペット枠の内包）成功。実PostgreSQLと専用SMTPで85 APIリクエストを通し、同時3候補受取の1成功/2競合、3装備枠、初回完了、Lv.5→4→5、ユーザー分離、退会時の新3テーブルCASCADEを確認。テストアカウント・関連メールは削除済みで、複製元の既存11テーブルの内容も一致しました。ブラウザー画像は `docs/screenshots/pet-*-v12.png`（専用表示データ）。ブラウザー検証はMac/Chrome用 `tests/browser-pet-play.py`、CIには `tests/pet-collection-smoke.mjs` と配信素材の確認を追加しています。CI自体のリモート実行はしていません。

ローカルプレビュー `http://localhost:5097/` は `task-board:pet-collection-final` へ更新し、既存DB・環境設定・`task-board-preview-keys` を引き継いでいます。移行のリハーサルと実適用の両方で既存11テーブルの件数・全行ダイジェスト一致を確認しました。更新前バックアップは `.local/backups/pets-20260908/`（700、dumpは600、Git/Docker対象外）に保持。鍵は同じボリュームを継続利用しています。readinessと匿名collection APIの401を確認済み。公開・commit・pushはしていません。

以下は以前の変更時点の検証記録です。最新の稼働イメージとバックアップは上段を参照してください。

配信の最終確認: 最初のpublish検証で新アトラスがContent対象外、非犬画像がDocker除外パターンに一致していたため、`TaskApi.csproj` と `.dockerignore` を修正しました。Dockerfileでもpublish後に6アトラスの存在を検査します。修正版に更新後、HTML/JS/CSS/6画像の計11点の配信SHA-256一致と、`PET_BROWSER_ORIGIN=http://localhost:5097 python3 tests/browser-pet-play.py` による実配信/CSP下の232チェックを確認（APIは全てモック応答で実データを不使用）。検証用の複製DB・停止コンテナ・DB内の一時dumpは削除済み。稼働中のプレビュー3コンテナ、データ・鍵のボリューム、更新前イメージ、ローカルdumpバックアップは保持しています。

2026-09-08 設定とタグ追加の分かりやすさ: メインの `open-team-hub-button` を削除し、「設定 → タグ・チーム」に作成・参加を集約しました。左のタグプルダウン末尾 `action:add-tag` は絞り込みではなく設定の追加フォームへの移動です。選択中のフィルターとscopeを保ち、追加先・単一名の入力例・未登録時の案内を表示します。スマホではフォーム全体を表示内へスクロールし、閉じると元のプルダウンへ戻ります。モーダルのフォーカス復帰は即時にし、後続フレームで別画面からフォーカスを奪わないようにしました。バックエンド165件・フロント86件・Docker build成功。Chromeモック17画面（PC/375px・classic/retro、320px設定）で新導線、重複/失敗/再試行、保存中ガード、絞り込み維持、チーム参加/タグ分離、Escとフォーカスを確認。広範な画面撮影は途中でタイムアウトしたため、新しいブラウザー環境で今回の変更を重点確認しています。画像は `docs/screenshots/settings-*-v11.png`。DB/API/Migration変更なし。既存DB・鍵・環境設定を引き継いでアプリのみ更新し、readiness、匿名APIの401、配信6ファイルの一致を確認しました。

2026-09-08 個人／チーム名タブ: ボード上部に `workspace-tabs` を追加しました。個人は「非公開」、所属チームは実際の名前と「共有」を表示します。設定のワークスペース選択とも同期し、作成／参加で追加、退出／削除で除去します。タスク・タグ・検索・ページ・旧件数を切り替え時にクリアし、旧スコープや旧所属一覧の遅延応答は破棄します。取得失敗時は確認済みのチーム名と再試行案内を維持し、所属不明の現行チームを個人と誤表示しません。矢印／Home／Endはフォーカス移動、Enter／Spaceで決定。複数チームは横スクロール、長い名前は省略表示とtitleです。バックエンド165件・フロント80件・Docker build成功。Chromeモック101画面で既存導線の回帰、6テーマ・PC/375px、実キー／ポインター、複数チームの分離、遅延応答、一覧取得失敗／再試行を確認しました。画像は `docs/screenshots/workspace-tabs-*-v10.png`。DB/API/Migration変更なし。 ローカルアプリのみ既存設定・DB・鍵を引き継いで更新し、readiness、匿名APIの401、配信4ファイルのソース一致を確認しました。

2026-09-08 TODOの追加ボタン移動: 左側「Task Board」の＋を撤去し、同じ `open-create-task-button` をTODO見出し右上（検索の左）へ移動しました。追加ロジック、タグ初期値、個人／共有スコープは変更していません。スマホの追加・検索操作領域は40px以上です。集中レイアウトでTODOが隠れる場合は既存の「ボード表示に戻す」から追加します。バックエンド165件・フロント70件、Docker build成功。Chromeモック60画面（6テーマ×5レイアウト×PC1440px/375px）で配置、実ポインタークリック、Esc後のフォーカス復帰、TODO初期値、タグ継承、共有タスク作成を確認。画像は `docs/screenshots/app/v9/todo-add-desktop-v9.png`。DB/API/Migration変更なし、ローカルアプリだけ既存設定・DB・鍵を引き継いで更新し、readinessと配信3ファイルの一致を確認しました。

2026-09-08 左側タグ絞り込み・チーム導線: バックエンド165件・フロント69件、非root Docker build成功。左側 `sidebar-tag-filter` は設定の分類選択と同じ状態・全件集計を使い、個人→共有で候補を即時破棄、取得失敗時も全件表示へ戻せます。`open-team-hub-button` から作成を直接開き、作成／コードで参加／管理を別パネルにしています。作成・参加後は専用の完了画面から共有ボードへ移動します。招待コードのコピーはユーザーのクリック時だけ行い、許可されなければ手動選択へ切り替えます。コードはダイアログを閉じると消去し、保存・自動送信しません。設定にも作成・参加の個別ボタンと選択中チームの管理入口を残しました。作成成功後の一覧取得失敗でも、APIが返したチーム名と選択肢を維持します。

Chromeモック83画面（前回59画面の回帰＋PC/375px・classic/retroで入口／作成／参加／招待／管理／参加完了の24画面）を確認。エラーなし・横はみ出しなし、作成失敗と再試行・多重送信防止・コピー成功と拒否・有効／無効招待・所有者／一般メンバー表示・退出・フォーカス復帰を検証しました。ClipboardとAPIはテスト用の置き換えで、既存プレビューのユーザーに対する作成・参加・招待・削除は行っていません。画像は `docs/screenshots/*-v8.png`、招待コードは利用できないテスト値です。今回DB/API/認証/Migrationは変更していません。既存DB・ログイン鍵・設定を引き継いでアプリのみ更新し、readiness、チームAPIの未認証401、配信6ファイルとテスト済みファイルの一致を確認しました。

2026-09-08 設定集約・プルダウン化: バックエンド165件・フロント59件、非root Docker build成功。設定タブは「表示 / タスク / ペット / アカウント」です。「タスク」に既存のワークスペース選択・更新・チーム管理・全件進捗・タグ登録／絞り込みを移動しました。作成／編集は閉じたチェック式プルダウンから複数選択でき、既存CSVと300文字上限は維持します。チーム画面は設定と重ねずに遷移し、閉じると同じタブへ戻ります。Escはプルダウン→モーダルの順に閉じます。320pxでは設定タブを2段にします。

Chromeモック59画面（PC1440px/375px・4テーマ×5レイアウト40画面、設定8、タグ選択8、編集2、320px設定1）で横はみ出し・JSエラーなし。複数タグの送信値、タグ登録、空分類、外側クリック、Esc、チームからのフォーカス復帰、遅い個人応答と共有候補の分離も確認。画像は `docs/screenshots/*-v7.png`。ブラウザー試験はモックAPIで行い、既存プレビューのユーザーデータは操作していません。今回DB/API/認証の変更やMigrationはありません。ローカルプレビューは既存DBと鍵を引き継いでアプリだけ更新し、readiness、未認証401、配信HTML/JS/CSSの6ファイル一致を確認しました。

2026-09-08 分類タグ・Windows風復元: バックエンド165件・フロント55件、非root runtime buildが成功。`TaskTagService` / `TaskTagsController` がscope別の登録と全件集計、`frontend/task-tags.js` が分類バー・候補の複数選択・非同期scope保護を担当します。既存CSVタグは変更せず統合し、登録済み空分類も保持。新規登録は1〜50文字・50個まで、カンマ/制御文字は不可。今回登録名の変更・削除は追加していません。Windows風は `retro` / Lv1、他の5テーマは `data-ui=modern` のシンプル配置を使用します。

実PostgreSQLでIdentity→チーム→報酬→タグのMigration3試験、タグ移行前後の既存6テーブル全列ハッシュ保持と制約を確認。確定runtimeの実PG＋SMTPスモーク78リクエストでは複数タグ/完全一致/未分類/ページング/個人・チーム分離/6並行登録/退会cleanup/Windows風設定を確認しました。Chromeモックでは分類44ケース、Windows風24ケースが成功し、PC1440px・375pxの横はみ出し/JSエラーなし。実データを使ったブラウザー認証試験とは区別してください。画像は `docs/screenshots/tags-*-v6.png` と `retro-*-v6.png`、テスト合成データは削除済みです。

以下は前段の表示簡略化・経験値追加時点の記録です。

2026-09-08 クラシック簡略化: 青空背景と青/グレーの配色を残し、Windows風のタイトルバー・ベベル枠を共通のサイドバー・カード・設定画面へ統一しました。`options.css` / `unlocks.css` を全テーマ共通のスコープへ変更し、CSSキャッシュ版を更新しています。バックエンド140件・フロント37件とruntime build成功。ChromeのモックAPIで3テーマ×5レイアウト×PC/375pxの30画面＋設定6画面を確認し、横はみ出し・配置差・行高・編集ボタン位置に問題なし。画像は `docs/screenshots/classic-simple-*-v5.png`。今回DB/認証/報酬/保存設定は変更せず、DB移行も行っていません。プレビューの配信HTML/CSSはソースとの一致、readinessはreadyを確認しました。

2026-09-08 完了取消・解放追加の最終ビルド: バックエンド140件・フロント35件、失敗・スキップなし。実PostgreSQL＋Mailpitで元の受取人への控除、6並行取消の1成功/5競合、退出後の控除、再完了と重複防止、100EXP/Lv2・250EXP/Lv3の解放と再ロックを確認しました。新旧混在の活動履歴を含む移行テストも成功しています。

`CompletionReward` が現在の完了報酬の受取人と元気の実増分を保持し、`PetService` が取消時の再計算を行います。`ProgressionCatalog` がサーバー側の解放判定を担当し、`GET /api/user/unlocks` で画面へ返します。未解放の保存は403、レベル低下時は該当する選択だけ犬/classic/boardへ戻します。`frontend/unlocks.css` が森・夕焼けとコンパクト・カード一覧・集中表示を追加します。きつね/パンダ/ドラゴンはimagegenによる新規透過画像で、素材と生成プロンプトは [ASSET_NOTES.md](../frontend/assets/pet/ASSET_NOTES.md) に記録しています。旧履歴の補正・保守的に残す値・Down制限は [DEPLOYMENT.md](DEPLOYMENT.md) を参照してください。

確定イメージ `task-board:progression-final` でIdentity・Team・Progressionの実SMTPスモーク3本を再実行し、すべて成功しました。Progressionは89 APIリクエストです。実ChromeでもTODO→DOINGの加算なし、75→100→75EXP、きつね解放・再ロック、取消通知後に過去の獲得文言が再表示されないことを確認しました。高Lv7の表示検証だけは専用データでseedし、追加テーマ・レイアウト保存とreload保持、375pxの横はみ出しなしを確認しています。画像は `docs/screenshots/progression-*-v4.png`。全合成ユーザーと関連データは後片付け済みです。

作業ホストの動作確認用URLは `http://localhost:5097/`、受信箱は `http://localhost:8027/`。`task-board-preview`（`task-board:settings-discovery-final`）/ `task-board-preview-db` / `task-board-preview-mail` を起動したままにしています。DB複製で予行し、旧アプリ停止・直前バックアップ後にタグMigrationへ更新しました。ユーザー・タスク・ペット・チーム・所属・報酬の全既存列ハッシュが移行前後で一致しています。現在のDB/ログイン鍵バックアップは `.local/backups/tags-20260908.2mQ5so/`（Git/Docker対象外、ディレクトリ700・ファイル600）です。機密を含むため共有・commitしないでください。以前のprogression/teamsバックアップも保持しています。元の `taskapi-postgres` とそのボリュームには触れていません。

以下は前段のチーム・設定追加時点の検証記録です。

2026-09-08 チーム・設定追加の検証: .NET 10/Dockerでバックエンド110件、フロント26件（海外タイムゾーンのJST期限テスト含む）、非root runtime buildが成功。旧データ→最新Migrationの保持、共有作成者SET NULL・管理者退会RESTRICT・個人孤立防止CHECKを実PostgreSQLで検証しました。3つの実SMTP確認済みアカウントで、ルートの分離・非メンバー拒否・6並行完了の1成功/5競合・報酬25EXP・所有権移譲・退出・退会後の共有保持を確認済みです。

`frontend/options.js` / `options.css` が設定・チームUI、`TeamService` / `WorkspaceWriteScope` が所属と共有タスク更新の保護を担当します。表示設定と表示名の更新でもIdentityのConcurrencyStampを変更し、同時ログインに設定を上書きされないようにしています。設定/退会の競合は409です。猫・兎は新規透過画像1枚ずつ、犬は既存6状態。素材と生成プロンプトは `ASSET_NOTES.md` に記録しています。

確定runtimeで実SMTPの登録・メール確認・パスワード再設定・Cookie失効・タスク保持・退会も再確認済み。実Chrome+実APIでテーマ/一覧/ペット設定の保存とreload保持、チーム作成、3状態のタスクと全件進捗、個人タスクとの双方向分離を確認しました。画面は `docs/screenshots/*-v3.png` に保存し、合成アカウントと関連データは後片付け済みです。

前回のチーム移行時も既存の2ユーザー・1タスク・1ペットを全既存列のハッシュ比較で保持確認しました。当時のDBとログイン鍵のバックアップは `.local/backups/teams-20260908.swuulS/` に保持しています。現在の稼働イメージと直近のバックアップは上段を参照してください。

以下は前段のIdentity実装時点の検証記録です。

認証の変更に伴いバックエンド・フロントエンドのテストを追加しています。件数や最新の成功状況はテスト出力とCIログで確認してください。HTTP統合テストとPostgreSQL固有のMigration・制約・競合試験を区別します。今回の実DB検証は独立したテストDBを使用します。

2026-09-07のローカル結果は、Dockerの.NET 10でバックエンド68件成功、runtimeイメージbuild成功、公開設定不足時の起動拒否確認済みです。実PostgreSQLで旧ユーザーのID・パスワードハッシュ・タスク・ペット保持、規約日時の未同意初期化、一意制約・関連削除も確認しています。CIには `tests/identity-migration.sql` による旧DB→新DB検証と、Mailpit・`tests/identity-smoke.mjs` によるSMTPフローを追加していますが、GitHubへ未pushのためリモートCIの成功は未確認です。

追加の最終検証では、バックエンド68件・フロントエンド14件が失敗・スキップなしで成功しました。実PostgreSQLとローカルMailpitで、新規登録→確認メール受信→確認→ログイン→タスク作成→再設定メール受信→パスワード再設定→旧Cookie拒否→新パスワードでのログイン→タスク保持→退会まで成功しています。これは外部メール事業者での到達試験ではありません。

また実Chromeで、移行済みの合成ユーザーが旧パスワードでログインし、メール登録・規約確認・メール確認・再ログインを経て、既存のタスクとペット（Lv3・42EXP・完了5件）を表示できることを確認しました。PC幅1280px・モバイル幅375pxで横方向のはみ出しはありません。検証用のデータであり、既存の利用者DBには適用していません。

公開前に必要なこと:

1. 本番ホスト・PostgreSQL・SMTP事業者を選び、SecretとHTTPSを設定する。
2. Data Protection鍵の永続領域、アクセス制限、保管時暗号化、共有・バックアップ・復元を設定する。
3. 実際の運営者名・連絡先・委託先・ログ/バックアップ保存方針を設定し、文書を実態に合わせて確認する。
4. 送信元とDNS設定を整え、確認・再設定メールを実際の受信箱で試す。
5. 既存DBがあればバックアップと停止時間を確保して移行する。新旧アプリの同時稼働は行わない。
6. PC・スマートフォン実機、キーボード操作、公開URLでの主要機能・セキュリティ試験を行う。
7. ソース公開時はライセンス方針・コミット情報・旧画像を含む履歴の公開範囲を判断する。履歴の書き換えは今回行わない。

手順と公式参考資料は [DEPLOYMENT.md](DEPLOYMENT.md)、完了判定は [PORTFOLIO_RELEASE_CHECKLIST.md](PORTFOLIO_RELEASE_CHECKLIST.md) を参照してください。
