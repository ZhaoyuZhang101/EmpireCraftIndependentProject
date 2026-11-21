"""Fit the geometry-based temperature normals using only the calibration blocks."""
import argparse
from pathlib import Path

import numpy as np

from score import HEIGHT, WIDTH, observation, read_model, tile_area


def ocean_fetch(ocean, lat):
    # Exact periodic boundary of a one-sided exponential average over 1,800 km.
    memory = np.exp(-40030.229 * np.cos(lat[:, 0]) / (WIDTH * 1800))
    fields = []
    for direction in (1, -1):
        row = ocean[:, ::direction]
        value = np.zeros(HEIGHT)
        for column in row.T:
            value = memory * value + (1 - memory) * column
        value /= 1 - memory**WIDTH
        field = np.empty_like(ocean, dtype=float)
        for x, column in enumerate(row.T):
            field[:, x] = value
            value = memory * value + (1 - memory) * column
        fields.append(field[:, ::direction])
    return fields


def fit(path):
    bed, distance, water, _, _ = read_model(path)
    if bed.shape != (HEIGHT, WIDTH):
        raise ValueError("Fit at the common observational resolution (256 x 128).")
    lat = np.broadcast_to(np.pi * ((np.arange(HEIGHT) + .5) / HEIGHT - .5)[:, None], bed.shape)
    degrees, sine = np.abs(np.rad2deg(lat)), np.sin(lat)
    sea = (water >= 1) & (water <= 3)
    west, east = ocean_fetch(sea, lat)
    westerlies = (.5 + .5 * np.tanh((degrees - 32) / 8)) * (.5 - .5 * np.tanh((degrees - 68) / 8))
    marine = west * westerlies + east * (1 - westerlies)
    inland, contrast = 1 - marine, west - east
    near = np.minimum(1, distance / (WIDTH * .05))
    elevation = np.where(sea, 0, bed) / 1000
    ones = np.ones_like(marine)
    annual = (ones, sine**2, sine**4, elevation, sine**2 * (marine - .5),
              np.exp(-((degrees - 24) / 13)**2) * (inland - .5), contrast * abs(sine))
    seasonal = [sine * term for term in (ones, near, inland, abs(sine) * inland, contrast)]
    phase = (np.arange(12) - 6) * np.pi / 6

    ids, area, t, _ = observation("calibration")
    keep = (area >= .5 * tile_area(HEIGHT)[ids // WIDTH]) & ~sea.ravel()[ids]
    ids, area, t = ids[keep], area[keep], t[keep]
    weight = area / area.sum()
    for name, terms, target in (("mean", annual, t.mean(1)),
                                ("July cosine", seasonal, 2 * (t * np.cos(phase)).mean(1)),
                                ("lag sine", seasonal, 2 * (t * np.sin(phase)).mean(1))):
        a = np.stack(terms, axis=-1).reshape(-1, len(terms))[ids]
        coefficients = np.linalg.solve((a.T * weight) @ a + .001 * np.eye(len(terms)),
                                       (a.T * weight) @ target)
        print(name, np.round(coefficients, 3))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("earth_export", type=Path)
    fit(parser.parse_args().earth_export)
