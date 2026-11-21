"""Bake Natural Earth v5.1.2 and the four HydroLAKES v1.0 lakes onto each player grid.

Usage: python Tools/EarthScale100/bake_player_water.py [--check] [--cache PATH]
Dependencies: numpy, Pillow, shapely. The NOAA ETOPO overview and its checksum ship with
the project. Signed reference levels are estimated from the original shoreline
terrain. The HydroLAKES supplement uses its source Elevation field in metres.
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
    "rivers_lake_centerlines": "bb854a900ecbd3b408df46d5e16e3e0f974ba55993f9d8b5c26e855273c0905a",
    "rivers_lake_centerlines_scale_rank": "65dca1bd1729a347dda0837f41b5d152252e33abd2d7ff3370a0539c33bdf89b",
    "geography_regions_polys": "b7b26e50ea917d3696aec87f932def2bf5f890f5770e441d59c162c6f4c92a77",
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


def equal_area(coordinates):
    # Cylindrical equal-area coordinates. The common area scale cancels when
    # comparing a lake intersection to half of the geographic cell's area.
    result = coordinates.copy()
    result[:, 1] = np.sin(np.deg2rad(result[:, 1]))
    return result


def lake_window(lake, width):
    from shapely import contains_xy
    west, south, east, north = lake.bounds
    step = 360 / width
    x = np.arange(max(0, int(np.floor((west + 180) / step))),
                  min(width, int(np.ceil((east + 180) / step))))
    y = np.arange(max(0, int(np.floor((90 - north) / step))),
                  min(width // 2, int(np.ceil((90 - south) / step))))
    xx, yy = np.meshgrid(x, y)
    inside = contains_xy(lake, (xx + .5) * step - 180, 90 - (yy + .5) * step)
    return xx[inside], yy[inside]


def lake_sources(footprints, terrain):
    from shapely import transform
    sources = []
    for lake in footprints:
        x, y = lake_window(lake, terrain.shape[1])
        if not len(x):
            continue
        values, counts = np.unique(terrain[y, x], return_counts=True)
        most = counts.argmax()
        # Some ETOPO lakes map one constant water surface in lieu of bathymetry.
        # A majority plateau distinguishes these from surveyed deep lake beds.
        plateau = int(values[most]) if 2 * counts[most] > len(x) else None
        sources.append((lake, transform(lake, equal_area), np.median(terrain[y, x]), plateau))
    return sources


def majority_cells(projected, x, y, step):
    import shapely
    west, north = x * step - 180, 90 - y * step
    cells = shapely.box(west, np.sin(np.deg2rad(north - step)),
                        west + step, np.sin(np.deg2rad(north)))
    return 2 * shapely.area(shapely.intersection(projected, cells)) > shapely.area(cells)


def representative_beds(sources, survey, bed):
    width = survey.shape[1]
    step = 360 / width
    corrected = []
    for lake, projected, median, plateau in sources:
        x, y = lake_window(lake, width)
        stage, mean = survey[y, x].astype(int), bed[y, x].astype(int)
        eligible = (stage >= median) & (mean > stage)
        if plateau is not None:
            # Shore samples can differ by a metre or two from an extensive
            # flat DEM water surface. Keep those cells level with the lake.
            eligible |= (mean == plateau) & (stage > plateau) & (stage <= plateau + 2)
        x, y = x[eligible], y[eligible]
        if not len(x):
            continue
        majority = majority_cells(projected, x, y, step)
        corrected.extend((y[majority] * width + x[majority]).tolist())
    return np.asarray(sorted(set(corrected)), dtype="<i4")


def supplemental_stages(lakes, survey):
    from shapely import transform
    width = survey.shape[1]
    result = []
    for lake, level in lakes:
        x, y = lake_window(lake, width)
        wet = majority_cells(transform(lake, equal_area), x, y, 360 / width) & (survey[y, x] == DRY)
        result.extend((int(row * width + col), level) for row, col in zip(y[wet], x[wet]))
    return sorted(result)


def encode(grid, corrected, extra, channels):
    # North-first signed metre/classification spans. Runs may cross rows.
    values = grid.ravel()
    edges = np.r_[0, np.flatnonzero(np.diff(values) != 0) + 1, values.size]
    runs = np.empty(len(edges) - 1, dtype=[("value", "<i2"), ("count", "<i4")])
    runs["value"] = values[edges[:-1]]
    runs["count"] = np.diff(edges)
    data = (struct.pack("<4sii", b"OWR4", grid.shape[1], len(runs)) + runs.tobytes()
            + struct.pack("<i", len(corrected)) + corrected.tobytes())
    data += struct.pack("<i", len(extra)) + b"".join(struct.pack("<ih", cell, stage) for cell, stage in extra)
    return data + struct.pack("<i", len(channels)) + np.asarray(channels, dtype="<i4").tobytes()


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
    from shapely.geometry import Polygon
    from bake_straits import passages, representative_channels
    straits = list(passages(Polygon(global_sea[0], global_sea[1:])))
    inland = [p for p in ocean if p is not global_sea]
    inland += [p for f in features("lakes", args.cache) for p in polygons(f)]
    marine = Image.new("L", (terrain.shape[1], terrain.shape[0]))
    draw_polygon(marine, global_sea, 255, 0)
    lakes = Image.new("I", marine.size, DRY)
    for poly in inland:
        draw_polygon(lakes, poly, lake_level(poly[0], terrain), DRY)
    from bake_major_lakes import load_footprints, supplemental_lakes
    footprints, _ = load_footprints(args.cache)
    sources = lake_sources(footprints, terrain)
    extra_lakes = supplemental_lakes()
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
        bed = np.fromfile(RESOURCES / f"Earth{width}.bytes", "<i2").reshape(size[::-1])
        corrected = representative_beds(sources, survey, bed)
        extra = supplemental_stages(extra_lakes, survey)
        channels = representative_channels(straits, bed, survey, MARINE, PASSAGE)
        payload = encode(survey, corrected, extra, channels)
        path = RESOURCES / f"EarthWater{width}.bytes"
        if args.check:
            if path.read_bytes() != payload:
                raise ValueError(f"{path.name} differs from the reference")
        else:
            path.write_bytes(payload)
        print(f"{path.name}: {len(corrected)} majority-lake beds, {len(extra)} supplemental lake cells, "
              f"{len(channels)} strait cells, {len(payload)} bytes, "
              f"sha256 {hashlib.sha256(payload).hexdigest()}")


if __name__ == "__main__":
    main()
