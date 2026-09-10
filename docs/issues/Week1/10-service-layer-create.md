---
title: "10 Service層を作ってControllerを薄くする"
labels:
  - backend
  - design
  - learning
milestone: Week1
---

## 今回やること
- DB操作をControllerからServiceへ移す
- `ITaskService` と `TaskService` を作る
- `Program.cs` にService登録を追加する

## 触るファイル
- Services/ITaskService.cs
- Services/TaskService.cs
- TasksController.cs
- Program.cs

## DB変更
- なし

## 実装メモ
- ControllerはServiceを呼ぶだけに近づける
- Serviceに `GetAll`, `GetById`, `Create`, `Update`, `Delete` を置く
- `builder.Services.AddScoped<ITaskService, TaskService>()` を追加する

## 確認方法
- CRUDがService分離前と同じように動くことを確認する

## Postman
- GET/POST/GET by id/PUT/DELETEを一通り確認する

## DBeaver
- 作成・更新・削除結果を確認する

## 学習ポイント
- ControllerはHTTPの入口
- Serviceはアプリの処理
- 役割分担によりコードを読みやすくする

## 完了条件
- CRUD処理がServiceへ移動している
- APIの動きが分離前と変わっていない

## 確認質問
- Controllerに残す処理は何ですか？
- Serviceに移す処理は何ですか？
