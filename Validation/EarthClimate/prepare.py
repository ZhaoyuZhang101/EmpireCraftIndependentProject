"""Rebuild the area-averaged CRU-CL v2.0 fixture using its official source archives."""

import argparse
import csv
import gzip
import hashlib
import io
from pathlib import Path
from urllib.request import urlopen

import numpy as np


HERE = Path(__file__).resolve().parent
CACHE = HERE.parents[1] / "ValidationOutput/EarthClimate/sources"
SOURCE = "https://crudata.uea.ac.uk/cru/data/hrg/tmc/"
DIGESTS = {
    "tmp": "dd56851264c51a4040cfd3b7ff4ff36f8da104c0c39f956c88b6614510b4a20c",
    "pre": "c0a46277c84ad989625e38bbaccf2ead026391a4cc359795a3a38ff1a5a2bfe3",
    "elv": "9618229d30952ba2fd2e2dcc741e57b86ed2c83d7a37c328005792d0a2d0ed82",
}
WIDTH, HEIGHT = 256, 128
NX, NY = 2160, 1080  # 10 arcminutes
RADIUS_KM = 6371.0088


def source_grid(cache, var):
    filename = f"grid_10min_{var}.dat.gz"
    path = cache / filename
    if not path.exists():
        cache.mkdir(parents=True, exist_ok=True)
        with urlopen(SOURCE + filename, timeout=120) as response:
            data = response.read()
        if hashlib.sha256(data).hexdigest() != DIGESTS[var]:
            raise ValueError(f"Source checksum mismatch for {filename}")
        path.write_bytes(data)
    if hashlib.sha256(path.read_bytes()).hexdigest() != DIGESTS[var]:
        raise ValueError(f"Cached source checksum mismatch: {path}")

    # Precipitation has another 12 columns of coefficients of variation after the monthly totals.
    columns = 3 if var == "elv" else 14
    fields = np.loadtxt(path, usecols=range(columns), dtype=np.float64)
    x = np.rint((fields[:, 1] + 180) * 6 - 0.5).astype(int)
    y = np.rint((fields[:, 0] + 90) * 6 - 0.5).astype(int)
    if not ((x >= 0) & (x < NX) & (y >= 0) & (y < NY)).all():
        raise ValueError(f"Unregistered coordinates in {filename}")
    ids = y * NX + x
    if np.unique(ids).size != ids.size or not np.isfinite(fields[:, 2:]).all():
        raise ValueError(f"Repeated coordinates or invalid values in {filename}")
    return ids, fields[:, 2:]


def holdout(tile):
    # Reserve one complete 22.5-degree square per 90-by-22.5-degree sector.
    bx, by = (tile % WIDTH) // 16, (tile // WIDTH) // 16
    key = f"orbis-crucl2:{by}:{bx // 4}".encode("ascii")
    return bx % 4 == hashlib.sha256(key).digest()[0] % 4


def area_overlaps(ids, width=WIDTH, height=HEIGHT):
    x, y = ids % NX, ids // NX
    # Use integer grid-edge indices to find all intersected target cells. Each
    # fine cell intersects at most two rows and two columns of the target grid.
    west, east = x * width // NX, ((x + 1) * width - 1) // NX
    south, north = y * height // NY, ((y + 1) * height - 1) // NY
    for dy in range(int((north - south).max()) + 1):
        for dx in range(int((east - west).max()) + 1):
            tx, ty = west + dx, south + dy
            keep = (tx <= east) & (ty <= north)
            cell_x, cell_y = x[keep], y[keep]
            tx, ty = tx[keep], ty[keep]
            left = np.maximum(cell_x / 6, tx * 360 / width)
            right = np.minimum((cell_x + 1) / 6, (tx + 1) * 360 / width)
            bottom = np.maximum(cell_y / 6, ty * 180 / height) - 90
            top = np.minimum((cell_y + 1) / 6, (ty + 1) * 180 / height) - 90
            lat_span = np.sin(np.deg2rad(top)) - np.sin(np.deg2rad(bottom))
            area = RADIUS_KM**2 * np.deg2rad(right - left) * lat_span
            yield keep, ty * width + tx, area


def prepare(cache, output):
    grids = [source_grid(cache, var) for var in ("elv", "tmp", "pre")]
    ids = grids[0][0]
    for grid_ids, _ in grids[1:]:
        ids = np.intersect1d(ids, grid_ids, assume_unique=True)
    values = []
    for grid_ids, field in grids:
        order = np.argsort(grid_ids)
        indices = order[np.searchsorted(grid_ids[order], ids)]
        values.append(field[indices])
    values = np.column_stack(values)
    values[:, 0] *= 1000  # Source elevation is in kilometres.
    if (values[:, 13:] < 0).any() or np.abs(values[:, 1:13]).max() > 100:
        raise ValueError("Invalid physical units in CRU source data.")

    n = WIDTH * HEIGHT
    area = np.zeros(n)
    sums = np.zeros((n, values.shape[1]))
    for keep, tile, weights in area_overlaps(ids):
        area += np.bincount(tile, weights=weights, minlength=n)
        selected = values[keep]
        for col in range(values.shape[1]):
            sums[:, col] += np.bincount(tile, weights=selected[:, col] * weights, minlength=n)
    source_y = ids // NX
    expected = RADIUS_KM**2 * np.deg2rad(1 / 6) * (
        np.sin(np.deg2rad((source_y + 1) / 6 - 90)) - np.sin(np.deg2rad(source_y / 6 - 90)))
    if not np.isclose(expected.sum(), area.sum(), rtol=1e-12):
        raise ValueError("Remapping did not conserve the source land area.")
    tiles = np.flatnonzero(area > 0)
    sums[tiles] /= area[tiles, None]
    columns = ["tile", "area_km2", "elevation_m"]
    columns += [f"{var}{month:02}" for var in ("t", "p") for month in range(1, 13)]
    output.mkdir(parents=True, exist_ok=True)
    # Preserve the source footprint for matching model resolutions above 256.
    # Packing the source availability mask also avoids shipping the coarse
    # climate observations at resolutions the game cannot meaningfully resolve.
    mask = np.zeros(NY * NX, dtype=np.uint8)
    mask[ids] = 1
    with (output / "land-mask.bin.gz").open("wb") as base:
        with gzip.GzipFile(fileobj=base, mode="wb", filename="", compresslevel=9, mtime=0) as gz:
            gz.write(np.packbits(mask).tobytes())
    for split in ("calibration", "holdout"):
        rows = [tile for tile in tiles if holdout(tile) == (split == "holdout")]
        destination = output / (split + ".csv.gz")
        with destination.open("wb") as base, gzip.GzipFile(fileobj=base, mode="wb", filename="", compresslevel=9, mtime=0) as gz:
            with io.TextIOWrapper(gz, encoding="ascii", newline="") as stream:
                writer = csv.writer(stream, lineterminator="\n")
                writer.writerow(columns)
                for tile in rows:
                    writer.writerow([tile, f"{area[tile]:.4f}", f"{sums[tile, 0]:.2f}",
                                     *[f"{v:.2f}" for v in sums[tile, 1:]]])
        print(f"{split}: {len(rows)} cells, {area[rows].sum():,.0f} km2. {destination}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, default=CACHE)
    parser.add_argument("--output", type=Path, default=HERE)
    settings = parser.parse_args()
    prepare(settings.cache, settings.output)
