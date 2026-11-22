"""GRWL 30 m Landsat transects for the Chinese, Indian and Nile study basins."""
import hashlib
import json
from pathlib import Path
import struct
import urllib.request
import zipfile
import zlib

import numpy as np
import shapefile
from scipy.ndimage import median_filter

from river_widths import RiverWidths, store_verified


ARCHIVE = "GRWL_vector_V01.01.zip"
URL = "https://zenodo.org/api/records/18624542/files/" + ARCHIVE + "/content"


def section_file(cache, tile, entry):
    folder = cache / "GRWL_transects"
    folder.mkdir(exist_ok=True)
    path = folder / (tile + ".dbf")
    member = "GRWL_vector_V01.01/" + path.name
    digest = entry["sha256"]
    if path.exists():
        if hashlib.sha256(path.read_bytes()).hexdigest() != digest:
            raise ValueError(f"GRWL {tile} transect checksum mismatch")
        return path
    if (cache / ARCHIVE).exists():
        with zipfile.ZipFile(cache / ARCHIVE) as archive:
            raw = archive.read(member)
    else:
        # Only the DBF is needed. It contains geographic coordinates, transect
        # widths, waterbody class, segment identity and along-segment order.
        offset, size = entry["offset"], entry["compressed_size"]
        end = offset + 30 + len(member) + size + 39
        req = urllib.request.Request(URL + f"?range={offset}", headers={"Range": f"bytes={offset}-{end}"})
        with urllib.request.urlopen(req, timeout=180) as response:
            if response.status != 206 or not response.headers.get("Content-Range", "").startswith(f"bytes {offset}-"):
                raise IOError(f"Server did not return the requested GRWL archive member {tile}")
            chunk = response.read()
        header = struct.unpack_from("<4s5H3I2H", chunk)
        start = 30 + header[-2] + header[-1]
        if header[0] != b"PK\x03\x04" or header[3] != 8 or chunk[30:30+header[-2]].decode() != member:
            raise ValueError(f"Invalid GRWL archive member {tile}")
        raw = zlib.decompress(chunk[start:start+size], -15)
    store_verified(path, raw, digest)
    return path


def read_transects(cache, tile, entry):
    path = section_file(cache, tile, entry)
    # Fields are in the source DBF's original order.
    fields = ["width_m", "segmentID", "segmentInd", "lakeFlag", "lon", "lat"]
    with shapefile.Reader(dbf=str(path)) as reader:
        return np.fromiter(reader.iterRecords(fields=fields), dtype=(float, (6,)), count=len(reader))


class RiverTransects(RiverWidths):
    def __init__(self, cache):
        manifest = json.loads(Path(__file__).with_name("grwl_section_tiles.json").read_text())
        starts, ends, sizes = [], [], []
        self.run_ids = []
        for tile, entry in manifest.items():
            records = read_transects(cache, tile, entry)
            w, seg, order, flag, lon, lat = records.T
            coords = records[:, 4:6]
            wet = ((flag == 0) | (flag == 2)) & (w >= 90) & (w < 65535)
            # Never jump between channels, over invalid transects or across a
            # reservoir even when an upstream data record reuses a segment ID.
            linked = ((np.diff(seg) == 0) & (np.diff(order) == 1) & wet[1:] & wet[:-1]
                      & (abs(np.diff(lon)) < .002) & (abs(np.diff(lat)) < .002))
            boundary = np.r_[0, np.flatnonzero(~linked)+1, len(w)]
            for i in np.flatnonzero(np.diff(boundary) >= 7):
                a, b = boundary[i:i+2]
                samples = np.r_[np.arange(a, b-1, 8), b-1]
                # Median of 13 neighbouring 30 m transects suppresses single-
                # pixel spikes while retaining the real local expansion of a reach.
                widths = median_filter(w[a:b], size=13, mode="nearest")[samples-a]
                starts.append(coords[samples[:-1]])
                ends.append(coords[samples[1:]])
                sizes.append((widths[:-1]+widths[1:])*.5)
                self.run_ids.append((tile, int(seg[a]), int(order[a]), len(samples)-1))
        self.set_segments(np.concatenate(starts), np.concatenate(ends), np.concatenate(sizes))
