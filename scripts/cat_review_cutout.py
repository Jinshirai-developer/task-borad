"""Remove the review cat's pale checkerboard, without redrawing any RGB pixels.

User-approved, local-only Python background removal. No packages or network needed.
This deliberately supports only non-interlaced 8-bit RGB/RGBA PNGs, not arbitrary art.
Only near-neutral light pixels connected to the canvas edge become transparent.
Enclosed highlights, cream fur, dark outlines, and saturated accessories stay intact.
"""

import argparse
from collections import deque
import hashlib
import json
from pathlib import Path
import struct
import zlib


SIGNATURE = b"\x89PNG\r\n\x1a\n"


def paeth(a, b, c):
    p = a + b - c
    distances = (abs(p - a), abs(p - b), abs(p - c))
    return (a, b, c)[distances.index(min(distances))]


def read_png(path):
    source = Path(path).read_bytes()
    if source[:8] != SIGNATURE:
        raise ValueError("Expected PNG")
    offset, compressed, header = 8, bytearray(), None
    while offset < len(source):
        length = struct.unpack_from(">I", source, offset)[0]
        kind = source[offset + 4:offset + 8]
        payload = source[offset + 8:offset + 8 + length]
        crc = struct.unpack_from(">I", source, offset + 8 + length)[0]
        if zlib.crc32(kind + payload) & 0xffffffff != crc:
            raise ValueError("PNG checksum mismatch")
        if kind == b"IHDR":
            header = struct.unpack(">IIBBBBB", payload)
        elif kind == b"IDAT":
            compressed.extend(payload)
        elif kind == b"IEND":
            break
        offset += 12 + length
    if header is None:
        raise ValueError("Missing PNG header")
    width, height, depth, color, compression, filtering, interlace = header
    if depth != 8 or color not in (2, 6) or any((compression, filtering, interlace)):
        raise ValueError("Only non-interlaced 8-bit RGB/RGBA supported")
    channels = 3 if color == 2 else 4
    stride = width * channels
    raw = zlib.decompress(compressed)
    if len(raw) != height * (stride + 1):
        raise ValueError("Unexpected PNG data length")
    rgba, previous = bytearray(), bytearray(stride)
    for y in range(height):
        start = y * (stride + 1)
        mode, row = raw[start], bytearray(raw[start + 1:start + 1 + stride])
        if mode not in range(5):
            raise ValueError("Unknown PNG filter")
        if mode:
            for i in range(stride):
                left = row[i - channels] if i >= channels else 0
                above = previous[i]
                corner = previous[i - channels] if i >= channels else 0
                predictor = (left if mode == 1 else above if mode == 2 else
                             (left + above) // 2 if mode == 3 else paeth(left, above, corner))
                row[i] = (row[i] + predictor) & 255
        if channels == 4:
            rgba.extend(row)
        else:
            for i in range(0, stride, 3):
                rgba.extend(row[i:i + 3])
                rgba.append(255)
        previous = row
    return width, height, rgba


def write_png(path, width, height, rgba):
    def chunk(kind, payload):
        return (struct.pack(">I", len(payload)) + kind + payload +
                struct.pack(">I", zlib.crc32(kind + payload) & 0xffffffff))

    stride = width * 4
    scanlines = b"".join(b"\0" + rgba[y * stride:(y + 1) * stride] for y in range(height))
    result = SIGNATURE + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    result += chunk(b"IDAT", zlib.compress(scanlines, 9)) + chunk(b"IEND", b"")
    # Exclusive creation: never overwrite an original or a reviewed asset silently.
    with Path(path).open("xb") as output:
        output.write(result)


def cutout(width, height, rgba, minimum=215, chroma=18):
    count = width * height
    candidate = bytearray(count)
    for n in range(count):
        r, g, b, alpha = rgba[n * 4:n * 4 + 4]
        candidate[n] = alpha == 0 or (min(r, g, b) >= minimum and max(r, g, b) - min(r, g, b) <= chroma)
    exterior, queue = bytearray(count), deque()

    def enqueue(n):
        if candidate[n] and not exterior[n]:
            exterior[n] = 1
            queue.append(n)

    for x in range(width):
        enqueue(x)
        enqueue((height - 1) * width + x)
    for y in range(height):
        enqueue(y * width)
        enqueue(y * width + width - 1)
    while queue:
        n = queue.popleft()
        x, y = n % width, n // width
        if x: enqueue(n - 1)
        if x + 1 < width: enqueue(n + 1)
        if y: enqueue(n - width)
        if y + 1 < height: enqueue(n + width)
    result = bytearray(rgba)
    for n, outside in enumerate(exterior):
        if outside:
            result[n * 4 + 3] = 0
    return result, exterior


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    if args.source.resolve() == args.output.resolve() or args.output.exists():
        parser.error("Output must be a new file; originals are preserved")
    width, height, before = read_png(args.source)
    after, exterior = cutout(width, height, before)
    assert all(before[channel::4] == after[channel::4] for channel in (0, 1, 2))
    assert .25 < sum(exterior) / (width * height) < .85, "Unexpected mask; inspect source before proceeding"
    write_png(args.output, width, height, after)
    check_width, check_height, check_pixels = read_png(args.output)
    assert (check_width, check_height, check_pixels) == (width, height, after)
    print(json.dumps({"source": str(args.source), "output": str(args.output),
                      "width": width, "height": height, "removed_background_pixels": sum(exterior),
                      "all_rgb_unchanged": True, "png_round_trip_verified": True,
                      "source_sha256": hashlib.sha256(args.source.read_bytes()).hexdigest()}, indent=2))


if __name__ == "__main__":
    main()
