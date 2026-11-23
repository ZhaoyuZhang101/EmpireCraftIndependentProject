"""Check EarthStraitPlayProbe camera PNGs against independently surveyed marine shores.

Run with the directory containing play.txt and the two native river-map PNGs:
    python Validation/EarthScale100/verify_strait_camera.py <capture> --cache <source-cache>
Install Tools/EarthScale100/requirements.txt. The pinned Natural Earth source
is reused from the water baker's cache. Add --artifacts <directory> for error maps.
Add --route to check the player's visible line and its entire WGS84 polyline.
"""
import argparse
import csv
import re
from pathlib import Path

import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt, label
from shapely import contains_xy, distance, points, prepare
from shapely.geometry import LineString, Point, box, shape
from shapely.ops import transform, unary_union

from verify_strait_shore import Atlas, ROOT, features


def camera_coordinates(center, width, height, world_width, pixels_per_tile):
    """Invert the player's north-up, local stereographic camera at pixel centres."""
    lon, lat = np.deg2rad(center)
    east, north = np.meshgrid(np.arange(width) + .5 - width / 2, height / 2 - np.arange(height) - .5)
    radians_per_pixel = 2 * np.pi / (world_width * pixels_per_tile)
    x, y = east * radians_per_pixel, north * radians_per_pixel
    up = 1 - (x*x + y*y) / 4
    horizontal = up * np.cos(lat) - y * np.sin(lat)
    longitude = lon + np.arctan2(x, horizontal)
    latitude = np.arctan2(up * np.sin(lat) + y * np.cos(lat), np.hypot(x, horizontal))
    return np.rad2deg(longitude), np.rad2deg(latitude)


def geometry(layer, cache, window):
    selected = []
    for feature in features(layer, cache):
        item = shape(feature["geometry"])
        if item.intersects(window):
            selected.append(item.intersection(window))
    result = unary_union(selected)
    prepare(result)
    return result


def leaf_colours(rgb):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    return (g-r > 15) & (g-b > 32) & (r-b > 4)


def check_izmit_trees(capture, longitude, latitude, source_water, ocean, weight, kilometres_per_pixel, world_width, artifacts):
    with Image.open(capture / "bosporus-natural.png") as image:
        rgb = np.array(image.convert("RGB"), dtype=np.int16)
    if rgb.shape[:2] != source_water.shape:
        raise ValueError("Natural and river-map captures must have the same footprint")
    # In this daylight scene, crowns are visibly yellow-green against blue sea.
    # Small coastal canopies can overhang legitimate banks. Only test connected
    # patches well inside the gulf. Require that the adjacent land still has foliage.
    leaf = leaf_colours(rgb)
    water_depth = distance_transform_edt(source_water) * kilometres_per_pixel
    inside_gulf = (longitude > 29.35) & (longitude < 29.87) & (latitude > 40.68) & (latitude < 40.82)
    control = (weight >= .999) & inside_gulf & (water_depth >= 2.5)
    dry = (weight >= .999) & (distance_transform_edt(~source_water) * kilometres_per_pixel >= 2.5)
    if not control.any() or not (leaf & dry).any():
        raise ValueError("Izmit camera must include open water and visible foliage on dry land")
    components, _ = label(leaf & control)
    counts = np.bincount(components.ravel())
    crowns = np.flatnonzero(counts[1:] >= 4) + 1
    candidates = np.isin(components, crowns)
    print(f"Izmit natural view: {len(crowns)} leaf-coloured patches ({candidates.sum()} pixels) in {control.sum()} "
          f"surveyed marine pixels >=2.5 km from shore; {np.count_nonzero(leaf & dry)} foliage-coloured dry control pixels.")
    # Source ownership explains overhangs. Every deep-water canopy patch still
    # fails the original appearance check, including one cast from a legitimate bank.
    owned = np.zeros_like(leaf)
    source_file = capture / "izmit-tree-roots.csv"
    wet_roots = 0
    missing_bank_tree = 0
    if source_file.is_file():
        with source_file.open(encoding="utf8", newline="") as source:
            roots = list(csv.DictReader(source))
        active = [root for root in roots if root["eligible"] == "1"]
        for root in active:
            lon, lat = map(float, (root["longitude"], root["latitude"]))
            if ocean.covers(Point(lon,lat)):
                wet_roots += 1
                print(f"  Tree rooted in source ocean at {lon:.6f} E, {lat:.6f} N.")
                continue
            if root["toggled"] != "1":
                continue
            x, y = root["tile_x"], root["tile_y"]
            with Image.open(capture / f"izmit-without-tree-{x}-{y}.png") as image:
                hidden = np.array(image.convert("RGB"), dtype=np.int16)
            if hidden.shape != rgb.shape:
                raise ValueError("Izmit source subtraction must use the identical camera footprint")
            change = np.max(np.abs(rgb-hidden), axis=2) >= 12
            removed_leaves = leaf & change & ~leaf_colours(hidden)
            removed = candidates & removed_leaves
            owned |= removed
            near = 111.2*np.hypot((longitude-lon)*np.cos(np.deg2rad(lat)), latitude-lat) <= 2
            dry_leaves = np.count_nonzero(near & ~source_water & removed_leaves)
            print(f"  Suppressing dry source ({x}, {y}) at {lon:.6f} E, {lat:.6f} N removes "
                  f"{removed.sum()} deep-water leaf pixels and {dry_leaves} dry-land leaf pixels near the root (<=2 km); "
                  f"GPU signed marine distance {float(root['gpu_marine_km']):.3f} km.")
            if world_width==4096 and dry_leaves==0:
                missing_bank_tree += 1
        print(f"  {len(roots)} geographic source tiles, {len(active)} eligible trees, {wet_roots} eligible roots in source ocean.")
    bad = candidates
    if bad.any():
        y, x = np.unravel_index(np.argmax(bad * water_depth), bad.shape)
        print(f"  {bad.sum()} offshore leaf-coloured pixels, {np.count_nonzero(bad & ~owned)} unexplained; deepest at {longitude[y,x]:.5f} E, "
              f"{latitude[y,x]:.5f} N ({water_depth[y,x]:.2f} km inside the sea).")
        if artifacts:
            rgb[bad] = [255, 48, 109]
            Image.fromarray(rgb.astype(np.uint8)).save(artifacts / "izmit-offshore-foliage.png")
    return len(crowns) + wet_roots + missing_bank_tree


def check(capture, report, site, world_width, atlas, ocean, lakes, artifacts, route, window=None):
    dims = re.search(
        rf"^{site}-river-map: (\d+)x(\d+), ([\d.]+) px/tile, center ([-.\d]+)E ([-.\d]+)N,",
        report, re.MULTILINE)
    if not dims:
        raise ValueError(f"Missing {site} camera settings in play.txt")
    w, h = map(int, dims.group(1, 2))
    scale, lon0, lat0 = map(float, dims.group(3, 4, 5))
    with Image.open(capture / (site + "-river-map.png")) as image:
        assert image.size == (w, h), f"{site} PNG dimensions disagree with its recorded camera"
        rgb = np.array(image.convert("RGB"), dtype=np.int16)
    longitude, latitude = camera_coordinates((lon0, lat0), w, h, world_width, scale)
    source_water = contains_xy(ocean, longitude, latitude)
    freshwater = contains_xy(lakes, longitude, latitude)
    # The chart has a pale blue area-water fill, ochre land and dark river ink.
    rendered_water = (rgb[..., 2] - rgb[..., 0] > 12) & (rgb[..., 0] > 103)
    if window is None:
        _, weight = atlas.sample(longitude, latitude)
    else:
        # The named throat and both its banks must be drawn, even when a page
        # is missing. An atlas-derived coverage mask would hide that regression.
        west, south, east, north = window
        weight = (longitude >= west) & (latitude >= south) & (longitude <= east) & (latitude <= north)
    # The atlas only defines the area under test. Expected land and sea come
    # from the independent vector polygon. Four pixels exclude the antialiased
    # shoreline and the small view displacement caused by coastal terrain relief.
    coast_distance = np.maximum(distance_transform_edt(source_water), distance_transform_edt(~source_water))
    checked = (weight >= .999) & (coast_distance >= 4)
    if freshwater.any():
        checked &= distance_transform_edt(~freshwater) >= 4
    wet, dry = np.count_nonzero(checked & source_water), np.count_nonzero(checked & ~source_water)
    if not wet or not dry:
        raise ValueError(f"{site} has insufficient surveyed land or water controls")
    errors = checked & (source_water != rendered_water)
    print(f"{site}: {errors.sum()}/{checked.sum()} wrong land/sea pixels; {wet} surveyed sea and {dry} dry controls, "
          f"at least 4 pixels from banks, {scale:g} px/tile.")
    if errors.any():
        components, _ = label(errors)
        counts = np.bincount(components.ravel())
        for group in np.argsort(counts[1:])[-3:][::-1] + 1:
            y, x = np.unravel_index(np.argmax((components == group) * coast_distance), errors.shape)
            expected = "sea" if source_water[y, x] else "dry land"
            print(f"  {counts[group]} wrong pixels; expected {expected} at "
                  f"{longitude[y,x]:.5f} E, {latitude[y,x]:.5f} N, pixel ({x},{y}), "
                  f"{coast_distance[y,x]:.1f} px inside surveyed area.")
    if artifacts:
        marked = rgb.copy()
        marked[errors & source_water] = [255, 48, 109]
        marked[errors & ~source_water] = [16, 186, 16]
        Image.fromarray(marked.astype(np.uint8)).save(artifacts / (site + "-reference-disagreement.png"))
    if site == "bosporus":
        natural = re.search(r"^bosporus-natural: (.+?), day ", report, re.MULTILINE)
        river = re.search(r"^bosporus-river-map: (.+?), day ", report, re.MULTILINE)
        if not natural or not river or natural[1] != river[1]:
            raise ValueError("Matching native natural and river-map cameras are required")
        crown_errors = check_izmit_trees(capture, longitude, latitude, source_water, ocean, weight,
                                        40030.2 / (world_width * scale), world_width, artifacts)
    else:
        crown_errors = 0
    line_errors = 0
    if route:
        with Image.open(capture / (site + "-sea-route-ink.png")) as ink_image:
            assert ink_image.size == (w,h), f"{site} route mask does not match the survey camera"
            ink = np.asarray(ink_image.convert("L")) > 127
        # The two-pixel ribbon can legitimately cover an antialiased bank.
        # It must visibly pass through the actual narrow throat, and cannot
        # appear on independently checked dry ground outside the shoreline band.
        center = {"bosporus": (29.0675, 41.110), "dardanelles": (26.384, 40.160),
                  "kanmon": (130.950, 33.949)}[site]
        throat = (np.abs(longitude-center[0]) < .06) & (np.abs(latitude-center[1]) < .005)
        dry_ink = np.count_nonzero(ink & checked & ~source_water)
        throat_ink = np.count_nonzero(ink & source_water & throat)
        line_errors = int(dry_ink) + int(throat_ink == 0)
        print(f"{site} visible route: {ink.sum()} route pixels, {throat_ink} at the surveyed throat, "
              f"{dry_ink} on checked dry ground.")
    return int(errors.sum()), crown_errors, line_errors


def check_route_line(capture, ocean):
    coords = np.loadtxt(capture/"maritime-route.csv", delimiter=",", skiprows=1)
    if coords.ndim != 2 or len(coords) < 2:
        raise ValueError("Expected a maritime route from the two real pointer clicks")
    route = LineString(coords)
    crossings = []
    for lat,west,east in ((40.160, 26.36546, 26.40206), (41.110, 29.06053, 29.07458)):
        cut = route.intersection(LineString([(west,lat),(east,lat)]))
        if cut.is_empty:
            raise ValueError(f"Maritime route missed the surveyed strait at {lat} N")
        crossings.append(route.project(cut.representative_point()))
    if crossings[0] >= crossings[1]:
        raise ValueError("Aegean to Black Sea route crossed the two straits in the wrong order")
    # Local east/north metres on the game's sphere about the regional camera.
    # Across the Turkish straits this orthographic metric distorts length <0.1%.
    def local_metres(longitude, latitude):
        lon = np.deg2rad(np.asarray(longitude)-27.85)
        lat, origin = np.deg2rad(latitude), np.deg2rad(40.72)
        east = np.cos(lat)*np.sin(lon)
        north = np.cos(origin)*np.sin(lat)-np.sin(origin)*np.cos(lat)*np.cos(lon)
        return 6371008.8*east, 6371008.8*north
    path = transform(local_metres, route)
    sea = transform(local_metres, ocean)
    samples = np.asarray(path.segmentize(100).coords)
    gaps = distance(points(samples),sea)
    worst = float(np.max(gaps))
    # Check every segment against the original WGS84 vector geometry. The metric
    # projection and equally spaced samples above only describe any disagreement.
    covered = ocean.covers(route)
    print(f"Maritime route: {len(coords)} published waypoints, {path.length/1000:.2f} km, "
          f"both surveyed cross-sections crossed in order, full WGS84 ocean coverage={covered}, "
          f"{len(samples)} diagnostic samples at <=100 m, maximum projected distance outside "
          f"the source ocean {worst:.1f} m.")
    if not covered:
        witness = route.difference(ocean).representative_point()
        print(f"  Outside the original ocean polygon at {witness.x:.6f} E, {witness.y:.6f} N.")
    return int(not covered)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("--cache", type=Path, default=ROOT / "ValidationOutput/BasinWater")
    parser.add_argument("--artifacts", type=Path)
    parser.add_argument("--route", action="store_true", help="Require the Aegean to Black Sea route and native overlay captures")
    args = parser.parse_args()
    report = (args.capture / "play.txt").read_text(encoding="utf8")
    generated = re.search(r"^PASS player menu generated Earth (\d+)x(\d+),", report, re.MULTILINE)
    if not generated or int(generated[1]) != 2 * int(generated[2]):
        raise ValueError("Expected a real generated Earth world and its dimensions in play.txt")
    if args.artifacts:
        args.artifacts.mkdir(parents=True, exist_ok=True)
    args.cache.mkdir(parents=True, exist_ok=True)
    # The Sea of Marmara and both offshore approaches are within this window.
    window = box(24.5, 38.5, 31, 43)
    ocean = geometry("ocean", args.cache, window)
    lakes = geometry("lakes", args.cache, window)
    atlas = Atlas()
    checks = [check(args.capture, report, site, int(generated[1]), atlas, ocean, lakes, args.artifacts, args.route)
              for site in ("bosporus", "dardanelles")]
    if "kanmon-river-map:" in report:
        seto = box(129, 32, 137, 36)
        sea = geometry("ocean", args.cache, seto)
        inland = geometry("lakes", args.cache, seto)
        kanmon_route = args.capture / "kanmon-route.csv"
        for site, bounds in (
            ("kanmon", (130.915, 33.916, 131.050, 34.000)),
            ("naruto", (134.612, 34.208, 134.702, 34.273)),
            ("akashi", (134.960, 34.580, 135.077, 34.661)),
        ):
            checks.append(check(args.capture, report, site, int(generated[1]), None, sea, inland,
                                args.artifacts, site == "kanmon" and kanmon_route.is_file(), window=bounds))
        if kanmon_route.is_file():
            path = LineString(np.loadtxt(kanmon_route, delimiter=",", skiprows=1))
            cut = LineString([(130.930, 33.949), (130.978, 33.949)])
            if not sea.covers(path) or not path.intersects(cut):
                raise ValueError("Player route does not stay within the vector ocean through the Kanmon throat")
            print(f"Kanmon: complete {len(path.coords)}-waypoint route in source ocean, surveyed throat crossed.")
    wrong, crowns, route_errors = map(sum, zip(*checks))
    if args.route:
        route_errors += check_route_line(args.capture,ocean)
    if wrong or crowns or route_errors:
        raise SystemExit(f"FAIL: {wrong} incorrect interior pixels, {crowns} offshore foliage errors, {route_errors} route errors")
    print("PASS both shores match and the surveyed Gulf interior is clear of tree crowns.")
    if args.route:
        print("PASS the complete WGS84 route is covered by the source ocean and is visible at both straits.")


if __name__ == "__main__":
    main()
