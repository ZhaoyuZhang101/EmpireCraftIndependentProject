"""Compare native Yangtze estuary and Huangpu camera captures with chart and satellite water."""
import argparse
import re
from pathlib import Path

import numpy as np
from PIL import Image
from scipy.ndimage import binary_opening, distance_transform_edt
from shapely import contains_xy
from shapely.geometry import box

from verify_strait_camera import ROOT, camera_coordinates, geometry
from huangpu import TILE_SHA256, lower_course
from worldcover import open_worldcover


def camera(report, name, directory, world_width):
    views = []
    settings = []
    for layer in ("Natural", "Rivers"):
        match = re.search(rf"^{name}-{layer}: ([-.\d]+)E ([-.\d]+)N, ([.\d]+) px/tile, "
                          r"ORBIS/FlatWorld/LocalSurface, atlas 1, preview 0,", report, re.MULTILINE)
        if not match:
            raise ValueError(f"Missing published {name}-{layer} local camera")
        settings.append(tuple(map(float, match.groups())))
        with Image.open(directory / f"{name}-{layer}.png") as image:
            views.append(np.asarray(image.convert("RGB"), dtype=np.int16))
    assert settings[0] == settings[1] and views[0].shape == views[1].shape
    lon, lat, scale = settings[0]
    h, w = views[0].shape[:2]
    return *camera_coordinates((lon, lat), w, h, world_width, scale), 40030.2/(world_width*scale), *views


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("--cache", type=Path, default=ROOT / "ValidationOutput/BasinWater")
    parser.add_argument("--artifacts", type=Path)
    args = parser.parse_args()
    report = (args.capture / "play.txt").read_text(encoding="utf8")
    width = int(re.search(r"^Earth (\d+)x\d+, Natural, survey=True", report, re.MULTILINE)[1])
    ocean = geometry("ocean", args.cache, box(119, 29, 124, 34))
    # Resample the independently classified 10 m tile onto a 30 m reference grid.
    with open_worldcover(args.cache, "N30E120", TILE_SHA256) as tile:
        cover = tile.read(1, out_shape=(12000, 12000))
        inverse = ~(tile.transform * tile.transform.scale(3, 3))
    def satellite(lon, lat):
        x, y = inverse * (lon, lat)
        return cover[np.clip(np.floor(y).astype(int), 0, len(cover)-1),
                     np.clip(np.floor(x).astype(int), 0, cover.shape[1]-1)]
    mainstem, widths = lower_course(args.cache)
    if args.artifacts:
        args.artifacts.mkdir(parents=True, exist_ok=True)
    failures = []
    for name in ("yangtze-estuary", "huangpu-wusong", "huangpu-bund", "huangpu-upper"):
        lon, lat, km, natural, rivers = camera(report, name, args.capture, width)
        marine = contains_xy(ocean, lon, lat)
        permanent = satellite(lon, lat) == 80
        area = (lon > 121.20) & (lon < 121.96) & (lat > 31.30) & (lat < 31.83)
        # Uncertain banks, date-dependent reclamation and narrow inland waterways
        # are excluded. Both independent sources agree on the interior controls.
        coast_km = distance_transform_edt(~marine)*km
        permanent_km = distance_transform_edt(~permanent)*km
        dry = area & (coast_km > 1.5) & (permanent_km > .9)
        wet = area & (distance_transform_edt(marine)*km > 1.5) & (distance_transform_edt(permanent)*km > .9)
        # Ochre land, pale basin water and blue river ink in the river-map palette.
        chart_water = rivers[..., 2] - rivers[..., 0] > 12
        # Daylight water is blue/teal. A soft natural bank and small overlapping
        # crowns can change that classification, so compare its area fraction.
        surface_water = (natural[..., 2] - natural[..., 0] > 32) & (natural[..., 2] - natural[..., 1] > -24)
        if name == "yangtze-estuary":
            # A faulty shore derivative paints one-pixel strips of pale sand along
            # dry simulation-cell edges. Trees and field details do not produce
            # long axial strips with a red-channel spike against both neighbours.
            inland = ((lon > 120.8) & (lon < 122.05) & (lat > 30.8) & (lat < 32.12) &
                      (coast_km > .7) & (permanent_km > .25))
            red = natural[..., 0]
            lines = np.zeros_like(inland)
            for axis, coordinate in ((0, (lat+90)*width/360), (1, (lon+180)*width/360)):
                grid = np.abs(coordinate-np.rint(coordinate)) <= .05
                peak = red - np.maximum(np.roll(red, -2, axis), np.roll(red, 2, axis)) > 32
                strip = np.ones((1, 10) if axis==0 else (10, 1), dtype=bool)
                lines |= binary_opening(inland & grid & peak, strip)
            print(f"{name} Natural: {lines.sum()} bright grid-edge bank pixels on independently surveyed dry ground")
            if lines.any():
                failures.append("inland rectangular bank marks")
        if name != "huangpu-upper":
            if min(dry.sum(), wet.sum()) < 100:
                raise ValueError(f"Insufficient independent interior controls at {name}")
            for layer, painted in (("Rivers", chart_water), ("Natural", surface_water)):
                drowned, missing = np.count_nonzero(dry & painted), np.count_nonzero(wet & ~painted)
                print(f"{name} {layer}: {drowned}/{dry.sum()} dry pixels flooded, "
                      f"{missing}/{wet.sum()} wet pixels missing ({km:.3f} km/px)")
                if drowned > max(4, dry.sum()*.015) or missing > max(4, wet.sum()*.015):
                    failures.append(f"{name} {layer} coastline")
                if args.artifacts:
                    marked = (rivers if layer == "Rivers" else natural).copy()
                    marked[dry & painted] = (234, 32, 73)
                    marked[wet & ~painted] = (243, 190, 11)
                    Image.fromarray(marked.astype(np.uint8)).save(args.artifacts / f"{name}-{layer}-errors.png")
        if name in ("huangpu-bund", "huangpu-wusong"):
            # Independent Landsat river stations with both banks on satellite land.
            # A 2x chart symbol reaches one measured full width from its centre.
            selected = (mainstem[:, 1] > (31.18 if name=="huangpu-bund" else 31.31)) & (
                mainstem[:, 1] < (31.31 if name=="huangpu-bund" else 31.385))
            m = np.array([np.cos(np.deg2rad(31.3)), 1.0])*111195.08
            tangent = np.gradient(mainstem, axis=0)*m
            normal = tangent[:, ::-1]*[-1, 1]/np.linalg.norm(tangent, axis=1)[:, None]
            probes = mainstem[:, None, :] + (widths[:, None]*normal/m)[:, None, :] * np.array([-2., 0., 2.])[None, :, None]
            cls = satellite(probes[..., 0], probes[..., 1])
            checked = probes[selected & (cls[:,0] != 80) & (cls[:,1] == 80) & (cls[:,2] != 80)]
            if len(checked)<6:
                raise ValueError(f"Missing independently confirmed Huangpu cross-sections at {name}")
            pixels = []
            for station in checked:
                rows = []
                for x, y in station:
                    i = np.argmin(((lon-x)*m[0])**2 + ((lat-y)*m[1])**2)
                    rows.append(chart_water.flat[i])
                pixels.append(rows)
            correctly_drawn = np.all(np.asarray(pixels) == [False, True, False], axis=1)
            print(f"{name}: {correctly_drawn.sum()}/{len(checked)} surveyed Huangpu cross-sections have "
                  "a wet centre and two dry banks")
            if correctly_drawn.mean() < .95:
                failures.append(f"{name} Huangpu banks")
        if name == "huangpu-upper":
            # Native WorldCover centre pixels from the upper reach, where the
            # Landsat transect survey used above has no continuous coverage.
            upper = ((120.978792,31.089875), (121.011542,31.072125), (121.034375,31.058875),
                     (121.107625,31.008292), (121.172292,30.957458), (121.192292,30.953708))
            for x, y in upper:
                point = np.argmin(((lon-x)*np.cos(np.deg2rad(y)))**2 + (lat-y)**2)
                if not chart_water.flat[point]:
                    failures.append(f"Upper Huangpu centre at {x}, {y}")
    if failures:
        raise SystemExit("FAIL " + ", ".join(failures))
    print("PASS surveyed estuary shores and Huangpu cross-sections")


if __name__ == "__main__":
    main()
