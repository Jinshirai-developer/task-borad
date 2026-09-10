---
title: "18 エラーレスポンスを整理する"
labels:
  - backend
  - error-handling
  - learning
milestone: Week1
---

## 今回やること
- 400/404/500 の役割を整理する
- 不正なクエリパラメータへの返し方を決める
- バリデーションエラーの見え方を確認する

## 触るファイル
- TasksController.cs
- Services/TaskService.cs
- DTOs/*

## DB変更
- なし

## 実装メモ
- 入力不正は400
- 対象なしは404
- 想定外のサーバーエラーは500
- まずは複雑な共通エラー設計を増やしすぎない

## 確認方法
- 不正なID、不正なBody、不正なpage/pageSizeをPostmanで確認する

## Postman
- `GET /api/tasks/9999`
- `POST /api/tasks` にtitleなしBody
- `GET /api/tasks?page=0&pageSize=5`

## DBeaver
- 不正リクエストでDBが変わっていないことを確認する

## 学習ポイント
- API品質は正常系だけでなく異常系も含む
- ステータスコードには役割がある
- エラーの見え方は利用者の使いやすさに関わる

## 完了条件
- 主な異常系の返し方を説明できる
- 代表的な不正入力で期待したステータスが返る

## 確認質問
- 400と404の違いは何ですか？
- page=0 はどのように扱うべきですか？
