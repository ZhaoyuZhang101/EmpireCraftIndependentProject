"""Bake stereographic ETOPO relief and Natural Earth marine shoreline charts."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image

from bake_player_presets import RESOURCES, ROOT, SOURCE
from bake_player_water import draw_polygon, features, polygons


SIZE = 1537  # A texel centre coincides with each geographic pole.
EXTENT = np.tan(np.pi / 8)  # 45 degrees from the pole at the middle of a side.


def signed_coast(coverage):
    """Bounded eight-neighbour distance, in quarter chart texels, positive at sea."""
    wet = coverage >= .5
    edge = np.zeros_like(wet)
    cut = wet[1:] != wet[:-1]
    edge[1:] |= cut
    edge[:-1] |= cut
    cut = wet[:, 1:] != wet[:, :-1]
    edge[:, 1:] |= cut
    edge[:, :-1] |= cut
    distance = np.where(edge, np.abs(coverage - .5), 32).astype(np.float32)
    for _ in range(32):
        p = np.pad(distance, 1, mode="edge")
        axial = np.minimum(np.minimum(p[1:-1, :-2], p[1:-1, 2:]), np.minimum(p[:-2, 1:-1], p[2:, 1:-1]))
        diagonal = np.minimum(np.minimum(p[:-2, :-2], p[:-2, 2:]), np.minimum(p[2:, :-2], p[2:, 2:]))
        distance = np.minimum(distance, np.minimum(axial + 1, diagonal + np.sqrt(np.float32(2))))
    return np.clip(np.rint(128 + np.where(wet, distance, -distance) * 4), 1, 255).astype(np.uint8)


def bake(cache):
    metadata = json.loads((SOURCE / "metadata.json").read_text(encoding="utf8"))["overview"]
    raw = (SOURCE / metadata["file"]).read_bytes()
    if hashlib.sha256(raw).hexdigest() != metadata["sha256"]:
        raise ValueError("ETOPO overview checksum mismatch")
    bed = np.frombuffer(raw, "<i2").reshape(metadata["dimensions"][::-1])
    height, width = bed.shape
    cache.mkdir(parents=True, exist_ok=True)
    ocean = [p for f in features("ocean", cache) for p in polygons(f)]
    global_sea = max(ocean, key=lambda p: np.ptp(np.asarray(p[0])[:, 0]))
    marine = Image.new("L", (width*2, height*2))
    draw_polygon(marine, global_sea, 255, 0)
    marine = np.asarray(marine)
    axis = ((np.arange(SIZE) + .5) / SIZE * 2 - 1) * EXTENT
    result = np.empty((SIZE, SIZE * 2), "<u2")
    coverage = np.empty((SIZE, SIZE * 2), np.float32)
    for start in range(0, SIZE, 64):
        rows = slice(start, start + 64)
        x, z = np.broadcast_arrays(axis, axis[rows, None])
        r = np.hypot(x, z)
        longitude = np.arctan2(x, z)
        col = (longitude / (2 * np.pi) + .5) * width - .5
        left = np.floor(col).astype(int) % width
        right, fx = (left + 1) % width, col - np.floor(col)
        coast_col = (longitude / (2 * np.pi) + .5) * marine.shape[1] - .5
        x0 = np.floor(coast_col).astype(int) % marine.shape[1]
        x1, wx = (x0 + 1) % marine.shape[1], coast_col - np.floor(coast_col)
        for side, sign in enumerate((-1, 1)):
            latitude = sign * (np.pi / 2 - 2 * np.arctan(r))
            v = .5 - latitude / np.pi
            row = np.clip(v * height - .5, 0, height - 1)
            top = row.astype(int)
            bottom, fy = np.minimum(top + 1, height - 1), row - top
            a = bed[top, left] * (1 - fx) + bed[top, right] * fx
            b = bed[bottom, left] * (1 - fx) + bed[bottom, right] * fx
            ring = np.minimum(1, 2 * height * np.minimum(v, 1 - v))
            pole = bed[0 if sign == 1 else -1].mean()
            values = pole * (1 - ring) + (a * (1 - fy) + b * fy) * ring
            columns = slice(side * SIZE, (side + 1) * SIZE)
            result[rows, columns] = np.rint(values) + 32768
            coast_row = np.clip(v * marine.shape[0] - .5, 0, marine.shape[0] - 1)
            y0 = coast_row.astype(int)
            y1, wy = np.minimum(y0 + 1, marine.shape[0] - 1), coast_row - y0
            a = marine[y0, x0] * (1-wx) + marine[y0, x1] * wx
            b = marine[y1, x0] * (1-wx) + marine[y1, x1] * wx
            coverage[rows, columns] = (a * (1-wy) + b * wy) / 255
    coast = np.concatenate([signed_coast(face) for face in np.hsplit(coverage, 2)], axis=1)
    return {"EarthPolarBed.bytes": result.tobytes(), "EarthPolarCoast.bytes": coast.tobytes()}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--cache", type=Path, default=ROOT / "ValidationOutput/BasinWater")
    args = parser.parse_args()
    for name, data in bake(args.cache).items():
        path = RESOURCES / name
        if args.check:
            if path.read_bytes() != data:
                raise ValueError(f"{name} differs from the geographic reference")
        else:
            path.write_bytes(data)
        print(f"{name}: two {SIZE} x {SIZE} caps, SHA-256 {hashlib.sha256(data).hexdigest()}")
