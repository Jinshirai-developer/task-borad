---
title: "13 isCompleted で絞り込みできるようにする"
labels:
  - backend
  - query
  - feature
milestone: Week1
---

## 今回やること
- `GET /api/tasks?isCompleted=true` で完了済みだけ取得する
- `false` で未完了だけ取得する
- 指定なしなら全件返す

## 触るファイル
- TasksController.cs
- Services/ITaskService.cs
- Services/TaskService.cs

## DB変更
- なし

## 実装メモ
- Controllerで `[FromQuery] bool? isCompleted` を受け取る
- Serviceで `Where` を使う
- nullable boolにして未指定を表現する

## 確認方法
- true/false/未指定の3パターンをPostmanで確認する

## Postman
- `GET /api/tasks`
- `GET /api/tasks?isCompleted=true`
- `GET /api/tasks?isCompleted=false`

## DBeaver
- `is_completed` の値とレスポンスを見比べる

## 学習ポイント
- クエリパラメータで一覧取得条件を変える
- `bool?` により指定なしを扱える
- 条件がある時だけ `Where` を追加する

## 完了条件
- 完了/未完了の絞り込みができる
- 未指定時は全件返る

## 確認質問
- `bool?` を使う理由は何ですか？
- `Where` は何のために使いますか？
