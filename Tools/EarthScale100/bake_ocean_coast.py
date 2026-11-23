"""Bake all marine banks from the pinned Natural Earth 1:10m ocean polygon.

OCS1 stores two independently deflated RGBA textures. The directory contains
the little-endian page ID and signed centre distance (1/32 km, offset 32768).
Zero IDs mark uniform land or open sea. Coastal pages store a signed distance
in 1/32 km steps around 128. Four geographic pages share the four channels
of a tile, so all coasts fit the same native resolution on a 4K device. Page
gutters contain neighbouring geographic samples. The ocean excludes inland lakes.
"""
import argparse
import hashlib
import struct
import zlib
from pathlib import Path

import numpy as np
import shapely
from scipy.spatial import cKDTree
from shapely.geometry import LineString, Polygon

from bake_major_lakes import RADIUS_KM, unit_vectors
from bake_player_presets import RESOURCES, ROOT
from bake_player_water import features, polygons


COLS, ROWS = 1440, 720
SAMPLES = 30  # Native samples per quarter degree before adding two gutter texels.
TILES_PER_ROW = 128
SLOTS_PER_ROW = 4 * TILES_PER_ROW
SATURATION_KM = 127 / 32


def physical_lines(ocean):
    # The world polygon closes along +/-180 and the polar frame. Those edges
    # are geographic cuts, not coastlines. Distances wrap on the unit sphere.
    for ring in shapely.get_rings(ocean):
        points = np.asarray(ring.coords)
        a, b = points[:-1], points[1:]
        cut = ((np.abs(a[:, 0]) == 180) & (a[:, 0] == b[:, 0])) | (
            (np.abs(a[:, 1]) == 90) & (a[:, 1] == b[:, 1]))
        for run in np.split(np.arange(len(cut)), np.flatnonzero(np.diff(cut)) + 1):
            if not cut[run[0]]:
                yield LineString(points[run[0]:run[-1]+2])


def index_lines(lines, spacing):
    xy = shapely.get_coordinates(shapely.segmentize(lines, spacing))
    return cKDTree(unit_vectors(xy[:, 0], xy[:, 1]))


def distance_km(tree, lon, lat, limit, workers):
    cap = 2*np.sin(limit/(2*RADIUS_KM))
    chord, _ = tree.query(unit_vectors(lon, lat), distance_upper_bound=cap, workers=workers)
    return 2*RADIUS_KM*np.arcsin(np.minimum(chord, cap)/2)


def deflate(pixels):
    encoder = zlib.compressobj(8, wbits=-15)
    return encoder.compress(pixels) + encoder.flush()


def pack_pages(terrain, tree, keys, workers):
    page = SAMPLES + 2
    offsets = np.arange(-1, SAMPLES+1) + .5
    encoder = zlib.compressobj(8, wbits=-15)
    packed = bytearray()
    for start in range(0, len(keys), SLOTS_PER_ROW):
        row, col = np.divmod(keys[start:start+SLOTS_PER_ROW], COLS)
        lon = (col[:, None, None]*SAMPLES + offsets[None, None, :]) / (4*SAMPLES) - 180
        lat = (row[:, None, None]*SAMPLES + offsets[None, :, None]) / (4*SAMPLES) - 90
        km = distance_km(tree, lon, lat, SATURATION_KM, workers)
        inside = shapely.contains_xy(terrain, (lon + 180) % 360 - 180, lat)
        blocks = np.zeros((SLOTS_PER_ROW, page, page), "u1")
        blocks[:len(row)] = 128 + np.rint(32*np.where(inside, km, -km)).astype(int)
        rgba = blocks.reshape(TILES_PER_ROW, 4, page, page).transpose(2, 0, 3, 1)
        packed.extend(encoder.compress(np.ascontiguousarray(rgba)))
    packed.extend(encoder.flush())
    return packed


def bake(cache, workers):
    source = [part for feature in features("ocean", cache) for part in polygons(feature)]
    shell, *holes = max(source, key=lambda part: np.ptp(np.asarray(part[0])[:, 0]))
    ocean = Polygon(shell, holes)  # The other polygon in the source is the Caspian Sea.
    shapely.prepare(ocean)
    lines = list(physical_lines(ocean))
    lon, lat = (np.arange(COLS)+.5)/4-180, ((np.arange(ROWS)+.5)/4-90)[:, None]
    tree = index_lines(lines, .015)
    broad = distance_km(tree, lon, lat, 600, workers)
    wet = shapely.contains_xy(ocean, lon, lat)
    directory = np.zeros((ROWS, COLS, 2), "<u2")
    directory[..., 1] = 32768 + np.rint(32*np.where(wet, broad, -broad)).astype(int)
    del tree

    # Include an entire quarter-degree page and its gutter if any part
    # could lie within the encoded distance band, including offshore islands.
    half = (.5 + .5/SAMPLES) / 4
    radius = np.deg2rad(half)*RADIUS_KM*np.hypot(1, np.cos(np.deg2rad(np.maximum(0, np.abs(lat)-half))))
    narrow = broad <= radius + SATURATION_KM + 1.0  # Coarse line sampling bound.
    key_y, key_x = np.where(narrow)
    tree = index_lines(lines, .002)  # At most 112 m from a real vector segment.
    nearest = distance_km(tree, lon[key_x], lat[key_y, 0], 32, workers)
    keep = nearest <= radius[key_y, 0] + SATURATION_KM + .12
    keys = key_y[keep]*COLS + key_x[keep]
    if len(keys) > 65535:
        raise ValueError("Ocean coastline exceeds the two-byte page directory")
    directory.reshape(-1, 2)[keys, 0] = np.arange(1, len(keys)+1)
    print(f"{len(keys)} marine pages across the globe, {4*SAMPLES} samples/degree", flush=True)
    raw = [deflate(directory), pack_pages(ocean, tree, keys, workers)]
    return struct.pack("<4s3I", b"OCS1", len(keys), *(len(part) for part in raw)) + b"".join(raw)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, default=ROOT / "ValidationOutput/BasinWater")
    parser.add_argument("--workers", type=int, default=8)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    args.cache.mkdir(parents=True, exist_ok=True)
    data = bake(args.cache, args.workers)
    target = RESOURCES / "EarthOceanShore.bytes"
    if args.check:
        if target.read_bytes() != data:
            raise ValueError("Installed ocean coastline differs from Natural Earth")
    else:
        target.write_bytes(data)
    print(f"{target.name}: {len(data):,} bytes, SHA-256 {hashlib.sha256(data).hexdigest()}")
