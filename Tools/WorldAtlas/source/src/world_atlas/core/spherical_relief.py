"""Physical sampling footprints for the ORBIS elevation export."""
import math

import numpy as np
from scipy.spatial import cKDTree

from world_atlas.physical.planetary_grid import lon_lat_to_unit_vector


def grid_vectors(shape):
    height, width = shape
    latitude = 90.0 - (np.arange(height) + 0.5) * 180.0 / height
    longitude = (np.arange(width) + 0.5) * 360.0 / width - 180.0
    return lon_lat_to_unit_vector(longitude[None, :], latitude[:, None])


def distance_to_sources_km(vectors, sources, radius_km):
    """Exact shortest arc to a source point, including across a pole."""
    if len(sources) == 0:
        return np.full(vectors.shape[:-1], np.inf, dtype=np.float32)
    chord, _ = cKDTree(sources).query(vectors)
    return (2 * radius_km * np.arcsin(np.clip(chord * 0.5, 0, 1))).astype(np.float32)


def shoreline_distance_km(land, radius_km):
    """Distance to the midpoints of the actual wet/dry cell interfaces."""
    height, width = land.shape
    rows, columns = np.nonzero(land != np.roll(land, -1, axis=1))
    meridians = lon_lat_to_unit_vector(
        (columns + 1.0) * 360.0 / width - 180.0,
        90.0 - (rows + 0.5) * 180.0 / height,
    )
    rows, columns = np.nonzero(land[:-1] != land[1:])
    parallels = lon_lat_to_unit_vector(
        (columns + 0.5) * 360.0 / width - 180.0,
        90.0 - (rows + 1.0) * 180.0 / height,
    )
    return distance_to_sources_km(
        grid_vectors(land.shape), np.concatenate((meridians, parallels)), radius_km,
    )


def smooth_field(values, radius, passes=1):
    """Area-weighted mean in a spherical cap spanning a given equatorial radius.

    Find each latitude ring's intersection with the cap by the spherical law
    of cosines. Periodic prefix integrals include fractional longitude cells
    and let a cap cover whole rings or the opposite side of a pole.
    """
    result = np.asarray(values, dtype=np.float32)
    if radius <= 0 or passes <= 0:
        return result
    height, width = result.shape
    latitude_step = math.pi / height
    rows = np.arange(height)
    latitude = math.pi / 2 - (rows + 0.5) * latitude_step
    sine, cosine = np.sin(latitude), np.cos(latitude)
    # Equal area to the former (2r+1)-cell square on the equatorial tangent plane.
    cap_radius = min(math.pi, (2 * radius + 1) * latitude_step / math.sqrt(math.pi))
    reach = math.ceil(cap_radius / latitude_step)
    pad = width // 2 + 1
    columns = np.arange(width)[None, :] + pad
    rings = []
    area = np.zeros((height, 1))
    for offset in range(-reach, reach + 1):
        neighbor = np.clip(rows + offset, 0, height - 1)
        limit = (math.cos(cap_radius) - sine * sine[neighbor]) / (cosine * cosine[neighbor])
        half_width = np.arccos(np.clip(limit, -1.0, 1.0)) * width / math.tau
        half_width[neighbor != rows + offset] = 0.0
        left, right = 0.5 - half_width, 0.5 + half_width
        left_cell, right_cell = np.floor(left).astype(int), np.floor(right).astype(int)
        rings.append((neighbor[:, None], left_cell[:, None], right_cell[:, None],
                      (left - left_cell)[:, None], (right - right_cell)[:, None]))
        area[:, 0] += 2 * half_width * cosine[neighbor]

    for _ in range(passes):
        wrapped = np.pad(result, ((0, 0), (pad, pad)), mode='wrap')
        integral = np.pad(np.cumsum(wrapped, axis=1, dtype=np.float64), ((0, 0), (1, 0)))
        total = np.zeros_like(result, dtype=np.float64)
        for neighbor, left, right, left_part, right_part in rings:
            start, stop = columns + left, columns + right
            mass = (integral[neighbor, stop] - integral[neighbor, start]
                    + right_part * wrapped[neighbor, stop] - left_part * wrapped[neighbor, start])
            total += mass * cosine[neighbor]
        result = (total / area).astype(np.float32)
    return result
