---
title: "23 フロントで一覧表示と作成フォームを作る"
labels:
  - frontend
  - feature
milestone: Week1
---

## 今回やること
- 素のHTML/CSS/JavaScriptでタスク一覧を表示する
- タスク作成フォームからAPIへPOSTする

## 触るファイル
- frontend/index.html
- frontend/style.css
- frontend/app.js

## DB変更
- なし

## 実装メモ
- `fetch` で `GET /api/tasks` を呼ぶ
- フォーム送信で `POST /api/tasks` を呼ぶ
- 作成後に一覧を再読み込みする

## 確認方法
- ブラウザで一覧表示を確認する
- フォームから作成してDBeaverにも反映されるか見る

## Postman
- API単体確認に使う

## DBeaver
- 作成したタスクがDBに保存されているか確認する

## 学習ポイント
- 自分で作ったAPIを画面から使う
- `fetch` の基本
- 画面とAPIとDBのつながりを見る

## 完了条件
- ブラウザにタスク一覧が表示される
- ブラウザからタスクを作成できる

## 確認質問
- `fetch` は何のために使いますか？
- 作成後に一覧を再取得する理由は何ですか？
