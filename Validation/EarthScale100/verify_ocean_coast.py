"""Check the player marine silhouette against the pinned Natural Earth ocean polygon."""
import argparse
import struct
import sys
import zlib
from pathlib import Path

import numpy as np
import shapely
from scipy.ndimage import binary_dilation, binary_erosion
from shapely.geometry import LineString, Polygon

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tools/EarthScale100"))
from bake_player_presets import RESOURCES
from bake_player_water import features, polygons
from verify_strait_shore import Atlas, smooth

# Close views, including coast far from surveyed routes and river mouths.
# Bounds only select independent test receivers. The baker has no geography list.
CHARTS = {
    "Cape Akritas, Tainaron and Maleas": (21.6, 36.35, 23.25, 37.04),
    "Cyclades": (24.15, 36.68, 25.89, 37.60),
    "Dalmatian islands": (15.34, 42.81, 17.60, 43.60),
    "Sardinia and Corsica": (8.11, 40.77, 9.86, 41.67),
    "Galicia": (-9.45, 42.16, -8.30, 43.33),
    "Lofoten": (12.57, 67.70, 15.38, 68.54),
    "Haiyang, Shandong": (120.78, 36.30, 121.77, 36.85),
    "Southwest Japan": (132.30, 32.57, 133.07, 33.34),
    "Cape Cod": (-70.55, 41.46, -69.82, 42.10),
    "Chile, Chiloe": (-74.22, -43.10, -73.21, -42.04),
    "New Zealand, Banks Peninsula": (172.53, -43.93, 173.19, -43.52),
    "Madagascar, Nosy Be": (48.00, -13.56, 48.59, -13.17),
    "Western Fiji across 180 degrees": (179.66, -16.94, 180.41, -16.28),
}


def ocean_polygon(cache):
    parts = [part for feature in features("ocean", cache) for part in polygons(feature)]
    exterior, *holes = max(parts, key=lambda part: np.ptp(np.asarray(part[0])[:, 0]))
    ocean = Polygon(exterior, holes)
    shapely.prepare(ocean)
    return ocean


class OceanAtlas:
    def __init__(self, path):
        raw = path.read_bytes()
        tag, count, directory_length, banks_length = struct.unpack_from("<4s3I", raw)
        assert tag == b"OCS1" and len(raw) == 16 + directory_length + banks_length
        at = 16 + directory_length
        directory = zlib.decompress(raw[16:at], -15)
        self.directory = np.frombuffer(directory, "<u2").reshape(720, 1440, 2)
        slots = self.directory[..., 0]
        assert np.array_equal(np.sort(slots[slots > 0]), np.arange(1, count + 1))
        page = 32
        pixels = np.frombuffer(zlib.decompress(raw[at:], -15), "u1")
        self.pages = pixels.reshape(-1, page, 128, page, 4).transpose(0, 2, 4, 1, 3)
        assert self.pages.shape[0] == (count + 511) // 512
        self.pages = self.pages.reshape(-1, page, page)

    def sample(self, lon, lat):
        x, y = np.broadcast_arrays((np.asarray(lon) + 180) % 360 * 4,
                                   np.clip((np.asarray(lat) + 90) * 4, 0, 720 - 1e-8))
        ix, iy = x.astype(int), y.astype(int)
        slot, signed = np.moveaxis(self.directory[iy, ix], -1, 0)
        shore = np.where(signed >= 32768, 127., -127.)
        mapped = slot > 0
        n = self.pages.shape[1] - 2
        u, v = .5 + (x[mapped] - ix[mapped])*n, .5 + (y[mapped] - iy[mapped])*n
        col, row = u.astype(int), v.astype(int)
        a, b = u - col, v - row
        ids = slot[mapped].astype(int) - 1
        def lookup(dy, dx):
            return self.pages[ids, row+dy, col+dx].astype(float) - 128
        shore[mapped] = ((1-a)*lookup(0, 0) + a*lookup(0, 1))*(1-b) + ((1-a)*lookup(1, 0) + a*lookup(1, 1))*b
        fine = shore / 32
        # Hardware lookup at the same geographic UV bilinearly interpolates
        # the signed centre distances, including across the date line.
        ax, ay = np.floor(x-.5).astype(int), np.floor(y-.5).astype(int)
        u, v = x-.5-ax, y-.5-ay
        def centre(dy, dx):
            return self.directory[np.clip(ay+dy, 0, 719), (ax+dx)%1440, 1].astype(float)
        south = (1-u)*centre(0, 0) + u*centre(0, 1)
        north = (1-u)*centre(1, 0) + u*centre(1, 1)
        coarse = ((1-v)*south + v*north - 32768)/32
        sign = np.sign(fine)
        fraction = smooth(1.5, 3.96, np.abs(fine))
        km = fine + sign*fraction*(np.maximum(np.abs(fine), sign*coarse) - np.abs(fine))
        return km, 1-smooth(1.5*40030.2/512+20, 3*40030.2/512+45, np.abs(km))

    def verify_seams(self):
        slots = self.directory[..., 0].astype(int)
        dry = self.directory[..., 1] < 32768
        y, x = np.nonzero(slots)
        count = wrapped = 0
        # Check both copies of every shared geographic sample; absent pages
        # represent constant open ocean or dry land on the far side of the border.
        for dy, dx in ((0, 1), (1, 0), (0, -1), (-1, 0)):
            other_y, other_x = np.clip(y + dy, 0, 719), (x + dx) % 1440
            neighbours = slots[other_y, other_x]
            for start in range(0, len(y), 2048):
                batch = slice(start, start + 2048)
                here = self.pages[slots[y[batch], x[batch]]-1]
                near = neighbours[batch] > 0
                there = self.pages[np.maximum(0, neighbours[batch]-1)]
                if dx:
                    a, b = (here[:, :, -2:], there[:, :, :2]) if dx > 0 else (here[:, :, :2], there[:, :, -2:])
                else:
                    a, b = (here[:, -2:, :], there[:, :2, :]) if dy > 0 else (here[:, :2, :], there[:, -2:, :])
                assert np.array_equal(a[near], b[near]), ("coastal page seam", dy, dx)
                constant = np.where(dry[other_y[batch], other_x[batch]], 1, 255)
                assert np.all(a[~near] == constant[~near, None, None]), ("unmapped coast", dy, dx)
            if dx + dy > 0:
                count += np.count_nonzero(neighbours)
                if dx: wrapped += np.count_nonzero((x == 1439) & (neighbours > 0))
        assert wrapped and count > 50000
        print(f"{count} geographic page seams, including {wrapped} across the antimeridian.")


def verify_banks(ocean, atlas):
    sections = (
        ("Cape Akritas", 21.84, 21.95, 36.75, 2),
        ("Cape Tainaron", 22.46, 22.52, 36.42, 2),
        ("Cape Maleas", 23.07, 23.23, 36.45, 2),
        ("Elafonisos", 22.91, 23.00, 36.50, 2),
        ("Naxos and Paros strait", 25.15, 25.67, 37.04, 3),
        ("Haiyang", 121.04, 121.86, 36.71, 3),
        ("Lofoten", 12.70, 13.70, 67.99, 4),
    )
    maximum, count = 0., 0
    for name, west, east, lat, banks in sections:
        reference = ocean.boundary.intersection(LineString([(west, lat), (east, lat)]))
        crosses = np.sort(shapely.get_coordinates(reference)[:, 0])
        lon = np.linspace(west, east, 3001)
        km, weight = atlas.sample(lon, lat)
        edges = np.flatnonzero(np.diff(km > 0))
        assert len(edges) == len(crosses) == banks and weight.min() > .999, name
        at = lon[edges] + (lon[1]-lon[0])*(-km[edges])/(km[edges+1]-km[edges])
        miss = np.abs(crosses-at) * 111.19508*np.cos(np.deg2rad(lat))
        assert np.max(miss) < .5, (name, miss)
        maximum = max(maximum, miss.max())
        count += banks
    print(f"{count} cape, island and mainland bank crossings, maximum section error {maximum*1000:.0f} m.")


def verify_worldwide(ocean, atlas):
    # Stratify by geographic region so a long, densely digitized fjord cannot
    # outvote small isolated islands on another continent. Evaluate exact vector
    # membership on both sides of the selected source banks.
    xy, ring = shapely.get_coordinates(shapely.get_rings(ocean), return_index=True)
    a, b = xy[:-1], xy[1:]
    valid = (ring[:-1] == ring[1:]) & (np.abs(a[:, 0]-b[:, 0]) < 5)
    valid &= ~((np.abs(a[:, 0]) == 180) & (a[:, 0] == b[:, 0]))
    valid &= np.maximum(np.abs(a[:, 1]), np.abs(b[:, 1])) < 88
    center = (a[valid]+b[valid])*.5
    scale = np.cos(np.deg2rad(center[:, 1]))
    tangent = b[valid]-a[valid]
    tangent[:, 0] *= scale
    size = np.linalg.norm(tangent, axis=1)
    normal = np.stack((-tangent[:, 1]/scale, tangent[:, 0]), axis=1)/np.maximum(1e-10, size[:, None])
    keys = ((center[:, 1]+90)//5).astype(int)*72 + ((center[:, 0]+180)//5).astype(int)
    rng = np.random.default_rng(20261123)
    choices = rng.permutation(np.flatnonzero(size > 1e-7))
    ordered = choices[np.argsort(keys[choices], kind="stable")]
    groups = np.split(ordered, np.flatnonzero(np.diff(keys[ordered]))+1)
    sample = np.concatenate([group[:12] for group in groups])
    errors = tested = 0
    for offset in (1.25, 4.):
        delta = normal[sample]*offset/111.19508
        left, right = center[sample]-delta, center[sample]+delta
        left[:, 0] = (left[:, 0]+180)%360-180
        right[:, 0] = (right[:, 0]+180)%360-180
        wet_left, wet_right = shapely.contains_xy(ocean, *left.T), shapely.contains_xy(ocean, *right.T)
        opposite = wet_left != wet_right
        for points, truth in ((left, wet_left), (right, wet_right)):
            km, weight = atlas.sample(*points[opposite].T)
            assert np.min(weight) > .999, "A sampled global marine bank fell back to the simulation grid"
            errors += np.count_nonzero((km > 0) != truth[opposite])
            tested += len(km)
    print(f"{len(groups)} coastal 5-degree regions, {tested} global cross-bank samples, {errors} sign errors.")
    assert len(groups) > 700 and tested > 20000
    assert errors/tested < .01


def verify(ocean, survey):
    total, missed, unsupported = 0, 0, 0
    for name, (west, south, east, north) in CHARTS.items():
        lon = (np.linspace(west, east, 287) + 180) % 360 - 180
        lat = np.linspace(south, north, 193)[:, None]
        truth = shapely.contains_xy(ocean, lon, lat)
        # Restrict the denominator to the coastal band instead of scoring trivially
        # uniform open sea and land. Exclude the outer frame of the sample window.
        band = binary_dilation(truth, iterations=12) ^ binary_erosion(truth, iterations=12)
        band[:12] = band[-12:] = False
        band[:, :12] = band[:, -12:] = False
        km, weight = survey.sample(lon, lat)
        wrong = ((km > 0) != truth) & band
        missing = (weight < .99) & band
        n = np.count_nonzero(band)
        assert n > 2000, name
        print(f"{name}: {n} coastal samples; {missing.sum()} without a surveyed shore; "
              f"{(wrong & ~missing).sum()} sign errors in surveyed area", flush=True)
        total += n
        missed += np.count_nonzero(wrong & ~missing)
        unsupported += np.count_nonzero(missing)
    print(f"Across {len(CHARTS)} charts: {total} near-coast samples, {unsupported} unsurveyed, {missed} mismatches.")
    assert not unsupported, "A coast falls back to the player grid."
    assert missed / total < .02, f"{missed / total:.3%} disagree with the source ocean."


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, default=ROOT / "ValidationOutput/BasinWater")
    parser.add_argument("--regional-only", action="store_true", help="measure the earlier strait/estuary coverage")
    args = parser.parse_args()
    shape = ocean_polygon(args.cache)
    if args.regional_only:
        verify(shape, Atlas(RESOURCES / "EarthGlobalStraitShore.bytes"))
    else:
        atlas = OceanAtlas(RESOURCES / "EarthOceanShore.bytes")
        atlas.verify_seams()
        verify_banks(shape, atlas)
        verify_worldwide(shape, atlas)
        verify(shape, atlas)
