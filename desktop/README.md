# Task Board for Windows

C# / .NET 10 / WPF / Microsoft Edge WebView2によるWindowsクライアントです。Web版と同じ画面・APIを利用し、Windows固有の起動、自動接続、ブラウザーログインを担当します。ZIPを展開して起動する形式です。

## 起動

1. `taskboard-windows-0.1.1-win-x64.zip`をWindows PCで**すべて展開**します。
2. `TaskBoard.Windows.exe`を起動します。
3. 公開サーバーに自動で接続し、登録・ログイン画面を開きます。ログイン済みならタスクボードへ進みます。URLの入力画面はありません。
4. 上部の「お試し」で、サーバーやアカウントなしに同梱デモを操作できます。「オンラインに戻る」で通常の画面へ戻ります。お試しのデータは再読み込みでリセットされます。

ZIPは.NETランタイムを同梱します。WebView2 Evergreen Runtimeがない場合は、起動画面のリンクからMicrosoftの配布ページを開けます。Windows x64向けです。ARM64もビルドスクリプトの`-Runtime win-arm64`で出力できますが、初版の検証対象はx64です。

接続先は`https://taskboard-8j6.pages.dev/`に固定しています。通信に失敗したときは「再試行」と「お試しモードを開く」を表示します。WebView2が未導入の場合はインストールへの導線を表示します。

WebView2プロファイルは`%LOCALAPPDATA%\TaskBoard`に保存します。オンラインとデモのプロファイルを分け、既存の公開サーバー用プロファイルを引き継ぎます。旧版の`settings.json`にある接続先は読み込みません。Windowsの管理者権限は要求しません。

## サーバー側の準備

Windowsの通常ログインは既存のWebログインを利用します。Googleを含む「ブラウザーでログイン」には、このブランチのサーバーと`20260910120638_DesktopBrowserSignIn`マイグレーションが必要です。

```sh
dotnet tool restore
dotnet ef database update
```

上記は接続先のDBにスキーマを追加する操作です。運用先の設定で実行してください。既存のローカルプレビューDBには、この開発作業で自動適用していません。GoogleのClient ID / Client Secret、SMTP、PostgreSQLはサーバー側だけに設定し、Windowsパッケージには含めません。

[ポートフォリオ用の紹介・配布ページ](https://taskboard-8j6.pages.dev/about) からZIPを直接ダウンロードできます。ブラウザ体験版も同じページから利用できます。実アカウント用APIは運営者のMac上で稼働しており、保存・共有にはMacとDockerの起動が必要です。

## ブラウザーでのログイン

1. アプリがサーバーに10分間有効なログイン要求を作成します。32バイトのランダムな秘密コードはアプリのメモリーだけに保持します。
2. 既定のブラウザーでTask Boardの確認ページを開きます。アプリとブラウザーに同じ8文字の公開コードを表示します。
3. 既存のWebログイン（GoogleまたはユーザーID・パスワード）を使います。メール・規約の確認が必要な場合は、既存の手続きを完了します。
4. 利用者がアカウントとコードを確認して承認します。
5. アプリは秘密コードをPOSTして一度だけセッションを受け取り、HttpOnly認証Cookieを接続先専用のWebView2プロファイルに設定します。CookieをJavaScriptや設定ファイルに書き出しません。

DBには両コードのSHA-256ハッシュ、有効期限、承認した利用者とSessionVersionを保存します。承認・消費には既存のDBロックを使い、同時要求での二重利用を防ぎます。承認後のセッション失効、ロックアウト、メール未確認、期限切れ、拒否では引き継げません。匿名のネイティブAPI呼び出しにも既存のCSRF保護を適用しています。

期限切れ要求は次回の開始時に削除し、未消費要求は全体で最大1,000件です。利用者の退会時は関連要求を削除します。アプリ側のキャンセルではポーリングを停止し、未承認要求は期限で無効になります。

Googleの認証は外部ブラウザーを利用する設計です。[Googleのネイティブアプリ向けOAuth説明](https://developers.google.com/identity/protocols/oauth2/native-app)、[MicrosoftのWebView2配布説明](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)も参照してください。

## ビルドと検証

Windowsに.NET 10 SDKを用意して実行します。

```powershell
./scripts/publish-windows.ps1
```

出力先は`artifacts/windows/win-x64/`です。通常の開発起動は次のコマンドです。

```powershell
dotnet run --project desktop/TaskBoard.Windows/TaskBoard.Windows.csproj
```

サーバーのテストは`dotnet test TaskApi.sln`、接続先検証は`dotnet test desktop/TaskBoard.Windows.Core.Tests/TaskBoard.Windows.Core.Tests.csproj`で実行します。フロントエンドの回帰テストは`node --test tests/*.test.cjs`です。

GitHub Actionsの`Windows client`は、Windows上でビルドしたZIPを展開し、そのEXEからWPF・WebView2の初期化と同梱デモの表示を確認します。mainへのpushでは公開サーバーへの自動接続と登録フォームの初期化も確認します。公開サーバー停止中でも開発ビルドは保存しますが、配布更新にはオンライン確認の成功結果も必要です。ZIPと結果JSONは非公開リポジトリのArtifactで14日間保持します。公開Releaseの作成は行いません。

## 初版の範囲

- オフラインでは同梱デモを利用します。実アカウントのオフライン編集・同期は未対応です。
- コード署名、自動更新、専用インストーラー、トレイ常駐、通知は未対応です。
- API・DB・ログインセッションはWeb版と共通です。Windows専用にC#/XAMLで全画面を書き直した構成ではありません。
- WindowsネイティブUIの目視確認と実Googleアカウントでの往復確認は、Windows環境で別途必要です。ビルド成功やデモ起動テストで全機能の動作確認が済んだとは扱いません。
