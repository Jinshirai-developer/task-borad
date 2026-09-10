"""Local alpha-only extraction invariants; no browser, services, or dependencies."""
import sys
from pathlib import Path
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from cat_review_cutout import cutout, read_png, write_png


class CatCutoutTests(unittest.TestCase):
    def test_exterior_only_preserves_enclosed_highlight_cream_and_outline(self):
        pixels = bytearray([242, 242, 244, 255] * 25)
        # A dark enclosing ring, with an ivory pixel and enclosed white highlight.
        for n in (6, 7, 8, 11, 13, 16, 17, 18):
            pixels[n * 4:n * 4 + 4] = bytes([45, 24, 15, 255])
        pixels[12 * 4:12 * 4 + 4] = bytes([255, 255, 255, 255])
        pixels[2 * 4:2 * 4 + 4] = bytes([254, 236, 203, 255])
        after, mask = cutout(5, 5, pixels)
        self.assertEqual(after[3], 0)
        self.assertEqual(after[12 * 4 + 3], 255)
        self.assertEqual(after[2 * 4 + 3], 255)
        self.assertEqual(after[6 * 4 + 3], 255)
        self.assertEqual(sum(mask), 15)
        for channel in (0, 1, 2):
            self.assertEqual(after[channel::4], pixels[channel::4])

    def test_png_round_trip_and_no_overwrite(self):
        with tempfile.TemporaryDirectory(prefix="cat-alpha-test-") as directory:
            path = Path(directory) / "sample.png"
            pixels = bytearray([15, 30, 45, 0, 250, 230, 210, 255])
            write_png(path, 2, 1, pixels)
            self.assertEqual(read_png(path), (2, 1, pixels))
            with self.assertRaises(FileExistsError):
                write_png(path, 2, 1, pixels)


if __name__ == "__main__":
    unittest.main()
