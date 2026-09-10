---
title: "12 バリデーションを追加する"
labels:
  - backend
  - validation
  - feature
milestone: Week1
---

## 今回やること
- タイトル必須や最大文字数などの入力チェックを追加する
- 不正な入力で400が返ることを確認する

## 触るファイル
- DTOs/CreateTaskRequest.cs
- DTOs/UpdateTaskRequest.cs
- TasksController.cs

## DB変更
- なし

## 実装メモ
- DTOに `[Required]`, `[MaxLength]` などを付ける
- `[ApiController]` によりModelStateエラー時は自動で400が返る

## 確認方法
- titleなし、titleが長すぎるケースをPostmanで確認する

## Postman
- `POST /api/tasks` に不正なBodyを送る
- `400 Bad Request` とエラー内容を見る

## DBeaver
- 不正データが保存されていないことを確認する

## 学習ポイント
- APIは正常系だけでなく入力ミスも扱う
- バリデーションはDB保存前に行う
- 400はクライアントからの入力が不正な時に使う

## 完了条件
- 不正な入力で400が返る
- 正しい入力では保存できる

## 確認質問
- `[Required]` は何をチェックしますか？
- 入力不正のときによく使うHTTPステータスは何ですか？
