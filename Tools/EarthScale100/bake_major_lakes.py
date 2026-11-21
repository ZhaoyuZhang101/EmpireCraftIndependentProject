"""Bake the Earth shore atlas from Natural Earth v5.1.2 and the HydroLAKES v1.0 subset.

OLK1 stores width and height as little-endian int32, then (uint8 value, uint16
count) runs in south-first row order. 128 marks shore; water is positive.
One code is one quarter of an equatorial texel on the project's mean-radius Earth.
"""
import argparse
import hashlib
import json
import struct
import zlib
from pathlib import Path

import numpy as np
import shapely
from shapely.geometry import Polygon, shape

from bake_player_presets import RESOURCES, ROOT, SOURCE

WIDTH = 8192
DETAIL_WIDTH = 65536
INDEX_WIDTH = 1024
PACKED_WIDTH = 4096
RADIUS_KM = 6371.0088
MIN_AREA_KM2 = 2000
# Smaller EU4 lake landmarks, using Natural Earth v5.1.2 geometry and IDs.
EU4_LANDMARK_IDS = {
    1159106781,  # Lake Okeechobee
    1159106953,  # Lake Champlain
    1159113219,  # Lake Chad, the mapped water surface in this release
    1159113451,  # Lake Pskov, the southern basin of Lake Peipus
    1159114083,  # Siling Co
    1159114143,  # Lake Geneva (Lac Léman)
    1159114173,  # Vättern
    1159114259,  # Lake Constance (Obersee)
    1159114609,  # Lake Balaton
    1159115109,  # Lake Sevan
    1159115487,  # Lake Constance (Untersee), unnamed in this Natural Earth release
    1159116233,  # East Dongting, unnamed in this Natural Earth release
    1159116351,  # Dongting Hu
    1159122891,  # Mälaren
    1159123301,  # Oulujärvi
    1159126747,  # Dead Sea, northern basin
    1159126815,  # Lake Tuz
}

# Additional regional landmarks in the existing Natural Earth water survey.
CHINA_LANDMARK_IDS = {
    1159113637,  # Nam Co
    1159113927,  # Bosten
    1159114069,  # Liangzi
    1159114305,  # Ngoring
    1159114321,  # Gaoyou
    1159114475,  # Gyaring
    1159114553,  # Manasarovar
    1159114715,  # Buir
    1159114885,  # Yamdrok
    1159115027,  # Honghu
    1159115069,  # Chaohu
    1159117583,  # Chagan
    1159118103,  # Ebinur
}


def supplemental_lakes():
    """Surveyed WGS84 polygons and reference surface elevations in metres."""
    raw = (SOURCE / "china-lakes-hydrolakes-v1.geojson").read_bytes()
    if hashlib.sha256(raw).hexdigest() != "c45e376f483e98e6436a4770bfa996d18c21339df6ca428364f0ac822adc717a":
        raise ValueError("HydroLAKES v1.0 Chinese lake subset checksum mismatch")
    return [(shape(f["geometry"]), f["properties"]["Elevation"]) for f in json.loads(raw)["features"]]


def ring_area_km2(ring):
    """Spherical area between a geographic ring and the equator, by longitude strips."""
    lon, lat = np.deg2rad(ring).T
    # Integral of sin(latitude) along each straight GeoJSON longitude/latitude edge.
    mean_sine = np.sin((lat[1:] + lat[:-1]) / 2) * np.sinc(np.diff(lat) / (2 * np.pi))
    return abs(np.dot(np.diff(np.unwrap(lon)), mean_sine)) * RADIUS_KM**2


def load_footprints(cache):
    """Dissolve all selected water polygons, sharing source selection with the water-bed bake."""
    from bake_player_water import features, polygons

    selected = []
    found_ids = set()
    landmark_ids = EU4_LANDMARK_IDS | CHINA_LANDMARK_IDS

    def include(rings, landmark=False):
        area = ring_area_km2(rings[0]) - sum(ring_area_km2(hole) for hole in rings[1:])
        if landmark or area >= MIN_AREA_KM2:
            polygon = Polygon(rings[0], rings[1:])
            selected.append(polygon if polygon.is_valid else shapely.make_valid(polygon))

    for feature in features("lakes", cache):
        lake_id = feature["properties"]["ne_id"]
        landmark = lake_id in landmark_ids
        if landmark:
            found_ids.add(lake_id)
        for rings in polygons(feature):
            include(rings, landmark)
    if found_ids != landmark_ids:
        raise ValueError(f"Missing Natural Earth landmark IDs: {landmark_ids - found_ids}")
    selected.extend(lake for lake, _ in supplemental_lakes())
    # The other polygon in the pinned Ocean layer is the enclosed Caspian Sea.
    ocean = [p for f in features("ocean", cache) for p in polygons(f)]
    world_ocean = max(ocean, key=lambda p: np.ptp(np.asarray(p[0])[:, 0]))
    for rings in ocean:
        if rings is not world_ocean:
            include(rings)
    # Huron includes Georgian Bay, its North Channel and Saginaw Bay in the same
    # layer. Dissolve these features so their shared edges cannot become dry banks.
    water = shapely.union_all(selected)
    return [p for p in shapely.get_parts(water) if p.geom_type == "Polygon"], len(selected)


def unit_vectors(lon, lat):
    lon, lat = np.broadcast_arrays(np.deg2rad(lon), np.deg2rad(lat))
    return np.stack((np.cos(lat) * np.cos(lon), np.cos(lat) * np.sin(lon), np.sin(lat)), axis=-1)


def sample_ring(ring, max_angle):
    lon, lat = np.asarray(ring.coords).T
    vertices = unit_vectors(lon, lat)
    starts, ends = vertices[:-1], vertices[1:]
    angle = 2 * np.arcsin(np.minimum(1, np.linalg.norm(ends - starts, axis=1) / 2))
    # Normalized chord interpolation advances fastest at a segment's midpoint,
    # with maximum angular derivative 2*tan(angle/2) over a unit parameter range.
    counts = np.maximum(1, np.ceil(2 * np.tan(angle / 2) / max_angle).astype(int))
    edge = np.repeat(np.arange(len(counts)), counts)
    offsets = np.cumsum(counts) - counts
    fraction = (np.arange(len(edge)) - offsets[edge]) / counts[edge]
    # All original vertices survive. Normalize chord interpolation to sample each
    # short great-circle edge at <= 1/16 texel spacing (about 0.305 km).
    samples = starts[edge] + fraction[:, None] * (ends[edge] - starts[edge])
    return samples / np.linalg.norm(samples, axis=1, keepdims=True)


def shore_tree(lake, max_angle):
    from scipy.spatial import cKDTree
    rings = (lake.exterior, *lake.interiors)
    return cKDTree(np.concatenate([sample_ring(ring, max_angle) for ring in rings]))


def signed_codes(lake, coast, lon, lat, step_rad):
    max_chord = 2 * np.sin(127 * step_rad / 8)
    chord, _ = coast.query(unit_vectors(lon, lat[:, None]), distance_upper_bound=max_chord)
    angle = 2 * np.arcsin(np.minimum(chord, max_chord) / 2)
    inside = shapely.contains_xy(lake, lon, lat[:, None])
    return np.rint(128 + np.where(inside, angle, -angle) * (4 / step_rad)).clip(1, 255).astype(np.uint8)


def distance_atlas(footprints):

    step = 360 / WIDTH
    step_rad = np.deg2rad(step)
    band = 127 / 4 * step
    field = np.ones((WIDTH // 2, WIDTH), np.uint8)
    for lake in footprints:
        west, south, east, north = lake.bounds
        # A geodesic within the band can reach at most this latitude. Bounding
        # longitude using its cosine includes even east/west fringes at Taymyr.
        max_lat = min(90, max(abs(south), abs(north)) + band)
        lon_band = min(180, band / np.cos(np.deg2rad(max_lat)))
        x0 = int(np.floor((west - lon_band + 180) / step))
        x1 = int(np.ceil((east + lon_band + 180) / step))
        y0 = max(0, int(np.floor((south - band + 90) / step)))
        y1 = min(WIDTH // 2, int(np.ceil((north + band + 90) / step)))
        columns = np.arange(x0, x1)
        wrapped = columns % WIDTH
        lon = (columns + .5) * step - 180
        lat = (np.arange(y0, y1) + .5) * step - 90
        values = signed_codes(lake, shore_tree(lake, step_rad/16), lon, lat, step_rad)
        # Outside both polygons the union takes the distance to the nearer shore.
        field[y0:y1, wrapped] = np.maximum(field[y0:y1, wrapped], values)
    return field


def detail_atlas(footprints):
    """Pack shore-intersecting 64-square pages with a one-texel bilinear gutter.

    The first 256 rows store a 1024x512 little-endian page index, two R8 texels
    per entry. Zero selects the overview. Both the index and pages run south first.
    """
    tile_step = 360 / INDEX_WIDTH
    step = 360 / DETAIL_WIDTH
    side = DETAIL_WIDTH // INDEX_WIDTH
    pitch = side + 2
    pages = {}
    offsets = (np.arange(pitch) - .5) * step
    for lake in footprints:
        west, south, east, north = lake.bounds
        # Also include the neighbouring side of a tile boundary when a shoreline
        # comes within a coarse texel. Both pages then sample the same fine bank.
        pad_y = 1.5 * 360 / WIDTH
        pad_x = pad_y / np.cos(np.deg2rad(max(abs(south), abs(north)) + pad_y))
        xs = np.arange(int(np.floor((west-pad_x+180)/tile_step)), int(np.ceil((east+pad_x+180)/tile_step)))
        ys = np.arange(max(0, int(np.floor((south-pad_y+90)/tile_step))),
                       min(INDEX_WIDTH//2, int(np.ceil((north+pad_y+90)/tile_step))))
        xx, yy = np.meshgrid(xs, ys)
        boxes = shapely.box(xx*tile_step-180-pad_x, yy*tile_step-90-pad_y,
                            (xx+1)*tile_step-180+pad_x, (yy+1)*tile_step-90+pad_y)
        near_shore = shapely.intersects(lake.boundary, boxes)
        coast = shore_tree(lake, np.deg2rad(step) / 8)
        shapely.prepare(lake)
        for x, y in zip(xx[near_shore], yy[near_shore]):
            key = int(y * INDEX_WIDTH + x % INDEX_WIDTH)
            values = signed_codes(lake, coast, x*tile_step-180+offsets,
                                  y*tile_step-90+offsets, np.deg2rad(step))
            if key in pages:
                np.maximum(pages[key], values, out=pages[key])
            else:
                pages[key] = values
    per_row = PACKED_WIDTH // pitch
    index = np.zeros(INDEX_WIDTH**2 // 2, dtype="<u2")
    index_rows = index.nbytes // PACKED_WIDTH
    field = np.zeros((index_rows + ((len(pages)+per_row-1)//per_row)*pitch, PACKED_WIDTH), np.uint8)
    if len(pages) >= 65536 or field.shape[0] > 8192:
        raise ValueError("Lake detail exceeds the runtime atlas limits")
    for slot, key in enumerate(sorted(pages)):
        index[key] = slot + 1
        x = (slot % per_row) * pitch
        y = index_rows + (slot // per_row) * pitch
        field[y:y+pitch, x:x+pitch] = pages[key]
    field[:index_rows] = index.view(np.uint8).reshape(index_rows, PACKED_WIDTH)
    return field, len(pages)


def encode(field):
    flat = field.ravel()
    edges = np.r_[0, np.flatnonzero(flat[1:] != flat[:-1]) + 1, flat.size]
    lengths = np.diff(edges)
    chunks = (lengths + 65534) // 65535
    runs = np.empty(int(chunks.sum()), dtype=[("value", "u1"), ("count", "<u2")])
    runs["value"] = np.repeat(flat[edges[:-1]], chunks)
    runs["count"] = 65535
    runs["count"][np.cumsum(chunks)-1] = (lengths - 1) % 65535 + 1
    return struct.pack("<4sii", b"OLK1", field.shape[1], field.shape[0]) + runs.tobytes()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="compare with the checked-in atlas")
    parser.add_argument("--cache", type=Path, default=ROOT / "ValidationOutput/BasinWater")
    args = parser.parse_args()
    args.cache.mkdir(parents=True, exist_ok=True)
    footprints, selected = load_footprints(args.cache)
    field = distance_atlas(footprints)
    data = encode(field)
    path = RESOURCES / "EarthMajorLakes.bytes"
    if args.check:
        if path.read_bytes() != data:
            raise ValueError(f"{path.name} differs from the pinned geographic reference")
    else:
        path.write_bytes(data)
    print(f"{selected} source polygons, {len(footprints)} dissolved polygons, "
          f"{sum(len(p.interiors) for p in footprints)} island rings. "
          f"{WIDTH} x {WIDTH//2}, {np.count_nonzero(field > 128)} water texels, "
          f"{len(data)} bytes, SHA-256 {hashlib.sha256(data).hexdigest()}.")
    packed, pages = detail_atlas(footprints)
    compressor = zlib.compressobj(level=6, wbits=-15)
    detail = struct.pack("<4sii", b"OLD1", packed.shape[1], packed.shape[0]) + compressor.compress(packed) + compressor.flush()
    path = RESOURCES / "EarthLakeDetail.bytes"
    if args.check:
        existing = path.read_bytes()
        if existing[:12] != detail[:12] or zlib.decompress(existing[12:], wbits=-15) != packed.tobytes():
            raise ValueError(f"{path.name} differs from the pinned geographic reference")
    else:
        path.write_bytes(detail)
    print(f"{pages} fine shoreline pages at {DETAIL_WIDTH} geographic columns, "
          f"{packed.shape[1]} x {packed.shape[0]} packed texels, {len(detail)} bytes.")


if __name__ == "__main__":
    main()
