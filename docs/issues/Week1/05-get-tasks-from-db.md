---
title: "05 GET /api/tasks でDBのタスク一覧を返す"
labels:
  - backend
  - db
  - feature
milestone: Week1
---

## 今回やること
- 固定データではなく、PostgreSQLの `tasks` テーブルから一覧を取得する
- Controllerから `AppDbContext` を使う最初の実装を行う

## 触るファイル
- TasksController.cs
- Data/AppDbContext.cs
- Models/TaskItem.cs

## DB変更
- なし

## 実装メモ
- `TasksController` に `AppDbContext` を注入する
- `_context.Tasks.ToList()` で一覧を取得する
- `Ok(tasks)` でレスポンスを返す

## 確認方法
- APIを起動する
- Postmanで `GET /api/tasks` を送る
- DBeaverの `tasks` テーブルとレスポンスを比べる

## Postman
- Method: `GET`
- URL: `/api/tasks`

## DBeaver
- `tasks` テーブルの中身を確認する
- データがなければ空配列 `[]` が返ることを確認する

## 学習ポイント
- ControllerからDBのデータを返す流れ
- `AppDbContext` を使った読み取り
- 固定レスポンスからDB連携への第一歩

## 完了条件
- `GET /api/tasks` がDBの内容を返す
- データ0件の場合に空配列が返る

## 確認質問
- ControllerがDBを操作するために受け取るものは何ですか？
- `_context.Tasks` は何を表していますか？
