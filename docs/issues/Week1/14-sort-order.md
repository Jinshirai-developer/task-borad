---
title: "14 sortOrder で並び順を切り替える"
labels:
  - backend
  - query
  - feature
milestone: Week1
---

## 今回やること
- `GET /api/tasks?sortOrder=asc` で古い順にする
- `GET /api/tasks?sortOrder=desc` で新しい順にする
- `isCompleted` と組み合わせられるようにする

## 触るファイル
- TasksController.cs
- Services/ITaskService.cs
- Services/TaskService.cs

## DB変更
- なし

## 実装メモ
- `sortOrder` をクエリパラメータで受け取る
- `OrderBy(task => task.CreatedAt)` を使う
- `OrderByDescending(task => task.CreatedAt)` を使う
- デフォルトは `desc` とする

## 確認方法
- asc/desc/未指定をPostmanで確認する
- `isCompleted` と組み合わせて確認する

## Postman
- `GET /api/tasks?sortOrder=asc`
- `GET /api/tasks?sortOrder=desc`
- `GET /api/tasks?isCompleted=false&sortOrder=desc`

## DBeaver
- `created_at` の順番とレスポンス順を見比べる

## 学習ポイント
- 一覧APIでは並び順のデフォルトを決める
- `OrderBy` と `OrderByDescending` の違い
- 複数の条件は順番に組み合わせられる

## 完了条件
- 並び順を切り替えられる
- 絞り込みと並び順を同時に使える

## 確認質問
- `OrderBy` と `OrderByDescending` の違いは何ですか？
- デフォルトを `desc` にする理由は何ですか？
