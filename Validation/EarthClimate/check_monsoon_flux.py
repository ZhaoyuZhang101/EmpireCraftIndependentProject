"""Compare ORBIS monthly net vapour transport to a reanalysis mean-flow estimate over the Indian Ocean."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
from urllib.request import urlopen

import numpy as np


SERVER = "https://psl.noaa.gov/thredds/dodsC/Datasets/ncep.reanalysis.derived/pressure/"
SLICES = {
    "shum": "ab1750d5ad162372622dbf5db9eb94a5a1c8ef8b8e2128181895e3621502181d",
    "uwnd": "d59d2b069a2cef8f1f49d2c3c48ffae6d551c62ee87b406df10c9305cf01b674",
    "vwnd": "08270410ec4349e565fda1f05c2f65971490bfefd0d56c760b1d976ddaf984ac",
}
PRESSURE = np.array([1000, 925, 850, 700, 600, 500, 400, 300])
LAT, LON = np.arange(10., 20.1, 2.5), np.arange(65., 92.6, 2.5)
# Normal direction, fixed coordinate, endpoints and whether all reference levels are offshore.
SECTIONS = {
    "Arabian Sea": ("east", 67.5, 11.25, 19.6875, True),
    "Bay of Bengal": ("north", 16.875, 85., 90., True),
    "Deccan interior": ("east", 78.75, 14.0625, 19.6875, False),
}


def source_url(name):
    return f"{SERVER}{name}.mon.ltm.1991-2020.nc.ascii?{name}[0:1:11][0:1:7][28:1:32][26:1:37]"


def read_reference(cache, name):
    path = cache / (name + ".ascii")
    if path.is_file():
        raw = path.read_bytes()
    else:
        with urlopen(source_url(name), timeout=60) as stream:
            raw = stream.read()
    if hashlib.sha256(raw).hexdigest() != SLICES[name]:
        raise ValueError(f"The NOAA {name} section differs from its pinned climate normals.")
    if not path.exists():
        cache.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)
    # The ASCII Grid lists one longitude row per month, level and latitude;
    # verify all indices and axes before reversing NOAA's descending latitude.
    text = raw.decode("ascii")
    values = np.full((12, len(PRESSURE), len(LAT), len(LON)), np.nan)
    for month, level, row, data in re.findall(r"^\[(\d+)\]\[(\d+)\]\[(\d+)\], (.+)$", text, re.M):
        values[int(month), int(level), int(row)] = np.fromstring(data, sep=",")
    for axis, expected in (("level", PRESSURE), ("lat", LAT[::-1]), ("lon", LON)):
        line = text.split(f"{name}.{axis}[{len(expected)}]\n")[1].splitlines()[0]
        if not np.array_equal(np.fromstring(line, sep=","), expected):
            raise ValueError(f"Unexpected NOAA {axis} coordinates")
    if not np.isfinite(values).all() or np.abs(values).max() > 500:
        raise ValueError("Invalid reanalysis field in the section.")
    return values[:, :, ::-1, :]


def integrated_mean_flow(humidity_g_kg, wind_m_s, levels_hpa):
    # Trapezoidal pressure integral of monthly mean q times monthly mean wind.
    product = .001 * humidity_g_kg * wind_m_s
    dp = -100. * np.diff(levels_hpa)
    return np.sum(.5 * (product[:, :-1] + product[:, 1:]) * dp[None, :, None, None], axis=1) / 9.80665


def section_mean(field, lat, lon, direction, fixed, lo, hi):
    normal, along = (lon, lat) if direction == "east" else (lat, lon)
    if fixed < normal[0] or fixed > normal[-1] or lo < along[0] or hi > along[-1] or hi <= lo:
        raise ValueError("Section extends outside the sampled field.")
    if direction == "north":
        field = field.swapaxes(1, 2)
    k = np.clip(np.searchsorted(normal, fixed), 1, len(normal)-1)
    fraction = (fixed-normal[k-1])/(normal[k]-normal[k-1])
    line = (1-fraction)*field[..., k-1] + fraction*field[..., k]
    knots = np.r_[lo, along[(along > lo) & (along < hi)], hi]
    samples = np.asarray([np.interp(knots, along, month) for month in line])
    return np.sum(.5*(samples[:, :-1]+samples[:, 1:])*np.diff(knots), axis=1)/(hi-lo)


def summarize(series):
    return {"monthly": series.tolist(), "jjas": float(series[5:9].mean()), "djf": float(series[[11, 0, 1]].mean())}


def compare(path, cache):
    raw = path.read_bytes()
    magic, w, h = struct.unpack_from("<4sii", raw)
    if magic != b"OMF1" or w != 2*h or h < 2 or len(raw) != 12 + 12*2*w*h*4:
        raise ValueError("Expected twelve pairs of spherical face-flux grids.")
    flux = np.frombuffer(raw, "<f4", offset=12).reshape(12, 2, h, w)
    if not np.isfinite(flux).all():
        raise ValueError("Nonfinite modelled vapour flux.")
    lat, lon = (np.arange(h)+.5)*180/h-90, (np.arange(w)+.5)*360/w-180
    humidity = read_reference(cache, "shum")
    reference = {d: integrated_mean_flow(humidity, read_reference(cache, name), PRESSURE)
                 for d, name in (("east", "uwnd"), ("north", "vwnd"))}
    sections = {}
    for name, (direction, fixed, lo, hi, offshore) in SECTIONS.items():
        # Net east flux lives on eastern faces; net north flux on northern faces.
        if direction == "east":
            model = section_mean(flux[:, 0], lat, lon+180/w, direction, fixed, lo, hi)
        else:
            model = section_mean(flux[:, 1], lat+90/h, lon, direction, fixed, lo, hi)
        row = {"positive": direction, "fixed_degrees": fixed, "endpoints_degrees": [lo, hi], "model": summarize(model)}
        if offshore:
            row["reanalysis_mean_flow"] = summarize(section_mean(reference[direction], LAT, LON, direction, fixed, lo, hi))
        sections[name] = row
    return {"units": "kg m^-1 s^-1", "model_sha256": hashlib.sha256(raw).hexdigest(),
            "reference": "NCEP/NCAR R1 1991-2020, 1000-300 hPa mean q times mean wind; transient covariance omitted",
            "sources": {k: {"url": source_url(k), "sha256": v} for k, v in SLICES.items()}, "sections": sections}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("flux", type=Path)
    parser.add_argument("--cache", type=Path, required=True)
    parser.add_argument("--json", type=Path)
    args = parser.parse_args()
    report = compare(args.flux, args.cache)
    print(json.dumps(report["sections"], indent=2))
    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(json.dumps(report, indent=2, allow_nan=False)+"\n", encoding="utf-8")
