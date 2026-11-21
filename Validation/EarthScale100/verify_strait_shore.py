"""Compare the player strait shoreline texture with Natural Earth ocean geometry."""
import argparse
import json
import os
import struct
import subprocess
import sys
import tempfile
import zlib
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import label
from shapely import contains_xy, get_coordinates, prepare
from shapely.geometry import LineString, Polygon, box

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tools/EarthScale100"))
from bake_player_water import features, polygons
from bake_straits import GATES

ASSET = ROOT / "Assets/ORBIS/Resources/ORBIS/FlatWorld/EarthStraitShore.bytes"


class Atlas:
    def __init__(self):
        assert ASSET.is_file(), "Missing player strait shoreline survey"
        data = ASSET.read_bytes()
        tag, cw, ch, fw, fh = struct.unpack_from("<4s4i", data)
        assert tag == b"OSH1" and cw == 2048 and fw == 4096 and max(ch, fh) <= 4096
        raw = np.frombuffer(zlib.decompress(data[20:], -15), dtype=np.uint8)
        assert raw.size == 2*cw*ch + fw*fh
        self.coarse = raw[:2*cw*ch].reshape(ch, cw, 2)
        self.fine = raw[2*cw*ch:].reshape(fh, fw)
        self.index = self.coarse.reshape(-1, 2)[:1440*720].copy().view("<u2").reshape(720, 1440)
        slots = self.index[self.index != 0]
        assert np.array_equal(np.sort(slots), np.arange(1, len(slots)+1))

    def pages(self, slot):
        y, x = divmod(int(slot)-1, 2048//18)
        y, x = 507+y*19, x*18
        lo, hi = self.coarse[y, x].astype(int)
        fine_slot = lo + 256*hi
        fine = None
        if fine_slot:
            fy, fx = divmod(fine_slot-1, 4096//130)
            fine = self.fine[fy*130:(fy+1)*130, fx*130:(fx+1)*130]
            assert fine.shape == (130, 130)
        else:
            lo, hi = self.coarse[y, x+1].astype(int)
            medium = lo + 256*hi - 1
            if medium >= 0:
                row, col = divmod(medium//2, 2048//66)
                head = self.coarse.shape[0] - (row+1)*66
                fine = self.coarse[head:head+66, col*66:(col+1)*66, medium%2]
                assert fine.shape == (66, 66)
        return self.coarse[y+1:y+19, x:x+18], fine

    def sample(self, lon, lat):
        lon, lat = np.broadcast_arrays(np.asarray(lon), np.asarray(lat))
        x = (lon+180)*4
        y = (lat+90)*4
        i, j = np.floor(x).astype(int), np.floor(y).astype(int)
        slot = self.index[j, i]
        shore, weight = np.zeros(lon.shape), np.zeros(lon.shape)
        for s in np.unique(slot[slot != 0]):
            mask = slot == s
            u, v = x[mask]-i[mask], y[mask]-j[mask]
            coarse, fine = self.pages(s)
            values = self.linear(coarse, u, v)
            km, w = (values[:, 0]-128)*.5, values[:, 1]/255
            if fine is not None:
                native = (self.linear(fine, u, v)-128)/16
                blend = smooth(.975, .999, w) * (1-smooth(2, 6, np.abs(km)))
                km += (native-km)*blend
            shore[mask], weight[mask] = km, w
        return shore, weight

    @staticmethod
    def linear(page, u, v):
        size = page.shape[0]-2
        x, y = .5+u*size, .5+v*size
        i, j = np.floor(x).astype(int), np.floor(y).astype(int)
        fx, fy = x-i, y-j
        if page.ndim == 3:
            fx, fy = fx[:, None], fy[:, None]
        return ((1-fx)*page[j, i] + fx*page[j, i+1])*(1-fy) + ((1-fx)*page[j+1, i] + fx*page[j+1, i+1])*fy


def smooth(a, b, x):
    t = np.clip((x-a)/(b-a), 0, 1)
    return t*t*(3-2*t)


def verify_seams(atlas):
    pairs = 0
    for j, i in zip(*np.nonzero(atlas.index)):
        page, detail = atlas.pages(atlas.index[j, i])
        if detail is None:
            required = (np.abs(page[..., 0].astype(int)-128) < 12) & (page[..., 1] > 249)
            assert not required.any(), f"Missing fine bank near {i}, {j}"
        for dy, dx in ((0, 1), (1, 0)):
            slot = atlas.index[j+dy, i+dx]
            first = page[-2:, :] if dy else page[:, -2:]
            if not slot:
                assert not first[..., 1].any(), "Region terminates with a hard atlas border"
                continue
            adjacent, other = atlas.pages(slot)
            second = adjacent[:2, :] if dy else adjacent[:, :2]
            assert np.array_equal(first, second), f"Coarse seam at {i}, {j}"
            if detail is not None and other is not None and detail.shape == other.shape:
                first = detail[-2:, :] if dy else detail[:, -2:]
                second = other[:2, :] if dy else other[:, :2]
                assert np.array_equal(first, second), f"Fine seam at {i}, {j}"
                pairs += 1
        if not atlas.index[j-1, i]:
            assert not page[:2, :, 1].any()
        if not atlas.index[j, i-1]:
            assert not page[:, :2, 1].any()
    assert pairs > 40
    return pairs


def verify_passages(atlas):
    for name, a, b, pad in GATES:
        # Four-neighbour water paths through the *displayed* signed shore at
        # native sample spacing, restricted to the selected strait and its mouths.
        low = np.floor((np.minimum(a, b)-pad)*512).astype(int)
        high = np.ceil((np.maximum(a, b)+pad)*512).astype(int)
        lon = (np.arange(low[0], high[0])+.5)/512
        lat = (np.arange(low[1], high[1])+.5)/512
        km, weight = atlas.sample(lon, lat[:, None])
        connected, _ = label((km > 0) & (weight > .99))
        first, last = (np.floor(np.array(p)*512).astype(int)-low for p in (a, b))
        basin = connected[first[1], first[0]]
        assert basin and connected[last[1], last[0]] == basin, f"Disconnected rendered passage: {name}"
    return len(GATES)


def verify_transects(atlas, ocean):
    # Both banks are inside a 2048-grid cell width at these cross-sections.
    sections = (
        ("Bosporus south", 41.055, 29.012, 29.075),
        ("Bosporus bend", 41.083, 29.02, 29.095),
        ("Bosporus north", 41.116, 29.035, 29.099),
        ("Dardanelles narrows", 40.150, 26.34, 26.43),
        ("Dardanelles north", 40.240, 26.42, 26.56),
        ("Kerch", 45.352, 36.60, 36.70),
        ("Oresund", 56.078, 12.51, 12.69),
    )
    errors = []
    for name, lat, west, east in sections:
        sea = ocean.intersection(LineString([(west, lat), (east, lat)]))
        assert sea.geom_type == "LineString" and not sea.is_empty
        reference = np.array([sea.coords[0][0], sea.coords[-1][0]])
        lon = np.linspace(west, east, 2001)
        km, weight = atlas.sample(lon, lat)
        assert np.min(weight) > .99, name
        edges = np.flatnonzero(np.diff(km > 0))
        assert len(edges) == 2 and km[0] < 0 and km[-1] < 0, (name, edges)
        actual = lon[edges] + (lon[1]-lon[0])*(-km[edges])/(km[edges+1]-km[edges])
        miss = np.abs(actual-reference)*111.2*np.cos(np.deg2rad(lat))
        assert np.max(miss) < .25, (name, miss)
        errors.extend(miss)
    return max(errors)


def verify_estuary_banks(atlas, ocean):
    # Independent WGS84 sections across both narrow channels and delta islands.
    # Compare every water/land crossing with the original vector boundary.
    sections = [
        ("Rosetta", (30.36,31.455), (30.40,31.455), 2),
        ("Damietta", (31.81,31.51), (31.875,31.545), 1),
        ("Yellow", (119.02,37.69), (119.31,37.79), 3),
        ("Yangtze and Chongming", (121.61,31.26), (121.61,31.79), 6),
        ("Hengmen", (113.56,22.59), (113.62,22.59), 1),
        ("West River", (113.14,22.535), (113.22,22.535), 2),
        ("Qiantang", (120.20,30.19), (120.20,30.26), 2),
        ("Meghna", (90.54,22.55), (90.90,22.55), 3),
        ("Bassac", (106.11,9.56), (106.11,9.74), 2),
    ]
    errors = []
    for name, begin, end, count in sections:
        a, b = np.asarray(begin), np.asarray(end)
        reference = get_coordinates(ocean.boundary.intersection(LineString([a, b])))
        t = np.linspace(0, 1, 2001)
        p = a + t[:,None]*(b-a)
        km, weight = atlas.sample(*p.T)
        edges = np.flatnonzero(np.diff(km > 0))
        assert weight.min() > .99 and len(edges) == len(reference) == count, name
        at = t[edges] + (t[1]-t[0])*(-km[edges])/(km[edges+1]-km[edges])
        # Order vector and raster crossings by distance along the section.
        order = np.argsort(np.sum((reference-a)*(b-a), axis=1))
        offset = a + at[:,None]*(b-a) - reference[order]
        miss = 111.2*np.linalg.norm(offset*[np.cos(np.deg2rad((a[1]+b[1])*.5)),1], axis=1)
        assert miss.max() < .20, (name, miss)
        errors.extend(miss)
    return len(errors), max(errors)


def verify_sites(atlas, ocean):
    sites = [(name+" mouth", *point, True) for name, a, b, _ in GATES for point in (a, b)]
    sites += [
        ("Gallipoli isthmus", 26.76, 40.52, False),
        ("Gulf of Saros west of the isthmus", 26.655, 40.52, True),
        ("Sea of Marmara east of the isthmus", 26.90, 40.52, True),
        ("European bank of Istanbul", 29.043, 41.083, False),
        ("Bosphorus shipping channel", 29.058, 41.083, True),
        ("Asian bank of Istanbul", 29.075, 41.083, False),
        ("Gallipoli bank across from Canakkale", 26.36, 40.15, False),
        ("Dardanelles shipping channel", 26.387, 40.15, True),
        ("Canakkale bank", 26.417, 40.15, False),
    ]
    for name, lon, lat, wet in sites:
        km, weight = atlas.sample([lon], [lat])
        assert contains_xy(ocean, lon, lat) == wet, (name, "invalid reference point")
        assert weight[0] > .98 and (km[0] > 0) == wet, (name, km, weight)
    for lon, lat in ((-79.5, 9.1), (32.50, 30.0), (51., 42.), (33., -1.4), (0., 0.)):
        _, weight = atlas.sample([lon], [lat])
        assert weight[0] == 0, (lon, lat, "unexpected regional override")
    return len(sites)


def verify_navigation(ocean, widths, artifacts):
    """Test the exact player route segments against the original vector ocean."""
    mouths = {name: (a, b) for name, a, b, _ in GATES}
    windows = {name: box(min(a[0], b[0])-pad, min(a[1], b[1])-pad,
                         max(a[0], b[0])+pad, max(a[1], b[1])+pad)
               for name, a, b, pad in GATES}
    black_sea = mouths["Bosporus"][1]
    mouths["Turkish straits"] = (mouths["Dardanelles"][0], black_sea)
    mouths["Istanbul nearshore pick"] = ((29.0529, 41.083), black_sea)
    mouths["Marmara sub-cell pick"] = ((28.11, 40.79), black_sea)
    if not widths:
        widths = [int(n) for n in (ASSET.parent / "PlayerWidths.txt").read_text().split()]
    if artifacts:
        artifacts.mkdir(parents=True, exist_ok=True)
    env = os.environ.copy()
    env.setdefault("DOTNET_ROLL_FORWARD", "Major")
    with tempfile.TemporaryDirectory() as temp:
        work = Path(temp)
        controls = work / "mouths.json"
        controls.write_text(json.dumps(mouths))
        project = ROOT / "Validation/FlatWorld/FlatWorld.Core.Tests.csproj"
        subprocess.run(["dotnet", "build", str(project), "-c", "Release", "--nologo", "--artifacts-path",
                        str(work / "dotnet"), "-o", str(work / "runner")], cwd=ROOT, env=env, check=True)
        for width in widths:
            output = (artifacts or work) / f"sailing-routes-{width}.json"
            subprocess.run(["dotnet", str(work / "runner/FlatWorld.Core.Tests.dll"), "--sea-routes", str(width),
                            str(controls), str(output.resolve())], cwd=ROOT, env=env, check=True)
            tracks = json.loads(output.read_text())
            assert tracks.keys() == mouths.keys()
            legs = 0
            for name, points in tracks.items():
                path = LineString(points)
                assert (tuple(points[0]), tuple(points[-1])) == mouths[name], (width, name, "endpoints moved")
                assert ocean.covers(path), (width, name, "dry leg", path.difference(ocean).wkt[:200])
                if name in windows:
                    assert windows[name].covers(path), (width, name, "left this passage")
                if name == "Turkish straits":
                    for west, east, lat in ((26.34, 26.43, 40.15), (29.02, 29.095, 41.083)):
                        assert path.crosses(LineString([(west, lat), (east, lat)])), (width, name, "missed a narrows")
                legs += len(points)-1
            print(f"Earth {width}: source ocean covers {legs} complete sailing legs across {len(tracks)} routes "
                  "(18 local straits, the Turkish straits together and two exact water picks).", flush=True)


def geographic_panels(atlas, ocean, directory):
    directory.mkdir(parents=True, exist_ok=True)
    for name, lon, lat, span in (("Bosporus", 29.066, 41.100, .31), ("Dardanelles", 26.40, 40.18, .48),
                                 ("Gallipoli-isthmus", 26.755, 40.525, .26)):
        w, h = 420, 360
        x = lon + ((np.arange(w)+.5)/w-.5)*span
        y = lat + (.5-(np.arange(h)+.5)/h)*span*h/w*np.cos(np.deg2rad(lat))
        distance, weight = atlas.sample(x, y[:, None])
        assert np.min(weight) > .99
        fields = [contains_xy(ocean, x, y[:, None]), distance > 0]
        image = Image.new("RGB", (2*w+1, h+26), "white")
        for i, wet in enumerate(fields):
            rgb = np.where(wet[..., None], [38, 106, 145], [221, 210, 178]).astype(np.uint8)
            image.paste(Image.fromarray(rgb), (i*(w+1), 26))
        draw = ImageDraw.Draw(image)
        draw.text((8, 6), name+" | Natural Earth 1:10m", fill="black")
        draw.text((w+9, 6), "Player shoreline atlas | north up", fill="black")
        image.save(directory/(name+".png"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, default=ROOT/"ValidationOutput/BasinWater")
    parser.add_argument("--artifacts", type=Path)
    parser.add_argument("--navigation", type=int, nargs="*", metavar="WIDTH",
                        help="Build and GIS-check exact player routes at the selected widths (default: all player widths)")
    args = parser.parse_args()
    atlas = Atlas()
    parts = [p for feature in features("ocean", args.cache) for p in polygons(feature)]
    shell, *holes = max(parts, key=lambda p: np.ptp(np.array(p[0])[:, 0]))
    ocean = Polygon(shell, holes)
    prepare(ocean)
    sites = verify_sites(atlas, ocean)
    error = verify_transects(atlas, ocean)
    estuary_count, estuary_error = verify_estuary_banks(atlas, ocean)
    pairs = verify_seams(atlas)
    routes = verify_passages(atlas)
    if args.artifacts:
        geographic_panels(atlas, ocean, args.artifacts)
    if args.navigation is not None:
        verify_navigation(ocean, args.navigation, args.artifacts)
    print(f"{routes} connected rendered straits, {sites} wet/dry sites, 14 strait banks within "
          f"{error*1000:.0f} m and {estuary_count} estuary banks within {estuary_error*1000:.0f} m "
          f"of source, {pairs} matching detail page seams.")


if __name__ == "__main__":
    main()
