# ごほうび素材の制作記録

- 6種類 × Lv.1〜5 × 帽子・リボン・クッション = 90点。内蔵の画像生成ツールで個別に生成。
- 初回の計画は `../rewards-v14-generation.json`。各 `{species}_{level}_{kind}.json` が採用素材の最終プロンプト・参照・生成元パスを記録する。
- 余分な耳・角が入った帽子11点を再生成。元の生成PNGはツール出力先、未採用の透過派生版は `frontend/assets/pet/archive/rewards-v14/rejected/` に保持。公開成果物には含めない。
- このスレッドで以前に承認された背景のみの処理を再利用。生成時点で十分なalphaがある画像はそのまま使い、それ以外は外周につながる無彩色の背景だけを透明化した。RGB、輪郭、衣装の白いハイライトは変更していない。
- `alpha-report.json` は90点の処理結果、`atlas-alpha-report.json` は6種の成長・表情アトラスの処理結果。`backgroundMinimum` は3点の灰色市松模様に合わせた個別設定。
- 生成キャンバスは正方形に限らない。表示には実ピクセルで測った `ratio` と正規化したalphaの境界を使用し、プレビューでも装備中でも縦横比を保つ。
- 最終PNGは `frontend/assets/pet/rewards-v2/`。`tests/test_reward_assets.py` が90点の重複・透過・境界・縦横比と6アトラスを検査する。

画像は帽子・リボン・クッションの単体素材。種類・成長段階ごとの位置は `frontend/pet-reward-art.js` にあり、素材の再描画や引き伸ばしではなく表示座標で調整する。
