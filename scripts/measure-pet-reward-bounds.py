"""Measure finished PNGs only: no image edits, generator calls, or alpha-report changes."""
import json
from pathlib import Path
from cat_review_cutout import read_png

ROOT = Path(__file__).resolve().parents[1]
metrics = {}
for species in ('dog', 'cat', 'rabbit', 'fox', 'panda', 'dragon'):
    for level in range(1, 6):
        for kind in ('hat', 'bow', 'mat'):
            width, height, pixels = read_png(ROOT / f'frontend/assets/pet/rewards-v2/{species}/lv-{level}-{kind}.png')
            visible = [n for n, alpha in enumerate(pixels[3::4]) if alpha >= 128]
            xs, ys = [n % width for n in visible], [n // width for n in visible]
            x, y, w, h = min(xs), min(ys), max(xs)+1-min(xs), max(ys)+1-min(ys)
            metrics[f'{species}_{level}_{kind}'] = { 'x':round(x/width,6), 'y':round(y/height,6),
                'width':round(w/width,6), 'height':round(h/height,6), 'ratio':round(w/h,6) }
(ROOT / 'frontend/assets/pet/rewards-v2/metrics.json').write_text(json.dumps(metrics, indent=2) + '\n')
print(json.dumps({'measured':len(metrics)}), flush=True)
