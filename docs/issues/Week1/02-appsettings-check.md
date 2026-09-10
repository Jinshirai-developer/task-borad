---
title: "02 appsettings.json のDB接続文字列を確認する"
labels:
  - backend
  - db
  - learning
milestone: Week1
---

## 今回やること
- `appsettings.json` の `DefaultConnection` を確認する
- DockerのPostgreSQL設定と接続文字列が一致しているか確認する

## 触るファイル
- appsettings.json
- docker-compose.yml
- Program.cs

## DB変更
- なし

## 実装メモ
- `ConnectionStrings:DefaultConnection` を `UseNpgsql` が読む
- `Host`, `Port`, `Database`, `Username`, `Password` をDocker設定と合わせる

## 確認方法
- `docker compose up -d`
- `docker compose ps`
- 必要なら `dotnet ef database update`

## Postman
- この回では必須ではない

## DBeaver
- Host: `localhost`
- Port: `5432`
- Database: `taskdb`
- Username: `taskuser`
- Password: `taskpass`

## 学習ポイント
- `docker-compose.yml` はDBコンテナを作る設定
- `appsettings.json` はAPIがDBへ接続する設定
- `Program.cs` はその接続文字列をEF Coreに渡す場所

## 完了条件
- APIがどのDBへ接続する設定になっているか説明できる

## 確認質問
- `DefaultConnection` はどこで使われていますか？
- `POSTGRES_DB` は接続文字列のどの項目と対応しますか？
