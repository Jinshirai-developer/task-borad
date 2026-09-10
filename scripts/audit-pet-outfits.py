"""Read-only PNG audit for the generated whole-pet outfit collection.

Never changes images. An optional JSON report is a derived audit artifact.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct

from cat_review_cutout import read_png


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pixels', action='store_true', help='Decode alpha and visible bounds')
    parser.add_argument('--require-complete', action='store_true')
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[1]
    jobs = json.loads((repo / 'docs/pet-outfits-v1/manifest.json').read_text())['jobs']
    records, missing, hashes = [], [], set()
    for job in jobs:
        path = repo / job['asset']
        if not path.is_file():
            missing.append(job['key'])
            continue
        source = path.read_bytes()
        assert source[:8] == b'\x89PNG\r\n\x1a\n', path
        width, height, depth, color = struct.unpack('>IIBB', source[16:26])
        digest = hashlib.sha256(source).hexdigest()
        assert digest not in hashes, 'Duplicate image: ' + job['key']
        hashes.add(digest)
        result = json.loads((repo / 'docs/pet-outfits-v1/results' / (job['key'] + '.json')).read_text())
        assert (result['key'], result['asset'], result['species'], result['level'], result['kind']) == (
            job['key'], job['asset'], job['species'], job['level'], job['kind'])
        assert hashlib.sha256(Path(result['rawPath']).read_bytes()).hexdigest() == digest, 'Original copy mismatch: ' + job['key']
        assert min(width, height) >= 512, (job['key'], width, height)
        record = {'key': job['key'], 'asset': job['asset'], 'width': width, 'height': height,
                  'depth': depth, 'colorType': color, 'sha256': digest, 'bytes': len(source),
                  'matchesGeneratedOriginal': True}
        if args.pixels and color == 2:
            # RGB PNGs cannot carry per-pixel alpha; no expensive decode needed.
            record['transparentFraction'] = 0
            record['hasRealTransparency'] = False
        elif args.pixels:
            w, h, rgba = read_png(path)
            transparent = sum(value == 0 for value in rgba[3::4])
            record['transparentFraction'] = transparent / (w * h)
            record['hasRealTransparency'] = transparent > w * h * .1
            if transparent:
                points = [i for i, value in enumerate(rgba[3::4]) if value >= 128]
                xs, ys = [i % w for i in points], [i // w for i in points]
                record['visibleBounds'] = [min(xs), min(ys), max(xs) + 1, max(ys) + 1]
        records.append(record)
    result = {'expected': 90, 'present': len(records), 'missing': missing, 'images': records}
    assert len(jobs) == 90
    if args.report:
        with args.report.open('x') as output:
            json.dump(result, output, ensure_ascii=False, indent=2)
            output.write('\n')
    print(json.dumps({k: v for k, v in result.items() if k != 'images'}), flush=True)
    if args.require_complete:
        assert len(records) == 90 and not missing


if __name__ == '__main__':
    main()
