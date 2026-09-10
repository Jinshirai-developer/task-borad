---
title: "09 DELETE /api/tasks/{id} でタスクを削除する"
labels:
  - backend
  - db
  - feature
milestone: Week1
---

## 今回やること
- IDを指定してタスクを削除する
- 削除対象がない場合は404を返す

## 触るファイル
- TasksController.cs

## DB変更
- なし

## 実装メモ
- `[HttpDelete("{id}")]` のアクションを追加する
- 対象タスクを取得する
- `_context.Tasks.Remove(task)` で削除対象にする
- `_context.SaveChanges()` でDBへ反映する

## 確認方法
- PostmanでDELETEを送る
- DBeaverで行が消えたか確認する

## Postman
- `DELETE /api/tasks/1`
- 削除後に `GET /api/tasks/1` を送って404を確認する

## DBeaver
- 対象IDの行が消えていることを確認する

## 学習ポイント
- DELETEは削除に使う
- 削除も `SaveChanges()` でDBへ反映される
- 削除済みIDを再取得すると404になる

## 完了条件
- 既存タスクを削除できる
- 削除後に一覧から消える

## 確認質問
- `Remove()` だけではDBから消えない理由は何ですか？
- 削除対象がない場合は何を返しますか？
