# Pet asset provenance

Organization note (2026-09-08): high-resolution source portraits now live in `sources/portraits/`; only the `*-web.png` runtime derivatives remain beside this file. Basenames and original generator output paths in the historical record below are unchanged. See [asset directory guide](README.md).

Current collection imagery (2026-09-08): all six species now have six expressions and four growth appearances. See [collection atlas provenance and exact prompts](COLLECTION_ASSET_NOTES.md). The single-image and transparency descriptions below document the earlier assets, which are retained for selection previews/fallback.

The six production mascot images were generated on 2026-09-07 with OpenAI's built-in image-generation tool for this project. No third-party character, brand, artist name, or external image was used as the visual reference.

## Prompt family

Base prompt:

> Original compact tan-and-cream dog mascot with floppy triangular ears, brown eyes, and a navy bandana with a small cream paw emblem. Handcrafted 1990s game pixel art, limited warm palette, consistent pixel scale, full body centered, true transparent alpha background, no floor, shadow, text, logo, trademark, watermark, or checkerboard.

State-only variations were requested while preserving the same identity:

- `idle`: friendly neutral standing pose
- `happy`: bright eyes, joyful smile, one paw lifted
- `working`: alert, focused expression, four paws planted
- `proud`: head held high, calm confident smile, one paw poised
- `sleepy`: seated, half-closed eyes, relaxed ears, subtle yawn
- `sad`: seated, lowered ears, sympathetic eyes, downturned mouth

## Files

The `portfolio-dog-*-v2.png` files are the generated source images. The `portfolio-dog-*-v2-web.png` files are 256px web derivatives used by the application. Every production derivative has a real alpha channel; an unsuccessful non-transparent generation was rejected and is not part of the project.

## Terms review

The [OpenAI Terms of Use](https://openai.com/policies/terms-of-use/) were reviewed on 2026-09-07. They state that, as between the user and OpenAI and to the extent permitted by applicable law, the user owns the output; they also state that output may not be unique and that the user remains responsible for its use. This provenance record is not a legal guarantee. Recheck the then-current terms and decide the repository's licensing policy before redistribution.

## Additional selectable species (2026-09-08 JST)

Two additional original idle sprites were generated with OpenAI's **built-in image-generation tool** for the selectable-pet feature. The existing dog assets were left unchanged. The same project's dog sprite was inspected as a style reference, but the final selected cat and rabbit generations used the text-only prompts below. No third-party character, artist, trademark, or external reference was used.

These additions each have **one idle image**, not six emotion-specific images. The application reuses that image for the selected species while preserving the existing state text and motion. Existing dog emotion images remain available.

### Selected output files

- Cat source: `portfolio-cat-idle-v1.png` (1254 × 1254).
- Cat runtime derivative: `portfolio-cat-idle-v1-web.png` (256 × 256).
- Rabbit source: `portfolio-rabbit-idle-v1.png` (1254 × 1254).
- Rabbit runtime derivative: `portfolio-rabbit-idle-v1-web.png` (256 × 256).

Built-in original output locations (retained, not deleted):

- Cat: `/Users/sj/.codex/generated_images/01a07c5b-9621-75b1-94a4-a9ebcd89ee00/exec-fba91fda-60a9-4940-b715-0c5ef761d60a.png`.
- Rabbit: `/Users/sj/.codex/generated_images/01a07c5b-9621-75b1-94a4-a9ebcd89ee00/exec-33d99ec5-2f44-4a63-b93c-25699ce4b0b3.png`.

The selected generated files were copied into this workspace. Runtime derivatives were mechanically reduced with macOS `sips -Z 256`; no manual background replacement, masking, or Python image editing was performed.

### Transparency and visual QA

Both source files and both runtime derivatives have a real 8-bit RGBA alpha channel. A read-only PNG audit confirmed alpha values spanning 0–255 and fully transparent background pixels:

- Cat web image: 38,906 fully transparent pixels out of 65,536.
- Rabbit web image: 52,775 fully transparent pixels out of 65,536.

Both 256px derivatives were visually checked: complete recognizable animals, no cropped body parts, no background grid, no text or watermark. The cat's generated framing is tighter than the requested margin; UI spacing should not assume identical intrinsic transparent margins across species. Very faint partially transparent generation artifacts may extend beyond the main silhouette. Opaque/checkerboard attempts, including an unsuccessful cat-padding correction, were rejected and were not copied into the project.

### Final selected cat prompt

```text
Use case: stylized-concept.
Generate ONE game-ready PNG sprite of a friendly original gray-and-cream cat wearing a small plain red bandana. Sitting idle, entire cat including curled tail and feet visible, amber eyes, dark brown outline, chunky visible square pixels, restrained 1990s pixel-art shading. Square canvas with at least 10% empty padding on every edge.
CRITICAL OUTPUT REQUIREMENT: Return an image with TRUE TRANSPARENT BACKGROUND, using an RGBA PNG alpha channel. The space surrounding the cat must have alpha=0. Do not render a checkerboard: the image data itself must be transparent. No background colors, no scenery, no floor, no shadows, no words, no logos, no watermarks. This is a transparent game sprite, not a mockup, screenshot, or depiction of a sprite on a background.
```

### Final selected rabbit prompt

```text
Use case: stylized-concept.
Asset type: game-ready selectable pet mascot sprite for a productivity web app.
Generate ONE adorable original white-and-warm-cream RABBIT wearing a small plain teal bandana. Friendly neutral seated idle pose, long upright ears with soft pink inner ears, gentle dark brown eyes, tiny pink nose, round paws, subtle fluffy tail. The complete rabbit including the tips of BOTH long ears, all feet and tail must be visible. No carrots or props.
Style: handcrafted 1990s game pixel art, chunky visible square pixels, dark warm brown outline, small restrained cream/pink/teal palette, simple readable silhouette, warm friendly expression. NOT realistic, NOT smooth vector.
Composition: square canvas, single rabbit centered horizontally and vertically. Rabbit INCLUDING EARS should occupy only the middle 72% of the canvas height. Leave at least 14% fully transparent padding at top and bottom and at least 14% on left and right. Do not enlarge the character to fill the image.
CRITICAL OUTPUT REQUIREMENT: return a PNG with TRUE TRANSPARENT BACKGROUND, using an actual RGBA PNG alpha channel. Every pixel surrounding the rabbit must have alpha=0. Do not render a checkerboard: the image data itself must be transparent. No background color, floor, shadow, scenery, words, logo, watermark or other character. This is a transparent game sprite, not a mockup or depiction on a background.
```

## Fox, panda, and dragon additions (2026-09-08 JST)

Three more original selectable idle sprites were generated with the **built-in `image_gen.imagegen` tool**, one independent call per species. All three succeeded on their first generation. No CLI/API-key fallback or model override was used. The source PNGs' embedded C2PA software-agent metadata identifies `gpt-image`, version `2.0`; this records the returned metadata rather than an explicit model-selection setting.

The existing dog, cat, and rabbit images were visually inspected to match their retro pixel-art family. The final generations used the text-only prompts recorded below, with no reference-image inputs. No existing project image was overwritten. No third-party image, artist name, brand, or named franchise character was included in the prompts.

### Files and original tool output locations

| Species | Source in this folder | Runtime derivative | Original built-in output |
| --- | --- | --- | --- |
| Fox | `portfolio-fox-idle-v1.png` | `portfolio-fox-idle-v1-web.png` | `/Users/sj/.codex/generated_images/01a07c5b-9621-75b1-94a4-a9ebcd89ee00/exec-a4994d16-b1ce-4b64-a3b3-0f6f9a40942b.png` |
| Panda | `portfolio-panda-idle-v1.png` | `portfolio-panda-idle-v1-web.png` | `/Users/sj/.codex/generated_images/01a07c5b-9621-75b1-94a4-a9ebcd89ee00/exec-ebf83008-a359-4f6b-bceb-300e3bd6305b.png` |
| Dragon | `portfolio-dragon-idle-v1.png` | `portfolio-dragon-idle-v1-web.png` | `/Users/sj/.codex/generated_images/01a07c5b-9621-75b1-94a4-a9ebcd89ee00/exec-7a7af01d-f102-4316-bebb-cc5f640e0933.png` |

Every source is 1254 × 1254 pixels. Each was copied into the workspace without changing the generated pixels, then mechanically reduced to a 256 × 256 runtime derivative with macOS `sips -Z 256`. Original tool outputs were retained. No manual masking, background removal, or Python image editing was performed.

These species each provide one idle image; they do not add emotion-specific sprite sets.

### QA

All six files were checked with `sips` and a read-only PNG alpha audit: noninterlaced 8-bit RGBA, alpha values spanning 0–255, and real fully transparent background pixels. The audit only read images. The 256px runtime images were also visually inspected.

| Runtime image | Fully transparent pixels / 65,536 | Main silhouette bounds, alpha > 127 | File size |
| --- | ---: | --- | ---: |
| Fox | 47,148 | x=65–204, y=32–223 | 49,016 bytes |
| Panda | 44,031 | x=60–195, y=34–223 | 53,016 bytes |
| Dragon | 46,029 | x=51–211, y=30–216 | 59,987 bytes |

The main visible silhouettes have more than 10% padding on all four sides. All characters are centered, fully visible, recognizable at runtime size, and visually consistent with the existing warm outlined pixel-art pets. No background grid, text, watermark, cropped ears/horns/wings/tails, or additional character is visible. As with the earlier generated sprites, faint partially transparent edge pixels can occur outside the main silhouette; no opaque checkerboard was accepted.

### Final fox prompt

```text
Use case: stylized-concept.
Asset type: game-ready selectable pet mascot sprite for a productivity web app.
Generate ONE adorable original orange-and-cream FOX wearing a small plain forest-green bandana. Friendly neutral seated idle pose, upright pointed ears with dark brown tips, gentle amber eyes, cream muzzle and chest, dark brown paws, one large fluffy orange tail with a cream tip curving alongside its body. The complete fox including ear tips, paws and tail must be visible. No props.
Style: handcrafted 1990s game pixel art, chunky visible square pixels, dark warm brown outline, restrained orange/cream/brown/green palette, compact big-headed friendly pet proportions and simple readable silhouette. Warm friendly expression. NOT realistic, NOT smooth vector. This should belong to the same kind of charming retro pixel-art mascot family as a small bandana-wearing dog, cat and rabbit.
Composition: square canvas, single fox centered horizontally and vertically. Fox INCLUDING EARS should occupy only the middle 72% of the canvas height and no more than 72% of its width. Leave at least 14% fully transparent padding on ALL FOUR sides. Do not enlarge the character to fill the image.
CRITICAL OUTPUT REQUIREMENT: return a PNG with TRUE TRANSPARENT BACKGROUND, using an actual RGBA PNG alpha channel. Every pixel surrounding the fox must have alpha=0. Do not render a checkerboard: the image data itself must be transparent. No background color, floor, shadow, scenery, words, logo, watermark, existing franchise character or other animal. This is a transparent game sprite, not a mockup or depiction on a background.
```

### Final panda prompt

```text
Use case: stylized-concept.
Asset type: game-ready selectable pet mascot sprite for a productivity web app.
Generate ONE adorable original giant PANDA cub wearing a small plain muted plum-purple bandana. Friendly neutral seated idle pose, round black ears, gentle dark brown eyes inside characteristic black oval eye patches, warm cream-white face and round belly, charcoal black arms and legs, short rounded paws. Entire body including ears, all paws and subtle tail visible. No bamboo, food or props.
Style: handcrafted 1990s game pixel art, chunky visible square pixels, dark warm brown outline, restrained cream/charcoal/brown/plum palette, compact big-headed friendly pet proportions and simple readable silhouette. Warm gentle expression. NOT realistic, NOT smooth vector. This should belong to the same kind of charming retro pixel-art mascot family as a small bandana-wearing dog, cat and rabbit.
Composition: square canvas, single panda centered horizontally and vertically. Panda INCLUDING EARS should occupy only the middle 72% of the canvas height and no more than 72% of its width. Leave at least 14% fully transparent padding on ALL FOUR sides. Do not enlarge the character to fill the image.
CRITICAL OUTPUT REQUIREMENT: return a PNG with TRUE TRANSPARENT BACKGROUND, using an actual RGBA PNG alpha channel. Every pixel surrounding the panda must have alpha=0. Do not render a checkerboard: the image data itself must be transparent. No background color, floor, shadow, scenery, words, logo, watermark, existing franchise character or other animal. This is a transparent game sprite, not a mockup or depiction on a background.
```

### Final dragon prompt

```text
Use case: stylized-concept.
Asset type: game-ready selectable pet mascot sprite for a productivity web app.
Generate ONE adorable original small moss-green DRAGON wearing a small plain muted coral bandana. Friendly neutral seated idle pose, gentle amber-brown eyes, rounded snout, two short ivory horns, tiny softly curved wings with muted golden membranes, warm cream belly scales, small rounded feet, one curled green tail with a blunt tip. This is a gentle pet, not a fierce monster. The entire dragon including both horn tips, wing tips, feet and tail must be visible. No fire, weapon or props.
Style: handcrafted 1990s game pixel art, chunky visible square pixels, dark warm brown outline, restrained moss-green/cream/ivory/gold/coral palette, compact big-headed friendly pet proportions and simple readable silhouette. Warm gentle expression. NOT realistic, NOT smooth vector. This should belong to the same kind of charming retro pixel-art mascot family as a small bandana-wearing dog, cat and rabbit. Original generic fantasy creature, no resemblance to a named franchise character.
Composition: square canvas, single dragon centered horizontally and vertically. Dragon INCLUDING HORNS AND WINGS should occupy only the middle 72% of the canvas height and no more than 72% of its width. Leave at least 14% fully transparent padding on ALL FOUR sides. Do not enlarge the character to fill the image.
CRITICAL OUTPUT REQUIREMENT: return a PNG with TRUE TRANSPARENT BACKGROUND, using an actual RGBA PNG alpha channel. Every pixel surrounding the dragon must have alpha=0. Do not render a checkerboard: the image data itself must be transparent. No background color, floor, shadow, scenery, words, logo, watermark, existing franchise character or other creature. This is a transparent game sprite, not a mockup or depiction on a background.
```
