"""Background-only alpha extraction; preserves every RGB pixel and all original poses."""
import hashlib
import json
from pathlib import Path
from cat_review_cutout import cutout, read_png, write_png

ROOT = Path(__file__).resolve().parents[1]
report = []
for species in ('dog', 'cat', 'rabbit', 'fox', 'panda', 'dragon'):
    source = ROOT / f'frontend/assets/pet/sources/atlases/portfolio-{species}-atlas-v1.png'
    target = ROOT / f'frontend/assets/pet/portfolio-{species}-atlas-v2-alpha.png'
    width, height, before = read_png(source)
    pixels, exterior = cutout(width, height, before)
    assert (width, height) == (1536, 1024)
    assert .35 < sum(exterior) / (width * height) < .85, species
    assert all(before[c::4] == pixels[c::4] for c in (0, 1, 2)), species
    if target.exists():
        assert read_png(target) == (width, height, pixels), species
    else:
        write_png(target, width, height, pixels)
    report.append({'species': species, 'backgroundPixels': sum(exterior), 'rgbUnchanged': True,
                   'sourceSha256': hashlib.sha256(source.read_bytes()).hexdigest()})
(ROOT / 'docs/reward-generation-v14/atlas-alpha-report.json').write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps(report), flush=True)
