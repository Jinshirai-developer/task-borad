---
title: "26 フロントにページング表示を追加する"
labels:
  - frontend
  - paging
  - feature
milestone: Week1
---

## 今回やること
- APIのページング結果を使って画面にページ移動を作る
- 前へ/次へボタンを作る

## 触るファイル
- frontend/index.html
- frontend/style.css
- frontend/app.js

## DB変更
- なし

## 実装メモ
- `page` と `pageSize` を状態として持つ
- `totalPages` を使ってボタンの有効/無効を切り替える
- 条件変更時はpageを1に戻す

## 確認方法
- 前へ/次へで表示データが変わるか確認する
- 検索条件変更時に1ページ目へ戻るか確認する

## Postman
- `page` と `pageSize` のAPI単体確認に使う

## DBeaver
- 件数とページ表示を見比べる

## 学習ポイント
- APIのメタ情報をUIに使う
- フロント側にも状態管理が必要
- 条件変更とページ番号の関係を考える

## 完了条件
- 画面でページ移動できる
- 条件変更時にページングが破綻しない

## 確認質問
- `totalPages` は画面で何に使いますか？
- 検索条件を変えた時にpageを1へ戻す理由は何ですか？
