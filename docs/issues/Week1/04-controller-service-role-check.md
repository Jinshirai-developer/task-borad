---
title: "04 Controller と Service の役割を確認する"
labels:
  - backend
  - learning
milestone: Week1
---

## 今回やること
- ControllerがAPIの入口であることを確認する
- Serviceがアプリの処理を書く場所であることを確認する
- 今のControllerが固定データを返している状態を確認する

## 触るファイル
- TasksController.cs
- Program.cs

## DB変更
- なし

## 実装メモ
- ControllerはGET/POST/PUT/DELETEを受け取る
- ServiceはDB操作や検索、ソートなどの処理を担当する
- Serviceを使う場合は `Program.cs` に `AddScoped` 登録が必要

## 確認方法
- `TasksController.Get()` が固定データを返していることを確認する

## Postman
- `GET /api/tasks`
- 返ってくるデータがDBではなく固定データであることを見る

## DBeaver
- この回では不要

## 学習ポイント
- Controllerは薄く保つ
- 処理が増えたらServiceに分ける
- DI登録によりControllerからServiceを使えるようにする

## 完了条件
- ControllerとServiceの役割を説明できる
- `AddScoped<ITaskService, TaskService>()` の意味を説明できる

## 確認質問
- Controllerの主な役割は何ですか？
- Serviceの主な役割は何ですか？
