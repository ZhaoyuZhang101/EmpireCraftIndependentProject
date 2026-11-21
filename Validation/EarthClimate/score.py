"""Score an ORBIS Earth export against independently gridded CRU-CL monthly normals."""

import argparse
import gzip
import hashlib
import json
import os
import struct
from pathlib import Path

import numpy as np

from prepare import NX, NY, area_overlaps


HERE = Path(__file__).resolve().parent
WIDTH, HEIGHT = 256, 128
RADIUS_KM = 6371.0088
REGIONS = {
    # West, east, south, north in degrees. Membership uses cell centres.
    "Amazon lowlands": (-73, -52, -10, 2),
    "Sahara interior": (-10, 28, 20, 30),
    "Congo basin": (16, 29, -5, 4),
    "Indian monsoon": (73, 86, 16, 28),
    "Sahel": (-13, 28, 12, 17),
    "Interior Australia": (122, 140, -31, -21),
    "Western Europe": (-5, 12, 46, 54),
    "Eastern Siberia": (105, 145, 55, 67),
}
# Pinned fcb360fd, default seed, independent CRU holdout. Values are monthly
# MAE and RMSE in that order. export_baseline.py reproduces both controls.
HISTORICAL_HOLDOUT = {
    256: {"temperature_c": (4.86895719, 6.65266413), "precipitation_mm": (48.04056912, 80.53693848)},
    512: {"temperature_c": (4.93411428, 6.72384013), "precipitation_mm": (48.22162780, 80.94000706)},
}


def tile_area(height):
    edges = np.linspace(-np.pi / 2, np.pi / 2, height + 1)
    return RADIUS_KM**2 * np.pi / height * np.diff(np.sin(edges))


def read_model(path):
    with Path(path).open("rb") as stream:
        return _read_fields(stream)


def _read_fields(stream):
    magic, w, h = struct.unpack("<4sii", stream.read(12))
    if magic != b"ECL1" or w != 2 * h or h < 1 or os.fstat(stream.fileno()).st_size != 12 + 101 * w * h:
        raise ValueError(f"Invalid Earth export: {stream.name}")
    n = w * h
    def field(dtype, count):
        return np.fromfile(stream, dtype=dtype, count=count)

    elevation = field("<i2", n)
    distance = field("<u2", n)
    water = field("u1", n)
    temperature = field("<f4", 12 * n).reshape(h, w, 12)
    precipitation = field("<f4", 12 * n).reshape(h, w, 12)
    if not np.isfinite(temperature).all() or not np.isfinite(precipitation).all() or (precipitation < 0).any():
        raise ValueError(f"Invalid monthly climate values: {stream.name}")
    return elevation.reshape(h, w), distance.reshape(h, w), water.reshape(h, w), temperature, precipitation


def collocate(path):
    return _collocate(read_model(path))


def _collocate(fields):
    elevation, _, water, t, p = fields
    h, w = elevation.shape
    land = (water == 0) | (water == 5)  # Land and river cells have exposed terrain.
    if h == HEIGHT:
        return land.ravel(), t.reshape(-1, 12), p.reshape(-1, 12)
    if h < HEIGHT and HEIGHT % h == 0:
        ratio = HEIGHT // h
        def enlarge(a):
            return a.repeat(ratio, axis=0).repeat(ratio, axis=1)
        return enlarge(land).ravel(), enlarge(t).reshape(-1, 12), enlarge(p).reshape(-1, 12)
    if h % HEIGHT != 0:
        raise ValueError("Expected nested global Earth grid dimensions.")
    ratio = h // HEIGHT
    def pool(a):
        return a.reshape(HEIGHT, ratio, WIDTH, ratio, *a.shape[2:]).sum(axis=(1, 3))
    modeled_area = pool(land * tile_area(h)[:, None])
    valid = modeled_area >= tile_area(HEIGHT)[:, None] * 0.5
    source_mask = np.unpackbits(np.frombuffer(gzip.decompress((HERE / "land-mask.bin.gz").read_bytes()), dtype=np.uint8))
    if source_mask.size != NX * NY:
        raise ValueError("Invalid CRU land mask size")
    coverage = np.zeros(w * h)
    for _, tiles, area in area_overlaps(np.flatnonzero(source_mask), w, h):
        coverage += np.bincount(tiles, weights=area, minlength=coverage.size)
    coverage = coverage.reshape(h, w)
    source_area = pool(coverage)
    def average(field):
        summed = np.stack([pool(field[:, :, m] * coverage) for m in range(12)], axis=2)
        return (summed / np.where(source_area > 0, source_area, 1)[:, :, None]).reshape(-1, 12)
    return valid.ravel(), average(t), average(p)


def observation(split):
    parts = ("calibration", "holdout") if split == "all" else (split,)
    tables = [np.loadtxt(HERE / (part + ".csv.gz"), delimiter=",", skiprows=1, ndmin=2) for part in parts]
    rows = np.concatenate(tables)
    return rows[:, 0].astype(int), rows[:, 1], rows[:, 3:15], rows[:, 15:27]


def metrics(model, reference, area):
    model = model.reshape(len(area), -1)
    reference = reference.reshape(len(area), -1)
    w = area[:, None] / (area.sum() * model.shape[1])
    delta = model - reference
    model_mean, reference_mean = np.sum(w * model), np.sum(w * reference)
    xm, xr = model - model_mean, reference - reference_mean
    scale = np.sqrt(np.sum(w * xm**2) * np.sum(w * xr**2))
    return {
        "observed": float(reference_mean), "modeled": float(model_mean),
        "bias": float(np.sum(w * delta)), "mae": float(np.sum(w * np.abs(delta))),
        "rmse": float(np.sqrt(np.sum(w * delta**2))),
        "correlation": float(np.sum(w * xm * xr) / scale) if scale > 0 else None,
    }


def region_summary(t, p, area):
    mt = np.average(t, axis=0, weights=area)
    mp = np.average(p, axis=0, weights=area)
    return {
        "temperature_c": mt.tolist(), "precipitation_mm": mp.tolist(),
        "annual_rain_mm": float(mp.sum()), "mean_temperature_c": float(mt.mean()),
        "jja_minus_djf_c": float(mt[5:8].mean() - mt[[11, 0, 1]].mean()),
        "jjas_rain_fraction": float(mp[5:9].sum() / mp.sum()) if mp.sum() else None,
    }


def score(model_file, split):
    model_file = Path(model_file)
    with model_file.open("rb") as stream:
        fields = _read_fields(stream)
        height, width = fields[0].shape
        stream.seek(0)
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    valid, t, p = _collocate(fields)
    del fields
    ids, area, ot, op = observation(split)
    total_area = float(area.sum())
    # Freeze the primary evaluation footprint using only the observations.
    # A model labeling dry land as lake or ocean still owes a prediction there.
    keep = area >= tile_area(HEIGHT)[ids // WIDTH] * 0.5
    ids, area, ot, op = ids[keep], area[keep], ot[keep], op[keep]
    t, p = t[ids].astype(float), p[ids].astype(float)
    if not len(area):
        raise ValueError("No matching land cells")
    stats = {}
    for name, modeled, observed in (("temperature_c", t, ot), ("precipitation_mm", p, op)):
        stats[name] = metrics(modeled, observed, area)
        # Removing each tile's annual mean isolates the local seasonal cycle.
        stats[name]["seasonal"] = metrics(modeled - modeled.mean(axis=1, keepdims=True),
                                          observed - observed.mean(axis=1, keepdims=True), area)
        stats[name]["by_month"] = [metrics(modeled[:, m], observed[:, m], area) for m in range(12)]
    annual, observed_annual = p.sum(axis=1), op.sum(axis=1)
    stats["annual_rain_mm"] = metrics(annual, observed_annual, area)
    stats["annual_temperature_c"] = metrics(t.mean(axis=1), ot.mean(axis=1), area)
    common = valid[ids]
    common_land = {"cells": int(common.sum()), "area_km2": float(area[common].sum())}
    if common.any():
        common_land["temperature_c"] = metrics(t[common], ot[common], area[common])
        common_land["precipitation_mm"] = metrics(p[common], op[common], area[common])
    wetdry = {}
    for label, mask_ref, mask_model in (("dry_under_250_mm", observed_annual < 250, annual < 250),
                                         ("wet_over_1500_mm", observed_annual >= 1500, annual >= 1500)):
        group_area = area[mask_ref]
        wetdry[label] = {
            "observed_area_fraction": float(group_area.sum() / area.sum()),
            "modeled_area_fraction": float(area[mask_model].sum() / area.sum()),
            "observed_group_recall": float(area[mask_ref & mask_model].sum() / group_area.sum()),
            "annual_rain_mm": metrics(annual[mask_ref], observed_annual[mask_ref], group_area),
        } if group_area.size else None
    lon = ((ids % WIDTH + 0.5) / WIDTH - 0.5) * 360
    lat = ((ids // WIDTH + 0.5) / HEIGHT - 0.5) * 180
    block_ids = (ids // WIDTH // 16) * (WIDTH // 16) + ids % WIDTH // 16
    blocks = {}
    for block in np.unique(block_ids):
        inside = block_ids == block
        blocks[str(block)] = {
            "cells": int(inside.sum()), "area_km2": float(area[inside].sum()),
            "temperature_c": metrics(t[inside], ot[inside], area[inside]),
            "precipitation_mm": metrics(p[inside], op[inside], area[inside]),
        }
    regions = {}
    for name, (west, east, south, north) in REGIONS.items():
        inside = (lon >= west) & (lon < east) & (lat >= south) & (lat < north)
        if inside.any():
            regions[name] = {"cells": int(inside.sum()), "area_km2": float(area[inside].sum()),
                             "observed": region_summary(ot[inside], op[inside], area[inside]),
                             "modeled": region_summary(t[inside], p[inside], area[inside])}
    return {"source": "CRU-CL v2.0 (1961–1990)", "split": split, "cells": len(ids),
            "export": {"file": model_file.name, "width": width, "height": height, "sha256": digest},
            "footprint_sha256": hashlib.sha256(np.sort(ids).astype("<u2").tobytes()).hexdigest(),
            "eligible_source_area_km2": float(area.sum()), "total_source_area_km2": total_area,
            "metrics": stats, "wet_dry": wetdry, "regions": regions, "blocks": blocks, "common_land": common_land}


def print_report(report):
    def display(value, spec):
        return format(value, spec) if value is not None else "n/a"

    fraction = report["eligible_source_area_km2"] / report["total_source_area_km2"]
    print(f"{report['split']} | {report['cells']} reference land cells | {fraction:.1%} of source land area")
    for key in ("temperature_c", "precipitation_mm", "annual_rain_mm"):
        s = report["metrics"][key]
        print(f"{key:18} MAE {s['mae']:.2f} | RMSE {s['rmse']:.2f} | bias {s['bias']:+.2f}"
              f" | r {display(s['correlation'], '.3f')}")
    for key in ("temperature_c", "precipitation_mm"):
        s = report["metrics"][key]["seasonal"]
        print(f"{key:18} within-cell seasonal RMSE {s['rmse']:.2f} | r {display(s['correlation'], '.3f')}")
    for key, s in report["wet_dry"].items():
        if s is not None:
            print(f"{key:18} area observed {s['observed_area_fraction']:.1%}, modeled "
                  f"{s['modeled_area_fraction']:.1%}, recall {s['observed_group_recall']:.1%}")
    print("Regional means (observed / modeled)")
    for key, region in report["regions"].items():
        o, m = region["observed"], region["modeled"]
        print(f"{key:19} annual {o['annual_rain_mm']:5.0f}/{m['annual_rain_mm']:5.0f} mm | "
              f"Jun-Sep rain {display(o['jjas_rain_fraction'], '.0%')}/{display(m['jjas_rain_fraction'], '.0%')} | "
              f"JJA-DJF {o['jja_minus_djf_c']:+5.1f}/{m['jja_minus_djf_c']:+5.1f} degC")


def check_improvement(report):
    width = report["export"]["width"]
    if report["split"] != "holdout" or width not in HISTORICAL_HOLDOUT:
        raise ValueError("The historical regression covers --split holdout at widths 256 and 512.")
    holdout_ids = "270c599eac1be55f630b38cf48e19f5268f8a24141e22fbc2b109bee36e5cbfa"
    if report["cells"] != 1806 or len(report["blocks"]) != 17 or report["footprint_sha256"] != holdout_ids:
        raise ValueError("The historical holdout footprint changed.")
    failures = []
    for variable, control in HISTORICAL_HOLDOUT[width].items():
        for statistic, baseline in zip(("mae", "rmse"), control):
            value = report["metrics"][variable][statistic]
            # Avoid counting sub-roundoff differences across libm implementations as skill.
            if not np.isfinite(value) or baseline - value <= 1e-5:
                failures.append(f"{variable} {statistic} {value:.6f} (historical {baseline:.6f})")
    if failures:
        raise ValueError("Historical holdout regression: " + ", ".join(failures))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("export", type=Path)
    parser.add_argument("--split", choices=("calibration", "holdout", "all"), default="calibration")
    parser.add_argument("--json", type=Path, help="Write full monthly, seasonal and regional results.")
    parser.add_argument("--check-improvement", action="store_true",
                        help="Require finite monthly MAE and RMSE below the pinned 256/512 holdout baselines.")
    args = parser.parse_args()
    report = score(args.export, args.split)
    print_report(report)
    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(json.dumps(report, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    if args.check_improvement:
        try:
            check_improvement(report)
        except ValueError as error:
            parser.exit(1, str(error) + "\n")
        print("PASS monthly temperature and precipitation holdout MAE and RMSE, pinned historical control.")
