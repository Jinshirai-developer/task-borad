---
title: "17 ページング結果に件数情報を含める"
labels:
  - backend
  - dto
  - feature
milestone: Week1
---

## 今回やること
- 一覧レスポンスに `items`, `totalCount`, `page`, `pageSize`, `totalPages` を含める
- フロントエンドでページング表示しやすい形にする

## 触るファイル
- DTOs/PagedResponse.cs
- Services/TaskService.cs
- TasksController.cs

## DB変更
- なし

## 実装メモ
- `Count()` で条件適用後の全件数を取得する
- `Skip` / `Take` 後のデータを `items` に入れる
- `totalPages` を計算する

## 確認方法
- Postmanでレスポンス構造を確認する

## Postman
- `GET /api/tasks?page=1&pageSize=5`

## DBeaver
- 条件に合う件数と `totalCount` を見比べる

## 学習ポイント
- APIレスポンスは画面で使いやすい形にする
- データ本体とメタ情報を分ける
- ページングUIには総件数が必要

## 完了条件
- ページング結果に件数情報が含まれる
- フロントでページ番号を作れる情報が返る

## 確認質問
- `items` と `totalCount` はそれぞれ何を表しますか？
- `totalPages` は何のために必要ですか？
