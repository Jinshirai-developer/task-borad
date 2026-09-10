Task Board for Windows — 0.1.0

1. ZIPをすべて展開してください。
2. TaskBoard.Windows.exeをダブルクリックして起動します。
3. サーバーが未設定の場合は「接続せずに試す」で画面を操作できます。
4. 実際のアカウントを使う場合は、Task BoardサーバーのHTTPS URLを入力します。

このZIPには.NETの実行環境を含めています。Microsoft Edge WebView2 Runtimeが
未インストールの場合は、起動画面の「WebView2を入手する」から導入してください。

Googleログインは通常のブラウザーで行います。Windowsアプリとブラウザーの
確認コードが同じことを確認して承認すると、アプリへログインできます。

・サーバーとPostgreSQLはこのZIPに含まれていません。
・お試しモードの操作は一時的なもので、再読み込みするとリセットされます。
・localhostは、そのWindows PC自身を指します。別のPCにあるサーバーのURLには使えません。
・接続設定とWebView2のデータは %LOCALAPPDATA%\TaskBoard に保存されます。
・この初版はコード署名・自動更新・専用インストーラーには未対応です。
・削除する場合はアプリを終了して展開フォルダーを削除してください。
  設定も消す場合は、上記のTaskBoardフォルダーを削除してください。

詳しい起動方法と検証範囲: リポジトリの desktop/README.md
