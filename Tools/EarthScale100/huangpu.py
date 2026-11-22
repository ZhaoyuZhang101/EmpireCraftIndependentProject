"""Surveyed Huangpu mainstem from the Dianfeng lake outlet to Wusongkou."""
import json
from pathlib import Path

import numpy as np
from rasterio.windows import from_bounds
from scipy.ndimage import binary_closing, distance_transform_edt, gaussian_filter1d, median_filter
from scipy.sparse import csr_matrix
from scipy.sparse.csgraph import dijkstra
from scipy.spatial import cKDTree

from river_transects import read_transects
from river_widths import chainage, complete_widths, KM_PER_DEGREE
from worldcover import open_worldcover


TILE_SHA256 = "f3859c80b7bd8d61a82f0adafee02378d418a2f1bafe32d8229b84301329c814"


def lower_course(cache):
    manifest = json.loads(Path(__file__).with_name("grwl_section_tiles.json").read_text())
    records = read_transects(cache, "NH51", manifest["NH51"])
    # Indices increase upstream. Beyond 2550 this survey turns into a southern
    # tributary. Below 83 it follows the receiving Yangtze past Wusongkou.
    lo, hi = 83, 2550
    reach = records[(records[:, 1] == 5) & (records[:, 2] >= lo) & (records[:, 2] <= hi)]
    reach = reach[np.argsort(-reach[:, 2])]
    if not np.array_equal(reach[:, 2], np.arange(hi, lo-1, -1)):
        raise ValueError("Incomplete GRWL survey of the Huangpu")
    points = reach[:, 4:6].copy()
    # Suppress the Landsat pixel staircase while retaining both surveyed ends.
    points[1:-1] = gaussian_filter1d(points, 1.5, axis=0, mode="nearest")[1:-1]
    width, flag = reach[:, 0], reach[:, 3]
    valid = np.isin(flag, (0, 2)) & (width >= 90) & (width < 65535)
    measured = np.full(len(reach), np.nan)
    measured[valid] = median_filter(width[valid], size=13, mode="nearest")
    full_width = complete_widths(points, measured, np.median(width[valid]))
    keep = np.r_[np.arange(0, len(reach)-1, 6), len(reach)-1]
    return points[keep], full_width[keep]


def upper_course(cache, join):
    with open_worldcover(cache, "N30E120", TILE_SHA256) as tile:
        window = from_bounds(120.963, 30.937, 121.243, 31.105, tile.transform).round_offsets().round_lengths()
        native = tile.read(1, window=window) == 80  # Permanent surface water.
        grid = tile.window_transform(window)
    metres = KM_PER_DEGREE*1000*np.array([np.cos(np.deg2rad(31.0)), 1.0])
    stride = 3
    wet = native.reshape(native.shape[0]//stride, stride, -1, stride).sum(axis=(1, 3)) >= 5
    pixel_m = stride*np.abs([grid.a, grid.e])*metres
    # A road deck can interrupt the satellite water mask. Close gaps of at most
    # two coarse pixels (48–56 m). These links retain zero bank clearance.
    connected = wet | binary_closing(wet, structure=np.ones((3, 3)))
    clearance = distance_transform_edt(wet, sampling=pixel_m[::-1])[connected]
    row, col = np.nonzero(connected)
    centres = np.column_stack(grid*((col+.5)*stride, (row+.5)*stride))
    a, b = cKDTree(np.column_stack((col, row))).query_pairs(np.sqrt(2)+1e-6, output_type="ndarray").T
    resistance = 1 + (70/(8+clearance))**2
    cost = np.linalg.norm((centres[a]-centres[b])*metres, axis=1)*(resistance[a]+resistance[b])*.5
    graph = csr_matrix((cost, (a, b)), shape=(len(row), len(row)))

    # Mark the outlet and the named channel at the delta's ambiguous forks.
    # Between them favour the middle of the observed water channel.
    anchors = np.array([(120.97575, 31.09121), (121.059, 31.04062),
                        (121.112, 31.00854), (121.213, 30.94771), join])
    error, nodes = cKDTree(centres*metres).query(anchors*metres)
    if max(error) > 45:
        raise ValueError("Huangpu upper control outside the measured channel")
    path = [nodes[0]]
    for start, end in zip(nodes, nodes[1:]):
        distances, previous = dijkstra(graph, directed=False, indices=start, return_predecessors=True)
        if not np.isfinite(distances[end]):
            raise ValueError("Disconnected WorldCover Huangpu channel")
        leg = [end]
        while leg[-1] != start:
            leg.append(previous[leg[-1]])
        leg.reverse()
        path.extend(leg[1:])
    centreline = gaussian_filter1d(centres[path], 1.5, axis=0, mode="nearest")
    centreline[[0, -1]] = anchors[[0, -1]]
    along = chainage(centreline)
    stations = np.linspace(0, along[-1], int(np.ceil(along[-1]/.10))+1)
    points = np.column_stack([np.interp(stations, along, axis) for axis in centreline.T])

    # Walk each perpendicular transect to the first dry native pixel on each
    # bank. A bridge or unbounded fork is a missing width observation.
    tangent = np.gradient(points, axis=0)*metres
    normal = tangent[:, ::-1]*[-1, 1] / np.linalg.norm(tangent, axis=1)[:, None]
    offsets = np.arange(-600, 601, 5)
    probes = points[:, None, :] + (normal/metres)[:, None, :]*offsets[None, :, None]
    x, y = (~grid) * (probes[:, :, 0], probes[:, :, 1])
    covered = native[np.floor(y).astype(int), np.floor(x).astype(int)]
    mid = len(offsets)//2
    left, right = ~covered[:, mid::-1], ~covered[:, mid:]
    valid = covered[:, mid] & left.any(axis=1) & right.any(axis=1)
    widths = np.full(len(points), np.nan)
    left_bank = (np.argmax(left[valid], axis=1)-.5)*5
    right_bank = (np.argmax(right[valid], axis=1)-.5)*5
    # The player draws the same radius on both sides. At a tributary mouth,
    # the far bank can be hundreds of metres away. Cap the observed radius
    # one native pixel beyond the nearer bank before median filtering.
    bank_limit = 2*(np.minimum(left_bank, right_bank)+10)
    widths[valid] = np.minimum(left_bank+right_bank, bank_limit)
    widths[valid] = median_filter(widths[valid], size=3, mode="nearest")
    return points, widths


def huangpu_course(cache):
    lower, lower_widths = lower_course(cache)
    upper, widths = upper_course(cache, lower[0])
    points = np.vstack((upper[:-1], lower))
    widths = np.r_[widths[:-1], lower_widths]
    return points, complete_widths(points, widths, np.nanmedian(widths))
