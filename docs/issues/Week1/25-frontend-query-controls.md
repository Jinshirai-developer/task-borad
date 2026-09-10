---
title: "25 フロントに絞り込み・並び順・検索を追加する"
labels:
  - frontend
  - query
  - feature
milestone: Week1
---

## 今回やること
- 画面から `isCompleted`, `sortOrder`, `search` を操作できるようにする
- クエリパラメータを組み立てて一覧APIを呼ぶ

## 触るファイル
- frontend/index.html
- frontend/style.css
- frontend/app.js

## DB変更
- なし

## 実装メモ
- セレクトや入力欄の値からクエリ文字列を作る
- `URLSearchParams` を使う
- 条件変更時に一覧を再取得する

## 確認方法
- 画面操作で一覧結果が変わるか確認する

## Postman
- API単体の条件確認に使う

## DBeaver
- 検索や絞り込み結果がDB内容と合っているか確認する

## 学習ポイント
- フロントからクエリパラメータを扱う
- APIの一覧機能を画面へつなげる
- 条件の組み合わせをUIで表現する

## 完了条件
- 絞り込み、並び順、検索を画面から使える

## 確認質問
- `URLSearchParams` は何のために使いますか？
- 条件変更時に再取得が必要な理由は何ですか？
