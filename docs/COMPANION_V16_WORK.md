# 相棒と進める5機能 v16

## 範囲

- セーブポイント: タスクごと・本人だけに「ここまで／次の一歩／リンク」を保存。再開時に取り出す。保存だけでステータスや経験値は変えない。
- お助けサイン: チーム内の4種の相談、1人の立候補、取り下げ、依頼者／チーム管理者による解決。担当者・XPを変えない。お礼は1タスク直近20件を保持し、次の相談でも消えない。
- 受け渡し便: チームメンバーへ資料リンク・依頼・完了条件を渡し、宛先だけが受領／質問。担当者を自動変更しない。
- 攻略ノート: 試したこと／学び／次の手を記録。選択ワークスペース内のタグ完全一致・キーワードで再利用。外部AIへ送信しない。
- 作品棚: DONEの成果を額縁／モニター／本として配置。作品名・できたこと・資料リンクを保持。画像を含む成果物はリンク先で開く初版（ファイルアップロード・自動サムネイル取得なし）。非公開または所属チーム内限定。

## 保存・安全

追加型Migration。タスクにJSONを同居させ削除のUndoでも一緒に復元する。APIで本人のセーブポイントだけを投影し、他人の個人メモは返さない。新操作は全て認証・CSRF・所属・xmin/更新時刻の競合検査を通す。退出／退会では個人メモを削除し未処理の依頼を解除。共有のノート／作品は残し退会者を匿名化する。削除Undo内の原本も同様に処理する。

既存の大量の未コミット差分、302 PNG、経験値・取得品・Lv.5までのごほうび・環境設定・鍵を維持。新しいラスター画像の生成／透明化はこの実装に含めず、画像透過は引き続きPython処理の許可待ち。

プライバシーポリシーには2026-09-08付で機能の技術説明を追記。既存の個人／チーム保存範囲を具体化した補足で、同意版は変更していない。法的審査済みという意味ではない。退会しても共有本文に本人が書いた氏名などは自動検出・除去されないため、必要なら退出／退会前に編集・削除する旨も案内する。

## API

個人は `/api/companion`、チームは `/api/teams/{teamId}/companion`。GET rootが一覧、GET `/notes?q=&tags=&excludeTaskId=` が検索、GET/POST `/tasks/{taskId}` が記録の取得／変更。通常のタスクAPIには内部JSONを返さない。

POSTは `action`・`version`・`expectedUpdatedAt` 必須。直前の記録レスポンスからversion/updatedAtをコピーする。競合409は自動再送せず、最新取得後に利用者が入力を確認して再保存する。レコードを指定する操作では `entryId` に応答のUUIDを使用する。

| action | 主な追加フィールド／権限 |
| --- | --- |
| `savepoint_save` / `savepoint_clear` | `nextStep`（保存時必須）、`summary`、`resourceUrl`／本人の記録のみ |
| `help_open` | `kind`（decision/review/material/together）、`message`／チーム |
| `help_offer` / `help_withdraw` | `entryId`／依頼者以外が立候補、取り消しは立候補者 |
| `help_resolve` / `help_cancel` | `entryId`／依頼者またはチーム管理者 |
| `handoff_send` | `recipientId`、`message`、`criteria`、`resourceUrl`／自分以外の現メンバーへ |
| `handoff_accept` / `handoff_question` | `entryId`、質問時は`message`必須／宛先だけ |
| `handoff_cancel` | `entryId`／送信者またはチーム管理者 |
| `note_add` / `note_edit` / `note_delete` | `tried`・`learned`必須、`nextStep`・`resourceUrl`任意。編集／削除は`entryId`必須で作成者または管理者 |
| `showcase_save` / `showcase_remove` | 保存時`kind`（frame/monitor/book）・`title`・`summary`必須、`resourceUrl`任意。変更は作成者または管理者 |

本文は項目ごと400文字、作品名120文字、URL1000文字。URLはhttp/httpsのみ、認証情報付きは禁止。サーバーでリンク先を取得しない。保存上限は1タスクにつき本人ごとのしおり1件、相談／受け渡し各1件、ノート12件、作品1件、お礼の履歴20件。一覧では直近のしおり・相談・受け渡し各20件、作品50件、ノート／お礼各10件。検索はタグ完全一致またはキーワード一致を最大10件返す。

## 検証・反映

バックエンド219件（サービス・HTTP）、JavaScript108件成功。専用Chromeで新機能186項目、既存v15の73項目を確認。入力の保持、競合、本人だけのしおり、HTML注入防止、3テーマ×320/375/1440pxを確認。スクリーンショットの保存先は `/private/tmp/task-companion-browser-ljmfdfl3`、APIは分離デモです。

Migration `20260908124231_AddCompanionWork` は `companion_json` 列の追加とUndoスナップショット上限拡張のみ。隔離PostgreSQL 16でIdentity→報酬→タグ→v15→v16の順に旧データの移行試験を通過し、v16適用前後の既存15テーブル全行ハッシュが一致。EFのpending model changesなし。

隔離PostgreSQL/Mailpitで94検証・108 APIリクエスト成功（4人の新規登録・実SMTP確認・所属分離・同時立候補・更新時刻のDB精度・XP維持・Undo・退出・退会を含む）。検証用アカウント・チーム・タスク・送信キューは後片付け済み。DBに残る1ユーザーは旧Migrationが作る初期レコードで、検証アカウントの残存ではない。PNG検査4件も成功。

ローカル `http://localhost:5097/` を `task-board:companion-v16` に更新済み。登録不要デモは `/index.html?demo=1`。実DBには上記追加型Migrationだけを適用し、既存14永続テーブル全行ハッシュ・環境変数・ログイン鍵を保持。短命のUndo表は実DB比較から除外している（移行試験では既存15表を比較）。配信HTML/JS/CSS 27ファイルのSHA-256がソースと一致。公開・commit・pushは未実施。

更新後も実配信のお試し画面で新機能186項目・既存73項目が成功。Chromeからの実APIアクセスは0件。画面記録は `/private/tmp/task-companion-browser-6ymnp0mp` と `/private/tmp/task-usability-browser-0u6xt40m`。PC／スマホの実スクリーンショットを目視確認し、readiness 200、匿名の相棒API 401、302 PNG不変も再確認。

今回作成した検証専用7コンテナ、専用DBの匿名ボリューム、`task-v16-qa`ネットワークだけを削除済み。検証データは破棄したが、試験コードから再作成できる。実プレビュー・実DB／メール・鍵ボリューム・旧アプリ・バックアップは保持。

バックアップ: `.local/backups/companion-20260908.bostgt4d/`。DB dump・鍵・環境変数を含み、700/600で保護、共有・commit禁止。更新前アプリ `task-board-preview-before-companion-v16` も停止状態で保持。自動更新スクリプトは `scripts/update-companion-preview.py`（v15から一度だけ実行、既にv16なら再実行しない）。復旧が必要なら現アプリを止めて旧コンテナへ切り替える。新しいデータを消すDown Migrationは行わない。
