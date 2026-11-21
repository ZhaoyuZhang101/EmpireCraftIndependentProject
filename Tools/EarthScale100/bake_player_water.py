"""Bake the public-domain Natural Earth v5.1.2 shoreline onto each player grid.

Usage: python Tools/EarthScale100/bake_player_water.py [--check] [--cache PATH]
Dependencies: numpy, Pillow. The NOAA ETOPO overview and its checksum ship with
the project. Signed reference levels are estimated from the original shoreline
terrain. They are not surveyed gauge heights or a dynamic lake-level record.
"""
import argparse
import hashlib
import json
import struct
import urllib.request
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

from bake_player_presets import ROOT, RESOURCES, SOURCE, WIDTHS


LAYERS = {
    "ocean": "f9696a1337c746a0f6c8c13bc60d0f230d2ef8d105198d5657726c8f8e763fc2",
    "lakes": "2d036f53dedec578001c5c30c2959ee7d4eebc1306900fa4367c49929ec8f2d9",
}
DRY, MARINE, PASSAGE = -32768, -32767, -32766


def features(layer, cache):
    path = cache / (layer + ".geojson")
    if not path.exists():
        url = ("https://raw.githubusercontent.com/nvkelso/natural-earth-vector/"
               f"v5.1.2/geojson/ne_10m_{layer}.geojson")
        path.write_bytes(urllib.request.urlopen(url, timeout=90).read())
    raw = path.read_bytes()
    if hashlib.sha256(raw).hexdigest() != LAYERS[layer]:
        raise ValueError(f"Natural Earth {layer} checksum mismatch")
    return json.loads(raw)["features"]


def polygons(feature):
    geometry = feature["geometry"]
    return geometry["coordinates"] if geometry["type"] == "MultiPolygon" else [geometry["coordinates"]]


def ring_pixels(ring, width):
    a = np.array(ring)
    return list(map(tuple, np.floor((a * [1, -1] + [180, 90]) * (width / 360)).astype(int)))


def draw_polygon(image, rings, value, background):
    draw = ImageDraw.Draw(image)
    for i, ring in enumerate(rings):
        draw.polygon(ring_pixels(ring, image.width), fill=value if i == 0 else background)


def lake_level(ring, terrain):
    # Approximately equal arc lengths keep densely digitized shoreline sections
    # from outvoting the rest. The integer mode captures flat shores and lake
    # surfaces; a median resolves ties between distinct shoreline elevations.
    a = np.array(ring)
    dx = np.diff(a[:, 0]) * np.cos(np.deg2rad((a[1:, 1] + a[:-1, 1]) / 2))
    along = np.r_[0, np.hypot(dx, np.diff(a[:, 1])).cumsum()]
    sample = np.linspace(0, along[-1], max(16, int(along[-1] * 24)), endpoint=False)
    w = terrain.shape[1]
    x = np.floor((np.interp(sample, along, a[:, 0]) + 180) * (w / 360)).astype(int) % w
    y = np.clip(np.floor((90 - np.interp(sample, along, a[:, 1])) * (w / 360)).astype(int), 0, w // 2 - 1)
    values, counts = np.unique(terrain[y, x], return_counts=True)
    return int(np.median(values[counts == counts.max()]))


def encode(grid):
    # North-first signed metre/classification spans. Runs may cross rows.
    values = grid.ravel()
    edges = np.r_[0, np.flatnonzero(np.diff(values) != 0) + 1, values.size]
    runs = np.empty(len(edges) - 1, dtype=[("value", "<i2"), ("count", "<i4")])
    runs["value"] = values[edges[:-1]]
    runs["count"] = np.diff(edges)
    return struct.pack("<4sii", b"OWR1", grid.shape[1], len(runs)) + runs.tobytes()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--cache", type=Path, default=ROOT / "ValidationOutput/BasinWater")
    args = parser.parse_args()
    args.cache.mkdir(parents=True, exist_ok=True)
    overview = json.loads((SOURCE / "metadata.json").read_text(encoding="utf8"))["overview"]
    raw = (SOURCE / overview["file"]).read_bytes()
    if hashlib.sha256(raw).hexdigest() != overview["sha256"]:
        raise ValueError("ETOPO overview checksum mismatch")
    terrain = np.frombuffer(raw, "<i2").reshape(overview["dimensions"][::-1])
    ocean = [poly for f in features("ocean", args.cache) for poly in polygons(f)]
    # The world-ocean polygon spans all longitudes. Disconnected polygons in
    # the same source layer are enclosed inland water, including the Caspian.
    global_sea = max(ocean, key=lambda p: np.ptp(np.array(p[0])[:, 0]))
    inland = [p for p in ocean if p is not global_sea]
    inland += [p for f in features("lakes", args.cache) for p in polygons(f)]
    marine = Image.new("L", (terrain.shape[1], terrain.shape[0]))
    draw_polygon(marine, global_sea, 255, 0)
    lakes = Image.new("I", marine.size, DRY)
    for poly in inland:
        draw_polygon(lakes, poly, lake_level(poly[0], terrain), DRY)
    for width in WIDTHS:
        size = (width, width // 2)
        water = np.asarray(marine.resize(size, Image.Resampling.BOX))
        survey = np.array(lakes.resize(size, Image.Resampling.NEAREST), dtype="<i2")
        # Intersected shoreline cells can carry a narrow channel across a
        # positive cell-average bed. Their dry centres stay available to land.
        coast = Image.new("L", size)
        draw = ImageDraw.Draw(coast)
        for ring in global_sea:
            draw.line(ring_pixels(ring, width), fill=255, width=1)
        survey[(survey == DRY) & ((water > 0) | (np.asarray(coast) != 0))] = PASSAGE
        survey[water >= 128] = MARINE
        payload = encode(survey)
        path = RESOURCES / f"EarthWater{width}.bytes"
        if args.check:
            if path.read_bytes() != payload:
                raise ValueError(f"{path.name} differs from the reference")
        else:
            path.write_bytes(payload)
        print(f"{path.name}: {len(payload)} bytes, sha256 {hashlib.sha256(payload).hexdigest()}")


if __name__ == "__main__":
    main()
