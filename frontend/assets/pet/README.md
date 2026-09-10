# ペット画像の案内

画像を探すときは、用途ごとに以下を開いてください。

| 場所 | 内容 | 本アプリで使用 |
| --- | --- | --- |
| このフォルダ直下 | 21枚：選択用の縮小画像、透過アトラス、猫の透過差分 | 使用中（互換素材を含む） |
| [rewards-v2/](rewards-v2/) | 90枚：ペット別・Lv.1〜5の帽子／リボン／クッション単体 | 使用中 |
| [drafts/outfits-v1/](drafts/outfits-v1/) | 90枚：ペット＋装備が一体になった生成原本 | 未反映・未透過 |
| [sources/](sources/) | 17枚：元サイズの立ち絵11枚、透過前アトラス6枚 | 制作・再生成用 |
| [archive/](archive/) | 11枚：不採用になった旧帽子素材 | 使用しない・保管のみ |

使用中のファイル名とURLは互換性のため維持しています。`sources`・`drafts`・`archive` はDockerのビルドコンテキストと公開成果物に含めません。画像そのものの変更や削除はしていません。

## ファイル名

- `portfolio-{species}-{pose}-…-web.png`：設定の選択プレビュー／フォールバック用。
- `portfolio-{species}-atlas-v2-alpha.png`：6表情 × 4成長姿の透過シート。
- `portfolio-cat-{idle|pet|hat|bow}-v3.png`：猫の承認済み透過素材（互換用の装備込み画像も保持）。
- `rewards-v2/{species}/lv-{1..5}-{hat|bow|mat}.png`：使用中の装備単体。
- `drafts/outfits-v1/{species}/lv-{1..5}-{hat|bow|mat}.png`：未反映の一体型画像。格子背景が描かれており、実透過ではありません。

`species` は `cat`（猫）／`dog`（犬）／`rabbit`（ウサギ）／`fox`（キツネ）／`panda`（パンダ）／`dragon`（ドラゴン）。`hat` は帽子、`bow` はリボン、`mat` はクッションです。

## 確認ページと記録

- [一体型90差分の一覧](../../previews/pet-outfits-v1/index.html)
- [使用中の装備の見本](../../previews/rewards-v14/index.html)
- [元画像の制作記録](ASSET_NOTES.md)／[アトラス・装備の制作記録](COLLECTION_ASSET_NOTES.md)
- [画像整理の方針・移動記録](../../../docs/assets/README.md)

整理後の確認: `python3 scripts/audit-asset-layout.py`（リポジトリのルートで実行）。
