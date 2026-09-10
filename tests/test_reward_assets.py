"""Verify the checked-in delivery assets, with no generator or network dependency."""
import hashlib
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from cat_review_cutout import read_png


class RewardAssets(unittest.TestCase):
    def test_all_ninety_are_distinct_rgba_cutouts_with_matching_bounds(self):
        script = (ROOT / 'frontend/pet-reward-metrics.js').read_text()
        metrics = json.loads(script.split('const PetRewardMetrics = ', 1)[1].rstrip().removesuffix(';'))
        self.assertEqual(len(metrics), 90)
        hashes = set()
        for species in ('dog', 'cat', 'rabbit', 'fox', 'panda', 'dragon'):
            for level in range(1, 6):
                for kind in ('hat', 'bow', 'mat'):
                    key = f'{species}_{level}_{kind}'
                    with self.subTest(key=key):
                        asset = ROOT / f'frontend/assets/pet/rewards-v2/{species}/lv-{level}-{kind}.png'
                        raw = asset.read_bytes()
                        self.assertEqual(raw[25], 6, 'RGBA PNG color type')
                        hashes.add(hashlib.sha256(raw).hexdigest())
                        width, height, pixels = read_png(asset)
                        alpha = pixels[3::4]
                        self.assertGreater(alpha.count(0), width * height * .2)
                        visible = [n for n, value in enumerate(alpha) if value >= 128]
                        self.assertGreater(len(visible), width * height * .015)
                        xs, ys = [n % width for n in visible], [n // width for n in visible]
                        measured = {'x': min(xs)/width, 'y': min(ys)/height,
                                    'width': (max(xs)+1-min(xs))/width, 'height': (max(ys)+1-min(ys))/height,
                                    'ratio': (max(xs)+1-min(xs))/(max(ys)+1-min(ys))}
                        for field, value in measured.items():
                            self.assertAlmostEqual(metrics[key][field], value, places=5)
        self.assertEqual(len(hashes), 90, 'No duplicate assets substituted for distinct designs')

    def test_all_six_atlases_keep_their_full_canvas_and_have_real_alpha(self):
        for species in ('dog', 'cat', 'rabbit', 'fox', 'panda', 'dragon'):
            with self.subTest(species=species):
                width, height, pixels = read_png(ROOT / f'frontend/assets/pet/portfolio-{species}-atlas-v2-alpha.png')
                self.assertEqual((width, height), (1536, 1024))
                self.assertGreater(pixels[3::4].count(0), width * height * .35)


if __name__ == '__main__':
    unittest.main()
