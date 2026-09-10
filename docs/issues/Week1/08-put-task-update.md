---
title: "08 PUT /api/tasks/{id} でタスクを更新する"
labels:
  - backend
  - db
  - feature
milestone: Week1
---

## 今回やること
- IDを指定して既存タスクを更新する
- `UpdatedAt` を更新時刻に変える

## 触るファイル
- TasksController.cs
- Models/TaskItem.cs

## DB変更
- なし

## 実装メモ
- `[HttpPut("{id}")]` のアクションを追加する
- 既存タスクをDBから取得する
- `Title`, `Description`, `IsCompleted`, `UpdatedAt` を更新する
- `_context.SaveChanges()` で保存する

## 確認方法
- PostmanでPUTを送る
- DBeaverで値と `updated_at` が変わったか確認する

## Postman
- Method: `PUT`
- URL: `/api/tasks/1`
- Body:
```json
{
  "title": "買い物を更新",
  "description": "牛乳と卵を買う",
  "isCompleted": true
}
```

## DBeaver
- 対象IDの行が更新されているか確認する

## 学習ポイント
- PUTは既存データの更新に使う
- 更新前に対象データが存在するか確認する
- 更新日時は変更タイミングで書き換える

## 完了条件
- 既存タスクを更新できる
- 存在しないIDでは404が返る

## 確認質問
- PUTではなぜ最初に既存タスクを取得しますか？
- `UpdatedAt` はいつ更新しますか？
