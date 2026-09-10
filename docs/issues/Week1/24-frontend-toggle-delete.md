---
title: "24 フロントで完了切り替えと削除を作る"
labels:
  - frontend
  - feature
milestone: Week1
---

## 今回やること
- 画面から完了/未完了を切り替える
- 画面からタスクを削除する

## 触るファイル
- frontend/index.html
- frontend/style.css
- frontend/app.js

## DB変更
- なし

## 実装メモ
- 完了切り替えで `PUT /api/tasks/{id}` を呼ぶ
- 削除ボタンで `DELETE /api/tasks/{id}` を呼ぶ
- 成功後に一覧を再読み込みする

## 確認方法
- ブラウザで完了状態を切り替える
- ブラウザで削除する
- DBeaverでDB状態を確認する

## Postman
- PUT/DELETEのAPI単体確認に使う

## DBeaver
- `is_completed` の変化と削除を確認する

## 学習ポイント
- UI操作をAPI呼び出しにつなげる
- PUTとDELETEを画面から使う
- API成功後に画面状態を更新する

## 完了条件
- ブラウザから完了切り替えできる
- ブラウザから削除できる

## 確認質問
- 完了切り替えにはどのHTTPメソッドを使いますか？
- 削除にはどのHTTPメソッドを使いますか？
