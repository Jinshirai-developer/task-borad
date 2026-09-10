---
title: "22 素のJSフロントから呼ぶためにCORSを確認する"
labels:
  - backend
  - frontend
  - cors
milestone: Week1
---

## 今回やること
- HTML/JavaScriptからAPIを呼ぶ前にCORSを確認する
- 必要なら開発用CORS設定を追加する

## 触るファイル
- Program.cs
- frontend/index.html
- frontend/app.js

## DB変更
- なし

## 実装メモ
- フロントを別オリジンで開く場合、CORSが必要になることがある
- 開発用に許可する範囲を決める
- 本番向けの広い許可はまだ深掘りしない

## 確認方法
- ブラウザのDevToolsでエラーを見る
- APIが呼べるか確認する

## Postman
- PostmanはCORSの影響を受けないため参考程度

## DBeaver
- この回では不要

## 学習ポイント
- CORSはブラウザの制約
- Postmanで成功してもブラウザで失敗することがある
- フロント連携前に確認しておくと詰まりにくい

## 完了条件
- ブラウザからAPIを呼べる状態になる
- CORSが何の問題か説明できる

## 確認質問
- CORSはAPI側のエラーですか、ブラウザの制約ですか？
- PostmanではCORS確認ができますか？
