"""Compare ORBIS annual upper-ocean vectors with NOAA/AOML near-surface drifter velocities."""

import argparse
import hashlib
import json
from pathlib import Path

import numpy as np


SERVER = "https://erddap.aoml.noaa.gov/gdp/erddap/griddap/"
STRIDE = "%5B0:4:1439%5D%5B0:4:631%5D"
QUERY = "drifter_annualmeans.csv?" + ",".join(component + STRIDE for component in ("U", "V", "N"))
SHA256 = "f3e6b7bf2fba9e9d4d92ea5e7c8edba5512ebe2e531d671d1211af087d233f0f"
MIN_DIRECTION_SPEED = .05  # m/s in the observed mean, after aggregation


def aggregate(rows, shape):
    """Area-weight both observed vector components within each model cell before taking speeds."""
    h, w = shape
    lon, lat, east, north, days = rows.T
    x = np.floor(((lon + 180) % 360) * w / 360).astype(int)
    y = np.floor((lat + 90) * h / 180).astype(int)
    ids = y * w + x
    area = np.cos(np.deg2rad(lat))
    valid = np.isfinite(east) & np.isfinite(north) & np.isfinite(days) & (days > 0)
    count = h*w
    total = np.bincount(ids, area, minlength=count)
    covered = np.bincount(ids[valid], area[valid], minlength=count)
    supported = (np.bincount(ids[valid], minlength=count) >= 4) & (covered >= .5 * total)

    def mean(field):
        summed = np.bincount(ids[valid], area[valid] * field[valid], minlength=count)
        return np.divide(summed, covered, out=np.zeros(count), where=covered > 0).reshape(shape)

    return mean(east), mean(north), supported.reshape(shape)


def velocity_metrics(u, v, east, north, area):
    obs, sim = np.hypot(u, v), np.hypot(east, north)
    headings = obs >= MIN_DIRECTION_SPEED
    calm = sim < 1e-6
    angle = np.degrees(np.arctan2(np.abs(u*north - v*east), u*east + v*north))
    angle[calm] = 90.
    return {
        "cells": len(area), "direction_cells": int(headings.sum()),
        "calm_predictions_on_direction_cells": int((headings & calm).sum()),
        "direction_mae_deg": float(np.average(angle[headings], weights=area[headings])) if headings.any() else None,
        "observed_mean_speed_ms": float(np.average(obs, weights=area)),
        "simulated_mean_speed_ms": float(np.average(sim, weights=area)),
        "vector_rmse_ms": float(np.sqrt(np.average((east-u)**2 + (north-v)**2, weights=area))),
        "still_ocean_rmse_ms": float(np.sqrt(np.average(obs**2, weights=area))),
    }


def compare(export, cache, split):
    from check_marine_air import source
    from check_wind_belts import ocean_blocks, read_export

    path = source(cache, (QUERY, SHA256), server=SERVER, filename="drifter-annual-1deg.csv")
    with path.open(encoding="ascii") as csv:
        if (csv.readline().strip() != "longitude,latitude,U,V,N" or
            csv.readline().strip() != "degrees_east,degrees_north,meters per second,meters per second,drifter-days per square degree"):
            raise ValueError("Expected the NOAA annual current vectors and drifter sampling density in their original units")
        rows = np.loadtxt(csv, delimiter=",")
    if (rows.shape != (360*158, 5) or not np.array_equal(rows[:, 0], np.repeat(np.arange(360)-179.875, 158)) or
        not np.array_equal(rows[:, 1], np.tile(np.arange(158)-72.875, 360))):
        raise ValueError("Incomplete one-degree sample of the registered NOAA drifter grid")

    ocean, _, currents = read_export(export)
    h, w = ocean.shape
    u, v, supported = aggregate(rows, (h, w))
    y, x = np.indices((h, w))
    blocks = (y * 8 // h) * 16 + x * 16 // w
    lat = (y+.5)*180/h - 90
    area = np.cos(np.deg2rad(lat))
    # Keep whole rows inside the observation domain. Drop partly marine model cells.
    full_row = (lat - 90/h >= -73.375) & (lat + 90/h <= 84.625)
    domain = supported & full_row & (ocean == 1) & np.isin(blocks, ocean_blocks(split))
    if not domain.any():
        raise ValueError("No eligible drifter observations on the requested model grid")
    by_latitude = {}
    for name, lower, upper in (("all", 0, 90), ("0-30", 0, 30), ("30-60", 30, 60), ("60-90", 60, 90)):
        keep = domain & (abs(lat) >= lower) & (abs(lat) < upper)
        by_latitude[name] = velocity_metrics(u[keep], v[keep], currents[2][keep], currents[3][keep], area[keep]) if keep.any() else None
    return {"split": split, "source_url": SERVER + QUERY, "source_sha256": SHA256,
            "export_sha256": hashlib.sha256(export.read_bytes()).hexdigest(),
            "represented_blocks": int(np.unique(blocks[domain]).size),
            "minimum_reference_heading_speed_ms": MIN_DIRECTION_SPEED, "by_absolute_latitude_deg": by_latitude}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("export", type=Path, help="OWB2 Earth export from --wind-belts")
    parser.add_argument("--cache", type=Path, default=Path("ValidationOutput/EarthClimate/sources"))
    parser.add_argument("--split", choices=("calibration", "ocean-holdout"), default="calibration")
    parser.add_argument("--json", type=Path)
    args = parser.parse_args()
    report = json.dumps(compare(args.export, args.cache, args.split), indent=2, allow_nan=False)
    print(report)
    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(report + "\n", encoding="utf-8")
