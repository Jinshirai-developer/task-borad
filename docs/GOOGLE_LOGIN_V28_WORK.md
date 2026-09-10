# Googleログイン v28

2026-09-09。Googleアカウントでのログイン、新規登録、既存アカウントへの連携を追加した。Google Calendar / Gmail / Driveの同期は今回の対象に含めていない。アプリ名は希望名が未確認のためTask Boardを維持した。

## 利用の流れ

- ログイン画面の「Google でログイン」からGoogleのアカウント選択へ進む。
- 連携済みなら既存のTask Boardアカウントへログインする。
- 初回はユーザーID・表示名を入力し、規約とプライバシーポリシーを確認して登録する。Googleの氏名を表示名に自動転記しない。
- 登録済みの利用者は初回画面の「登録済みのアカウントに連携する」を選び、現在のユーザーIDとパスワードを確認する。既存のタスク・育成・チーム・契約は同じアカウントに残る。既存の登録メールを変更・自動確認しない。
- Gmailと確認済みGoogle Workspaceのメール以外では、本アプリの確認メールも必要。Googleで一度確認された外部メールの所有権が現在も同じとは限らないため。
- Googleだけで作成したアカウントも、後日の規約確認とメールによるパスワード設定が可能。

## Google側の設定

2026-09-09にローカル環境へGoogleのクライアントIDとシークレットを設定し、Googleログインを有効化した。以下は別環境で設定するときの手順。未設定の環境ではボタンを表示しない。

1. [Google Cloud Console](https://console.cloud.google.com/auth/overview)でプロジェクトを選択し、Google Auth Platformのアプリ情報・サポート連絡先・対象ユーザーを設定する。アプリ名は公開時に使用する名前にする。
2. クライアントを作成し、種類は「ウェブ アプリケーション」を選ぶ。
3. 承認済みリダイレクトURIに **`http://localhost:5097/signin-google`** を登録する。ホスト名・ポート・パスまで一致させる。`127.0.0.1`や別ポートを混ぜない。
4. テスト用の公開状態では、Google Consoleの対象ユーザー設定も確認する。
5. クライアントID・シークレットを、リポジトリ対象外の秘密管理先へ保存する。チャット・コミット・フロントエンドのJavaScriptには入れない。
6. アプリの実行環境に下記を渡して再起動する。現在のpreviewコンテナには既存のDB接続、認証鍵、Stripeテスト契約の設定があるため、それらを保持して更新する。

```dotenv
Authentication__Google__Enabled=true
Authentication__Google__ClientId=YOUR_CLIENT_ID.apps.googleusercontent.com
Authentication__Google__ClientSecret=YOUR_CLIENT_SECRET
Authentication__PublicBaseUrl=http://localhost:5097
```

上記は設定形式の例であり、実際の認証情報ではない。Google CloudからダウンロードしたクライアントJSONを使う場合も、`.local/`などGit・Dockerビルド対象外の場所に保管する。

現在のv28ローカルpreview用に`python3 scripts/connect-google-preview.py /path/to/client.json --check`でJSONを検査できる。`--check`を外した実行は初回の有効化専用で、既存設定・全行・鍵をバックアップしてGoogleの3設定だけを追加する。有効化済み環境で再実行しない。認証情報は`.local/google-login/client.json`（権限600）にも保管する。秘密値は出力しない。

本番ではHTTPSの公開originと、そのoriginの`/signin-google`を使用する。リバースプロキシ経由では、信頼するプロキシからのscheme/hostがASP.NET Coreへ正しく渡る設定を先に行う。現在は設定済みPublicBaseUrlと実リクエストのoriginが異なるGoogle開始・callbackを拒否する。

## 認証の実装

- Microsoft.AspNetCore.Authentication.Google 10.0.11。標準OAuth authorization code + PKCE、state保護、correlation cookieを使用。サーバー側でコードを交換し、Google UserInfoから本人情報を取得する。
- Googleの`sub`でIdentityのUserLoginsへ紐付ける。メールが同じという理由だけでは既存アカウントへログイン・統合しない。
- 新規登録・連携はCSRF付きPOST。Googleへの移動開始もCSRF付きPOSTで認証URLを取得してからブラウザーを移動する。戻り先は固定のローカルパス。任意のreturnUrlは受け取らない。
- 外部認証の一時cookieは5分・HttpOnly。登録・連携完了とキャンセル時に破棄。アプリ本体のcookieやセッション失効の仕組みを維持する。
- scopeはopenid / email / profileのみ。アクセストークン・更新トークンをDBやブラウザーストレージへ保存しない。Googleのエラー詳細や秘密値を画面へ返さない。
- Googleによる登録も従来と同じ登録停止・上限・PostgreSQL advisory lockに従う。既存のIdentityテーブルを使用し、マイグレーションは追加していない。
- Googleのボタン画像は公式ブランドガイドの未加工画像をローカル配信する。

## 検証

- .NET: 298件成功（Google関連24件を追加）。CSRF、PKCE、state改ざん、異なるブラウザー、期限切れ・キャンセル、Google障害、確認されていないメール、既存メールの衝突、誤パスワード、登録上限、アカウントロック、既存セッションの切り替え防止、正式origin、再ログイン時のsub照合、規約更新、パスワード設定を検証。
- JavaScript: 147件成功。Googleの有効/無効表示、遷移先の検証、初回登録・連携・規約確認を含む。
- Safari: PC、375px、320px幅で表示確認。新規登録と連携の切り替え、フォームの入力要否を確認。表示確認用の合成APIを使用し、実アカウントやGoogleへは接続していない。
- 実Googleアカウントとの往復はOAuthクライアント設定後に実施する。上記HTTPテストは標準Googleミドルウェアを通し、Googleサーバーへの通信部分のみテスト用応答へ差し替えている。

## ローカル反映結果

- 稼働イメージ: `task-board:google-v28`、URL: `http://localhost:5097/`。
- `scripts/update-google-preview.py`により反映。17テーブルの全行ダイジェスト、マイグレーション、環境変数・認証鍵ボリュームを更新前後で一致確認。
- HTML/CSS/JS 35ファイルとGoogleロゴの配信内容が作業ツリーに一致。
- 有効なStripeテスト契約1件を保持。Stripeへの書き込みなし。
- バックアップ: `.local/backups/google-v28-ux1xziwq/`。ロールバック用コンテナ: `task-board-preview-before-google-v28`。
- 初回のコード反映では`googleLoginEnabled`をfalseに保った。その後、利用者が作成したOAuthクライアントのJSONをSafariの「JSON をダウンロード」から取得し、以下の設定反映を行った。

## Google接続設定の反映

- `scripts/connect-google-preview.py`で同じv28イメージへ認証情報を設定した。変更した環境変数はGoogleのEnabled / ClientId / ClientSecretの3件のみ。
- 17テーブル、マイグレーション、認証鍵、Google以外の環境設定、Stripeテスト契約1件を保持。バックアップは`.local/backups/google-connect-v28-ev2eltiv/`、復旧用コンテナは`task-board-preview-before-google-connect-v28`。
- config APIの有効化と、CSRF付きPOSTから正しいクライアントID・リダイレクトURI・PKCE・最小scopeを持つGoogle認証URLが返ることを確認した。外部のGoogle認証へのアクセスはこのHTTP検査では行わない。
- 設定スクリプトの4テスト成功。認証ファイルの不備、別用途のクライアント、環境変数への改行混入を拒否し、既存のDB・契約・鍵設定を保持することを確認。
- 利用者のGoogleアカウントを選択してログインを完了する操作は未実施。実アカウントとの往復完了を確認した状態ではない。
- 実環境のSafariで「Google でログイン」を押し、Googleの「アカウントを選択してください」画面まで到達することを確認。利用者が使用するアカウントを選べるよう、その画面を開いた状態にした。

## 参照先

- [Microsoft: Google external login setup](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/social/google-logins?view=aspnetcore-10.0)
- [Google: OpenID Connect](https://developers.google.com/identity/openid-connect/openid-connect)
- [Google: メールの確認状態とGoogleの権威性](https://developers.google.com/identity/gsi/web/guides/verify-google-id-token)
- [Google: ボタンのブランドガイド](https://developers.google.com/identity/branding-guidelines)
