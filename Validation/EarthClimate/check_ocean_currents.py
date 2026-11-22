"""Compare basin contrasts and open-ocean annual SST anomalies with NOAA OISST v2."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
import rasterio

from check_marine_air import SERVER, source
from check_wind_belts import ocean_blocks, read_export


SST = ("noaa.oisst.v2/sst.ltm.1991-2020.nc", "16f54e4717a949a8e8ddd143cadfed308a1cc0ea4a2be43c522d899543932b34")
MASK = ("noaa.oisst.v2/lsmask.nc", "7f5a9e7459a42c29ec5658e1458f71205034de1a01fc30f8225d5905a8fe2078")
# Predefined three-by-three OISST windows around the cell containing each landmark.
BASINS = (
    ("North Atlantic", 32, ("Gulf Stream", -77), ("Canary", -15)),
    ("North Pacific", 32, ("Kuroshio", 133), ("California", -120)),
    ("South Atlantic", -28, ("Brazil", -47), ("Benguela", 14)),
)


def observed_sst(cache):
    path = source(cache, SST)
    with rasterio.open(f'NETCDF:"{path}":sst') as sst, rasterio.open(source(cache, MASK)) as mask:
        expected = rasterio.transform.from_origin(0, 90, 1, 1)
        if (sst.shape != (180, 360) or mask.shape != sst.shape or sst.count != 12 or mask.count != 1
                or sst.transform != expected or mask.transform != expected):
            raise ValueError("Expected twelve monthly OISST bands and the matching one-degree marine mask")
        if any(sst.tags(b).get("units") != "degC" for b in sst.indexes):
            raise ValueError("Expected SST in degrees Celsius")
        if (any(scale != 1 for scale in sst.scales) or any(offset != 0 for offset in sst.offsets)
                or mask.tags(1).get("NETCDF_VARNAME") != "mask"):
            raise ValueError("Unexpected packed SST or land-sea mask")
        monthly = sst.read(masked=True).astype(float).filled(np.nan)
        wet = mask.read(1)
        if not np.isin(wet, (0, 1)).all() or np.any((monthly < -5) | (monthly > 40)):
            raise ValueError("Out-of-range OISST temperatures or mask values")
        # The paired means use only marine cells with all twelve months present.
        return np.where(wet == 1, monthly.mean(axis=0), np.nan)


def model_at_oisst(ocean, sea_anomaly):
    # Reference ocean coordinates run north to south, from the Greenwich meridian.
    # Use the same cell-centred interpolation and longitude wrap as the marine air field.
    h, w = ocean.shape
    x = ((np.arange(360) + .5 + 180) % 360) * w / 360
    y = (179.5 - np.arange(180))[:, None] * h / 180
    containing_ocean = ocean[y.astype(int), x.astype(int)].astype(bool)
    x -= .5
    y = np.clip(y - .5, 0, h - 1)
    x0, y0 = np.floor(x).astype(int), np.floor(y).astype(int)
    dx, dy = x - x0, y - y0
    x1, y1 = (x0 + 1) % w, np.minimum(h - 1, y0 + 1)
    x0 %= w
    sim = ((1-dy)*((1-dx)*sea_anomaly[y0, x0] + dx*sea_anomaly[y0, x1])
           + dy*((1-dx)*sea_anomaly[y1, x0] + dx*sea_anomaly[y1, x1]))
    open_ocean = (ocean[y0, x0] & ocean[y0, x1] & ocean[y1, x0] & ocean[y1, x1]).astype(bool)
    return sim, containing_ocean, open_ocean


def paired_contrasts(simulated, reference, containing_ocean):
    def samples(label, lon, rows):
        cols = (int(lon % 360) + np.arange(-1, 2)) % 360
        window = np.ix_(rows, cols)
        obs, sim = reference[window], simulated[window]
        valid = containing_ocean[window] & np.isfinite(obs)
        counts = valid.sum(axis=1)
        row_means = [np.where(valid, v, 0).sum(axis=1) / np.maximum(1, counts) for v in (obs, sim)]
        return label, counts, row_means

    result = {}
    for basin, lat, west, east in BASINS:
        rows = 90-lat + np.arange(-1, 2)
        west_name, west_count, west_fields = samples(*west, rows)
        east_name, east_count, east_fields = samples(*east, rows)
        common = (west_count > 0) & (east_count > 0)
        if not common.any():
            raise ValueError(f"No paired ocean support in {basin} at the exported resolution")
        # Identical latitude weights cancel the motionless zonal SST background
        # exactly even when one coast has fewer valid cells than its counterpart.
        weights = np.cos(np.deg2rad(89.5 - rows[common]))
        obs_delta, sim_delta = (float(np.average((a-b)[common], weights=weights))
                                for a, b in zip(west_fields, east_fields))
        result[basin] = {"west": west_name, "east": east_name, "latitude_deg": lat,
                         "west_longitude_deg": west[1], "east_longitude_deg": east[1],
                         "matched_latitude_rows": int(common.sum()),
                         "west_points": int(west_count[common].sum()), "east_points": int(east_count[common].sum()),
                         "observed_difference_c": obs_delta, "simulated_difference_c": sim_delta,
                         "still_ocean_difference_c": 0., "bias_c": sim_delta-obs_delta}
    return result


def open_ocean_score(simulated, reference, open_ocean, split):
    lat = 89.5 - np.arange(180)
    lon = (np.arange(360) + .5 + 180) % 360
    blocks = 16 * ((lat + 90) / 22.5).astype(int)[:, None] + (lon / 22.5).astype(int)
    domain = open_ocean & np.isfinite(reference) & np.isin(blocks, ocean_blocks(split))
    counts = domain.sum(axis=1, keepdims=True)
    domain &= counts >= 2
    if not domain.any():
        raise ValueError("No open-ocean latitude has two eligible longitudes")
    area = np.broadcast_to(np.cos(np.deg2rad(lat))[:, None], domain.shape)[domain]

    def zonal_difference(field):
        zonal = np.where(domain, field, 0).sum(axis=1, keepdims=True) / np.maximum(1, counts)
        return (field - zonal)[domain]

    # Remove the zonal mean separately from each field at the same longitudes
    # inside this split. A model driven solely by latitude has zero skill here.
    obs, sim = zonal_difference(reference), zonal_difference(simulated)
    diff = sim - obs
    mean = lambda a: float(np.average(a, weights=area))
    still_rmse = np.sqrt(mean(obs**2))
    denominator = still_rmse * np.sqrt(mean(sim**2))
    return {"split": split, "points": int(domain.sum()), "blocks": int(np.unique(blocks[domain]).size),
            "current_mae_c": mean(abs(diff)), "current_rmse_c": float(np.sqrt(mean(diff**2))),
            "still_mae_c": mean(abs(obs)), "still_rmse_c": float(still_rmse),
            "correlation": mean(sim * obs) / denominator if denominator > 1e-12 else None}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("export", type=Path, help="OWB2 Earth diagnostic from FlatWorld tests --wind-belts")
    parser.add_argument("--cache", type=Path, default=Path("ValidationOutput/EarthClimate/sources"))
    parser.add_argument("--split", choices=("calibration", "ocean-holdout"), default="calibration",
                        help="geographic split for the open-ocean spatial score")
    parser.add_argument("--json", type=Path)
    args = parser.parse_args()
    ocean, _, currents = read_export(args.export)
    simulated, containing_ocean, open_ocean = model_at_oisst(ocean, currents[0])
    reference = observed_sst(args.cache)
    results = {"period": "1991–2020", "export_sha256": hashlib.sha256(args.export.read_bytes()).hexdigest(),
               "sources": {k: {"url": SERVER + item[0], "sha256": item[1]} for k, item in (("sst", SST), ("mask", MASK))},
               "west_minus_east_annual_sst": paired_contrasts(simulated, reference, containing_ocean),
               "open_ocean_zonal_anomaly": open_ocean_score(simulated, reference, open_ocean, args.split)}
    summary = json.dumps(results, ensure_ascii=False, indent=2, allow_nan=False)
    print(summary)
    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(summary + "\n", encoding="utf-8")
