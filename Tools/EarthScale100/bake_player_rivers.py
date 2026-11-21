"""Bake major rivers and their lake centrelines from Natural Earth v5.1.2.

Requires NumPy, Pillow (shared data reader), SciPy, and Shapely 2. Source coordinates
are WGS84. Natural Earth scale ranks and min_zoom express map prominence.
Simplification in a local ground metric preserves endpoints and confluences.
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path

import numpy as np
import shapely
from shapely.geometry import LineString, box, shape

from bake_player_water import features
from bake_player_presets import ROOT, RESOURCES, SOURCE


# Rank 5 includes the Colorado, Marañón and upper Mississippi. One rank-0
# record (Tongariro) is marked for local maps only, at zoom 7.2.
MAX_SCALE_RANK = 5
OVERVIEW_ZOOM = 4.7

# These lower-ranked reaches continue a selected stem or connect it to a major
# lake. Names and source rivernum together distinguish distant namesakes and
# the unrelated records sharing rivernum zero in this release.
CONTINUATIONS = {
    (28, "Angara"), (32, "Angara"),  # Lake Baikal, middle Angara reservoirs
    (222, "Orange"), (335, "Tocantins"), (373, "Guaporé"),
    (0, "Rio Salinas"), (0, "Usumacinta"),  # Chixoy downstream to the gulf
    (1001, "Tonle Sap"),  # Lake Tonle Sap to the Mekong
}


def quantize(lon, lat):
    # Normalized unsigned 16-bit coordinates resolve up to ~610 m at the equator.
    uv = np.column_stack((((lon + 180) % 360) / 360, (lat + 90) / 180))
    return np.rint(uv * 65535).clip(0, 65535).astype("<u2")


def yangtze_estuary(cache):
    """Trace the chart centreline where the 1:10m river becomes a tidal polygon."""
    from scipy.ndimage import distance_transform_edt
    from scipy.sparse import csr_matrix
    from scipy.sparse.csgraph import dijkstra
    from scipy.spatial import cKDTree

    # The 1:10m Yangtze line ends at Zhenjiang. The 1:110m Chang line supplies
    # the seaward endpoint. The 1:10m ocean polygon contains the intervening banks and islands.
    anchors = np.array([[119.606375, 32.196885], [121.858226, 31.414904]])
    west, south, east, north = 119.57, 31.25, 121.92, 32.40
    window = box(west, south, east, north)
    water = shapely.union_all([shape(f["geometry"]).intersection(window) for f in features("ocean", cache)])
    step = .0025
    lon = np.arange(west, east, step) + step/2
    lat = np.arange(south, north, step) + step/2
    shapely.prepare(water)
    wet = shapely.contains_xy(water, lon, lat[:, None])
    km = 111.2 * np.array([np.cos(np.deg2rad(lat.mean())), 1.0])
    clearance = distance_transform_edt(wet, sampling=step*km[::-1])[wet]
    rows, cols = np.nonzero(wet)
    xy = np.column_stack((lon[cols], lat[rows]))
    # Eight-connected water pixels. The extra cost near the banks favours the
    # open channel through bends and avoids cutting across Chongming Island.
    a, b = cKDTree(np.column_stack((rows, cols))).query_pairs(1.42, output_type="ndarray").T
    resistance = 1 + 1 / (.15 + clearance)**2
    cost = np.linalg.norm((xy[a]-xy[b])*km, axis=1) * (resistance[a]+resistance[b]) / 2
    graph = csr_matrix((cost, (a, b)), shape=(len(xy), len(xy)))
    distances, (start, end) = cKDTree(xy*km).query(anchors*km)
    lengths, previous = dijkstra(graph, directed=False, indices=start, return_predecessors=True)
    if distances.max() > .6 or not np.isfinite(lengths[end]):
        raise ValueError("The mapped Yangtze estuary no longer connects its surveyed endpoints")
    path = [end]
    while path[-1] != start:
        path.append(previous[path[-1]])
    return np.vstack((anchors[0], xy[path[::-1]], anchors[1]))


def pack_part(points, rank, terrain, endpoint_keys):
    p = np.asarray(points, dtype=np.float64)
    # Unwrap before simplification so a short crossing never spans the map.
    lon = np.rad2deg(np.unwrap(np.deg2rad(p[:, 0])))
    east = max(.02, np.cos(np.deg2rad(p[:, 1].mean())))
    metric = np.column_stack((lon * east, p[:, 1]))
    line = LineString(metric)
    if line.length == 0:
        return None
    # Natural Earth digitizes some parts upstream. Use smoothed terrain at the
    # ends to orient the optical current. Flat lake links have no gauged direction.
    end_heights = []
    for fractions in (np.linspace(0, .12, 9), np.linspace(.88, 1, 9)):
        samples = [line.interpolate(float(t), normalized=True) for t in fractions]
        x = [int(((v.x / east + 180) % 360) / 360 * terrain.shape[1]) for v in samples]
        y = [min(terrain.shape[0]-1, int((90-v.y) / 180 * terrain.shape[0])) for v in samples]
        end_heights.append(np.median(terrain[y, x]))
    # A tributary can terminate at an interior vertex of another part. Retain
    # the original survey vertex whenever both land on the same stored point.
    keys = quantize(p[:, 0], p[:, 1])
    cuts = [0]
    cuts.extend(i for i in range(1, len(p) - 1) if tuple(keys[i]) in endpoint_keys)
    cuts.append(len(p) - 1)
    simple = [metric[0]]
    for start, end in zip(cuts, cuts[1:]):
        span = LineString(metric[start:end+1])
        simple.extend(span.simplify(.0075, preserve_topology=False).coords[1:])
    simple = np.asarray(simple)
    if end_heights[0] < end_heights[1]:
        simple = simple[::-1]
    xy = quantize(simple[:, 0] / east, simple[:, 1])
    xy = xy[np.r_[True, np.any(xy[1:] != xy[:-1], axis=1)]]
    if len(xy) < 2:
        return None
    return struct.pack("<BH", rank, len(xy)) + xy.tobytes()


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
    source = features("rivers_lake_centerlines", args.cache)
    selected = []
    feature_count = 0
    for feature in source:
        geometry = feature["geometry"]
        properties = feature["properties"]
        rank = int(properties["scalerank"])
        if geometry["type"] not in ("LineString", "MultiLineString") or not 0 <= rank <= 10:
            raise ValueError("Unexpected Natural Earth river feature")
        major = rank <= MAX_SCALE_RANK and properties["min_zoom"] <= OVERVIEW_ZOOM
        if not major and (properties["rivernum"], properties["name"]) not in CONTINUATIONS:
            continue
        coordinates = geometry["coordinates"]
        if not coordinates:
            continue
        lines = coordinates if geometry["type"] == "MultiLineString" else [coordinates]
        feature_count += 1
        selected.extend((rank, points) for points in lines)
    selected.append((1, yangtze_estuary(args.cache)))
    endpoints = np.asarray([pt for _, points in selected for pt in (points[0], points[-1])])
    endpoint_keys = set(map(tuple, quantize(endpoints[:, 0], endpoints[:, 1])))
    parts = []
    for rank, points in selected:
        part = pack_part(points, rank, terrain, endpoint_keys)
        if part:
            parts.append(part)
    data = struct.pack("<4sI", b"ORV1", len(parts)) + b"".join(parts)
    path = RESOURCES / "EarthRivers.bytes"
    if args.check:
        if path.read_bytes() != data:
            raise ValueError("EarthRivers differs from pinned source geometry")
    else:
        path.write_bytes(data)
    print(f"Selected {feature_count} nonempty features from {len(source)} source records, "
          f"{len(selected)-1} source parts plus the Yangtze tidal reach, {len(parts)} drawable parts, "
          f"{sum((len(p)-3)//4 for p in parts)} points, {len(data)} bytes, "
          f"sha256 {hashlib.sha256(data).hexdigest()}")


if __name__ == "__main__":
    main()
