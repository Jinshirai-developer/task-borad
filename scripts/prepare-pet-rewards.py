"""Prepare generated reward PNGs using the background-only method approved in this thread.

No repainting, RGB edits, resizing, API calls, or overwriting generator originals.
Outputs are derived image artifacts and alpha bounding-box metadata, not new drawings.
"""
import hashlib
import json
import sys
from pathlib import Path

from cat_review_cutout import cutout, read_png, write_png

ROOT = Path(__file__).resolve().parents[1]
manifest = json.loads((ROOT / "docs/rewards-v14-generation.json").read_text())
only = set(sys.argv[1:]) # Optional explicit job keys for reviewed replacement assets.
assert only <= {job['key'] for job in manifest['jobs']}, 'Unknown reward key'
metrics = json.loads((ROOT / 'frontend/assets/pet/rewards-v2/metrics.json').read_text()) if only else {}
report = json.loads((ROOT / 'docs/reward-generation-v14/alpha-report.json').read_text()) if only else []
for original in manifest["jobs"]:
    if only and original['key'] not in only:
        continue
    record = ROOT / "docs/reward-generation-v14" / (original["key"] + ".json")
    job = json.loads(record.read_text()) if record.exists() else original
    if not job.get("rawPath"):
        continue
    source = Path(job["rawPath"])
    target = ROOT / job["asset"]
    width, height, before = read_png(source)
    transparent = sum(alpha == 0 for alpha in before[3::4])
    if transparent > width * height * .2:
        pixels, removed = before, 0
    else:
        pixels, exterior = cutout(width, height, before, minimum=job.get('backgroundMinimum', 215))
        removed = sum(exterior)
    assert all(before[c::4] == pixels[c::4] for c in (0, 1, 2)), job["key"]
    visible = [i for i, alpha in enumerate(pixels[3::4]) if alpha >= 128]
    assert width * height * .015 < len(visible) < width * height * .8, job["key"]
    xs, ys = [i % width for i in visible], [i // width for i in visible]
    x, y, right, bottom = min(xs), min(ys), max(xs) + 1, max(ys) + 1
    assert x > 0 and y > 0 and right < width and bottom < height, "Clipped object: " + job["key"]
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists():
        assert read_png(target) == (width, height, pixels), "Existing output differs: " + job["key"]
    else:
        write_png(target, width, height, pixels)
    metrics[job["key"]] = {
        "x": round(x / width, 6), "y": round(y / height, 6),
        "width": round((right - x) / width, 6), "height": round((bottom - y) / height, 6),
        "ratio": round((right - x) / (bottom - y), 6)
    }
    report = [entry for entry in report if entry['key'] != job['key']]
    report.append({"key": job["key"], "path": job["asset"], "removed": removed,
                   "rgbUnchanged": True, "sourceSha256": hashlib.sha256(source.read_bytes()).hexdigest()})
output = ROOT / "frontend/assets/pet/rewards-v2"
output.mkdir(parents=True, exist_ok=True)
(output / "metrics.json").write_text(json.dumps(metrics, indent=2) + "\n")
(ROOT / "docs/reward-generation-v14/alpha-report.json").write_text(json.dumps(report, indent=2) + "\n")
print(json.dumps({"prepared": len(report), "expected": len(manifest["jobs"]), "keys": [item["key"] for item in report]}), flush=True)
