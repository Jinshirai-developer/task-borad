---
title: "03 AppDbContext と TaskItem を確認する"
labels:
  - backend
  - db
  - learning
milestone: Week1
---

## 今回やること
- `TaskItem` がタスク1件分を表すことを確認する
- `AppDbContext` がDB操作の入口であることを確認する
- C#のプロパティ名とDBカラム名の対応を確認する

## 触るファイル
- Models/TaskItem.cs
- Data/AppDbContext.cs
- Program.cs

## DB変更
- なし

## 実装メモ
- `DbSet<TaskItem> Tasks` は `tasks` テーブルを操作する窓口
- `entity.ToTable("tasks")` で対応するテーブル名を指定する
- `HasColumnName` でDBカラム名を指定する

## 確認方法
- Migration適用済みならDBeaverで `tasks` テーブルを見る

## Postman
- この回では必須ではない

## DBeaver
- `tasks` テーブルのカラムを確認する
- `id`, `title`, `description`, `is_completed`, `created_at`, `updated_at`

## 学習ポイント
- EntityはDBに保存するデータの形
- DbContextはC#とDBをつなぐ入口
- EF CoreがC#の操作をSQLに変換する

## 完了条件
- `TaskItem` と `tasks` テーブルの対応を説明できる

## 確認質問
- `TaskItem` は何を表すクラスですか？
- `DbSet<TaskItem> Tasks` は何のためにありますか？
