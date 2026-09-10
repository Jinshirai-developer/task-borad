# 使いやすさ・お試し・透過画像 v15

## 実装範囲

1. 登録不要のお試し: 実API・Cookie・実データと分離したブラウザー内のサンプル。タスク・チーム・ペットを体験でき、リセット可能。
2. 担当者: チームの在籍者のみ選択。退出・退会時に解除。自動更新は画面表示中のみ定期取得し、編集中・移動中・保存中は適用しない。
3. 元に戻す: 移動後の短時間の取消、削除の短時間復元。スコープ・操作者・バージョン・期限を検証し、完了報酬を重複させない。
4. チェックリスト: タスク詳細内で編集。カードは完了数/全件数のみ。項目完了はタスクの完了や経験値と独立。
5. タグ整理: 設定で名前変更・削除・統合。該当スコープの付与済みタグも原子的に更新し、タスク自体は削除しない。
6. 週間振り返り: ペット/設定から任意で表示。日本時間の月曜始まりで今週・先週の有効完了実績を比較。経験値を追加しない。
7. 画像（未完了）: 生成原本90枚を保持した別バージョンの実透過PNGを予定。背景のみのPython処理について確認中。許可後に実透過、輪郭、白い体毛、6種90枚の参照を検証する。

## 安全・検証方針

- 開始前の大量の未コミット差分を維持。無関係な削除の復元・commit・pushは行わない。
- APIの認証・CSRF・所属確認・競合検出を維持。既存XP・取得品・鍵を維持する。
- メイン画面に管理パネルを増やさず、既存の詳細・設定・一時通知を使用。
- サービス/API・JavaScriptテスト、専用ブラウザー、隔離PostgreSQLでMigrationと競合を検証。
- 実データをテストで操作しない。稼働環境の更新前にはバックアップと既存データ比較を実施する。

## 進捗

6機能を実装・反映済み。バックエンド197件、JavaScript99件（既存装備の素材込み）、Python PNG検査4件成功。分離Chromeと反映後の実配信それぞれ73項目成功（お試しAPIを使用、実利用者データ不使用）。320/375/1440px、クラシック/Windows風/ダークで横はみ出し・ブラウザーエラーなし。専用PostgreSQL/Mailpitで66 APIリクエスト、52ステータス/競合検証成功。4並行取消は1成功/3競合、担当者・チェックリスト・30秒サーバーUndo/15秒UI通知・タグ整理・週間集計を確認。専用の3テストアカウントは退会済み、残数0。

Migration `20260908100945_AddTaskUsability` は担当者・チェックリスト列と短命の `task_undo_entries` テーブルを追加。v14からの移行で既存14テーブルの全データハッシュ一致、新列は未割当/空配列を確認。EFのpending model changesなし。CIの移行試験を旧仕様検証→v15 fixture→最新移行→既存データ検証の順に更新し、専用PostgreSQLでIdentity・報酬・タグ・v15の4段階すべて成功。CI設定は未pushのためリモートCI自体は未実行。

画像: imagegenスキルの標準手順で猫Lv.1帽子の背景抽出を1枚試行。内蔵ツールの出力は1254×1254のRGB PNGで、`sips -g hasAlpha` は `no`、目視でも格子背景が残るため不採用。原本90枚を上書きしていません。Pythonによる背景だけの透明化はユーザー確認待ちで、未実施です。

試行画像（未採用・生成元のまま保持）: `/Users/sj/.codex/generated_images/01a07b00-539b-71d0-b7c0-e6fead9a9400/exec-07bfe2ba-09d7-4a53-8ce2-05e9e0b0eef8.png`

試行プロンプト: `background-extraction`。猫Lv.1帽子PNGを編集対象とし、描かれた格子背景のみを実alpha=0へ除去、しっぽとの隙間/ひげを含む輪郭を維持、顔・姿勢・帽子・赤いスカーフ・ドット密度・色・陰影・構図は変更しない、床/影/文字/背景を追加しないよう指定しました。CLI/APIへの切り替えはしていません。

## ローカル反映

- 通常: `http://localhost:5097/`、登録不要: `http://localhost:5097/index.html?demo=1`
- 稼働イメージ: `task-board:usability-v15`。DB/メールコンテナと既存ログイン鍵・環境設定を維持。
- `scripts/update-usability-preview.py` でバックアップ後に追加型Migrationを適用。既存14データテーブルの全内容一致、配信24ファイルのSHA-256一致、readiness、匿名週間APIの401を確認。
- バックアップ: `.local/backups/usability-20260908.e09ziv84/`（ディレクトリ700・ファイル600、秘密を含むため共有・commit禁止）。復旧用に `task-board-preview-before-usability-v15` を停止状態で保持。復旧時に追加した列・テーブルを自動削除しない。
- 実配信ブラウザー記録: `/private/tmp/task-usability-browser-c5x5ltq7`。既存302 PNGのバイト列と使用中111素材は変更していない。新規一体型90枚は未反映。
- 専用の検証コンテナ・匿名DBボリューム・ネットワークは作業後に削除。実DB、鍵、バックアップ、旧アプリは保持。公開・commit・pushなし。

画像のPython処理は明示的な許可待ちです。6機能の完了を画像透過の完了とは扱いません。

## 画像編集の実送信プロンプト（不採用の1試行）

入力: `frontend/assets/pet/drafts/outfits-v1/cat/lv-1-hat.png`

```text
Use case: background-extraction.
Image 1 is the EDIT TARGET, not a style suggestion. This is an existing approved pixel-art cat wearing a blue baseball cap with a gold paw mark and a red neckerchief. Remove ONLY the painted gray-and-white checkerboard background. Return the same full-body cat as a genuinely transparent PNG with actual alpha=0 outside its silhouette, including the gap between the tail and body and spaces around whiskers. Do NOT draw a checkerboard or any solid-color background. Preserve the exact original cat identity, face, pose, anatomy, hat, scarf, whiskers, pixel size, palette, outline, shadows, framing and proportions; do not redraw, restyle, crop, add or remove accessories. No backdrop, floor, drop shadow, text or watermark. Transparent background via real alpha channel is mandatory.
```
