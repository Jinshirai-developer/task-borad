---
title: "06 POST /api/tasks でタスクを作成する"
labels:
  - backend
  - db
  - feature
milestone: Week1
---

## 今回やること
- PostmanからJSONを送り、DBにタスクを登録する
- `POST` と `[FromBody]` の使い方を体験する

## 触るファイル
- TasksController.cs
- Models/TaskItem.cs
- Data/AppDbContext.cs

## DB変更
- なし

## 実装メモ
- `[HttpPost]` のアクションを追加する
- `[FromBody] TaskItem task` でJSON Bodyを受け取る
- `_context.Tasks.Add(task)` で追加する
- `_context.SaveChanges()` でDBへ保存する

## 確認方法
- Postmanでタスク作成リクエストを送る
- DBeaverで `tasks` テーブルに行が増えたか見る

## Postman
- Method: `POST`
- URL: `/api/tasks`
- Body:
```json
{
  "title": "買い物",
  "description": "牛乳を買う",
  "isCompleted": false
}
```

## DBeaver
- `tasks` テーブルの行が増えていることを確認する

## 学習ポイント
- POSTは主にBodyでデータを受け取る
- `[FromBody]` はJSONをC#オブジェクトへ変換する
- `SaveChanges()` でDBへ反映される

## 完了条件
- Postmanからタスクを作成できる
- 作成したタスクがDBeaverで見える

## 確認質問
- `[HttpPost]` と `[FromBody]` の違いは何ですか？
- `SaveChanges()` は何をするメソッドですか？
