---
title: "11 Request DTO と Response DTO を導入する"
labels:
  - backend
  - dto
  - design
milestone: Week1
---

## 今回やること
- APIの入力用DTOと出力用DTOを作る
- EntityをそのままAPI入出力に使わない形へ整理する

## 触るファイル
- DTOs/CreateTaskRequest.cs
- DTOs/UpdateTaskRequest.cs
- DTOs/TaskResponse.cs
- Services/TaskService.cs
- TasksController.cs

## DB変更
- なし

## 実装メモ
- POST/PUTはRequest DTOで受け取る
- レスポンスはResponse DTOで返す
- `TaskItem` はDB保存用のEntityとして扱う

## 確認方法
- PostmanのBodyとレスポンス形式を確認する

## Postman
- POST/PUTでRequest DTOに合わせたJSONを送る
- GETでResponse DTOの形を確認する

## DBeaver
- DB保存結果を確認する

## 学習ポイント
- EntityとDTOの役割を分ける
- APIで受け取る形とDBに保存する形は同じとは限らない
- DTOによりAPIの形を安定させやすくなる

## 完了条件
- API入出力にDTOを使っている
- CRUDが以前と同じように動く

## 確認質問
- Request DTOは何のためにありますか？
- Response DTOは何のためにありますか？
