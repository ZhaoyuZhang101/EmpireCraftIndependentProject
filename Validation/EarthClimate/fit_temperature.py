"""Fit global heat-transfer coefficients against CRU calibration land blocks alone."""
import argparse
import hashlib
from pathlib import Path
import struct
import subprocess

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
    plateau = (westerly * (1-sea_near) * np.clip(upstream-height-.7, 0, 2.5),
               np.maximum(0, hemisphere[..., None] * COS))
    return (np.stack(np.broadcast_arrays(*mean), axis=-1).reshape(h*w, -1),
            np.stack(np.broadcast_arrays(*annual), axis=-1).reshape(h*w, -1),
            [(g * coupling, months) for g, months in pairs], plateau)


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


def fit_plateau(path, ids, obs, area, fold):
    fields = read_model(path)
    if fields[0].shape != (HEIGHT, WIDTH):
        raise ValueError("The pre-plateau control must use the 256 x 128 reference grid.")
    land = np.isin(fields[2].ravel()[ids], [0, 5])
    _, _, _, plateau = geometry(fields)
    exposure = terms_for([plateau], ids[land])[..., 0]
    residual = obs[land] - fields[3].reshape(-1, 12)[ids[land]]
    weights = area[land, None]

    def coefficient(mask):
        x = exposure[mask]
        denominator = (weights[mask] * x**2).sum()
        if denominator <= 0:
            raise ValueError("No exposed land for the plateau heat fit.")
        return (weights[mask] * x * residual[mask]).sum() / denominator

    all_land = coefficient(slice(None))
    folds = [coefficient(fold[land] != k) for k in range(5)]
    print(f"Warm-season plateau heat: {land.sum()} calibration land/river cells, {all_land:.4f} K/km")
    print("  Leave-one-fold-out K/km:", np.round(folds, 4))
    return round(all_land, 4)


def read_monthly_predictor(path, shape, columns, magic):
    """Interpolate gross condensation or surface-reaching precipitation to land-cell centres."""
    data = Path(path).read_bytes()
    if len(data) < 12:
        raise ValueError("Truncated ORC1 header")
    code, w, h = struct.unpack_from('<4sii', data)
    height, width = shape
    if code != magic or w != min(columns,width) or h != w//2 or len(data) != 12+48*w*h:
        raise ValueError("Invalid cloud forecast grid")
    rain = np.frombuffer(data, '<f4', offset=12).reshape(h, w, 12)
    if not np.isfinite(rain).all() or rain.min() < 0:
        raise ValueError("Invalid simulated cloud-forecast rain")
    x = (np.arange(width)+.5)*w/width-.5
    y = np.clip((np.arange(height)+.5)*h/height-.5, 0, h-1)
    i, j = np.floor(x).astype(int), y.astype(int)
    u, v = (x-i)[None, :, None], (y-j)[:, None, None]
    rows = (1-u)*rain[:, i%w] + u*rain[:, (i+1)%w]
    rain = (1-v)*rows[j] + v*rows[np.minimum(h-1, j+1)]
    return rain.reshape(-1,12)


def read_cloud(path, shape, columns):
    return -np.expm1(-read_monthly_predictor(path, shape, columns, b'ORC1')/45)


def surface_weights(temperature, ids, shape):
    height, width = shape
    lat = (np.pi*((ids//width+.5)/height-.5))[:, None]
    dec = np.arcsin(np.sin(np.deg2rad(23.44))*np.cos(PHASE+.42))
    hour = np.arccos(np.clip(-np.tan(lat)*np.tan(dec), -1, 1))
    sun = 1361/(300*np.pi)*(1-.0334*np.cos(PHASE+.20))*(hour*np.sin(lat)*np.sin(dec)
                                                          + np.cos(lat)*np.cos(dec)*np.sin(hour))
    f = np.clip((4-temperature)/18, 0, 1)
    return sun, f*f*(3-2*f)


def surface_fit(temp, cloud, ids, shape, target, area, train):
    sun, cold = surface_weights(temp, ids, shape)
    weight = area[train, None]
    def reference(exposure):
        return np.sum(weight*exposure[train]*cloud[train]) / np.sum(weight*exposure[train])
    short_ref, long_ref = reference(sun), reference(cold)
    terms = np.stack(((short_ref-cloud)*sun, (cloud-long_ref)*cold), axis=-1)
    gain = solve(terms[train].reshape(-1,2), (target-temp)[train].ravel(), np.repeat(area[train],12), 0)
    return np.array([short_ref, long_ref, *gain])


def surface_change(temp, cloud, ids, shape, params):
    sun, cold = surface_weights(temp, ids, shape)
    solar_ref, cold_ref, shortwave, longwave = params
    return shortwave*(solar_ref-cloud)*sun + longwave*(cloud-cold_ref)*cold


def check_surface_folds(directory, model, plateau_model, fits, ids, area, obs, fold, ocean_air):
    """Refit the historical background in each fold and run a separate cloud forecast from its temperatures."""
    directory.mkdir(exist_ok=True, parents=True)
    shape = model[0].shape
    if shape != (HEIGHT, WIDTH) or plateau_model[0].shape != shape:
        raise ValueError("Surface heat cross-validation requires 256 x 128 controls.")
    ocean = marine_adjustment(ocean_air, *shape)[:, None]
    cells = np.arange(HEIGHT*WIDTH)
    def matrices(fields, sample):
        m, a, pairs, plat = geometry(fields)
        return m[sample], a[sample], terms_for(pairs, sample), terms_for([plat], sample)[...,0]
    historical = matrices(plateau_model, ids)
    current = matrices(model, cells)
    def reconstruct(matrix, background, coefficients):
        m, a, detail, _ = matrix
        return ((m@background[0])[:, None]+(a@background[1])[:, None]*COS
                +(a@background[2])[:, None]*SIN+detail@coefficients)
    land = np.isin(model[2].ravel()[ids], [0,5])
    plateau_land = np.isin(plateau_model[2].ravel()[ids], [0,5])
    source = model[3].reshape(-1,12)
    root = Path(__file__).resolve().parents[2]
    project = root/'Validation/FlatWorld/FlatWorld.Core.Tests.csproj'
    subprocess.run(['dotnet','build',str(project),'-c','Release','--artifacts-path',str(directory/'build')], check=True, cwd=root)
    dll = directory/'build/bin/FlatWorld.Core.Tests/release/FlatWorld.Core.Tests.dll'
    n = len(fits)-1
    all_backgrounds = []
    for k, (background, coeff) in enumerate(fits):
        tr = (fold != k) & plateau_land
        exposure = historical[-1][tr]
        residual = (obs-reconstruct(historical,background,coeff)-ocean[ids])[tr]
        gain = np.sum(area[tr,None]*exposure*residual)/np.sum(area[tr,None]*exposure**2)
        field = reconstruct(current, [np.round(b,3) for b in background], np.round(coeff,4))
        all_backgrounds.append(field+ocean+round(gain,4)*current[-1])
    # Lake stage is missing from ECL1. Only its local lapse term uses stage
    # instead of bed; recover that integer-metre difference from the pinned
    # runtime control and reapply each fold's own independently fitted lapse.
    lakes = model[2].ravel() == 4
    valid = (source[lakes] > -85) & (source[lakes] < 55)
    stage_delta = np.where(valid, source[lakes]-all_backgrounds[-1][lakes], np.nan)
    stage_km = np.round(1000*np.nanmedian(stage_delta,axis=1)/round(fits[-1][0][0][3],3))/1000
    if not np.isfinite(stage_km).all():
        raise ValueError('Cannot recover a lake stage from a fully clipped control.')
    old, new = np.empty_like(obs), np.empty_like(obs)
    for k in range(n):
        field = all_backgrounds[k]
        field[lakes] += stage_km[:,None]*round(fits[k][0][0][3],3)
        temp = np.clip(field,-85,55).astype('<f4')
        normals = directory/f'fold-{k}.f32'
        temp.tofile(normals)
        fit_cloud, forecast = directory/f'fold-{k}.fit.orc', directory/f'fold-{k}.orc'
        for command, output in (('--surface-fit-cloud', fit_cloud), ('--surface-cloud', forecast)):
            subprocess.run(['dotnet', '--roll-forward', 'Major', str(dll), command, str(output), str(WIDTH), str(normals)],
                           check=True, cwd=root)
        base, tr = temp[ids], fold != k
        params = surface_fit(base, read_cloud(fit_cloud, shape, 64)[ids], ids, shape, obs, area, tr & land)
        change = surface_change(base, read_cloud(forecast, shape, 128)[ids], ids, shape, params)
        old[~tr], new[~tr] = base[~tr], np.clip(base+change*land[:,None],-85,55)[~tr]
        print(f'  Fold {k} solar/cold reference and sensitivities:',np.round(params,4))
    report('Five-fold land heat before radiation',old,obs,area)
    report('Five-fold land heat with independent cloud forecasts',new,obs,area)
    np.savez_compressed(directory/'calibration-temperatures.npz',tile=ids,fold=fold,background=old,radiation=new)


def fit(path, check_runtime, ocean_air=None, plateau_baseline=None, surface_control=None,
        surface_cloud=None, check_cloud=None, cloud_folds=None, snow_control=None,
        snow_fit_cloud=None, snow_fit_rain=None, check_snow_rain=None):
    fields = read_model(path)
    if fields[0].shape != (HEIGHT, WIDTH):
        raise ValueError("Fit at the common observational resolution (256 x 128).")
    mean, annual, pairs, plateau = geometry(fields)
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
    fits = []
    for k in (0, 1, 2, 3, 4, -1):
        train = fold != k
        land = train & ~ocean
        background = [solve(a[land], y[land], area[land], .001) for a, y in zip(matrices, targets)]
        normals = ((matrices[0]@background[0])[:, None] + (matrices[1]@background[1])[:, None]*COS
                   + (matrices[2]@background[2])[:, None]*SIN)
        coeff = solve(detail[train].reshape(-1, len(LABELS)), (obs-normals)[train].ravel(), np.repeat(area[train], 12), .002)
        predicted = normals + detail@coeff
        old[~train], updated[~train] = normals[~train], predicted[~train]
        fits.append((background,coeff))
    print(f"Calibration: {len(ids)} cells; complete spatial blocks per fold:",
          [len(np.unique(block[fold == k])) for k in range(5)])
    report("Five-fold original background", old, obs, area)
    report("Five-fold seasonal energy model", updated, obs, area)
    for name, values in zip(("mean", "July cosine", "lag sine"), background):
        print(name, np.round(values, 3))
    for name, value in zip(LABELS, coeff):
        print(name, f"{value:.4f}")
    plateau_heat = fit_plateau(plateau_baseline, ids, obs, area, fold) if plateau_baseline else 0
    surface_params = None
    if surface_control:
        control = read_model(surface_control)
        if control[0].shape != (HEIGHT,WIDTH):
            raise ValueError('Use the 256 x 128 pre-radiation control for fitting.')
        cloud = read_cloud(surface_cloud, control[0].shape, 64)[ids]
        land = np.isin(control[2].ravel()[ids], [0, 5])
        params = surface_fit(control[3].reshape(-1, 12)[ids], cloud, ids, control[0].shape, obs, area, land)
        surface_params = np.round(params, 4)
        print('Land cloud radiation: solar/cold reference fractions and sensitivities', surface_params)
        if cloud_folds:
            check_surface_folds(cloud_folds.resolve(),control,read_model(plateau_baseline),fits,ids,area,obs,fold,ocean_air)
    snow_gain = None
    if snow_control:
        from snow_energy import snow_response
        control = read_model(snow_control)
        shape = control[0].shape
        if shape != (HEIGHT, WIDTH):
            raise ValueError('Fit the seasonal snow response at 256 x 128.')
        land = np.isin(control[2].ravel()[ids], [0,5])
        base = control[3].reshape(-1,12)[ids]
        cloud = read_cloud(snow_fit_cloud, shape, 128)[ids]
        rain = read_monthly_predictor(snow_fit_rain, shape, 128, b'ORP1')[ids]
        exposure = snow_response(base,rain,cloud,surface_weights(base,ids,shape)[0])[land]
        weights = area[land,None]
        snow_gain = round(np.sum(weights*exposure*(obs-base)[land])/np.sum(weights*exposure**2),5)
        print(f'Seasonal snow energy sensitivity: {snow_gain:.5f} K/(W/m2)')
    if check_runtime:
        # ECL1 stores lake beds, while the runtime applies the local lapse rate at lake stage.
        # All other cells have a directly reconstructible temperature, including ocean and ice.
        if isinstance(check_runtime, Path):
            fields = read_model(check_runtime)
            mean, annual, pairs, plateau = geometry(fields)
        adjustment = marine_adjustment(ocean_air, *fields[0].shape) if ocean_air else None
        cloud = read_cloud(check_cloud, fields[0].shape, 128) if check_cloud else None
        snow_rain = read_monthly_predictor(check_snow_rain, fields[0].shape, 128, b'ORP1') if check_snow_rain else None
        sample = np.flatnonzero(fields[2].ravel() != 4)
        error = 0
        for start in range(0, len(sample), 8192):
            cells = sample[start:start+8192]
            predicted = ((mean[cells]@np.round(background[0], 3))[:, None]
                         + (annual[cells]@np.round(background[1], 3))[:, None]*COS
                         + (annual[cells]@np.round(background[2], 3))[:, None]*SIN
                         + terms_for(pairs, cells)@np.round(coeff, 4))
            if plateau_heat:
                predicted += terms_for([plateau], cells)[..., 0] * plateau_heat
            if adjustment is not None:
                predicted += adjustment[cells, None]
            if cloud is not None:
                predicted = np.clip(predicted,-85,55)
                change = surface_change(predicted,cloud[cells],cells,fields[0].shape,surface_params)
                predicted += change*np.isin(fields[2].ravel()[cells],[0,5])[:,None]
            if snow_rain is not None:
                predicted = np.clip(predicted,-85,55)
                forcing = snow_response(predicted,snow_rain[cells],cloud[cells],surface_weights(predicted,cells,fields[0].shape)[0])
                predicted += snow_gain*forcing*np.isin(fields[2].ravel()[cells],[0,5])[:,None]
            error = max(error, abs(np.clip(predicted, -85, 55) - fields[3].reshape(-1, 12)[cells]).max())
        print(f"{len(sample)} non-lake cells, maximum runtime discrepancy {error:.6f} C")
        if error > .003:
            raise AssertionError("Runtime export differs from the fitted heat model. Supply the matching marine-air, plateau, and cloud forcing controls for its generation.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("earth_export", type=Path)
    parser.add_argument("--check-runtime", nargs="?", const=True, type=Path, metavar="EXPORT",
                        help="compare all reconstructible cells of this or another export with the fitted C# model")
    parser.add_argument("--ocean-air", type=Path, metavar="OWB2",
                        help="include the wind-driven marine-air signal exported on the same Earth geometry")
    parser.add_argument("--plateau-baseline", type=Path, metavar="ECL1",
                        help="fit warm-season downwind heating to residuals of the pre-plateau control")
    parser.add_argument('--surface-control',type=Path,metavar='ECL1',help='pre-cloud 256-column background temperature')
    parser.add_argument('--surface-cloud',type=Path,metavar='ORC1',help='frozen 64-column fit predictor from --surface-fit-cloud in the core test executable')
    parser.add_argument('--check-cloud',type=Path,metavar='ORC1',help='runtime predictor from --surface-cloud on the same geometry as --check-runtime')
    parser.add_argument('--cloud-folds',type=Path,metavar='DIR',help='refit the background and generate independent cloud forecasts for five entire spatial folds')
    parser.add_argument('--snow-control',type=Path,metavar='ECL1',help='pre-snow player temperatures for the seasonal-snow coefficient')
    parser.add_argument('--snow-fit-cloud',type=Path,metavar='ORC1',help='128-column gross condensation on the pre-snow calibration background')
    parser.add_argument('--snow-fit-rain',type=Path,metavar='ORP1',help='128-column surface-reaching precipitation for fitting')
    parser.add_argument('--check-snow-rain',type=Path,metavar='ORP1',help='surface-reaching precipitation on the generation being verified')
    args = parser.parse_args()
    if args.ocean_air and not (args.check_runtime or args.cloud_folds):
        parser.error("--ocean-air requires --check-runtime or --cloud-folds")
    if bool(args.surface_control) != bool(args.surface_cloud):
        parser.error('--surface-control and --surface-cloud are used together')
    if args.cloud_folds and not (args.surface_control and args.plateau_baseline and args.ocean_air):
        parser.error('--cloud-folds needs --surface-control, --surface-cloud, --ocean-air and --plateau-baseline')
    if args.check_cloud and not (args.surface_cloud and args.check_runtime):
        parser.error('--check-cloud requires --surface-cloud and --check-runtime')
    if args.surface_cloud and args.check_runtime and not args.check_cloud:
        parser.error('Surface radiation runtime verification needs the 128-column predictor from --check-cloud')
    if any((args.snow_control,args.snow_fit_cloud,args.snow_fit_rain)) and not all((args.snow_control,args.snow_fit_cloud,args.snow_fit_rain)):
        parser.error('--snow-control, --snow-fit-cloud and --snow-fit-rain are fitted together')
    if bool(args.snow_control and args.check_runtime) != bool(args.check_snow_rain) or (args.check_snow_rain and not args.check_cloud):
        parser.error('Checking seasonal snow requires the snow fit and both runtime ORC1/ORP1 predictor fields')
    fit(args.earth_export,args.check_runtime,args.ocean_air,args.plateau_baseline,args.surface_control,
        args.surface_cloud,args.check_cloud,args.cloud_folds,args.snow_control,args.snow_fit_cloud,args.snow_fit_rain,args.check_snow_rain)
