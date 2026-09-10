---
title: "19 DTO設計と責務を整理する"
labels:
  - backend
  - dto
  - design
milestone: Week1
---

## 今回やること
- EntityとDTOの責務を見直す
- ControllerとServiceのどちらで変換するか方針を揃える
- ファイル配置を読みやすくする

## 触るファイル
- DTOs/*
- Models/TaskItem.cs
- Services/TaskService.cs
- TasksController.cs

## DB変更
- なし

## 実装メモ
- `TaskItem` はDB保存用
- Request DTOは入力用
- Response DTOは出力用
- 変換場所を一貫させる

## 確認方法
- CRUD、検索、ページングが整理前と同じ動きをすることを確認する

## Postman
- CRUDと一覧条件を一通り確認する

## DBeaver
- 保存される値が変わっていないことを確認する

## 学習ポイント
- 設計整理は動くものを壊さずに行う
- Entityを外へ出しすぎない
- 一貫性があるとコードを追いやすい

## 完了条件
- DTOとEntityの役割を説明できる
- 変換方針がコード内で揃っている

## 確認質問
- EntityとDTOの違いは何ですか？
- 変換場所を揃えるメリットは何ですか？
