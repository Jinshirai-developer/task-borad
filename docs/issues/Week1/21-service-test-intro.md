---
title: "21 Serviceの単体テスト入門"
labels:
  - backend
  - test
  - learning
milestone: Week1
---

## 今回やること
- 手動確認だけに頼らない考え方を学ぶ
- まずはServiceの成功ケースと失敗ケースをテストする

## 触るファイル
- TaskApi.Tests/*
- Services/TaskService.cs
- TaskApi.sln

## DB変更
- なし

## 実装メモ
- テストプロジェクトを作る
- Serviceのメソッド単位でテストする
- 最初から大量のテストを書かない

## 確認方法
- `dotnet test` を実行する

## Postman
- この回では補助確認のみ

## DBeaver
- この回では不要

## 学習ポイント
- テストは動作確認を自動化する道具
- 成功ケースと失敗ケースを分けて考える
- 手動確認と自動テストは役割が違う

## 完了条件
- 最低1つ以上のServiceテストが通る
- `dotnet test` の見方が分かる

## 確認質問
- 手動確認だけに頼ると何が困りますか？
- 成功ケースと失敗ケースとは何ですか？
