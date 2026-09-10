# Task Board ポートフォリオ配布サイト

既存フロントエンドの登録不要デモを再利用し、紹介ページとWindows版ZIPの配布を提供します。実アカウントを保存するASP.NET Core / PostgreSQLサーバーとは別のサイトです。

## 配布範囲

- `release.json` の推測しにくい `base` URLを伝えて案内します。ルートや異なるパスは404です。
- 全レスポンスに検索除外とリファラー非送信を設定します。URLが転送された場合、受け取った人もアクセスできます。本人確認によるアクセス制限ではありません。
- デモは必ず `?demo=1` で開きます。実APIへの書き込み、実ログイン、招待送信、決済は提供しません。
- 配布ファイルはSites管理のR2に保存し、ZIPをストリーミング返却します。Drive画面、ログイン、他サイトへの移動は不要です。

## ソースとビルド

アプリの正本は `Jinshirai-developer/task-borad` です。`portfolio`、`frontend`、`docs/screenshots/app/v22/board-demo.png` を専用Sitesチェックアウトへ同期し、そちらには配布サイトの必要なソースのみを保存します。.NETサーバー、実データ、資格情報は同期しません。

Node.js 22以降で実行します。追加のnpm依存関係はありません。

```sh
cd portfolio
npm test
npm run build
npm run dev
```

`TASKBOARD_WINDOWS_ZIP` で検証済みZIPの絶対パスを指定できます。既定はリポジトリ内 `.local/windows-0.1.0/` です。ビルドはZIP全体のSHA-256を検証し、WorkerとSHA-256付きの配布オブジェクトを生成します。公開画面のHTML/CSS/JavaScriptはWorkerへまとめ、画像と16MiBずつのZIP断片はR2へ保存します。HTTP Rangeによる途中再開に対応します。

アップロードは登録済みSHA-256とサイズに一致するオブジェクトだけを受け付けます。Sitesの一時的な秘密値 `RELEASE_UPLOAD_TOKEN` と期限 `RELEASE_UPLOAD_EXPIRES` の両方が必要です。完了マーカーは全オブジェクトのアップロード・検証後に保存します。配布前に秘密値を削除して再デプロイし、アップロード経路を無効化します。

`dist` と `uploads` は生成物です。Siteの登録先は `.openai/hosting.json` を再利用し、作り直しません。
