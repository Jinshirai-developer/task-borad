# Deployment Guide

無料のポートフォリオデモ向けの公開手順です。フロントエンドとAPIは同一originで配信し、HTTPSを終端できるリバースプロキシまたはマネージドコンテナ基盤の背後に置きます。コンテナの8080番ポート、PostgreSQL、Mailpitを直接インターネットへ公開しないでください。

v17のStripe連携はテスト専用・初期無効です。実際の請求を有効にする設定はありません。Sandboxの準備、固定価格、秘密設定、Webhook転送、未実施の外部決済試験は [BILLING_V17_WORK.md](BILLING_V17_WORK.md) を参照してください。登録不要デモの疑似購入はStripe接続試験ではありません。

## 必須設定

[.env.example](../.env.example) に設定項目を掲載しています。ASP.NET Coreは `.env` を自動では読み込みません。環境変数・Secretとして注入するか、コンテナの `--env-file` を使用してください。接続文字列を含むファイルをシェルで `source` する手順は想定していません。

| 設定 | 内容 |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | 公開環境は `Production` |
| `AllowedHosts` | 実際の公開ホスト名 |
| `ConnectionStrings__DefaultConnection` | PostgreSQL接続文字列。Secret管理 |
| `Authentication__PublicBaseUrl` | 実際のHTTPS origin。パス・query・fragment・ユーザー情報なし |
| `Authentication__KeyRingPath` | アプリから読み書きできる保護された永続鍵ディレクトリ |
| `Email__Host` / `Port` / `Security` | SMTP接続先。公開環境は `StartTls` または `SslOnConnect` |
| `Email__Username` / `Password` | SMTP認証情報。利用先の要件に従いSecret管理 |
| `Email__FromAddress` / `FromName` | 配信に利用する確認済み送信元 |
| `Legal__OperatorName` | 実際の運営者名。内部設定として保持し、公開APIには返さない |
| `Legal__OperatorDisplayName` | 公開するサービス・運営窓口の表示名（例: Task Board（個人運営）） |
| `Legal__ContactEmail` | 実際に受信できる問い合わせ用メール。画面はリンク名で表示するが、APIとmailtoの宛先には含まれる |
| `Legal__HostingProvider` / `EmailProvider` | 実際の利用先・必要に応じ保存/処理地域 |
| `Legal__LogRetention` / `BackupRetention` | 実際の保存期間と削除・更新方針 |
| `Registration__Enabled` / `MaxUsers` | 登録受付と総数上限（初期値100） |

`Development` / `Testing` 以外では、HTTPS origin、鍵の保存先、暗号化SMTP、Legalの必須項目等が不足すると起動を拒否します。メールリンクのoriginはリクエストの `Host` ではなく `Authentication__PublicBaseUrl` から生成します。

`GET /api/auth/config` の `publicReleaseReady` は、運営情報の項目が埋まっているという機械的な状態です。法的承認、設定内容の真偽、実配送、公開試験の合格を保証しません。文書と実際の運用を公開前に照合してください。

公開APIの `operatorName` は `Legal__OperatorDisplayName` を返します。表示名の未設定時に実名へ戻す処理はありません。規約では、個人運営であることと、運営者の氏名・住所を問い合わせに応じて遅滞なく案内する方法を記載しています。実際に案内できる問い合わせ窓口を運用してください。[個人情報保護委員会の説明](https://www.ppc.go.jp/personalinfo/legal/guidelines_tsusoku/)を参照し、表示方法と運用を一致させてください。

メールアドレスのリンク表示は非公開化ではありません。API・HTMLのmailto宛先・実メールの差出人からアドレスを確認できます。アドレス自体を非公開にする場合は、有効な専用の問い合わせ・送信元とGoogle側のサポート連絡先も別途整える必要があります。

## Cookie・CSRF・鍵の運用

- 公開環境の認証Cookieは `__Host-TaskBoard.Auth`、HttpOnly / Secure / SameSite=Lax / Path=/。発行から8時間有効で、自動延長しません。
- 永続ログインはありません。ただしブラウザー終了時に必ずCookieが削除されるとは限らないため、共有端末では明示的にログアウトします。
- ブラウザー向け更新系APIで、`GET /api/auth/csrf` の `token` と対応Cookieを取得し、`X-CSRF-TOKEN` を付けます。登録・ログインも例外ではありません。認証状態変更後は再取得します。Stripe専用WebhookだけはCookie/CSRFではなく、公式SDKによる未加工本文の署名・時刻・テストモード検証を使います。
- ログアウトは `SessionVersion` を更新し、そのユーザーの全Cookieセッションを失効させます。送信済みの確認・再設定リンクはログアウトだけでは失効しません。
- メール確認・パスワード再設定はSecurityStampも更新します。以前のCookieや古いリンクは使用できず、再ログインが必要です。
- Data Protection鍵はCookie、メールトークン、送信キュー暗号文の保護に使います。鍵の紛失で既存セッション・リンクが無効になり、送信待ちの内容も復号できなくなります。
- `Authentication__KeyRingPath` は使い捨てのコンテナ層ではなく永続ボリュームにします。複数インスタンスは同じ鍵領域とアプリ名を共有します（コードのアプリ名は `TaskBoard.Identity.v1`）。
- 実行ユーザーだけに必要な読み書き権限を与え、保管時の暗号化とバックアップを構成します。現在のコードはファイル保存を指定するだけで、鍵ファイルの暗号化を自動構成しません。暗号化済みストレージ、または環境に合った鍵保護の追加が必要です。
- DBと鍵の復元を組み合わせて試験します。鍵ファイル・Cookie・パスワード・メール内リンクをログやリポジトリへ記録しません。

Live Serverや別originフロントエンド向けのCORSは提供していません。

## メール配信

ローカルでは `docker compose up -d` でPostgreSQLとMailpitを起動します。MailpitはSMTP `localhost:1025`、受信箱UI `http://localhost:8025/`。開発用のテストメールを捕捉し、実際の宛先には配信しません。

本番SMTPは利用先を決定してから設定します。送信元ドメインの確認、SPF / DKIM / DMARCなど利用先が求める設定を行い、実際の受信箱で到達・迷惑メール判定・問い合わせ導線を確認してください。今回、本番メール事業者の契約・DNS変更・実配送は行っていません。

送信処理の仕様:

- 確認リンクは24時間、再設定リンクは30分有効。秘密情報はURLフラグメントに含め、画面で読み取り後にURLから除去します。
- 宛先・本文等はData Protectionで暗号化して `email_outbox` に保管します。各ユーザー・用途につき1件で、新しい予約は置き換えます。
- ワーカーは通常5秒間隔で待機し、送信成功後は行を削除します。失敗時は間隔を延ばし、合計5回まで送信を試みます。
- 5回失敗した行も永続保存しません。確認メールは最長24時間、再設定メールは30分の期限が過ぎた後のワーカー処理で削除します。ワーカー停止中の経過時間は即時削除を意味しません。
- 複数ワーカーはPostgreSQLの行ロックで同時送信を避けます。ただしSMTP受理後・DB保存前のクラッシュ等による重複配送を完全には排除できません。
- 再送・再設定依頼は共通の `202` 応答で、同一ユーザーへの予約は1分間隔に制限します。認証系APIにはIP単位のレート制限もあります。
- 失敗ログにはキューID・回数・例外型を記録し、宛先や本文を含むSMTP例外メッセージは出力しません。配信失敗とキュー滞留を監視します。

## Identityへの移行

対象Migrationは `20260907135907_AddIdentityAndEmailLifecycle` です。今回の検証は独立したテストDBで行い、既存本番DBへの適用を意味しません。

1. 旧イメージdigest、現在のMigration ID、設定を記録し、DBのバックアップと復元手順を確認する。
2. 既存DBの複製で移行し、ユーザーID・タスク・ペットの件数と関連を確認する。
3. 短いメンテナンス時間を確保し、旧アプリとワーカーを停止する。**新旧アプリを同じDBで同時稼働させない。**
4. .NET 10 SDKとEF CLIのある専用jobからMigrationを適用する。
5. 永続鍵・SMTP・Legal設定を備えた新イメージを起動し、readinessと主要フローを確認する。
6. 全利用者へ再ログインを案内する。旧ユーザーは追加設定画面でメールを登録し、規約・メール確認を完了する。

```bash
dotnet tool restore
dotnet ef database update --configuration Release
dotnet ef migrations has-pending-model-changes --configuration Release
```

実行jobには対象DB接続と必要なアプリ設定を明示し、誤ったDBへ向かないことを確認してください。ローカルでは `ASPNETCORE_ENVIRONMENT=Development` を指定します。

`UserProfile` は `IdentityUser<int>` を継承し、既存 `user_profiles.id` とタスク・ペットの外部キーを維持します。メールを推測で埋めたり、旧ユーザーの同意を既成事実にしたりしません。旧PBKDF2形式はログイン成功時にIdentity形式へ更新します。

旧Bearer用の認証情報はMigrationで廃止します。Downしても以前のBearerトークンは復元できません。移行後にログイン成功したパスワードはIdentity形式へ更新され、旧アプリのPBKDF2専用検証では読み取れません。**Downして旧イメージを起動するだけでは復旧できません。** ロールバックはMigration前のDBバックアップと旧イメージをセットで復元し、移行後の書き込み損失を含む判断と説明を済ませてから実行します。

実行用イメージはSDKとEF CLIを含みません。アプリ起動時の自動Migrationは行いません。

## チーム・設定の移行

`20260907150513_AddTeamsAndPreferences` は個人タスクを個人のまま維持し、表示をclassic/board、既存ペットをdogで初期化します。旧アプリを停止してから適用してください。新モデルでは共有タスクの作成者FKがSET NULLとなるため、旧アプリの退会処理と互換ではありません。

アカウント削除は個人タスクを先に削除し、チーム作成者の関連を外してから退会します。チーム管理者は引き継ぎ/チーム削除が必要です。共有タスクは全メンバーが更新・削除できるため、招待コードの共有先に注意する案内を出します。共有内容・保持ルール変更に伴い文書版は `2026-09-07-teams` となり、既存利用者は再確認します。

チームがあるDBのDownは拒否します。退避データをレビューしたうえで移行前バックアップから復元してください。テーマ・種類の変更もDownでは失われます。小規模デモ向けにワークスペース書き込みをPG advisory lockで直列化しており、大規模運用時はロック粒度・負荷の見直しが必要です。

## 完了取消・レベル解放の移行

`20260907160508_AddReversibleRewardsAndUnlocks` は報酬履歴 `completion_rewards` を追加します。DBとログイン鍵をバックアップし、複製DBで予行してから旧アプリを停止して適用してください。旧アプリは受取人履歴や取消処理を書かないため、新旧の同時稼働はできません。

- DONEで25EXP、DONEからTODO/DOINGへ戻すと元の受取人から25EXPを控除します。再完了ではその時の完了者に付与し、TODOとDOING間の移動では変わりません。状態・報酬・解放設定は同じトランザクションで保存します。
- 旧個人タスクは所有者を既知の受取人として移行します。既にTODO/DOINGへ戻っている報酬は取り消し、総経験値・レベル・完了数を補正します。旧共有タスクは受取人の記録がないためNULLとし、作成者や操作した人を推測して控除しません。
- 過去の元気の実増分は不明なので履歴の `energy_granted` は0で移行し、以前の元気を推測で差し引きません。移行後の報酬は上限適用後の実増分を記録して取り消します。
- 削除済みタスク等による不明な活動履歴は `legacy_last_completed_at` / `legacy_streak_days` に保守的に残します。現存する旧タスクだけでは過去の連続日数を完全には復元できません。
- タスク・チームの削除は完了取消とは扱わず、報酬履歴のタスク参照を外して獲得済みEXPを保持します。退会者の受取人参照は外し、後日の取消でアカウントやペットを復活させません。
- 解放は現在レベルで判定します。取消で必要レベルを下回った選択は犬 / classic / boardへ戻し、ペット名は保持します。再解放しても以前の選択へは自動で戻しません。

報酬履歴または追加の種類・テーマ・レイアウトを使用しているDBではDownを拒否します。ロールバックは移行前バックアップと旧イメージの組み合わせで行います。`tests/progression-migration.sql` は専用の `identitycheck` DBでのみ使用する移行テストです。利用者DBでseedを実行しないでください。

## 分類タグ・Windows風テーマの移行

`20260907172910_AddTaskTagsAndRetroTheme` は個人/チームごとの `task_tag_definitions` と、themeの `retro` 許可を追加します。既存のユーザー・タスク・CSVタグ・ペット・報酬は書き換えません。現存するCSVタグと登録定義を読み取り時に統合するため、既存タグを新規登録扱いにしたり、登録上限を理由に捨てたりしません。

DBとログイン鍵をバックアップし、複製で予行したうえで旧アプリを停止して適用してください。タグ定義は個人/チームのいずれか一方に属し、所有者/チーム削除時に一緒に消えます。チームから退出した利用者はチームのタグも取得・追加できません。登録と上限判定は既存のワークスペース書き込みロック内で実行します。

登録済みタグまたはWindows風の選択があるDBではDownを拒否します。ロールバックには移行前のDB・鍵・旧イメージを使用してください。`tests/task-tags-migration.sql` は `identitycheck` 専用で、移行前後の6テーブル全列ハッシュと制約を検証します。分類の完全一致は最大500件のワークスペース内でトークンを解析してからページングします。大規模化ではタグの正規化テーブル化や集計方法の再設計が必要です。

## リバースプロキシ・通信

- 公開入口でHTTPをHTTPSへ移動し、HTTPS応答のHSTSを確認する。
- PostgreSQLにも利用先が指定するTLS設定を使う。接続文字列・SMTP認証情報をリポジトリやイメージへ含めない。
- アプリポートへ到達できる送信元を信頼するプロキシまたは内部ネットワークに限定する。
- レート制限に使う転送ヘッダーは信頼するプロキシからだけ受け付ける。

基盤がアプリへの直接アクセスを遮断し、転送ヘッダーを上書きする構成に限り、次の設定を利用できます。

```text
ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
```

任意の転送ヘッダーを信頼するとIP偽装の原因になります。公開基盤に合わせて信頼済みproxy/networkを明示する方を優先し、HTTPS判定と利用者IPを実測してください。現在のレート制限はインスタンス内メモリ単位なので、複数インスタンスでは入口の共通制限も検討します。

## 登録制限・保持・監視

- 登録数は `Registration__MaxUsers`、緊急の登録停止は `Registration__Enabled=false` で制御する。停止しても既存ユーザーのログイン・復旧・退会は維持する。
- タスクに機密情報・他人の個人情報を入れないよう案内する。メールは機能のために取得するので、利用目的と委託先を公開文書で明示する。
- 退会は稼働中DBのアカウント・個人タスク・ペット・設定・所属・送信待ち情報を削除する。共有タスクは作成者との関連を外してチームに残す。ログやバックアップを同時に消す実装ではない。
- ログとバックアップの保存・削除方針を実際に設定し、Legalの表示と一致させる。未確認・放置アカウントの自動削除jobは今回未実装。必要なら利用者に保持方針を案内して追加する。
- DB容量、登録数、429/503、配信失敗、キュー滞留、鍵領域の容量・権限を監視する。
- Livenessは `GET /health`、readinessは `GET /health/ready`。後者はDB接続と未適用Migrationを確認し、不備があれば503。メールの到達や法的確認は検査しない。

## 公開URLでのスモークテスト

1. HTTP→HTTPS、HSTS、CSP、CookieのSecure / HttpOnly / SameSite、APIのno-storeを確認する。
2. 未認証の `/api/tasks` が302ではなく401になる。CSRFなしの登録・ログインが400になる。
3. 登録→実メール受信→確認→再ログイン→タスクCRUD→完了報酬を確認する。
4. 確認前はタスク・ペットが403になり、再送・追加設定・退会はできる。
5. 再設定の実メール、期限切れ・改ざん・再利用拒否、新パスワード、既存セッション失効を確認する。
6. ログアウト後の全Cookieが無効になり、ログアウト前に送った確認リンクは使えることを確認する。
7. 別アカウントのタスクを取得・変更できず、退会で関連データが消えることを確認する。
8. コンテナ再作成後も鍵が維持され、有効なセッション・リンクが意図せず失効しないことを確認する。
9. 運営情報が正しく表示され、設定済み表示を法的承認と誤解させないことを確認する。
10. パスワード・Cookie・CSRF/メールトークン・接続文字列がアプリ/プロキシ/配信ログに残らないことを確認する。

## 参考資料

- [ASP.NET Core Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-10.0)
- [メール確認とパスワード回復](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/accconfirm?view=aspnetcore-10.0)
- [ASP.NET CoreのCSRF対策](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0)
- [Data Protectionの設定と鍵保護](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0)
- [プロキシと転送ヘッダー](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)
- [個人情報保護委員会：入力画面で直接取得する場合の利用目的明示](https://www.ppc.go.jp/all_faq_index/faq1-q4-17/)
