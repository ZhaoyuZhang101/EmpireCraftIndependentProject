"""Compare a generated Earth export with CRU-CL v2.0 on its native grid in Europe and the Mediterranean."""
import argparse
import json
from pathlib import Path

import numpy as np

from prepare import CACHE, HEIGHT, NX, WIDTH, area_overlaps, holdout, source_grid
from score import read_model, tile_area


REGIONS = {
    "western_greece": (19.5, 21.5, 38.3, 40.1),
    "eastern_greece": (22.0, 24.2, 37.5, 40.1),
    "peloponnese": (21.3, 23.3, 36.4, 38.3),
    "crete": (23.4, 26.4, 34.8, 35.8),
    "inland_balkans": (19, 25, 42, 46),
    "dinaric_coast": (14.1, 18.5, 42.2, 45.6),
    "southern_italy": (12.5, 18.5, 37, 42),
    "po_valley": (8, 13, 44.5, 45.8),
    "southern_iberia": (-6, 2, 36, 40.8),
    "western_anatolia": (26, 30, 36.5, 40),
    "levant": (34, 37, 31, 37),
    "maghreb": (-6, 12, 34, 37.5),
    "atlantic_europe": (-5, 8, 46, 52),
    "central_europe": (10, 22, 47, 53),
    "southern_scandinavia": (8, 18, 55, 61),
}
SITES = {
    "Ioannina": (20.852, 39.665), "Arta": (20.987, 39.161), "Larissa": (22.418, 39.639),
    "Athens": (23.728, 37.984), "Thessaloniki": (22.946, 40.640), "Tripoli": (22.373, 37.509),
    "Heraklion": (25.144, 35.338), "Sarajevo": (18.413, 43.856), "Belgrade": (20.448, 44.787),
    "Sofia": (23.322, 42.699), "Skopje": (21.430, 41.998), "Split": (16.441, 43.508),
    "Rome": (12.497, 41.9), "Izmir": (27.142, 38.424), "Madrid": (-3.704, 40.417),
    "Barcelona": (2.169, 41.387), "Marseille": (5.369, 43.296), "Berlin": (13.405, 52.520),
    "London": (-.127, 51.508), "Vienna": (16.37, 48.209), "Tunis": (10.182, 36.807),
}


def regional_reference(width, height, cache):
    """Intersect fine source cells with each native target, including partial coastal overlap."""
    step = 360 / width
    west = min(box[0] for box in REGIONS.values()) - step
    east = max(box[1] for box in REGIONS.values()) + step
    south = min(box[2] for box in REGIONS.values()) - step
    north = max(box[3] for box in REGIONS.values()) + step
    grids = []
    for variable in ("elv", "tmp", "pre"):
        ids, field = source_grid(cache, variable)
        longitude, latitude = (ids % NX + .5) / 6 - 180, (ids // NX + .5) / 6 - 90
        keep = (longitude >= west - 1/6) & (longitude <= east + 1/6) & (latitude >= south - 1/6) & (latitude <= north + 1/6)
        order = np.argsort(ids[keep])
        grids.append((ids[keep][order], field[keep][order]))
    ids = np.intersect1d(np.intersect1d(grids[0][0], grids[1][0]), grids[2][0])
    values = np.column_stack([field[np.searchsorted(indices, ids)] for indices, field in grids])
    values[:, 0] *= 1000  # CRU elevation is in km.
    overlaps = list(area_overlaps(ids, width, height))
    cells = np.unique(np.concatenate([tile for _, tile, _ in overlaps]))
    area = np.zeros(len(cells))
    sums = np.zeros((len(cells), values.shape[1]))
    for keep, tiles, weight in overlaps:
        slots = np.searchsorted(cells, tiles)
        area += np.bincount(slots, weight, minlength=len(cells))
        for column in range(values.shape[1]):
            sums[:, column] += np.bincount(slots, weight * values[keep, column], minlength=len(cells))
    return cells, area, sums / area[:, None]


def normals(temp, rain):
    return {
        "temperature_c": temp.tolist(), "precipitation_mm": rain.tolist(),
        "annual_mm": float(rain.sum()), "djf_mm": float(rain[[11, 0, 1]].sum()),
        "jja_mm": float(rain[5:8].sum()),
    }


def compare(path, cache, split):
    elevation, _, water, temperature, rain = read_model(path)
    height, width = elevation.shape
    cells, area, reference = regional_reference(width, height, cache)
    x, y = cells % width, cells // width
    lon, lat = (x + .5) * 360 / width - 180, (y + .5) * 180 / height - 90
    # Locate the native cell centre on the project's registered 256-wide reference grid.
    parent = (2 * y + 1) * HEIGHT // (2 * height) * WIDTH + (2 * x + 1) * WIDTH // (2 * width)
    unique_parents, parent_slots = np.unique(parent, return_inverse=True)
    reserved = np.array([holdout(i) for i in unique_parents])[parent_slots]
    selected = np.ones(len(cells), dtype=bool) if split == "all" else reserved == (split == "holdout")
    share = area / tile_area(height)[y]
    water, elevation = water.ravel()[cells], elevation.ravel()[cells]
    temperature, rain = temperature[y, x], rain[y, x]
    regions = {}
    for name, box in REGIONS.items():
        west, east, south, north = box
        mask = selected & (share >= .5) & (lon >= west) & (lon < east) & (lat >= south) & (lat < north)
        row = regions[name] = {"bounds_west_east_south_north": box, "reference_cells": int(mask.sum())}
        if mask.any():
            weights = area[mask] / area[mask].sum()
            row.update(area_km2=float(area[mask].sum()), game_water_cells=int((~np.isin(water[mask], (0, 5))).sum()),
                       game=normals(weights @ temperature[mask], weights @ rain[mask]),
                       observed=normals(weights @ reference[mask, 1:13], weights @ reference[mask, 13:]))
    sites = {}
    for name, (site_lon, site_lat) in SITES.items():
        cell = int((site_lat + 90) / 180 * height) * width + int((site_lon + 180) / 360 * width)
        slot = np.searchsorted(cells, cell)
        if slot == len(cells) or cells[slot] != cell or not selected[slot]:
            continue
        sites[name] = {
            "tile": cell, "center_lon_lat": [float(lon[slot]), float(lat[slot])],
            "reference_land_share": float(share[slot]), "split": "holdout" if reserved[slot] else "calibration",
            "game_water_kind": int(water[slot]), "game_elevation_m": int(elevation[slot]),
            "observed_land_elevation_m": float(reference[slot, 0]),
            "game": normals(temperature[slot], rain[slot]), "observed": normals(reference[slot, 1:13], reference[slot, 13:]),
        }
    return {"source": "CRU-CL v2.0 land normals, 1961–1990", "model": path.name,
            "width": width, "height": height, "split": split, "regions": regions, "sites": sites}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("export", type=Path)
    parser.add_argument("--cache", type=Path, default=CACHE, help="Directory of the checksum-verified 10-arcminute CRU source archives.")
    parser.add_argument("--split", choices=("calibration", "holdout", "all"), default="calibration")
    parser.add_argument("--json", required=True, type=Path)
    args = parser.parse_args()
    if args.json.resolve() == args.export.resolve():
        parser.error("Choose a JSON output path distinct from the model export.")
    report = compare(args.export, args.cache, args.split)
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(report, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    for name, region in report["regions"].items():
        count = region["reference_cells"]
        if count:
            print(f"{name:22} n={count:3d}  JJA game {region['game']['jja_mm']:.1f}  CRU {region['observed']['jja_mm']:.1f} mm")
        else:
            print(f"{name:22} n=0")
