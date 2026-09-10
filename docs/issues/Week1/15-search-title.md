---
title: "15 search でタイトル検索できるようにする"
labels:
  - backend
  - query
  - feature
milestone: Week1
---

## 今回やること
- `GET /api/tasks?search=xxx` でタイトル部分一致検索を行う
- `isCompleted` や `sortOrder` と組み合わせる

## 触るファイル
- TasksController.cs
- Services/ITaskService.cs
- Services/TaskService.cs

## DB変更
- なし

## 実装メモ
- `search` をクエリパラメータで受け取る
- 空文字や未指定なら検索しない
- `Contains` を使ってタイトル部分一致にする

## 確認方法
- 検索あり/なしをPostmanで確認する
- 他の条件との組み合わせを確認する

## Postman
- `GET /api/tasks?search=買い物`
- `GET /api/tasks?search=買い物&isCompleted=false`
- `GET /api/tasks?search=買い物&sortOrder=desc`

## DBeaver
- `title` の値と検索結果を見比べる

## 学習ポイント
- 検索条件も一覧APIの一部
- 条件が増えても小さく積み重ねる
- 一覧APIを実用寄りに育てる

## 完了条件
- タイトル検索ができる
- 検索、絞り込み、並び順を同時に使える

## 確認質問
- `Contains` は何をするメソッドですか？
- searchが未指定の時はどう扱うべきですか？
