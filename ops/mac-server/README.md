# Mac上での一時運用

既存のlocalhost:5097とは別のASP.NET Core / PostgreSQL環境です。配布サイトが接続を中継し、登録・ログイン・保存はMac上のAPIで処理します。Windows ZIPと登録不要デモは引き続きSites側にあります。

現在の入口は `https://taskboard-8j6.pages.dev` です。Cloudflare PagesがMacへ中継し、旧Sitesのアプリ入口からも転送します。現在の切り替え・復旧手順は [Cloudflare Pagesの運用手順](../cloudflare-pages/README.md) を参照してください。

## 構成

- `taskboard-mac-app`: APIと実画面。ホストへの公開は127.0.0.1:5099のみ。
- `taskboard-mac-db`: 専用DB。ホストへの公開は127.0.0.1:5439のみ。
- 専用DockerボリュームにDBと認証用キーを保存します。既存の開発用DBは使いません。
- Cloudflare Quick Tunnelを使う一時運用です。トンネルを作り直すと接続先が変わるため、PagesとSitesの接続設定も更新します。長期運用には固定トンネルか常設サーバーへの移行が必要です。
- Macの電源、インターネット、Docker、トンネルが稼働している間に利用できます。再起動後はDockerの起動とトンネルURLを確認してください。

## 設定

秘密値はGit管理外の `.local/mac-server/database.env` と `runtime.env` に保存します。権限はディレクトリ700、ファイル600です。実行環境はProduction、SMTPはTLS、認証キーは永続化します。開発用Mailpitを本番メール送信の代わりには使いません。

配布Workerの `MAC_SERVER_ORIGIN` はトンネルのHTTPS origin、`MAC_SERVER_PROXY_KEY` は秘密値です。APIの `PublicProxy__Secret` に同じ秘密値を設定します。中継は元のCookieやIPヘッダーをそのまま転送せず、アプリの認証CookieとCSRFトークンだけを渡します。直接トンネルにアクセスしても、秘密値なしでは404です。

現在のPages URLを開くと、`/login.html?mode=register` に移動します。短いURLは本人確認の仕組みではありません。API側の認証・メール確認・権限・CSRF検証は別途すべて有効です。確認メールは新originと `Authentication__PublicEntryPath=/` を使います。旧Sitesの長い入口と過去の確認メールも、新originへ転送します。旧Sitesのリンク閲覧用Cookieは移行前のアプリ経路で使用していたものです。

## 起動と停止

設定とマイグレーションを済ませた後、リポジトリ直下で実行します。

```sh
docker compose -f ops/mac-server/compose.yml up -d
docker compose -f ops/mac-server/compose.yml --profile public up -d tunnel
docker compose -f ops/mac-server/compose.yml stop
```

`down -v` は使わないでください。DBと認証キーを削除します。ログは各コンテナ最大10MB×3ファイルでローテーションします。現在、自動バックアップは未設定です。

Googleの承認済みリダイレクトURIは、現在のPagesの固定origin + `/signin-google` を使用します。既存のlocalhost用と旧Sites用のURIも残しています。
