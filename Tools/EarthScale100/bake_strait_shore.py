"""Bake the Natural Earth v5.1.2 marine banks at straits and river mouths.

OSH1 stores a raw-Deflate RG8 regional atlas followed by an R8 detail atlas.
The 0.25-degree geographic index occupies the first 507 rows of the RG atlas.
Each nonzero RG pair is a one-based little-endian page number. Its 18 x 19
page stores a detail-page number in the first row, then 16 x 16 signed
distances (0.5 km per code) and regional weights, with a one-sample gutter.
The 130 x 130 detail pages hold 128 x 128 samples (1/16 km per code) plus
gutters. The second header texel can instead address a 66 x 66 estuary page
in the unused lower part of the regional RG atlas, one page per channel.
Both atlases are south-first, matching Unity geographic UVs.
"""
import argparse
import hashlib
import struct
import zlib
from pathlib import Path

import numpy as np
import shapely
from scipy.spatial import cKDTree
from shapely.geometry import Polygon, box

from bake_major_lakes import unit_vectors
from bake_player_presets import RESOURCES, ROOT
from bake_player_water import features, polygons
from bake_straits import APPROACHES, GATES, polygon_parts

TILE = .25
FADE = 1.5  # Degrees outside the surveyed route's window.
MOUTH_FADE = .60
INDEX_WIDTH = 1440
COARSE_WIDTH, FINE_WIDTH = 2048, 4096
HEADER_ROWS = (INDEX_WIDTH**2//2 + COARSE_WIDTH-1)//COARSE_WIDTH
EARTH_KM = 6371.0088

# Some lines stop at the head of a tidal polygon hundreds of kilometres
# inland. Include its whole receiving bay and intervening islands. The boxes
# only choose coverage; all water and dry banks come from the ocean polygon.
ESTUARIES = (
    (30.26, 31.34, 31.95, 31.64),       # Rosetta and Damietta / Mediterranean
    (119.55, 31.12, 122.12, 32.39),     # Yangtze / East China Sea
    (119.95, 29.98, 121.65, 30.73),     # Qiantang / Hangzhou Bay
    (112.97, 21.86, 114.00, 23.19),     # Pearl / Lingdingyang and Modaomen
    (119.05, 25.87, 119.72, 26.25),     # Min / Taiwan Strait
    (87.82, 21.43, 91.08, 23.62),       # Hooghly, Padma and Meghna
    (67.32, 23.64, 68.07, 24.12),       # Indus / Arabian Sea
    (72.55, 21.45, 73.19, 21.86),       # Narmada / Gulf of Khambhat
    (94.19, 15.64, 96.46, 16.85),       # Irrawaddy / Andaman Sea
    (105.77, 9.42, 106.88, 10.34),      # Mekong / South China Sea
    (-52.82, -2.76, -48.25, 1.01),      # Amazonas, Xingu and Tocantins / Pará
    (-62.42, 8.52, -60.30, 10.02),      # Orinoco / Atlantic
    (-58.55, -34.98, -57.51, -33.98),   # Paraná / Río de la Plata
    (5.16, 4.22, 7.02, 5.25),           # Niger / Gulf of Guinea
)


def river_windows(ocean, cache):
    from bake_player_rivers import source_parts
    # Use only source endpoints already touching the open-ocean polygon. This
    # adds shore detail at river mouths without building seaward lines from
    # nearby headwaters, drained wetlands, inland lakes or the Caspian.
    coast = shapely.STRtree(shapely.get_parts(ocean.boundary))
    river_parts, _ = source_parts(cache)
    ends = sorted({tuple(point) for _, line in river_parts for point in (line[0], line[-1])})
    near, _ = coast.query(shapely.points(ends), predicate="dwithin", distance=.012)
    windows = [box(*bounds) for bounds in ESTUARIES]
    for index in np.unique(near):
        lon, lat = ends[index]
        # The polar charts already supply the high-latitude coast. A small
        # local footprint still registers a subcell river with its exact bank.
        rx = .22 / max(.4, np.cos(np.deg2rad(lat)))
        windows.append(box(lon-rx, lat-.22, lon+rx, lat+.22))
    return windows


def coordinates(tile, size, offset):
    # Same integer geographic sample centres in adjoining page gutters.
    return (tile*size + np.arange(-1, size+1) + .5)*(TILE/size) - offset


def distances(ocean, tree, lon, lat, codes_per_km):
    inside = shapely.contains_xy(ocean, lon, lat[:, None])
    max_chord = 2*np.sin(127/(codes_per_km*2*EARTH_KM))
    chord, _ = tree.query(unit_vectors(lon, lat[:, None]), distance_upper_bound=max_chord)
    km = (2*EARTH_KM)*np.arcsin(np.minimum(chord, max_chord)/2)
    return np.rint(128 + np.where(inside, km, -km)*codes_per_km).clip(1, 255).astype(np.uint8)


def coverage(rectangles, lon, lat, fade):
    # Smoothly feather the union outside the complete route windows. The
    # rounded outer border is independent of the packing and geographic grid.
    nearest = np.full((lat.size, lon.size), fade)
    for west, south, east, north in rectangles:
        dx = np.maximum(0, np.maximum(west-lon, lon-east))
        dy = np.maximum(0, np.maximum(south-lat, lat-north))
        nearest = np.minimum(nearest, np.hypot(dx, dy[:, None]))
    t = np.minimum(1, nearest/fade)
    return np.rint(255*(1-t*t*(3-2*t))).astype(np.uint8)


def regional_pages(ocean, straits, mouths, bounds, coarse, detail, estuary):
    west, south, east, north = bounds
    # 2 degrees cover more than the saturated 63.5 km field even at Oresund.
    x_margin = 2/max(.2, np.cos(np.deg2rad(max(abs(north), abs(south)))))
    clip = box(west-x_margin, south-2, east+x_margin, north+2)
    local_ocean = ocean.intersection(clip)
    shapely.prepare(local_ocean)
    # Intersect the original boundary: edges introduced by clipping the ocean
    # would put fictitious beaches along rectangular page or region borders.
    lines = ocean.boundary.intersection(clip)
    vertices = shapely.get_coordinates(shapely.segmentize(lines, .0008))
    tree = cKDTree(unit_vectors(vertices[:, 0], vertices[:, 1]))
    for y in range(int(np.floor((south+90)/TILE)), int(np.ceil((north+90)/TILE))):
        lat = coordinates(y, 16, 90)
        for x in range(int(np.floor((west+180)/TILE)), int(np.ceil((east+180)/TILE))):
            lon = coordinates(x, 16, 180)
            strait = coverage(straits, lon, lat, FADE)
            weight = np.maximum(strait, coverage(mouths, lon, lat, MOUTH_FADE))
            if not np.any(weight):
                continue
            key = y*INDEX_WIDTH + x
            field = distances(local_ocean, tree, lon, lat, 2)
            coarse[key] = np.stack((field, weight), axis=-1)
            # At most one 1.74-km coarse sample separates a fragment from a
            # neighbour used in its bilinear read. Include both sides of a seam.
            near = np.abs(field.astype(int)-128) < 14
            if np.any(near & (strait >= 247)):
                detail[key] = distances(local_ocean, tree, coordinates(x, 128, 180),
                                        coordinates(y, 128, 90), 16)
            elif np.any(near & (weight >= 247)):
                estuary[key] = distances(local_ocean, tree, coordinates(x, 64, 180),
                                         coordinates(y, 64, 90), 16)


def pack(coarse, detail, estuary, cw=COARSE_WIDTH, fw=FINE_WIDTH, global_atlas=False):
    coarse_per_row, fine_per_row = cw//18, fw//130
    # Two R8 66x66 pages share the R and G channels of a regional tile.
    # Pack upwards from the last row so old OSH1 page addresses still work.
    middle_per_row = 2*(cw//66)
    middle_rows = ((len(estuary)+middle_per_row-1)//middle_per_row)*66
    header_rows = (INDEX_WIDTH**2//2 + cw-1)//cw
    ch = header_rows + (((len(coarse)+coarse_per_row-1)//coarse_per_row) + (middle_rows+18)//19)*19
    fh = ((len(detail)+fine_per_row-1)//fine_per_row)*130
    if max(ch, fh) > (8192 if global_atlas else 4096) or max(len(coarse),len(detail),len(estuary)) >= 65536:
        raise ValueError("Regional coastline exceeds the runtime atlas limits")
    index = np.zeros((ch, cw, 2), np.uint8)
    fine = np.zeros((fh, fw), np.uint8)
    fine_slots = {key: slot for slot, key in enumerate(sorted(detail), 1)}
    middle_slots = {key: slot for slot, key in enumerate(sorted(estuary), 1)}
    for slot, key in enumerate(sorted(coarse), 1):
        index.reshape(-1, 2)[key] = slot % 256, slot//256
        row, col = divmod(slot-1, coarse_per_row)
        y, x = header_rows+row*19, col*18
        index[y+1:y+19, x:x+18] = coarse[key]
        if key in fine_slots:
            f = fine_slots[key]
            index[y, x] = f % 256, f//256
            row, col = divmod(f-1, fine_per_row)
            fine[row*130:(row+1)*130, col*130:(col+1)*130] = detail[key]
        elif key in middle_slots:
            m = middle_slots[key]
            index[y, x+1] = m % 256, m//256
            row, col = divmod((m-1)//2, cw//66)
            bottom = ch-(row+1)*66
            index[bottom:bottom+66, col*66:(col+1)*66, (m-1)%2] = estuary[key]
    compressor = zlib.compressobj(level=6, wbits=-15)
    payload = compressor.compress(index) + compressor.compress(fine) + compressor.flush()
    return struct.pack("<4s4i", b"OSH2" if global_atlas else b"OSH1", cw, ch, fw, fh) + payload


def bake(cache):
    parts = [p for feature in features("ocean", cache) for p in polygons(feature)]
    shell, *holes = max(parts, key=lambda p: np.ptp(np.array(p[0])[:, 0]))
    ocean = Polygon(shell, holes)
    straits = [box(min(a[0], b[0])-p, min(a[1], b[1])-p, max(a[0], b[0])+p, max(a[1], b[1])+p)
               for _, a, b, p in GATES+APPROACHES]
    mouths = river_windows(ocean, cache)
    groups = shapely.union_all([region.buffer(fade+TILE, quad_segs=1, join_style="mitre")
                                for regions, fade in ((straits, FADE), (mouths, MOUTH_FADE))
                                for region in regions])
    coarse, detail, estuary = {}, {}, {}
    for group in polygon_parts(groups):
        windows = [[r.bounds for r in regions if group.covers(r)] for regions in (straits, mouths)]
        regional_pages(ocean, *windows, group.bounds, coarse, detail, estuary)
    return pack(coarse, detail, estuary), len(coarse), len(detail), len(estuary)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, default=ROOT/"ValidationOutput/BasinWater")
    parser.add_argument("--check", action="store_true", help="compare the rebake byte for byte")
    args = parser.parse_args()
    args.cache.mkdir(exist_ok=True, parents=True)
    payload, coarse, fine, estuary = bake(args.cache)
    path = RESOURCES/"EarthStraitShore.bytes"
    if args.check:
        if path.read_bytes() != payload:
            raise ValueError("Strait shoreline differs from the pinned geographic reference")
    else:
        path.write_bytes(payload)
    print(f"{coarse} regional pages, {fine} strait and {estuary} estuary detail pages, {len(payload)} bytes, "
          f"sha256 {hashlib.sha256(payload).hexdigest()}")


if __name__ == "__main__":
    main()
