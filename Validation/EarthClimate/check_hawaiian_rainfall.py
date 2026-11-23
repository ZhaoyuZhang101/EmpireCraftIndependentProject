"""Compare ECL1 exports with the University of Hawai‘i 1978–2007 Rainfall Atlas.

Download StateASCIIGrids_mm.zip (or a single island's millimetre ASCII grids)
from https://rainfall.geography.hawaii.edu/downloads . Observations are used
only here. Each model tile is compared with its observed land-area mean.
"""

import argparse
from pathlib import Path
import struct
from zipfile import ZipFile

import numpy as np


def reference_tiles(archive, width):
    with ZipFile(archive) as bundle:
        name = next(name for name in bundle.namelist() if name.lower().endswith("_ann.txt"))
        with bundle.open(name) as grid:
            header = dict(grid.readline().decode("ascii").lower().split() for _ in range(6))
            rainfall = np.loadtxt(grid)
    rows, columns = np.nonzero(rainfall != float(header["nodata_value"]))
    cell = float(header["cellsize"])
    west = float(header["xllcorner"]) + columns * cell
    south = float(header["yllcorner"]) + (rainfall.shape[0] - 1 - rows) * cell
    east, north = west + cell, south + cell
    step = 360 / width
    x = np.floor((west + 180) / step).astype(int)
    y = np.floor((south + 90) / step).astype(int)
    xcut = np.minimum(east, (x + 1) * step - 180)
    ycut = np.minimum(north, (y + 1) * step - 90)
    # Each Atlas pixel intersects at most two rows and two columns of the
    # model. Longitude width times the difference of sin(lat) is spherical area.
    ids = np.unique(np.concatenate([y * width + x + offset for offset in (0, 1, width, width + 1)]))
    area = np.zeros(len(ids))
    amount = np.zeros(len(ids))
    values = rainfall[rows, columns]
    for dy, low, high in ((0, south, ycut), (1, ycut, north)):
        lat_weight = np.sin(np.radians(high)) - np.sin(np.radians(low))
        for dx, lon_weight in ((0, xcut - west), (1, east - xcut)):
            weights = lat_weight * lon_weight
            group = np.searchsorted(ids, (y + dy) * width + x + dx)
            area += np.bincount(group, weights=weights, minlength=len(ids))
            amount += np.bincount(group, weights=weights * values, minlength=len(ids))
    valid = area > 0
    return ids[valid], area[valid], amount[valid] / area[valid]


def compare(model, archive):
    with model.open("rb") as f:
        magic, width, height = struct.unpack("<4sii", f.read(12))
    count = width * height
    if magic != b"ECL1" or width != 2 * height or model.stat().st_size != 12 + 101 * count:
        raise ValueError(f"Invalid ECL1 export: {model}")
    ids, area, observed = reference_tiles(archive, width)
    water = np.memmap(model, dtype="u1", mode="r", offset=12 + 4 * count, shape=(count,))
    rain = np.memmap(model, dtype="<f4", mode="r", offset=12 + 53 * count, shape=(count, 12))
    land = np.isin(water[ids], (0, 5))
    coverage = area[land].sum() / area.sum()
    print(f"{model.name}: {land.sum()} matched land tiles, {coverage:.1%} of Atlas land area")
    if not land.any():
        return
    weights = area[land] / area[land].sum()
    simulated = rain[ids[land]].sum(axis=1, dtype=np.float64)
    reference = observed[land]
    delta = simulated - reference
    a, b = simulated - weights @ simulated, reference - weights @ reference
    denominator = np.sqrt((weights @ (a * a)) * (weights @ (b * b)))
    corr = weights @ (a * b) / denominator if denominator > 0 else float("nan")
    print(f"  annual MAE {weights @ np.abs(delta):.0f}, RMSE {np.sqrt(weights @ (delta * delta)):.0f}, "
          f"bias {weights @ delta:+.0f} mm, spatial correlation {corr:.2f}")
    for name, lon, lat in (("Hilo", -155.09, 19.72), ("Waikoloa", -155.795, 19.94), ("Mauna Kea", -155.47, 19.82)):
        tile = int((lat + 90) * height / 180) * width + int((lon + 180) * width / 360)
        found = np.flatnonzero(ids == tile)
        if found.size and land[found[0]]:
            print(f"  {name}: simulated {rain[tile].sum():.0f}, observed {observed[found[0]]:.0f} mm/year")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("atlas_zip", type=Path)
    parser.add_argument("models", type=Path, nargs="+")
    args = parser.parse_args()
    for model_file in args.models:
        compare(model_file, args.atlas_zip)
