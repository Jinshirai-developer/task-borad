---
title: "01 Program.cs と起動設定を確認する"
labels:
  - backend
  - learning
milestone: Week1
---

## 今回やること
- ASP.NET Core Web API の起動入口を確認する
- Controller を使うための設定を確認する
- まだアプリの処理は増やさない

## 触るファイル
- Program.cs
- Properties/launchSettings.json

## DB変更
- なし

## 実装メモ
- `builder.Services.AddControllers()` でController機能を登録する
- `app.MapControllers()` でControllerのルートを有効にする

## 確認方法
- `dotnet run` でAPIが起動することを確認する

## Postman
- この回では必須ではない

## DBeaver
- この回では不要

## 学習ポイント
- `Program.cs` はアプリ起動時の設定を書く場所
- Controllerを使うには登録とルーティング有効化が必要

## 完了条件
- APIの起動入口を説明できる
- `AddControllers` と `MapControllers` の役割を説明できる

## 確認質問
- `AddControllers()` は何のためにありますか？
- `MapControllers()` は何を有効にしますか？
