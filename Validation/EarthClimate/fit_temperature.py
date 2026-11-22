"""Fit global heat-transfer coefficients against CRU calibration land blocks alone."""
import argparse
import hashlib
from pathlib import Path

import numpy as np

from score import HEIGHT, WIDTH, observation, read_model, tile_area


PHASE = (np.arange(12) - 6) * np.pi / 6
COS, SIN = np.cos(PHASE), np.sin(PHASE)
LABELS = ("equatorial convection", "subtropical sensible heat", "coastal upwelling",
          "westerly relief", "winter marine heat", "winter poleward barrier",
          "nearby heat storage", "tropical seasonal lag", "solar harmonics",
          "delayed solar harmonics", "continental winter shape", "continental winter lag",
          "seasonal convergence", "winter continental outflow", "premonsoon sensible heat",
          "summer free-air lapse")


def periodic(source, step, length, centered=True):
    """One-sided exponential averages on a closed row, sampled at its cell centres."""
    memory = np.exp(-step / length)
    half = np.sqrt(memory) if centered else 1
    fields = []
    for direction in (1, -1):
        ordered = source[:, ::direction]
        value = np.zeros(source.shape[0])
        for column in ordered.T:
            value = memory * value + (1 - memory) * column
        value /= 1 - memory**source.shape[1]
        field = np.empty_like(source, dtype=float)
        for x, column in enumerate(ordered.T):
            field[:, x] = half * value + (1 - half) * column
            value = memory * value + (1 - memory) * column
        fields.append(field[:, ::direction])
    return fields


def regional(source, lat, length):
    # A meridian and its antipode form one closed great circle through both poles.
    h, w = source.shape
    ring = np.concatenate((source, np.roll(source[::-1], w // 2, axis=1))).T
    south, north = periodic(ring, 20015.1145 / h, length * .6)
    meridional = ((south + north) * .5).T[:h]
    return periodic(meridional, 40030.229 * np.cos(lat[:, 0]) / w, length)


def sector(field, lat, bearing, distance):
    """Bilinear sample at a great-circle distance; the same row offset serves all meridians."""
    h, w = field.shape
    sl, cl = np.sin(lat), np.cos(lat)
    angle = distance / 6371.0088
    azimuth = np.deg2rad(bearing)
    sl2 = sl * np.cos(angle) + cl * np.sin(angle) * np.cos(azimuth)
    y = np.clip((np.arcsin(np.clip(sl2, -1, 1)) / np.pi + .5) * h - .5, 0, h - 1)
    x = np.arange(w) + np.arctan2(np.sin(azimuth) * np.sin(angle) * cl,
                                    np.cos(angle) - sl * sl2) * w / (2 * np.pi)
    ix, iy = np.floor(x).astype(int), np.floor(y).astype(int)
    dy, dx = y - iy, x - ix
    iy1 = np.minimum(h - 1, iy + 1)
    return ((1 - dy) * ((1 - dx) * field[iy, ix % w] + dx * field[iy, (ix + 1) % w])
            + dy * ((1 - dx) * field[iy1, ix % w] + dx * field[iy1, (ix + 1) % w]))


def air_source(field, lat, bearing):
    # Mirror the poleward component smoothly across the equator. This keeps the
    # source direction continuous even on a perfectly symmetric artificial world.
    hemisphere = np.tanh(6 * np.sin(lat))
    angle = np.rad2deg(np.arctan2(np.sin(np.deg2rad(bearing)),
                                  np.cos(np.deg2rad(bearing)) * hemisphere))
    return sum(weight * sector(field, lat, angle, distance)
               for distance, weight in ((700, .25), (1700, .4), (3100, .35)))


def geometry(fields):
    bed, distance, water = fields[:3]
    h, w = bed.shape
    lat = np.pi * ((np.arange(h) + .5) / h - .5)[:, None]
    d, sine = np.abs(np.rad2deg(lat)), np.sin(lat)
    s = abs(sine)
    ocean = ((water >= 1) & (water <= 3)).astype(float)
    elevation = np.where(ocean, 0, bed) / 1000.
    relief = np.where((water == 0) | (water == 5), np.maximum(0, elevation), 0)
    west, east = periodic(ocean, 40030.229 * np.cos(lat[:, 0]) / w, 1800, centered=False)
    westerly = (.5 + .5 * np.tanh((d - 32) / 8)) * (.5 - .5 * np.tanh((d - 68) / 8))
    marine = west * westerly + east * (1 - westerly)
    contrast = west - east
    inland = 1 - marine
    near = np.minimum(1, distance / (w * .05))
    subtropical = np.exp(-((d - 24) / 13)**2)
    ones = np.ones_like(marine)
    mean = (ones, s*s, s**4, elevation, s*s*(marine-.5), subtropical*(inland-.5), contrast*s)
    annual = tuple(sine * term for term in (ones, near, inland, s*inland, contrast))

    near_w, near_e = regional(ocean, lat, 500)
    sea_near = .5 * (near_w + near_e)
    far_w, far_e = regional(ocean, lat, 1750)
    regional_land = 1 - .5 * (far_w + far_e)
    terrain_w, terrain_e = regional(relief, lat, 650)
    height = .5 * (terrain_w + terrain_e)
    upstream = air_source(height, lat, -90)
    # Blend poleward hemispheric sources across the equator, where insolation has no preferred winter side.
    pole_weight = .5 + .5 * np.tanh(6 * sine)
    poleward = sum(weight * (pole_weight * sector(height, lat, 0, km)
                             + (1-pole_weight) * sector(height, lat, 180, km))
                   for km, weight in ((700, .25), (1700, .4), (3100, .35)))
    mild_air = air_source(sea_near, lat, -125) * np.exp(-np.maximum(0, air_source(height, lat, -125) - height)/1.2)
    hemisphere = np.tanh(6 * sine)
    winter = .5 * (1 - hemisphere[..., None] * COS)
    cold = 27 - 46 * s**2.8 - 5 * height - s * (7 + 17 * regional_land)
    snow = np.clip((3 - cold) / 28, 0, 1.5)
    # Smooth suppression of continental surface heating on a perennial ice sheet.
    warm = 27 - 46 * s**2.8 - 5.5 * height + s * (7 + 17 * regional_land)
    melt = np.clip((warm + 5) / 10, 0, 1)
    coupling = np.sqrt(np.clip(2 * (1 - sea_near), 0, 1)) * (.25 + .75 * melt*melt*(3-2*melt))

    declination = np.arcsin(np.sin(np.deg2rad(23.44)) * np.cos(PHASE + .42))
    hour = np.arccos(np.clip(-np.tan(lat[..., None]) * np.tan(declination), -1, 1))
    flux = 1361/np.pi * (1-.0334*np.cos(PHASE+.20)) * (
        hour * np.sin(lat[..., None])*np.sin(declination) + np.cos(lat[..., None])*np.cos(declination)*np.sin(hour))
    higher = flux - flux.mean(-1, keepdims=True) - 2 * (
        (flux * COS).mean(-1, keepdims=True)*COS + (flux * SIN).mean(-1, keepdims=True)*SIN)
    excursion = 5 + 23*regional_land + 4*np.minimum(2, poleward)*regional_land
    convective = np.exp(-((np.rad2deg(lat)[..., None] - excursion[..., None]*COS)/13)**2)
    constant = np.ones(12)
    # A continental winter high supplies cold air towards a warmer downwind
    # sea. A high intervening divide shelters the lowlands from that flow.
    cold_source = (1-air_source(sea_near, lat, -30)) * (east + np.maximum(0, upstream-height))
    cold_source *= np.exp(-np.maximum(0, air_source(height, lat, -30)-height)/1.2)
    cold_source *= np.sin(abs(lat))**.5
    # Equatorward marine access and a warmer poleward continental source
    # favor sensible heating before the wet monsoon reaches tropical land.
    south_minus_north = sum(weight * (sector(sea_near, lat, 180, km)-sector(sea_near, lat, 0, km))
                            for km, weight in ((700, .25), (1700, .4), (3100, .35)))
    moist_coast = np.maximum(0, hemisphere*south_minus_north)*np.exp(-((d-17)/15)**2)*(1 + np.maximum(0, poleward-height))
    summer = .5 * (1 + hemisphere[...,None] * COS)
    pairs = (
        (np.exp(-(d/11)**2), constant), (subtropical*regional_land, constant),
        (subtropical*(far_w-far_e), constant), (s*np.clip(upstream-height, -3, 3), constant),
        (snow*mild_air, winter), (s*np.clip(poleward-height, -3, 3), winter),
        (sine*(marine-sea_near), COS), (np.exp(-((d-16)/14)**2)*hemisphere, SIN),
        (ones, higher/55), (regional_land, np.roll(higher, 1, axis=-1)/55),
        (s*s*regional_land, np.cos(2*PHASE)), (s*s*regional_land, np.sin(2*PHASE)),
        (ones, convective), (cold_source, winter), (moist_coast*hemisphere, SIN),
        (relief, summer))
    return (np.stack(np.broadcast_arrays(*mean), axis=-1).reshape(h*w, -1),
            np.stack(np.broadcast_arrays(*annual), axis=-1).reshape(h*w, -1),
            [(g * coupling, months) for g, months in pairs])


def terms_for(pairs, ids):
    h, w = pairs[0][0].shape
    y, x = np.divmod(ids, w)
    return np.stack([g[y, x, None] * np.broadcast_to(months, (h, w, 12))[y, x]
                     for g, months in pairs], axis=-1)


def solve(matrix, target, weight, ridge):
    w = weight / weight.sum()
    return np.linalg.solve((matrix.T*w) @ matrix + ridge*np.eye(matrix.shape[1]), (matrix.T*w) @ target)


def thermal_classes(t):
    cold, hot = t.min(1), t.max(1)
    group = np.where(cold >= 18, 0, np.where(hot <= 10, np.where(hot > 0, 1, 2), np.where(cold > 0, 3, 4)))
    heat = np.where(hot >= 22, 0, np.where((t > 10).sum(1) >= 4, 1, np.where(cold < -38, 3, 2)))
    return group, np.where(group < 3, group, 4*group+heat)


def report(label, t, obs, area):
    error = t - obs
    mean = lambda value: np.average(value, axis=0, weights=area)
    group, heat = thermal_classes(t)
    obs_group, obs_heat = thermal_classes(obs)
    print(label)
    print("  Monthly MAE / RMSE", round(mean(abs(error).mean(1)), 4), round(np.sqrt(mean((error**2).mean(1))), 4))
    print("  Local seasonal-anomaly MAE", round(mean(abs(error-error.mean(1)[:, None]).mean(1)), 4))
    print("  Coldest / warmest / annual-range MAE",
          *(round(mean(abs(a(t) - a(obs))), 4) for a in (lambda x: x.min(1), lambda x: x.max(1), lambda x: np.ptp(x, axis=1))))
    print("  Temperature group / summer subtype agreement (dryness excluded)",
          *(f"{mean(x):.2%}" for x in (group == obs_group, heat == obs_heat)))
    print("  Monthly MAE Jan-Dec", np.round(mean(abs(error)), 3))


def marine_adjustment(circulation_file, height, width):
    # These are the simulated marine-air anomalies on the physical grid.
    # Reconstruct the runtime's cell-centred, periodic lookup independently.
    from check_wind_belts import read_export
    _, _, currents = read_export(circulation_file)
    air = currents[1]
    h, w = air.shape
    if w != min(128, width) or h != w // 2:
        raise ValueError("Ocean-air diagnostic and climate export grids differ")
    x = (np.arange(width) + .5) * w / width - .5
    y = np.clip((np.arange(height) + .5) * h / height - .5, 0, h-1)[:, None]
    ix, iy = np.floor(x).astype(int), np.floor(y).astype(int)
    fx, fy = x-ix, y-iy
    def row(j):
        return (1-fx)*air[j, ix % w] + fx*air[j, (ix+1) % w]
    return .65 * ((1-fy)*row(iy) + fy*row(np.minimum(iy+1, h-1))).ravel()


def fit(path, check_runtime, ocean_air=None):
    fields = read_model(path)
    if fields[0].shape != (HEIGHT, WIDTH):
        raise ValueError("Fit at the common observational resolution (256 x 128).")
    mean, annual, pairs = geometry(fields)
    ids, area, obs, _ = observation("calibration")
    keep = area >= .5*tile_area(HEIGHT)[ids // WIDTH]
    ids, area, obs = ids[keep], area[keep], obs[keep]
    ocean = (fields[2].ravel()[ids] >= 1) & (fields[2].ravel()[ids] <= 3)
    block = (ids//WIDTH//16)*(WIDTH//16) + ids%WIDTH//16
    # Entire 22.5-degree blocks are excluded per fold, including their original-model fit.
    fold = np.array([hashlib.sha256(f"thermal-v1:{b}".encode("ascii")).digest()[0] % 5 for b in block])
    detail = terms_for(pairs, ids)
    matrices = (mean[ids], annual[ids], annual[ids])
    targets = (obs.mean(1), 2*(obs*COS).mean(1), 2*(obs*SIN).mean(1))
    old, updated = np.zeros_like(obs), np.zeros_like(obs)
    for k in (0, 1, 2, 3, 4, -1):
        train = fold != k
        land = train & ~ocean
        background = [solve(a[land], y[land], area[land], .001) for a, y in zip(matrices, targets)]
        normals = ((matrices[0]@background[0])[:, None] + (matrices[1]@background[1])[:, None]*COS
                   + (matrices[2]@background[2])[:, None]*SIN)
        coeff = solve(detail[train].reshape(-1, len(LABELS)), (obs-normals)[train].ravel(), np.repeat(area[train], 12), .002)
        predicted = normals + detail@coeff
        old[~train], updated[~train] = normals[~train], predicted[~train]
    print(f"Calibration: {len(ids)} cells; complete spatial blocks per fold:",
          [len(np.unique(block[fold == k])) for k in range(5)])
    report("Five-fold original background", old, obs, area)
    report("Five-fold seasonal energy model", updated, obs, area)
    for name, values in zip(("mean", "July cosine", "lag sine"), background):
        print(name, np.round(values, 3))
    for name, value in zip(LABELS, coeff):
        print(name, f"{value:.4f}")
    if check_runtime:
        # ECL1 stores lake beds, while the runtime applies the local lapse rate at lake stage.
        # All other cells have a directly reconstructible temperature, including ocean and ice.
        if isinstance(check_runtime, Path):
            fields = read_model(check_runtime)
            mean, annual, pairs = geometry(fields)
        adjustment = marine_adjustment(ocean_air, *fields[0].shape) if ocean_air else None
        sample = np.flatnonzero(fields[2].ravel() != 4)
        error = 0
        for start in range(0, len(sample), 8192):
            cells = sample[start:start+8192]
            predicted = ((mean[cells]@np.round(background[0], 3))[:, None]
                         + (annual[cells]@np.round(background[1], 3))[:, None]*COS
                         + (annual[cells]@np.round(background[2], 3))[:, None]*SIN
                         + terms_for(pairs, cells)@np.round(coeff, 4))
            if adjustment is not None:
                predicted += adjustment[cells, None]
            error = max(error, abs(np.clip(predicted, -85, 55) - fields[3].reshape(-1, 12)[cells]).max())
        print(f"{len(sample)} non-lake cells, maximum runtime discrepancy {error:.6f} C")
        if error > .003:
            raise AssertionError("Runtime export does not reproduce the fitted parameterization. Coupled maps require their --ocean-air diagnostic.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("earth_export", type=Path)
    parser.add_argument("--check-runtime", nargs="?", const=True, type=Path, metavar="EXPORT",
                        help="compare all reconstructible cells of this or another export with the fitted C# model")
    parser.add_argument("--ocean-air", type=Path, metavar="OWB2",
                        help="include the wind-driven marine-air signal exported on the same Earth geometry")
    args = parser.parse_args()
    if args.ocean_air and not args.check_runtime:
        parser.error("--ocean-air requires --check-runtime")
    fit(args.earth_export, args.check_runtime, args.ocean_air)
