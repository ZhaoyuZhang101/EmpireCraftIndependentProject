"""Compare near-surface air normals outside the CRU land footprint with NCEP/NCAR R1."""
import argparse
import hashlib
from pathlib import Path
from urllib.request import urlopen

import h5py
import numpy as np

from score import read_model


SERVER = "https://downloads.psl.noaa.gov/Datasets/"
AIR = ("ncep.reanalysis.derived/surface_gauss/air.2m.mon.ltm.1981-2010.nc",
       "d6bab996c276bd1364f1448df50b9e5088a7236facba8cc907b3fcffec748160")
LAND = ("ncep.reanalysis/surface_gauss/land.sfc.gauss.nc",
        "7da97a6d508a499302002891bb11e7c284d99019698cb89de1b5f5da37df3ca3")


def source(cache, info):
    remote, digest = info
    path = cache / Path(remote).name
    if not path.exists():
        cache.mkdir(parents=True, exist_ok=True)
        with urlopen(SERVER + remote, timeout=40) as response:
            raw = response.read()
        if hashlib.sha256(raw).hexdigest() != digest:
            raise ValueError("Download differs from the pinned NOAA file")
        path.write_bytes(raw)
    if hashlib.sha256(path.read_bytes()).hexdigest() != digest:
        raise ValueError(f"Source integrity error: {path}")
    return path


def compare(export, cache):
    with h5py.File(source(cache, AIR)) as air, h5py.File(source(cache, LAND)) as mask:
        lat, lon = air["lat"][:], air["lon"][:]
        assert np.array_equal(lat, mask["lat"][:]) and np.array_equal(lon, mask["lon"][:])
        assert air["air"].attrs["units"] == b"degK" and air["air"].shape == (12, len(lat), len(lon))
        reference = np.moveaxis(air["air"][:].astype(float) - 273.15, 0, -1)
        land = mask["land"][0].astype(bool)
    nodes, weights = np.polynomial.legendre.leggauss(len(lat))
    if not np.allclose(np.sin(np.deg2rad(lat)), nodes[::-1], atol=1e-6):
        raise ValueError("Unexpected Gaussian grid")
    weights = np.broadcast_to(weights[::-1, None], land.shape)
    _, _, _, t, _ = read_model(export)
    h, w, _ = t.shape
    xf = np.mod(lon.astype(float) + 180., 360.) / 360 * w - .5
    yf = np.clip((lat.astype(float) + 90.) / 180 * h - .5, 0, h - 1)[:, None]
    x, y = np.floor(xf).astype(int), np.floor(yf).astype(int)
    dx, dy = (xf - x)[:, None], (yf - y)[..., None]
    samples = ((1 - dy) * ((1-dx)*t[y, x % w] + dx*t[y, (x+1) % w]) +
               dy * ((1-dx)*t[np.minimum(y+1, h-1), x % w] + dx*t[np.minimum(y+1, h-1), (x+1) % w]))
    for name, domain in (("Marine (including seasonal ice)", ~land),
                         ("Antarctic land south of 60S", land & (lat[:, None] < -60))):
        error = samples[domain] - reference[domain]
        area = weights[domain]
        mae = np.average(abs(error).mean(1), weights=area)
        rmse = np.sqrt(np.average((error**2).mean(1), weights=area))
        print(f"{name}: {domain.sum()} points, monthly air MAE {mae:.2f} C, RMSE {rmse:.2f} C")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("export", type=Path)
    parser.add_argument("--cache", type=Path, default=Path("ValidationOutput/EarthClimate/sources"))
    args = parser.parse_args()
    compare(args.export, args.cache)
