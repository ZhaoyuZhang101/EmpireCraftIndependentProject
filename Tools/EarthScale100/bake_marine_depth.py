"""Bake native NOAA ETOPO 2022 60s depths for the Earth's surveyed marine regions.

OBD1 contains the atlas width and height (little-endian int32), then a raw
Deflate RG8 texture. A 1440 x 720 directory in its first 507 rows addresses
south-first 15 x 15 native samples per quarter degree, with one gutter texel on
every side. R stores log2(1 + depth in metres) / 14, G is wet-data support.
Dry / nodata samples are (0, 0) for support-normalised bilinear filtering.
"""
import argparse
import hashlib
import json
import struct
import zlib
from pathlib import Path

import numpy as np
from PIL import Image

from bake_player_presets import RESOURCES, ROOT, SOURCE


COLS, ROWS = 1440, 720
WIDTH, SAMPLES = 2048, 15
PAGE = SAMPLES + 2
HEADER_ROWS = (COLS * ROWS + WIDTH - 1) // WIDTH


def regional_keys(path):
    shore = path.read_bytes()
    if shore[:4] not in (b"OSH1",b"OSH2"):
        raise ValueError("Unrecognised regional shoreline directory")
    count = ROWS * COLS
    directory = zlib.decompressobj(-15).decompress(shore[20:], count * 2)
    return np.flatnonzero(np.frombuffer(directory, dtype="<u2", count=count))


def bake(source, shore):
    metadata = json.loads((SOURCE / "metadata.json").read_text(encoding="utf8"))
    digest = hashlib.sha256()
    with source.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    if digest.hexdigest() != metadata["source_sha256"]:
        raise ValueError("Expected the NOAA ETOPO 2022 60s surface GeoTIFF from global/metadata.json")

    keys = regional_keys(shore)
    per_row = WIDTH // PAGE
    height = HEADER_ROWS + (len(keys) + per_row - 1) // per_row * PAGE
    if not 0 < len(keys) < 65536 or height > 8192:
        raise ValueError("Marine depth regions exceed the runtime atlas limits")
    data = np.zeros((height, WIDTH, 2), np.uint8)
    slots = np.arange(1, len(keys) + 1, dtype=np.uint16)
    data.reshape(-1, 2)[keys] = np.column_stack((slots % 256, slots // 256))
    # Limit the larger image allowance to the checked source.
    old_limit = Image.MAX_IMAGE_PIXELS
    Image.MAX_IMAGE_PIXELS = COLS * ROWS * SAMPLES**2
    try:
        with Image.open(source) as image:
            if image.size != (COLS * SAMPLES, ROWS * SAMPLES) or image.mode != "F":
                raise ValueError("Unexpected geographic raster resolution or sample format")
            # South-up indexing here and in the shader. Adjacent gutters index
            # the same source nodes, including at the antimeridian.
            dem = np.asarray(image)[::-1]
            nodata = float(image.tag_v2[42113])
    finally:
        Image.MAX_IMAGE_PIXELS = old_limit
    offsets = np.arange(-1, SAMPLES + 1)
    for slot, key in enumerate(keys):
        y, x = divmod(key, COLS)
        xx = (x * SAMPLES + offsets) % dem.shape[1]
        yy = np.clip(y * SAMPLES + offsets, 0, dem.shape[0] - 1)
        z = dem[np.ix_(yy, xx)]
        wet = np.isfinite(z) & (z < 0) & (z != nodata)
        depth = np.where(wet, -z, 0)
        row, col = divmod(slot, per_row)
        bottom, left = HEADER_ROWS + row * PAGE, col * PAGE
        page = data[bottom:bottom + PAGE, left:left + PAGE]
        page[:, :, 0] = np.rint(np.log2(1 + depth) * (255 / 14)).clip(0, 255)
        page[:, :, 1] = wet * 255

    compressor = zlib.compressobj(level=9, wbits=-15)
    return struct.pack("<4s2i", b"OBD1", WIDTH, height) + compressor.compress(data) + compressor.flush()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=ROOT / "ValidationOutput/EarthScale100/global/ETOPO2022-60s.tif")
    parser.add_argument("--shore", type=Path, default=RESOURCES / "EarthGlobalStraitShore.bytes")
    parser.add_argument("--output", type=Path, default=RESOURCES / "EarthMarineDepth.bytes")
    parser.add_argument("--check", action="store_true", help="compare a fresh bake with the installed asset")
    args = parser.parse_args()
    payload = bake(args.source,args.shore)
    path = args.output
    if args.check:
        if path.read_bytes() != payload:
            raise ValueError("Installed marine depths differ from the NOAA source bake")
    else:
        path.write_bytes(payload)
    print(f"{path.name}: {len(payload)} bytes, sha256 {hashlib.sha256(payload).hexdigest()}")


if __name__ == "__main__":
    main()
