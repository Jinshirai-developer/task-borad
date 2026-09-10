# Cloudflare Pages URLへの移行

Mac上のAPI・DBを維持したまま、登録画面とWindows配布への入口を `https://<project>.pages.dev` にまとめます。希望名は `taskboard`。名前の空き状況と正式なURLはCloudflareのプロジェクト作成結果で確定します。

## ビルドと確認

```sh
node --test ops/cloudflare-pages/worker.test.mjs portfolio/worker.test.mjs
node ops/cloudflare-pages/build.mjs
```

`.local/cloudflare-pages/dist` をDirect Uploadプロジェクトに配置します。出力に秘密値は含みません。Windows ZIP・登録不要デモ・紹介画面は既存Sites/R2から配信するため、既存プロジェクトとオブジェクトを残します。

## 本番の設定と切り替え順序

1. Cloudflareにログインし、無料のPagesプロジェクトを作成。返された本番originを記録する。
2. Google OAuthの承認済みリダイレクトURIに、新origin + `/signin-google` を追加する。既存のlocalhostとSitesのURIは残す。
3. Pagesの本番環境に `PUBLIC_ORIGIN`（新origin）、`MAC_SERVER_ORIGIN`（現在のトンネルorigin）、秘密値 `MAC_SERVER_PROXY_KEY` を設定する。プレビュー環境には秘密値を設定しない。Workerもoriginを厳密に照合する。
4. Macの `runtime.env` を権限600のままバックアップし、`Authentication__PublicBaseUrl` を新origin、`Authentication__PublicEntryPath` を `/` に変更する。アプリコンテナだけを再作成し、DB・キー・トンネルを維持する。
5. Pagesを本番配置し、登録画面・確認メール・Googleの往復・ログインと保存・Windowsダウンロードを確認する。
6. 既存Sitesの `CANONICAL_APP_ORIGIN` に新originを設定し、旧アプリURLから新URLへ転送する。配布・体験版は既存Sitesで維持する。

旧URLで開いたタブは開き直してログインします。Cookieはドメイン間で移しません。以前の確認メールのURLも、新URLへクエリとフラグメントを維持して転送します。Google認証の途中に切り替えた場合は新URLから再開します。

## 復旧

旧Sitesの `CANONICAL_APP_ORIGIN` を解除し、Macの環境設定を切り替え前のバックアップへ戻してアプリだけを再作成します。Pagesの `PUBLIC_ORIGIN` を解除すると新URLでのアプリ中継は停止します。DB・キーのボリュームは削除しません。

## 運用上の範囲

検索エンジンには全ページをnoindex、robots.txtをDisallowで指示します。短いURLは推測可能で、URLを知る本人だけを識別する仕組みではありません。アプリの認証・メール確認・CSRF・個人/チームの権限はAPIで引き続き検証します。

Macの電源・ネット接続・Docker・トンネルが必要です。Quick Tunnelを作り直した場合は両ゲートウェイの `MAC_SERVER_ORIGIN` を更新します。Cloudflare Pages Functionsの無料枠を利用し、有料プランは有効にしません。
