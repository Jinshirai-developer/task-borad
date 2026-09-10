---
title: "07 GET /api/tasks/{id} でタスク1件を取得する"
labels:
  - backend
  - db
  - feature
milestone: Week1
---

## 今回やること
- IDを指定してタスク1件を取得する
- 存在しないIDでは `404 Not Found` を返す

## 触るファイル
- TasksController.cs

## DB変更
- なし

## 実装メモ
- `[HttpGet("{id}")]` のアクションを追加する
- `_context.Tasks.Find(id)` または `FirstOrDefault` で取得する
- 見つからない場合は `NotFound()` を返す

## 確認方法
- 存在するIDと存在しないIDでPostman確認する

## Postman
- `GET /api/tasks/1`
- `GET /api/tasks/9999`

## DBeaver
- 取得対象のIDが存在するか確認する

## 学習ポイント
- URLの一部から値を受け取る
- 1件取得では存在しないケースを考える
- `404` はリソースが見つからない時に使う

## 完了条件
- 存在するIDで1件返る
- 存在しないIDで404が返る

## 確認質問
- `{id}` はどこから値を受け取っていますか？
- 見つからない時に返すHTTPステータスは何ですか？
