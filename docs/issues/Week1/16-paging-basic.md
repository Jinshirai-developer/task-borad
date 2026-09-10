---
title: "16 page と pageSize でページングする"
labels:
  - backend
  - query
  - feature
milestone: Week1
---

## 今回やること
- `GET /api/tasks?page=1&pageSize=5` でページングする
- 一覧APIで全件返さない考え方を学ぶ

## 触るファイル
- TasksController.cs
- Services/ITaskService.cs
- Services/TaskService.cs

## DB変更
- なし

## 実装メモ
- `page` と `pageSize` をクエリパラメータで受け取る
- `Skip((page - 1) * pageSize)` を使う
- `Take(pageSize)` を使う
- `page` は1以上、`pageSize` は上限を決める

## 確認方法
- pageを変えて返るデータが変わることを確認する

## Postman
- `GET /api/tasks?page=1&pageSize=5`
- `GET /api/tasks?page=2&pageSize=5`
- `GET /api/tasks?search=買い物&page=1&pageSize=5`

## DBeaver
- 全件数とレスポンス件数を見比べる

## 学習ポイント
- 一覧APIで全件返すと重くなりやすい
- `Skip` と `Take` で取得範囲を指定する
- ページングは実務の一覧APIでよく使う

## 完了条件
- page/pageSizeで取得範囲を変えられる
- 検索や並び順と組み合わせられる

## 確認質問
- `Skip` は何をするメソッドですか？
- `Take` は何をするメソッドですか？
