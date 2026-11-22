"""Bake major river courses from Natural Earth, GRWL and ESA WorldCover.

Dependencies are in requirements.txt. Source coordinates
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
from river_widths import RiverWidths, complete_widths
from river_transects import RiverTransects
from river_centerlines import RiverCenterlines
from huangpu import huangpu_course


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

# Named basins and principal tributaries omitted by the global overview cutoff.
# Natural Earth's historical transliterations and feature numbers are the keys.
REGIONAL_COURSES = {
    (247, "Yuan"), (270, "Han"), (278, "Gan"), (456, "Wu"), (457, "Xiang"),
    (538, "Jialing"), (539, "Dong"), (552, "Bei"), (385, "Fuchun"), (473, "Min"),
    (271, "Mahäna Nadï"), (279, "Krishna"), (307, "Yamuna"), (308, "Narmada"),
    (403, "Cauvery"), (550, "Tapi"),
}

# At these named junctions separately digitized 1:10m reaches miss by less
# than a kilometre. Identify the tributary endpoint by its source coordinates
# and project onto its known receiver before either line is simplified.
# Pairs of equal identifiers stitch the adjacent parts of the same river.
JUNCTIONS = (
    (18, 18, 112.78883, 29.74396),       # middle Chang Jiang sections
    (1, 1, 114.30323, 30.59304),         # Yangtze sections at Wuhan
    (1, 1, 116.20753, 29.77949),         # Lake Poyang outlet at Hukou
    (270, 270, 109.73927, 32.93679),     # Han upstream of Danjiangkou
    (270, 1, 114.28956, 30.56635),       # Han at Wuhan
    (538, 18, 106.58684, 29.58564),      # Jialing at Chongqing
    (456, 18, 107.40244, 29.73029),      # Wu at Fuling
    (96, 96, 112.80470, 23.14789),       # Xi distributary at Sanshui
    (552, 96, 112.81935, 23.15286),      # Bei joining the Pearl delta
    (95, 95, 114.83074, 35.01980),       # Huang reaches at Lankao
    (55, 55, 87.93808, 24.80573),        # Bhagirathi leaves the Ganges
    (55, 51, 89.74928, 23.84801),        # Ganges and Jamuna form the Padma
)


def join_surveyed_reaches(parts, ids):
    for source, target, lon, lat in JUNCTIONS:
        metric = np.array([np.cos(np.deg2rad(lat)), 1.0])
        anchor = np.array([lon, lat])
        error, source_index, end = min(
            (np.linalg.norm((coords[side]-anchor)*metric), i, side)
            for i, (_, coords) in enumerate(parts) if ids[i] == source for side in (0, -1))
        if error > .0001:
            raise ValueError(f"Missing river {source} endpoint near {anchor}")
        source_coords = parts[source_index][1]
        a = source_coords[end]*metric
        # Work in a ground metric. Inserting the same station into the two
        # polylines allows pack_part to protect it as a shared survey vertex.
        def projection(i, coords):
            p = coords*metric
            dv = np.diff(p, axis=0)
            t = np.sum((a-p[:-1])*dv, axis=1)/np.maximum(1e-20, np.sum(dv*dv, axis=1))
            nearest = p[:-1] + np.clip(t, 0, 1)[:,None]*dv
            dist = np.linalg.norm(nearest-a, axis=1)
            k = int(np.argmin(dist))
            return dist[k], i, k, nearest[k]/metric
        distance, i, k, station = min((projection(i, coords) for i, (_, coords) in enumerate(parts)
                                       if ids[i] == target and i != source_index and len(coords) > 1),
                                      key=lambda item: item[0])
        if distance > .009:  # 1 km maximum even if a future dataset changes ID.
            raise ValueError(f"River {source} and {target} no longer meet near {anchor}")
        rank, coords = parts[i]
        if not np.any(np.all(np.isclose(coords[k:k+2], station, rtol=0, atol=1e-10), axis=1)):
            parts[i] = rank, np.insert(coords, k+1, station, axis=0)
        source_coords[end] = station


def quantize(lon, lat):
    # This coarse key registers common stations from separate legacy surveys.
    # The final ORV2 coordinates use an independent 24-bit quantizer.
    uv = np.column_stack((((lon + 180) % 360) / 360, (lat + 90) / 180))
    return np.rint(uv * 65535).clip(0, 65535).astype("<u2")


def source_parts(cache):
    source = features("rivers_lake_centerlines", cache)
    selected = []
    ids = []
    feature_count = 0
    # The dissolved 10m Huang record ends on the delta plain about 8 km from
    # the mapped coast. Its 10m scale-rank counterpart follows the channel to
    # the Bohai. Splice only the downstream tail at the last common vertex.
    ranked = features("rivers_lake_centerlines_scale_rank", cache)
    huang, = [np.asarray(line.coords) for f in ranked if f["properties"]["rivernum"] == 95
              for line in shapely.get_parts(shape(f["geometry"])) if line.bounds[2] > 119.2]
    huang_keys = {tuple(key): i for i, key in enumerate(quantize(*huang.T))}
    for feature in source:
        geometry, properties = feature["geometry"], feature["properties"]
        rank = int(properties["scalerank"])
        if geometry["type"] not in ("LineString", "MultiLineString") or not 0 <= rank <= 10:
            raise ValueError("Unexpected Natural Earth river feature")
        key = properties["rivernum"], properties["name"]
        major = rank <= MAX_SCALE_RANK and properties["min_zoom"] <= OVERVIEW_ZOOM
        if not major and key not in CONTINUATIONS and key not in REGIONAL_COURSES:
            continue
        coordinates = geometry["coordinates"]
        if not coordinates:
            continue
        feature_count += 1
        for points in coordinates if geometry["type"] == "MultiLineString" else [coordinates]:
            if key == (95, "Huang") and points[-1][0] > 119:
                keys = quantize(*np.asarray(points).T)
                upstream, coastal = next((i, huang_keys[tuple(keys[i])]) for i in reversed(range(len(keys)))
                                         if tuple(keys[i]) in huang_keys)
                if upstream < len(keys)-4 or not 0 < coastal < len(huang)-1:
                    raise ValueError("The two Huang surveys no longer share their lower course")
                points = np.vstack((points[:upstream+1], huang[coastal+1:]))
            selected.append((rank, np.asarray(points, dtype=np.float64)))
            ids.append(properties["rivernum"])
    join_surveyed_reaches(selected, ids)
    return selected, feature_count


def finish_mapped_outlets(parts, cache):
    from river_widths import grwl_reader
    from shapely.geometry import Point
    from shapely.ops import substring

    downstream_survey = set()
    def outlet(lon, lat):
        anchor = np.array([lon, lat])
        distance, index = min((np.linalg.norm(coords[-1]-anchor), i) for i, (_, coords) in enumerate(parts))
        if distance > .0001:
            raise ValueError(f"Missing surveyed river endpoint near {anchor}")
        downstream_survey.add(index)
        return index

    # Source endpoints identify the existing line. OBJECTID and station on
    # the GRWL v1.02 summary product select the Landsat-observed tail.
    # The Hengmen record continues past the outlet into a different tidal arm.
    with grwl_reader(cache) as reader:
        for lon, lat, oid, mouth in ((30.39853, 31.44339, 10040, (30.36723,31.46772)),
                                     (113.55592, 22.58308, 7950, (113.59769,22.59360)),
                                     (120.14894, 30.19583, 11527, (120.20023,30.22262))):
            index = outlet(lon, lat)
            rank, source = parts[index]
            record, = [r for r in reader.iterShapeRecords(bbox=(lon,lat,lon,lat))
                       if r.record["OBJECTID"] == oid]
            line = shape(record.shape.__geo_interface__)
            start = line.project(Point(source[-1]))
            stop = line.project(Point(mouth))
            tail = substring(line, start, stop)
            if line.distance(Point(source[-1])) > .004 or line.distance(Point(mouth)) > .0001:
                raise ValueError(f"GRWL outlet {oid} no longer matches its river")
            parts[index] = rank, np.vstack((source, np.asarray(tail.coords)))

    # The West River reaches Natural Earth's tidal footprint at Jiangmen.
    # GRWL 7987 identifies its seaward station but lies 1.37 km east of the
    # upstream survey. Follow the connected, mapped channel between them.
    i = outlet(113.16554, 22.57563)
    rank, line = parts[i]
    tidal = tidal_centreline(cache, [line[-1], (113.18139,22.54373)],
                             (113.144,22.527,113.205,22.593), .00055)
    parts[i] = rank, np.vstack((line, tidal[1:]))

    # The independently digitized Damietta endpoint and marine shore differ by
    # 0.20 km, less than a stored longitude sample. Register the terminal to
    # the measured shore so even a narrow channel intersects its receiver.
    i = outlet(31.84474, 31.52534)
    _, line = parts[i]
    station = Point(line[-1])
    water = shapely.union_all([shape(f["geometry"]).intersection(station.buffer(.015))
                               for f in features("ocean", cache)])
    if water.is_empty or station.distance(water) > .004:
        raise ValueError("Damietta terminal is no longer adjacent to its surveyed marine bank")
    line[-1] = shapely.shortest_line(station, water).coords[-1]
    return downstream_survey


def yangtze_estuary(cache):
    # The 1:10m line ends at Zhenjiang; the 1:110m Chang line supplies the
    # seaward endpoint. The 1:10m ocean polygon supplies the banks and islands.
    return tidal_centreline(cache, [(119.606375,32.196885), (121.858226,31.414904)],
                            (119.57,31.25,121.92,32.40), .0025)


def tidal_centreline(cache, anchors, bounds, step):
    """Trace a chart centreline between stations inside a tidal polygon."""
    from scipy.ndimage import distance_transform_edt
    from scipy.sparse import csr_matrix
    from scipy.sparse.csgraph import dijkstra
    from scipy.spatial import cKDTree

    anchors = np.asarray(anchors)
    west, south, east, north = bounds
    window = box(west, south, east, north)
    water = shapely.union_all([shape(f["geometry"]).intersection(window) for f in features("ocean", cache)])
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
        raise ValueError(f"The mapped estuary no longer connects {anchors.tolist()}")
    path = [end]
    while path[-1] != start:
        path.append(previous[path[-1]])
    return np.vstack((anchors[0], xy[path[::-1]], anchors[1]))


def river_stations(points, terrain, endpoint_keys, mapped_outlet, centerlines):
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
        simple.extend(span.simplify(.0013, preserve_topology=False).coords[1:])
    simple = np.asarray(simple)
    # The observed tidal reach already runs toward its marine receiver.
    # Coarse mean DEM heights in a flat, diked delta can reverse that sign.
    if not mapped_outlet and end_heights[0] < end_heights[1]:
        simple = simple[::-1]
    course = np.column_stack(((simple[:, 0]/east + 180) % 360 - 180, simple[:, 1]))
    protected = [tuple(key) in endpoint_keys for key in quantize(*course.T)]
    course = centerlines.refine(course, protected)
    xy = stored_coordinates(course)
    xy = xy[np.r_[True, np.any(xy[1:] != xy[:-1], axis=1)]]
    if len(xy) < 2:
        return None
    return xy


def stored_coordinates(points):
    uv = (points + [180, 90]) / [360, 180]
    return np.rint(uv * 16777215).clip(0, 16777215).astype("<u4")


def pack_part(xy, rank, widths):
    stations = np.empty(len(xy), dtype=[("xy", "<u4", 2), ("width", "<u2")])
    stations["xy"] = xy
    stations["width"] = widths
    return struct.pack("<BH", rank, len(xy)) + stations.tobytes()


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
    selected, feature_count = source_parts(args.cache)
    downstream_survey = finish_mapped_outlets(selected, args.cache)
    downstream_survey.add(len(selected))
    selected.append((1, yangtze_estuary(args.cache)))
    endpoints = np.asarray([pt for _, points in selected for pt in (points[0], points[-1])])
    endpoint_keys = set(map(tuple, quantize(endpoints[:, 0], endpoints[:, 1])))
    index = RiverWidths.from_cache(args.cache)
    transects = RiverTransects(args.cache)
    centerlines = RiverCenterlines(transects)
    cross_sections = 0
    courses = []
    for index_in_survey, (rank, points) in enumerate(selected):
        xy = river_stations(points, terrain, endpoint_keys, index_in_survey in downstream_survey, centerlines)
        if xy is not None:
            points = xy.astype(float) / 16777215 * [360, 180] - [180, 90]
            measured = index.match(points)
            local = transects.match(points)
            supported = np.isfinite(local)
            measured[supported] = local[supported]
            cross_sections += supported.sum()
            courses.append((xy, rank, points, measured))
    # A course with no Landsat evidence uses a cartographic estimate from
    # observed stations of similar map prominence in this same data release.
    ranks = sorted({rank for _, rank, _, _ in courses})
    typical = {}
    for rank in ranks:
        pool = np.concatenate([w[np.isfinite(w)] for _, k, _, w in courses if k == rank])
        if len(pool):
            typical[rank] = float(np.median(pool))
    parts = []
    direct = total = 0
    for xy, rank, points, measured in courses:
        fallback = typical[min(typical, key=lambda k: abs(k-rank))]
        parts.append(pack_part(xy, rank, complete_widths(points, measured, fallback)))
        direct += np.isfinite(measured).sum()
        total += len(xy)
    # This regional addition has its own surveys and does not contribute to
    # the rank-wide width estimates used for the global Natural Earth set.
    huangpu, widths = huangpu_course(args.cache)
    parts.append(pack_part(stored_coordinates(huangpu), 7, widths))
    total += len(huangpu)
    data = struct.pack("<4sI", b"ORV2", len(parts)) + b"".join(parts)
    path = RESOURCES / "EarthRivers.bytes"
    if args.check:
        if path.read_bytes() != data:
            raise ValueError("EarthRivers differs from pinned source geometry")
    else:
        path.write_bytes(data)
    print(f"Selected {feature_count} nonempty river features, "
          f"{len(selected)-1} source parts plus the Yangtze tidal reach and Huangpu, {len(parts)} drawable parts, "
          f"{total} stations ({direct} GRWL matches on the global set, including {cross_sections} local transect matches, "
          f"plus {len(huangpu)} Huangpu stations), "
          f"{centerlines.accepted} registered sections covering {centerlines.kilometres:.0f} km, {len(data)} bytes, "
          f"sha256 {hashlib.sha256(data).hexdigest()}")


if __name__ == "__main__":
    main()
