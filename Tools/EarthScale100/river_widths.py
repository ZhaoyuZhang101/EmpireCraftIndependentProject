"""Match Landsat reach medians to the Earth preset's surveyed river courses."""
import hashlib
import io
from pathlib import Path
import tempfile
import urllib.request
import zipfile

import numpy as np
import shapefile
import shapely


FILENAME = "GRWL_summaryStats_V01.02.zip"
URL = "https://zenodo.org/api/records/18624542/files/" + FILENAME + "/content"
SHA256 = "78aa43a7104d24b1696e9b1547347eec13ba5d660aa990c1e7cea98e7ae70e4a"
KM_PER_DEGREE = 111.195


def store_verified(path, data, digest):
    if hashlib.sha256(data).hexdigest() != digest:
        raise ValueError(f"GRWL checksum mismatch: {path.name}")
    temp = tempfile.NamedTemporaryFile(dir=path.parent, delete=False)
    scratch = Path(temp.name)
    try:
        with temp:
            temp.write(data)
        scratch.replace(path)
    finally:
        scratch.unlink(missing_ok=True)


def grwl_archive(cache):
    path = cache / FILENAME
    if not path.exists():
        with urllib.request.urlopen(URL, timeout=180) as response:
            raw = response.read()
        store_verified(path, raw, SHA256)
    elif hashlib.sha256(path.read_bytes()).hexdigest() != SHA256:
        raise ValueError("Cached GRWL V01.02 checksum mismatch")
    return path


def grwl_reader(cache):
    with zipfile.ZipFile(grwl_archive(cache)) as archive:
        return shapefile.Reader(**{ext: io.BytesIO(archive.read("GRWL_summaryStats." + ext))
                                   for ext in ("shp", "shx", "dbf")})


def chainage(points):
    delta = np.diff(points, axis=0)
    delta[:, 0] = (delta[:, 0]+180) % 360 - 180
    delta[:, 0] *= np.cos(np.deg2rad((points[1:, 1]+points[:-1, 1])*.5))
    return np.r_[0, np.cumsum(np.hypot(delta[:, 0], delta[:, 1]))] * KM_PER_DEGREE


class RiverWidths:
    def __init__(self, lines, widths):
        # Break long reach records into indexed segments to compare the local
        # tangent at bends. Independent source parts never share a connection.
        starts, ends, sizes = [], [], []
        for points, width in zip(lines, widths):
            if len(points) < 2:
                continue
            p = np.array(points, dtype=float)
            p[:, 0] = np.rad2deg(np.unwrap(np.deg2rad(p[:, 0])))
            p = shapely.get_coordinates(shapely.simplify(shapely.LineString(p), .0007,
                                                          preserve_topology=False))
            a, b = p[:-1].copy(), p[1:].copy()
            shift = 360*np.floor(((a[:, 0]+b[:, 0])*.5 + 180)/360)
            a[:, 0] -= shift
            b[:, 0] -= shift
            for offset in (0, -360, 360):
                mask = np.ones(len(a), dtype=bool) if offset == 0 else (
                    np.maximum(a[:, 0], b[:, 0]) > 179.5 if offset < 0
                    else np.minimum(a[:, 0], b[:, 0]) < -179.5)
                if np.any(mask):
                    starts.append(a[mask]+[offset, 0])
                    ends.append(b[mask]+[offset, 0])
                    sizes.append(np.full(mask.sum(), width))
        self.set_segments(np.concatenate(starts), np.concatenate(ends), np.concatenate(sizes))

    def set_segments(self, a, b, widths):
        self.a, self.b, self.widths = a, b, widths
        self.tree = shapely.STRtree(shapely.linestrings(np.stack((self.a, self.b), axis=1)))

    @classmethod
    def from_cache(cls, cache):
        lines, widths = [], []
        with grwl_reader(cache) as reader:
            for item in reader.iterShapeRecords(fields=["width_med_", "lakeFlag", "nSegPx"]):
                width, flag, support = item.record
                # Landsat river widths become less reliable below three 30 m
                # pixels. Exclude lake/canal transects and isolated samples.
                if flag not in (0, 2) or not 90 <= width < 65535 or support < 3:
                    continue
                points = np.asarray(item.shape.points)
                for part in np.split(points, list(item.shape.parts[1:])):
                    lines.append(part)
                    widths.append(width)
        return cls(lines, widths)

    def match(self, points):
        """Return measured full widths in metres, or NaN for unsupported stations."""
        result = np.full(len(points), np.nan)
        x, y = points.T
        east = np.maximum(.01, np.cos(np.deg2rad(y)))
        reach = 2.5 / KM_PER_DEGREE
        search = shapely.box(x-reach/east, y-reach, x+reach/east, y+reach)
        station, segment = self.tree.query(search)
        if not len(station):
            return result
        scale = np.column_stack((east[station], np.ones(len(station)))) * KM_PER_DEGREE
        direction = (self.b[segment]-self.a[segment])*scale
        length = np.linalg.norm(direction, axis=1)
        unit = direction / np.maximum(length, 1e-12)[:, None]
        offset = (points[station]-self.a[segment])*scale
        along = np.clip(np.sum(offset*unit, axis=1), 0, length)
        distance = np.linalg.norm(offset - along[:, None]*unit, axis=1)
        # Compare each incoming/outgoing tangent separately at a sharp bend.
        tangents = np.diff(points, axis=0)
        tangents[:, 0] = (tangents[:, 0]+180) % 360 - 180
        tangents[:, 0] *= (east[1:]+east[:-1])*.5
        tangents /= np.maximum(np.linalg.norm(tangents, axis=1), 1e-12)[:, None]
        alignment = np.maximum(abs(np.sum(tangents[np.maximum(station-1, 0)]*unit, axis=1)),
                               abs(np.sum(tangents[np.minimum(station, len(tangents)-1)]*unit, axis=1)))
        widths = self.widths[segment]
        # Allow for registration of the 1:10m centreline within a wide
        # channel, while keeping narrow parallel channels distinct.
        valid = (length > 1e-7) & (alignment > .64) & (distance < np.minimum(2.5, .65+widths/1700))
        station, widths = station[valid], widths[valid]
        if not len(station):
            return result
        cost = distance[valid] + (1-alignment[valid])*.9
        order = np.lexsort((segment[valid], cost, station))
        first = order[np.r_[True, np.diff(station[order]) != 0]]
        result[station[first]] = widths[first]
        best_cost = np.full(len(points), np.inf)
        best_cost[station[first]] = cost[first]
        # Two equally plausible reaches of very different width (e.g. near
        # a confluence) are resolved from this course's neighbours instead.
        ratio = widths / result[station]
        ambiguous = (cost < best_cost[station]+.15) & ((ratio < .5) | (ratio > 2))
        result[station[ambiguous]] = np.nan
        return result


def complete_widths(points, measured, fallback):
    """Interpolate along the course, tapering to a rank estimate over long gaps."""
    measured = np.asarray(measured, dtype=float)
    along = chainage(points)
    valid = np.flatnonzero(np.isfinite(measured))
    if not len(valid):
        return np.full(len(points), round(fallback), dtype="<u2")
    distances = along[valid]
    right = np.clip(np.searchsorted(distances, along), 0, len(valid)-1)
    nearest = np.minimum(abs(along-distances[right]), abs(along-distances[np.maximum(right-1, 0)]))
    # Work in log-width so short transitions between different medians stay
    # positive. Short unmapped gaps borrow only this course's own measurements.
    blend = np.clip((18-nearest)/12, 0, 1)
    logw = np.interp(along, distances, np.log(measured[valid]))
    return np.rint(np.exp(blend*logw + (1-blend)*np.log(fallback))).clip(1, 65535).astype("<u2")
