# アセット画像の整理

2026-09-08に用途別に整理しました。**302枚を保持し、184枚を移動。画像の削除・再圧縮・透過処理はしていません。** 移動前後の場所と全画像のSHA-256は [移動記録](reorganization-20260908.json) にあります。

## 現在の配置

```text
frontend/assets/pet/
├── portfolio-*.png       使用中・互換用の21枚（URLを維持）
├── rewards-v2/           使用中の装備単体90枚
├── drafts/outfits-v1/    未透過・未反映の一体型90枚
├── sources/portraits/    元サイズの立ち絵11枚
├── sources/atlases/      透過前のアトラス6枚
└── archive/rewards-v14/  不採用の帽子11枚

frontend/previews/        確認ページ一式（猫の承認時の画像7枚も保持）
docs/screenshots/
├── app/v*/              ボード・タグ・チーム・設定など
└── pets/                ペット関連、装備見本、一体型画像の一覧
```

画面キャプチャは合計66枚。元のバージョンと、最終／途中の違いが分かるよう分類しています。

## 参照と公開範囲

- アプリが使用する111枚のパスとバイト列は維持。
- 制作記録、JSON内の参照画像・成果物パス、一覧ページ、READMEの画像リンクも新しい場所に更新。外部の生成ツール原本は移動・削除していません。
- 制作原本のv1アトラスは公開対象から除外。公開用のv2-alphaアトラスは従来どおり使用。
- `sources`・`drafts`・`archive` はDockerビルドコンテキスト・publish成果物から除外し、Dockerfile／CIでも検査。
- 既存の作業ツリーにあった無関係な変更や削除はそのまま維持。稼働中のアプリ・DB・経験値・保存記録は変更せず、再デプロイもしません。

## 確認方法

```sh
python3 scripts/audit-asset-layout.py
python3 scripts/audit-pet-outfits.py --pixels --require-complete
python3 tests/browser-pet-outfits.py
```

整理検査は302枚のバイト一致、移動先の存在、旧配置の残存、配布用111枚、新しい一体型90枚の参照、READMEの画像リンクを確認します。画像を変更しません。

## 整理後の検証結果

- 302枚すべてのSHA-256一致、184枚の移動先と使用中111枚の元パス維持を確認。
- 一体型画像の検査: 90枚すべて存在し、生成原本とバイト一致。
- 確認ページ: 分離Chromeで20項目成功、90画像すべて読み込み成功。
- PNG検査: 4件成功。Docker build/publishとバックエンド180件のテスト成功。
- 制作原本・未反映・不採用フォルダがDockerコンテキストと配布物に含まれないことを確認。

検証用イメージは `task-board:assets-organized-check`。稼働中の `task-board:rewards-v14` は更新しておらず、DB・保存記録・鍵も変更していません。

個別の案内は [ペット画像](../../frontend/assets/pet/README.md)、[画面キャプチャ](../screenshots/README.md)、[確認ページ](../../frontend/previews/README.md) を参照してください。
