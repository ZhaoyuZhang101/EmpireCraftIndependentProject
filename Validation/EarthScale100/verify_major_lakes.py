"""Check the shipped major-lake atlas against named sites and vector shorelines."""
import argparse
from functools import lru_cache
import struct
import sys
import zlib
from pathlib import Path

import numpy as np
import shapely
from shapely import contains_xy
from shapely.geometry import LineString, box, shape

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tools/EarthScale100"))
from bake_player_water import features, polygons

ASSET = ROOT / "Assets/ORBIS/Resources/ORBIS/FlatWorld/EarthMajorLakes.bytes"


def read_atlas(path):
    assert path.is_file(), f"Missing geographic lake atlas {path.name}"
    raw = path.read_bytes()
    tag, width, height = struct.unpack_from("<4sii", raw)
    assert (tag, width, height) == (b"OLK1", 8192, 4096)
    runs = np.frombuffer(raw, dtype=[("value", "u1"), ("count", "<u2")], offset=12)
    assert np.all(runs["count"] > 0) and np.all(runs["value"] > 0)
    assert runs["count"].sum(dtype=np.int64) == width * height
    return np.repeat(runs["value"], runs["count"]).reshape(height, width)


def sample(atlas, lon, lat):
    height, width = atlas.shape
    x = (lon + 180) * width / 360 - .5
    y = np.clip((lat + 90) * height / 180 - .5, 0, height - 1)
    i, j = int(np.floor(x)), int(np.floor(y))
    fx, fy = x - i, y - j
    i0, i1, j1 = i % width, (i + 1) % width, min(j + 1, height-1)
    a = atlas[j, i0] * (1-fx) + atlas[j, i1] * fx
    b = atlas[j1, i0] * (1-fx) + atlas[j1, i1] * fx
    return a * (1-fy) + b * fy


def verify_sites(atlas):
    # Geographic points in open water, on islands and on the adjacent mainland.
    sites = {
        "Caspian Sea": (51, 42, True),
        "Superior": (-87.5, 47.5, True), "Michigan": (-87.1, 44.15, True),
        "Huron": (-82.55, 44.9, True), "Georgian Bay": (-80.45, 45.25, True),
        "Saginaw Bay opening into Huron": (-83.173, 44.213, True),
        "Erie": (-80.45, 42.25, True), "Ontario": (-77.7, 43.62, True),
        "Victoria": (33, -1.4, True), "Baikal": (108, 53.5, True),
        "Lake Chad southern pool": (14.536, 13.063, True), "Lake Chad west of mapped pool": (14.0, 13.0, False),
        "Tanganyika": (29.6, -6, True), "Malawi": (34.55, -12.2, True),
        "Great Bear": (-121, 66, True), "Great Slave": (-114.5, 61.65, True),
        "Ladoga": (31.4, 60.9, True), "Qinghai": (100.17, 36.87, True),
        "Taymyr": (101.6, 74.55, True), "Geneva": (6.53, 46.46, True),
        "Peipus": (27.5, 58.63, True),
        "Lake Pskov": (27.883, 58.074, True), "Balaton": (18.083, 47.0, True),
        "Constance, Obersee": (9.470, 47.571, True),
        "Constance, Untersee": (9.031, 47.703, True),
        "Dongting": (112.214, 28.938, True), "East Dongting": (112.917, 29.377, True),
        "Vättern": (14.436, 58.250, True), "Mälaren": (17.556, 59.392, True),
        "Sevan": (45.374, 40.276, True), "Tuz": (33.420, 38.694, True),
        "Dead Sea": (35.486, 31.487, True), "Oulujärvi": (27.092, 64.314, True),
        "Champlain": (-73.323, 44.495, True), "Okeechobee": (-80.793, 26.960, True),
        "Siling Co": (89.099, 31.794, True),
        "Chaohu": (117.608, 31.526, True), "Poyang": (116.221, 29.137, True),
        "Taihu": (120.127, 31.195, True), "Hongze": (118.720, 33.300, True),
        "Huoqiu north bank": (116.414, 32.395, False),
        "Datong next to Dongting": (112.478, 29.202, True),
        "Gaoyou": (119.340, 32.841, True), "Honghu": (113.401, 29.907, True),
        "Liangzi": (114.567, 30.263, True), "Xingkai": (132.409, 45.032, True),
        "Hulun": (117.482, 49.008, True), "Buir": (117.735, 47.843, True),
        "Chagan": (124.298, 45.233, True), "Nam Co": (90.442, 30.693, True),
        "Yamdrok": (90.912, 28.988, True), "Gyaring": (97.286, 34.928, True),
        "Ngoring": (97.631, 34.878, True), "Manasarovar": (81.465, 30.689, True),
        "Bosten": (86.950, 41.994, True), "Ebinur": (82.978, 44.869, True),
        "Sayram": (81.180, 44.609, True), "Dianchi": (102.721, 24.849, True),
        "Erhai": (100.182, 25.783, True), "northern Fuxian": (102.899, 24.579, True),
        "between Dianchi and Fuxian": (102.823, 24.662, False),
        "between the Yellow River headwater lakes": (97.45, 34.91, False),
        "Lop Nur dry bed": (90.45, 40.18, False),
        "Manitoulin Island": (-82.33, 45.8, False),
        "Isle Royale": (-88.9, 48, False), "Olkhon Island": (107.39, 53.18, False),
        "Great Slave northeast shore": (-112.7, 62.4, False),
        "Caspian east shore": (54.9, 41.8, False), "Qinghai east shore": (100.88, 36.98, False),
    }
    for name, (lon, lat, wet) in sites.items():
        value = sample(atlas, lon, lat)
        assert (value > 129 if wet else value < 127), f"{name} ({lon}, {lat}) = {value:.1f}"
    # These narrow lake centres straddle both banks at the 4.9-km overview.
    # Test their signed distance here and their open-water interiors on the fine pages.
    for lon, lat in ((120.738, 31.437), (116.353, 32.280)):
        assert sample(atlas, lon, lat) > 128, f"Missing secondary lake at {lon}, {lat}"
    # Unrelated marine areas and polar caps are outside the finite lake-distance band.
    distant = ((0, 0), (-84, 59), (10, 88), (150, -70), (-180, 0), (180, 0), (0, -90), (0, 90))
    for lon, lat in distant:
        assert sample(atlas, lon, lat) == 1
    # The southern Dead Sea evaporation ponds are excluded from the natural shoreline.
    assert sample(atlas, 35.46, 31.17) < 127
    return len(sites), len(distant)


def unit_vectors(coordinates):
    lon, lat = np.deg2rad(coordinates).T
    return np.stack((np.cos(lat) * np.cos(lon), np.cos(lat) * np.sin(lon), np.sin(lat)), axis=-1)

def distance_reference(width, cache):
    from bake_major_lakes import load_footprints

    minor = []
    primary, _ = load_footprints(cache, minor)
    secondary = [p for p in shapely.get_parts(shapely.union_all(minor)) if p.geom_type == "Polygon"]
    fine = width == 65536
    bodies = primary + secondary
    if fine:
        bodies = [p for p in shapely.get_parts(shapely.union_all(bodies)) if p.geom_type == "Polygon"]
    nearby = shapely.STRtree(bodies)
    step = 360 / width
    radius = 32 * step

    @lru_cache(None)
    def edges(index):
        rings = [bodies[index].exterior, *bodies[index].interiors]
        starts = np.concatenate([unit_vectors(ring.coords[:-1]) for ring in rings])
        ends = np.concatenate([unit_vectors(ring.coords[1:]) for ring in rings])
        normals = np.cross(starts, ends)
        lengths = np.linalg.norm(normals, axis=1)
        valid = lengths > 1e-14
        starts, ends, normals = starts[valid], ends[valid], normals[valid] / lengths[valid, None]
        return starts, ends, normals, np.cross(normals, starts), np.cross(ends, normals)

    def read(x, y):
        dx = radius / np.cos(np.deg2rad(abs(y) + radius))
        point, result = unit_vectors((x, y)), 1.0
        for index in nearby.query(box(x-dx, y-radius, x+dx, y+radius)):
            starts, ends, normals, forward, backward = edges(index)
            on_arc = (forward @ point >= 0) & (backward @ point >= 0)
            angles = np.arccos(np.clip(np.maximum(starts @ point, ends @ point), -1, 1))
            angles[on_arc] = np.arcsin(np.clip(np.abs(normals[on_arc] @ point), 0, 1))
            distance = angles.min() * (1 if contains_xy(bodies[index], x, y) else -1)
            code = np.clip(128 + np.rad2deg(distance) * 4 / step, 1, 255)
            if not fine and index >= len(primary) and code < 112:
                rounded = np.rint(code)
                t = np.clip((rounded - 96) / 16, 0, 1)
                code = 1 + (rounded-1)*t*t*(3-2*t)
            result = max(result, code)
        return result
    return read


def verify_distances(width, read_pixel, cache):
    # Independent reference: project each test point onto every original great-circle
    # segment, checking the two endpoints when the perpendicular foot leaves its arc.
    names = {"Lake Taymyr", "Great Bear Lake", "Lake Baikal", "Qinghai Hu", "Lake Victoria", "Chao Hu", "Nam Co"}
    if width == 65536:
        names.add("Lake Balaton")
    step = 360 / width
    cases = 0
    max_error = 0.0
    expected_at = distance_reference(width, cache)
    for feature in features("lakes", cache):
        name = feature["properties"]["name"]
        if name not in names:
            continue
        names.remove(name)
        (rings,) = polygons(feature)
        offsets = ((-7, 0), (7, 0), (0, -4), (0, 4))
        if name == "Lake Taymyr" and width == 8192:
            # At 75 N merely padding 32 longitude texels would cut off the atlas
            # around 40 km instead of its 155 km spherical support radius.
            offsets += ((-80, 0), (80, 0), (-150, 0), (150, 0), (0, -22), (0, 22))
        for ring_index, ring in enumerate(rings):
            # Test a different distribution on every ring, including island coasts.
            take = 16 if ring_index == 0 else 2
            for lon, lat in np.asarray(ring)[np.linspace(0, len(ring)-2, take, dtype=int)]:
                for dx, dy in offsets:
                    i = int((lon + 180) / step) + dx
                    j = int((lat + 90) / step) + dy
                    x, y = (i + .5) * step - 180, (j + .5) * step - 90
                    expected = expected_at(x, y)
                    actual = int(read_pixel(i, j))
                    error = abs(actual - expected)
                    assert error <= .65, f"{name} ({x:.3f}, {y:.3f}): {actual} vs {expected:.2f}"
                    max_error = max(max_error, error)
                    cases += 1
    assert not names, f"Missing Natural Earth features: {sorted(names)}"
    return cases, max_error


def verify_detail(cache):
    raw = ASSET.with_name("EarthLakeDetail.bytes").read_bytes()
    tag, width, height = struct.unpack_from("<4sii", raw)
    assert tag == b"OLD1" and width in (4096, 8192) and 0 < height <= 8192
    index_rows = 1024*512*2//width
    assert (height-index_rows)%66 == 0
    image = np.frombuffer(zlib.decompress(raw[12:], -15), np.uint8).reshape(height, width)
    table = image[:index_rows].view("<u2").reshape(512, 1024)
    occupied = table[table != 0]
    assert np.array_equal(np.sort(occupied), np.arange(1, 1 + occupied.size))

    def page(slot):
        row, col = divmod(int(slot)-1, width//66)
        tile = image[index_rows+row*66:index_rows+(row+1)*66, col*66:(col+1)*66]
        assert tile.shape == (66, 66)
        return tile

    def pixel(x, y):
        slot = table[y//64, (x//64)%1024]
        assert slot, f"Missing shoreline page at {x}, {y}"
        return page(slot)[1+y%64, 1+x%64]

    seams = 0
    for y, x in zip(*np.nonzero(table)):
        current = page(table[y, x])
        for dy, dx in ((0, 1), (1, 0)):
            adjacent = table[(y+dy)%512, (x+dx)%1024]
            if not adjacent:
                continue
            other = page(adjacent)
            a, b = (current[:, -2:], other[:, :2]) if dx else (current[-2:, :], other[:2, :])
            # Pages can omit a distant lake. The two gutters must agree wherever
            # either one represents the shore or the water within 4.9 km of it.
            near = (np.abs(a.astype(int)-128) < 32) | (np.abs(b.astype(int)-128) < 32)
            assert np.array_equal(a[near], b[near]), f"Broken lake detail seam at {x}, {y}"
            seams += 1

    def fine_code(lon, lat):
        x, y = (lon+180)*65536/360-.5, (lat+90)*65536/360-.5
        i, j = int(x), int(y)
        fx, fy = x-i, y-j
        return ((1-fy)*((1-fx)*pixel(i,j) + fx*pixel(i+1,j))
                + fy*((1-fx)*pixel(i,j+1) + fx*pixel(i+1,j+1)))

    # The middle of Fuxian is narrow enough for coarse texels to straddle both banks.
    for lon, lat in ((102.887, 24.494), (100.182, 25.783), (102.721, 24.849), (81.180, 44.609),
                     (120.738, 31.437), (116.353, 32.280), (112.478, 29.202)):
        assert fine_code(lon, lat) > 129, f"Missing surveyed lake interior at ({lon}, {lat})"
    assert fine_code(102.823, 24.662) < 127, "The ridge between Dianchi and Fuxian flooded"

    balaton = next(f for f in features("lakes", cache) if f["properties"]["name"] == "Lake Balaton")
    lake = shape(balaton["geometry"])
    for lon in np.arange(17.28, 18.125, .002):
        cut = lake.intersection(LineString([(lon, 46.5), (lon, 47.2)]))
        section = max(cut.geoms, key=lambda part: part.length) if cut.geom_type == "MultiLineString" else cut
        centre = section.interpolate(.5, normalized=True)
        assert fine_code(centre.x, centre.y) > 128, f"Dry water-section centre at {centre}"
    checks, error = verify_distances(65536, pixel, cache)
    return occupied.size, seams, checks, error


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, default=ROOT / "ValidationOutput/BasinWater")
    args = parser.parse_args()
    atlas = read_atlas(ASSET)
    sites, distant = verify_sites(atlas)
    count, error = verify_distances(atlas.shape[1], lambda x,y: atlas[y,x], args.cache)
    print(f"{sites} named lake/land sites and {distant} distant sites; {count} spherical shoreline samples, "
          f"maximum error {error:.3f} R8 codes.")
    pages, seams, fine_count, fine_error = verify_detail(args.cache)
    print(f"{pages} fine pages, {seams} shared page edges, 423 wet sections of Lake Balaton; "
          f"{fine_count} fine spherical shoreline samples, maximum error {fine_error:.3f} R8 codes.")
