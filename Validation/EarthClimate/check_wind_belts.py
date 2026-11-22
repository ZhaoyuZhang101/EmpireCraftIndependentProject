"""Compare monthly wind directions to NOAA NCEP/NCAR R1 on development or reserved ocean blocks."""
import argparse
import hashlib
import json
from pathlib import Path
import struct

import h5py
import numpy as np

from check_marine_air import LAND, SERVER, source
from prepare import holdout
from score import HEIGHT, WIDTH, observation, tile_area


WINDS = {
    "uwnd": ("ncep.reanalysis.derived/surface_gauss/uwnd.10m.mon.ltm.1991-2020.nc",
             "45b9eadc8181b29cb9ebcbcc21503867bc69ec1272ac925d9dcf7f3b1cc18875"),
    "vwnd": ("ncep.reanalysis.derived/surface_gauss/vwnd.10m.mon.ltm.1991-2020.nc",
             "78130fd7e5c6f5dcec0ce0009d93639afd9645d77ace60d14c13581ac763fd0b"),
}


def read_export(path):
    data = path.read_bytes()
    if len(data) < 12:
        raise ValueError("Truncated OWB2 header")
    magic, w, h = struct.unpack_from("<4sii", data)
    if magic != b"OWB2" or w != 2*h or h < 1 or len(data) != 12 + w*h*(1 + 4*(4*12+4)):
        raise ValueError("Expected a complete OWB2 wind and ocean export")
    count = w*h
    ocean = np.frombuffer(data, "u1", count, offset=12).reshape(h, w)
    winds = np.frombuffer(data, "<f4", 4*12*count, offset=12+count).reshape(4, h, w, 12)
    currents = np.frombuffer(data, "<f4", 4*count, offset=12+count*(1+4*12*4)).reshape(4,h,w)
    if np.any(ocean > 1) or not np.isfinite(winds).all() or not np.isfinite(currents).all():
        raise ValueError("Invalid simulated mask or circulation")
    return ocean, winds, currents


def calibration_blocks():
    ids, area, _, _ = observation("calibration")
    ids = ids[area >= .5 * tile_area(HEIGHT)[ids // WIDTH]]
    return np.unique((ids // WIDTH // 16) * (WIDTH // 16) + ids % WIDTH // 16)


def ocean_blocks(split):
    if split == "calibration":
        return calibration_blocks()
    if split != "ocean-holdout":
        raise ValueError("Expected calibration or ocean-holdout")
    # All original coordinate-only hash holdouts, including the ocean-only
    # blocks. Freeze this independent footprint before evaluating polar winds.
    blocks = np.array([16*y+x for y in range(8) for x in range(16) if holdout(WIDTH*16*y+16*x)], dtype="u1")
    if hashlib.sha256(blocks.tobytes()).hexdigest() != "d741344b3edd3c109e529d42782b0108ff174003051cdcd6b02590cffbafb3ec":
        raise ValueError("The registered ocean holdout geography changed")
    return blocks


def comparison(path, cache, split="calibration"):
    ocean, winds, _ = read_export(path)
    h, w = ocean.shape
    with h5py.File(source(cache, LAND)) as mask:
        lat, lon = mask["lat"][:].astype(float), mask["lon"][:].astype(float)
        marine = mask["land"][0] == 0
    observed = []
    for component, info in WINDS.items():
        with h5py.File(source(cache, info)) as weather:
            if not np.array_equal(lat, weather["lat"][:]) or not np.array_equal(lon, weather["lon"][:]):
                raise ValueError("Reanalysis and land mask grids differ")
            field = weather[component]
            if field.shape != (12, len(lat), len(lon)) or field.attrs["units"] != b"m/s":
                raise ValueError("Expected twelve monthly 10 m wind components in metres per second")
            values = np.moveaxis(field[:], 0, -1)
            if not np.isfinite(values).all() or np.max(np.abs(values)) > 150:
                raise ValueError("Invalid or missing wind on the reference grid")
            observed.append(values)
    u, v = observed
    nodes, weights = np.polynomial.legendre.leggauss(len(lat))
    if not np.allclose(np.sin(np.deg2rad(lat)), nodes[::-1], atol=1e-6):
        raise ValueError("Expected the NCEP Gaussian latitudes")
    area = weights[::-1, None, None]
    # Keep the reference Gaussian nodes and quadrature. All four surrounding
    # ORBIS cells must be entirely ocean, making interpolated coasts ineligible.
    xf = ((lon+180) % 360) * w / 360 - .5
    yf = np.clip((lat+90) * h / 180 - .5, 0, h-1)[:, None]
    x0, y0 = np.floor(xf).astype(int), np.floor(yf).astype(int)
    dx, dy = (xf-x0)[None, :, None], (yf-y0)[:, :, None]
    x1, y1 = (x0+1) % w, np.minimum(y0+1, h-1)
    x0 %= w
    ocean_support = ocean[y0, x0] & ocean[y0, x1] & ocean[y1, x0] & ocean[y1, x1]
    bx = (((lon + 180) % 360) / 22.5).astype(int)
    by = ((lat + 90) / 22.5).astype(int)
    blocks = by[:, None]*16 + bx
    selected = ocean_blocks(split)
    domain = marine & (ocean_support != 0) & np.isin(blocks, selected)

    def interpolate(field):
        return (1-dy)*((1-dx)*field[y0,x0] + dx*field[y0,x1]) + dy*((1-dx)*field[y1,x0] + dx*field[y1,x1])

    regimes = {"all": np.ones(len(lat), dtype=bool), "0-30": abs(lat) < 30,
               "30-60": (abs(lat) >= 30) & (abs(lat) < 60), "60-90": abs(lat) >= 60}
    report = {}
    for name, region in regimes.items():
        active = (domain & region[:,None])[...,None]
        # Exclude weak observed headings or ambiguous signs using the same
        # observation-only thresholds for both simulations. A zero simulated
        # vector counts as a 90-degree direction error and ties fail sign tests.
        headings = active & (np.hypot(u,v) >= 1)
        zonal = active & (np.abs(u) >= 1)
        meridional = active & (np.abs(v) >= 1)
        row = {"ocean_points": int(active.sum()), "direction_months": int(headings.sum()),
               "zonal_sign_months": int(zonal.sum()), "meridional_sign_months": int(meridional.sum())}

        def weighted(value, valid):
            weight = area * valid
            return float((value*weight).sum()/weight.sum()) if valid.any() else None

        for kind, (east, north) in zip(("previous_belts", "seasonal_belts"), (winds[:2], winds[2:])):
            e, n = interpolate(east), interpolate(north)
            angle = np.rad2deg(np.arctan2(abs(e*v - n*u), e*u + n*v))
            angle = np.where(np.hypot(e,n) < 1e-6, 90., angle)
            row[kind] = {"direction_mae_deg": weighted(angle, headings),
                         "east_sign_agreement": weighted(e*u > 0, zonal),
                         "north_sign_agreement": weighted(n*v > 0, meridional)}
        report[name] = row
    sources = {name: {"url": SERVER + info[0], "sha256": info[1]} for name, info in {**WINDS,"land":LAND}.items()}
    return {"sources": sources, "period": "1991–2020", "split": split, "eligible_blocks": len(selected),
            "export_sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            "represented_blocks": int(np.unique(blocks[domain]).size), "minimum_reference_speed_ms": 1,
            "minimum_reference_component_ms": 1, "by_absolute_latitude_deg": report}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("export", type=Path)
    parser.add_argument("--split", choices=["calibration","ocean-holdout"], default="calibration")
    parser.add_argument("--cache", type=Path, default=Path("ValidationOutput/EarthClimate/sources"))
    parser.add_argument("--json", type=Path)
    args = parser.parse_args()
    result = comparison(args.export, args.cache, args.split)
    summary = json.dumps(result, indent=2, ensure_ascii=False)
    print(summary)
    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(summary + "\n", encoding="utf-8")
