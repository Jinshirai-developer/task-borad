# 猫の見た目確認 v2

作成日: 2026-09-08。猫の絵柄を承認するための隔離された静的プレビューです。

## 範囲

- このフォルダーだけで表示。アプリの API、認証、localStorage、経験値、ごほうび、保存データにアクセスしません。
- 通常 / 撫でる / 帽子あり / リボンあり の4枚で方向性を確認します。
- 帽子とリボンは CSS の後付けパーツではなく、猫と同じ画像内に描いた装備です。
- 背景と猫は別の要素。アニメーション対象は透過 PNG の猫だけです。
- おやつと休憩は背景固定を確認する仮モーションです。専用の食事・寝姿ではありません。
- 他の種類、成長段階、装備と表情の組合せ、本アプリへの組込みは、見た目の承認後の範囲です。

## 素材と制作方法

内蔵 image_gen ツールの編集モードを使用。モデル ID はツールから指定・取得できないため記載していません。
初回の絵柄制作は内蔵 image_gen の編集モード。透過できなかった2枚は、2026-09-08にユーザーから明示的な了承を得て、ローカルPythonで背景だけを除去しました。
CLI / 外部画像 API は使用していません。キャンバスは透明度の読み取り検査のみに使用します。
猫の元画像は `../../assets/pet/sources/portraits/portfolio-cat-idle-v1.png` を参照。
参照元の正確なパス: `/Applications/task-app/frontend/assets/pet/sources/portraits/portfolio-cat-idle-v1.png`。既存素材は変更していません。

| ファイル | 内容 | 元素材 / 生成結果 |
| --- | --- | --- |
| cat-idle.png | 通常 | 元の透過 PNG をそのままコピー |
| cat-pet-transparent.png | 撫でる | 初回生成の cat-pet.png をPythonで背景除去 |
| cat-hat-transparent.png | 帽子あり | 初回生成の cat-hat.png をPythonで背景除去 |
| cat-ribbon.png | リボンあり | 初回生成後、背景抽出に成功 |

初回生成の3枚には市松模様が焼き込まれていたため、そのままでは採用していません。
現在の4枚すべてについて、ブラウザーで alpha = 0 の背景ピクセルが存在し、外周の99%以上が透明であることを確認済みです。
元の cat-pet.png / cat-hat.png と本アプリの既存素材は変更せず残しています。

### Pythonによる背景除去

再現スクリプト: `/Applications/task-app/scripts/cat_review_cutout.py`（標準ライブラリのみ、通信・追加インストールなし）。
画面の外周につながる明るい無彩色の背景だけを探し、該当ピクセルのalphaを0に変更しています。
選別条件はRGBの最小値が215以上、最大値と最小値の差が18以下。輪郭の内側に閉じた白いハイライトや、暖色の毛・ヒゲは除去しません。
縦横サイズ・位置・全RGB値は入力と完全一致。輪郭の膨張・縮小、補間、再描画はしていません。
出力はRGBA PNGで、書き出し後の読み直し一致検査も実施しています。

```bash
python3 scripts/cat_review_cutout.py frontend/previews/cat-style-v2/cat-pet.png frontend/previews/cat-style-v2/cat-pet-transparent.png
python3 scripts/cat_review_cutout.py frontend/previews/cat-style-v2/cat-hat.png frontend/previews/cat-style-v2/cat-hat-transparent.png
```

既存ファイルへの上書きは拒否するため、再実行時は新しい出力名を指定してください。

| 素材 | 画像サイズ | 背景を透明化したピクセル数 | RGB変更 |
| --- | --- | --- | --- |
| 撫でる | 1254 × 1254 | 925,837 | なし |
| 帽子 | 1254 × 1254 | 927,761 | なし |

## 正確な生成プロンプト

各差分は「共通プロンプト + 対象の追加プロンプト」を、元画像1枚を参照して別々に実行しました。

### 共通

```text
Use case: identity-preserve.
Input image 1 is the EDIT TARGET: this project's original gray-and-cream pixel-art cat, on an actual transparent background. Produce ONE edited standalone full-body cat PNG, not a sprite sheet, comparison, card or mockup.
Keep the exact same character identity, cream facial blaze, gray fur markings, amber eye color, ear shape, cream paws, curled cream-tipped tail, body proportions and camera angle. Match the original chunky square pixel grid, warm dark outline thickness, limited palette and stepped shading. Do not smooth into vector art or detailed painting. Preserve the original canvas framing and full-body scale; keep both ears, feet and tail fully visible. No text, no logo, no human hands, no hearts or floating symbols.
The output must have REAL TRANSPARENT BACKGROUND: RGBA PNG, alpha=0 for all empty space surrounding the cat. Preserve the source transparency. Do NOT paint white, ivory, black, gradient, a checkerboard, a floor, a cast shadow or any background behind the cat. The character is a cutout to place on arbitrary light and dark backgrounds.
```

### 撫でる

```text
Change ONLY expression and a tiny affectionate gesture: close the eyes into happy gentle curved smiles; relaxed ears, a soft pleased smile and a very slight head lean as if enjoying being stroked. Keep sitting with both front paws down, and retain the red bandana. This is the next frame of the same cat, not another cat. Do not make the whole body tilt.
```

### 帽子

```text
Change ONLY by adding a small soft muted sky-blue cloth cap naturally worn between the ears. The cap follows the skull angle, sits in contact with the fur, and tucks behind the near ear where appropriate, leaving both ears readable. Draw the cap as integral pixel art with the SAME pixel size, warm brown outline, folds, stepped highlights and shaded underside as the cat, not a flat geometric overlay. Keep the idle eyes open, exact original face and seated pose, and the red bandana.
```

### リボン

```text
Change ONLY the red bandana into a small muted sky-blue fabric bow at the front of the neck, with a narrow collar. Tie the bow naturally against the chest fur below the chin; small central knot, asymmetrical folded loops and short tails. Match the cat's EXACT chunky pixel size, warm brown outline, highlights and shaded fabric folds. Some fur should overlap the collar so it feels worn rather than pasted on. Keep the idle eyes open and the exact original face and seated pose. No red bandana underneath the new bow.
```

### 背景抽出（初回生成結果を入力、差分ごとに別実行）

```text
Use case: background-extraction. Remove the entire light gray and white checkerboard background from this image. Return the cat as a clean cutout with a genuinely TRANSPARENT background in the PNG alpha channel. All background pixels must have alpha 0. This is background removal, not drawing an image of transparency. Keep the cat, its face/expression, fur, accessory, pixel-art outlines, colors, exact size and position unchanged. Preserve fine whiskers, tail, ears, paws, cap/ribbon if present. No white or dark matte, checkerboard, shadow, halo or new elements. Deliver an actual transparent-background PNG.
```

リボンには実際の透過が付きました。撫でる・帽子は不透明なままでした。

### 撫でる・帽子の背景抽出再試行（初回生成結果を入力、別実行）

```text
Remove the checkerboard background from this cat image and make it transparent. Keep the cat and its accessories exactly as they are. Return a transparent PNG cutout.
```

## 確認

`python3 tests/browser-cat-review.py` は、このフォルダーを localhost:5089 で配信した状態で実行します。
4枚の画像データの透明度、明・暗・市松背景、背景を固定した3つの操作、動作停止、動きを減らす設定、375pxの表示、API通信がないことを確認します。
`python3 tests/test_cat_cutout.py` で、囲まれた白いハイライト・毛色・輪郭の維持、RGB不変、PNGの読み書き一致、上書き拒否も確認します。

## 現在の状態

4案の透過処理と確認用画面が完成。その後、ユーザーの「本アプリに反映して」により、猫の「いつもの相棒」への組込みを実施しました。本アプリの素材は `../../assets/pet/portfolio-cat-{idle,pet,hat,bow}-v3.png` です。他の種類・成長姿は未展開です。現在の本アプリ配信・検証記録は `docs/HANDOFF.md` を参照してください。
Pythonの単体テスト2件成功。ブラウザーテスト成功（4枚の透過、明・暗・市松背景、撫でる・おやつ・休憩での背景固定、動作停止、動きを減らす設定、375px表示、API通信なし）。
ブラウザー検証の最終記録: `/private/tmp/task-cat-review-ua9j_icb`。明暗背景とスマホ表示のスクリーンショットも目視確認済みです。
4案の比較スクリーンショット: `cat-review-four.png`（暗い背景のブラウザー表示を撮影。画像素材を再生成・合成したものではありません）。
上記は静的見本制作時の検証記録です。後続の本アプリ組込みでは表示コードのみを変更し、経験値、ごほうび、認証、保存処理は変更していません。

生成元（全ファイル `/Users/sj/.codex/generated_images/01a07b00-539b-71d0-b7c0-e6fead9a9400/` 内）:

- 撫でるの絵柄見本: `exec-16b231cb-92a0-455c-b03d-c8ae968d1330.png`
- 帽子の絵柄見本: `exec-19d86db2-4d19-409b-8c52-a124f04831fb.png`
- リボンの初回生成: `exec-83e11e58-7553-4058-b612-87bb8ec9a2ea.png`
- 採用したリボンの透過結果: `exec-141e67b6-9e14-4726-83fc-d12a44a8e3a5.png`
- 未採用の再抽出（撫でる / 帽子）: `exec-82c858e7-69ce-40f2-ade3-b0df52723aa2.png` / `exec-0b73a39e-3c3b-4903-9b92-d02ddfce80cf.png`
- 未採用の短い指示での再抽出（撫でる / 帽子）: `exec-ec95285b-b44a-4eb9-9874-36577bdc4078.png` / `exec-d714fb12-a888-483d-a3ab-7e1158faea5f.png`
