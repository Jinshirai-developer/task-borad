# Pet collection atlases — 2026-09-08 JST

Organization note: the opaque v1 atlas originals are now in `sources/atlases/`, and rejected accessory derivatives are in `archive/rewards-v14/rejected/`. Active transparent atlases, cat cutouts, web derivatives, and `rewards-v2/` keep their runtime paths. The unintegrated whole-pet images are separately stored in `drafts/outfits-v1/`. See [asset directory guide](README.md).

Generated with OpenAI’s built-in image-generation tool (imagegen skill). The references were this project’s existing original idle mascots, inspected before generation. No external character, artist, brand, or logo was requested. Original source outputs remain in the generator output directory; final files were copied to this project.

## Runtime files

### Species-specific reward sprites (v14)

`rewards-v2/{species}/lv-{1..5}-{hat|bow|mat}.png`: 90 independently generated inventory sprites, not CSS recolors. Six species each have fifteen names/designs in `Services/PetRewardArtCatalog.cs`. Exact prompts/references/original output paths are recorded in `docs/rewards-v14-generation.json` and per-item `docs/reward-generation-v14/*.json` records. Eleven hats were regenerated to remove unwanted animal ears/horns; rejected derivatives are retained only in the local review folder.

Generation used the imagegen skill's built-in tool. The previously user-approved background-only Python method was reused for generated checkerboard/ivory mattes. `scripts/prepare-pet-rewards.py` changes only exterior background alpha; RGB pixels, original files, outlines, white highlights and fabric shading remain unchanged. `alpha-report.json` records original SHA-256 hashes and removal counts. `pet-reward-metrics.js` contains measured visible bounds, so previews and equipped sprites retain their natural proportions.

All six atlases now use derived `portfolio-{species}-atlas-v2-alpha.png` files. `scripts/prepare-pet-atlases.py` preserves every original RGB pixel and all 24 poses per species; its alpha report records the checks. The original opaque v1 sheets remain archived below. The approved base cat uses its v3 idle/happy body with separate generated accessories; its treat/rest still use gentle motion, not new eating/sleeping drawings.

`pet-reward-art.js` fits hats/bows by species and growth stage, with standing-pose offsets. Cushions align to each stage's feet and remain outside the moving body. Working/lying atlas poses intentionally hide hats/bows, retaining the saved equipment and cushion. Reduced motion is respected. New claims stop at Lv.5; pet progression/growth still continues, and existing Lv.6–20 owned items retain their legacy CSS appearance. Species changes adapt the design without issuing a second reward.

### Approved cat cutouts (v13 history; superseded by v14 above)

The cat's `base` / 「いつもの相棒」 now uses four approved 1254 × 1254 RGBA images: `portfolio-cat-idle-v3.png`, `portfolio-cat-pet-v3.png`, `portfolio-cat-hat-v3.png`, and `portfolio-cat-bow-v3.png`. They are exact copies from the [cat review](../../previews/cat-style-v2/NOTES.md), which contains the generation prompts, source paths and explicitly user-approved Python alpha-only background removal. This integration does not generate or repaint any new bitmaps.

The character lives in a transparent child of a stationary frame. Hat + bow and equipped happy faces reuse aligned CSS display clips from the approved images; the cushion remains outside the moving child. Cat reward thumbnails use the same dressed artwork. XP, reward eligibility, the three saved equipment slots, growth selection and memories still come from the unchanged server APIs.

Scope: these four cat assets depict one growth stage and one blue cap/bow design. On this base cat, different saved reward levels currently share the approved blue design. Growth stages, other species and their reward color/shape variants retain the old atlases and CSS cosmetics below. Opaque atlases no longer rotate/bob as a whole; they still swap their original expression/pose cells. The base cat's treat/rest use gentle nibbling/breathing motion, not newly illustrated eating/sleeping poses. Further species/stage/costume art is separate follow-up work, not claimed complete here.

### Original atlases (archived as sources; runtime uses v2-alpha)

Six `portfolio-{species}-atlas-v1.png` files, each 1536 × 1024. Each contains six expressions (idle, happy, working, proud, sleepy, sad) and four optional growth appearances (base, explorer Lv.5, grown Lv.10, festival Lv.20): 144 illustrated states in total. These are expression/pose swaps, not frame-by-frame skeletal animations. CSS adds small species-specific gestures and respects reduced motion.

The final sheets intentionally use a pale ivory portrait background, not transparent cutouts. Initial dog/cat outputs had unwanted gradients and rabbit had a rendered checkerboard; those were rejected and regenerated with flat backgrounds. Minor background texture/color variation remains. The old single-image transparent sprites are retained for species selection/fallback. Do not describe the new atlases as transparent RGBA sprites.

Rows do not land on a perfectly uniform grid; `rowEdges` in `pet-play.js` defines visually checked slicing boundaries so feet/ears are not cut. Bitmap pixels were not manually repainted or edited with Python. Hats, bows and cushions are code-native CSS pieces, with three shapes and twenty colorways. Preview and equipped items share the same implementation.

## Prompts

Base atlas prompt:

```text
Use case: stylized-concept.
Asset type: ONE production game sprite atlas PNG, exactly 6 columns by 4 rows, 24 equal cells, landscape 3:2 canvas.
Image 1 is the character identity and pixel-art style REFERENCE, not an edit target. Create a NEW versioned atlas of this exact pet, not a photo of a sheet.
Keep its face, distinctive markings, bandana color, warm dark outline, chunky square pixels and limited 1990s handheld-game palette consistent across every cell.
COMPOSITION IS CRITICAL: invisible uniform 6 x 4 grid fills the full canvas edge-to-edge. EACH cell contains exactly one centered FULL BODY pet with ALL ears/tail/wings/feet visible and at least 15% transparent margin on EACH side within its cell. No overlap between cells. All characters face slightly toward the viewer and share the same baseline and head position within their cells. No grid lines, captions, numbers, text, scenery or shadows.
Columns left to right in EVERY row:
1 neutral friendly idle;
2 joyful eyes closed smile, species-specific affectionate gesture;
3 focused alert working pose;
4 proud happy celebration, head held high and a raised paw;
5 lying down comfortably asleep, eyes closed;
6 mildly sad with lowered ears and sympathetic expression, not distressed.
Rows top to bottom:
1 original small companion, same design as reference;
2 Lv5 young explorer: slightly more developed proportions and a tiny tan explorer vest, recognizable same animal;
3 Lv10 grown companion: visibly more mature graceful body, fuller tail/fur/wings as appropriate, same fur colors and original bandana, no vest;
4 Lv20 celebration companion: same recognizable adult wearing a short cream-and-gold ceremonial cape with star clasp, NO crown/hat.
For all rows/columns preserve exact grid positioning and empty padding. The atlas is sliced by CSS at exact equal grid coordinates.
CRITICAL: genuinely TRANSPARENT RGBA background, alpha=0 in all empty space. Do not depict a checkerboard or colored background. No text, logo, trademark, watermark, border, scenery, extra animals or floating symbols.
```

Species suffixes:

- dog: tan and cream floppy-eared dog with navy bandana. Joy gesture: tail lifted toward the opposite side and excited paw wave.
- cat: gray and cream cat with amber eyes and red bandana. Joy gesture: licking a raised paw, grooming with content smile.
- rabbit: cream white rabbit with teal bandana and pink inner ears. Joy gesture: one ear bending, happy tiny hop.
- fox: orange and cream fox with forest green bandana and large white-tipped tail. Joy gesture: wrapping and flicking the fluffy tail, pleased smile.
- panda: cream and charcoal panda cub with purple bandana. Joy gesture: happily waving both round paws.
- dragon: moss green dragon with cream belly, amber eyes, tiny ivory horns and coral bandana. Joy gesture: spreading and fluttering its tiny gold-membrane wings.

Fox/panda/dragon suffix:

```text
Background must be plain solid pale ivory #fffaf0 if transparent export is unavailable. Never a gradient, texture, vignette or checkerboard. Keep clear 40-pixel empty margins in each 256px cell.
```

Dog/cat/rabbit corrective edit prompt (using the inspected first-generation atlas as reference):

```text
Edit this production sprite atlas. Preserve the exact 6 columns x 4 rows, 24 characters, all faces, colors, clothing, poses. Replace ALL background and checkerboard/gradients with absolutely uniform solid pale ivory #fffaf0. No shadow or glow. Improve each cell: reduce each animal to fit comfortably within the middle 70% of its cell, leaving at least 15% empty padding all sides. Cell dimensions exactly256x256, full1536x1024 image. Keep chunky pixel art outlines. Do not crop any ears, paws or tails. Sleeping poses centered lower but within the cell. Output a clean flat background bitmap atlas; no labels, scenery, grid lines or text. Maintain same pet identity and expressions.
```

## Original selected output paths

- dog: `/Users/sj/.codex/generated_images/01a07b00-539b-71d0-b7c0-e6fead9a9400/exec-a60b4e9e-ae02-4b7a-9fc6-5ca92da93a22.png`
- cat: `/Users/sj/.codex/generated_images/01a07b00-539b-71d0-b7c0-e6fead9a9400/exec-ca3137b9-f9ee-4bac-8a92-fb65e74207c2.png`
- rabbit: `/Users/sj/.codex/generated_images/01a07b00-539b-71d0-b7c0-e6fead9a9400/exec-8409ece9-f80d-4238-8b85-bc7357245333.png`
- fox: `/Users/sj/.codex/generated_images/01a07b00-539b-71d0-b7c0-e6fead9a9400/exec-204a3bd9-8f1e-48e1-9a9b-7581245b0bca.png`
- panda: `/Users/sj/.codex/generated_images/01a07b00-539b-71d0-b7c0-e6fead9a9400/exec-ec9ddb4a-d98b-40c9-b68e-28b11a6721b4.png`
- dragon: `/Users/sj/.codex/generated_images/01a07b00-539b-71d0-b7c0-e6fead9a9400/exec-305b721f-1bb0-4696-9f0c-36ddaf915b22.png`

Each final atlas was visually inspected, then exercised in a real Chrome browser at desktop and mobile sizes. See `tests/browser-pet-play.py` for the isolated mock-API UI checks. See the historical `ASSET_NOTES.md` for the previous asset provenance; this record supersedes its earlier statement that non-dog species only have one expression.
