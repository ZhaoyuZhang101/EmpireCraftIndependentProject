"""Bake the filtered Earth overview and sparse native 60-second land relief."""
import argparse
import hashlib
import json
import struct
import zlib
from pathlib import Path

import numpy as np
import rasterio
from rasterio.windows import Window

from bake_player_presets import RESOURCES, ROOT, SOURCE


NATIVE = 60  # Samples per geographic degree.
PAGE = NATIVE + 2
WIDTH = 8192
SOUTH = -60
NORTH = 60
LAND_FLOOR = -512  # Below the lowest exposed land on Earth. Ocean optics has its own DEM.


def source_pages(tiff, latitude):
    # The immediate neighbouring sample is duplicated on each of the four sides.
    top = (89 - latitude) * NATIVE - 1
    values = tiff.read(1, window=Window(0, top, 21600, PAGE))[::-1]
    if not np.isfinite(values).all() or np.any(values == tiff.nodata):
        raise ValueError("Missing NOAA elevation in a land detail strip")
    strip = np.pad(values, ((0, 0), (1, 1)), mode="wrap")
    return np.lib.stride_tricks.sliding_window_view(strip, PAGE, axis=1)[:, ::NATIVE, :].transpose(1, 0, 2)


def deflate(tag, width, height, *blocks):
    compressor = zlib.compressobj(8, wbits=-15)
    result = bytearray(struct.pack("<4sII", tag, width, height))
    for block in blocks:
        result.extend(compressor.compress(block))
    result.extend(compressor.flush())
    return result


def bake(tiff):
    overview = np.empty((1800, 3600), "<u2")
    for lat in range(-90, 90):
        strip = tiff.read(1, window=Window(0, (89-lat)*NATIVE, 21600, NATIVE))[::-1]
        if not np.isfinite(strip).all() or np.any(strip == tiff.nodata):
            raise ValueError("Missing NOAA overview elevation")
        metres = strip.reshape(10, 6, 3600, 6).mean(axis=(1, 3), dtype=np.float64)
        overview[(lat+90)*10:(lat+91)*10] = np.rint(metres) + 32768

    shape = (NORTH - SOUTH, 360)
    active = np.zeros(shape, bool)
    bases = np.zeros(shape, np.int32)
    steps = np.ones(shape, np.int32)
    for y, lat in enumerate(range(SOUTH, NORTH)):
        values = source_pages(tiff, lat)
        highest = np.ceil(values.max(axis=(1, 2))).astype(int)
        lowest = np.maximum(LAND_FLOOR, np.floor(values.min(axis=(1, 2))).astype(int))
        active[y] = highest >= -500
        # Leave headroom for a duplicate node to adopt a neighbouring page's
        # coarser quantisation. A 64-metre quantum is enough even for 9 km of
        # local relief. Low-lying flat pages retain a one-metre quantum.
        pending = active[y].copy()
        for step in (1, 2, 4, 8, 16, 32, 64):
            base = ((lowest - 32) // step) * step
            fits = (highest + 32 - base) <= 255 * step
            choose = fits & pending
            bases[y, choose], steps[y, choose] = base[choose], step
            pending &= ~fits
        if np.any((highest + 32 - bases[y] > 255 * steps[y]) & active[y]):
            raise ValueError("Elevation range exceeds the page encoding")

    n = int(active.sum())
    columns = WIDTH // PAGE
    height = (n + columns - 1) // columns * PAGE
    if height > WIDTH or np.any((bases[active] + 1024 < 0) | (bases[active] + 1024 >= 8192)):
        raise ValueError("Land detail exceeds the atlas or elevation directory")
    directory = np.zeros((*shape, 2), "<u2")
    directory[:, :, 0][active] = np.arange(1, n + 1)
    directory[:, :, 1][active] = (bases[active] + 1024) | (np.log2(steps[active]).astype(int) << 13)
    atlas = np.zeros((height, WIDTH), "u1")
    edges = (slice(None, 2), slice(None), slice(-2, None))
    max_error = 0.
    for y, lat in enumerate(range(SOUTH, NORTH)):
        values = np.maximum(source_pages(tiff, lat), LAND_FLOOR)
        quantum = np.broadcast_to(steps[y, :, None, None], values.shape).copy()
        for dy in (-1, 0, 1):
            if not 0 <= y + dy < len(steps):
                continue
            for dx in (-1, 0, 1):
                if dx == 0 and dy == 0:
                    continue
                # Powers of two share a lattice. All copies of the same
                # geographic sample adopt the largest touching page's quantum.
                # Both decoded edge rows/columns and their gradients then agree.
                area = quantum[:, edges[dy+1], edges[dx+1]]
                np.maximum(area, np.roll(steps[y+dy], -dx)[:, None, None], out=area)
        rounded = np.rint(values / quantum).astype(np.int32) * quantum
        codes = (rounded - bases[y, :, None, None]) // steps[y, :, None, None]
        row = active[y]
        if not ((codes[row] >= 0) & (codes[row] <= 255)).all():
            raise ValueError("Quantized elevation is outside its page range")
        if np.any(row):
            max_error = max(max_error, np.abs(values[row] - rounded[row]).max())
        for x in np.flatnonzero(row):
            page_y, page_x = divmod(int(directory[y, x, 0]) - 1, columns)
            atlas[page_y*PAGE:(page_y+1)*PAGE, page_x*PAGE:(page_x+1)*PAGE] = codes[x]
    print(f"{n} native pages ({WIDTH} x {height} R8), maximum elevation error {max_error:g} m")
    return {"EarthLandOverview.bytes": deflate(b"OLO1", 3600, 1800, overview),
            "EarthLandDetail.bytes": deflate(b"OLD1", WIDTH, height, directory, atlas)}, n, float(max_error)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=ROOT / "ValidationOutput/EarthScale100/global/ETOPO2022-60s.tif")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    source = json.loads((SOURCE / "metadata.json").read_text(encoding="utf8"))
    with args.source.open("rb") as stream:
        if hashlib.file_digest(stream, "sha256").hexdigest() != source["source_sha256"]:
            raise ValueError("Expected the NOAA ETOPO 2022 60s ice-surface GeoTIFF in global/metadata.json")
    with rasterio.open(args.source) as tiff:
        if (tiff.width, tiff.height) != (21600, 10800) or tuple(tiff.bounds) != (-180., -90., 180., 90.):
            raise ValueError("Unexpected registration of the NOAA source")
        files, count, error = bake(tiff)
    metadata = {
        "dataset": source["dataset"], "doi": source["dataset_doi"], "source_sha256": source["source_sha256"],
        "vertical_datum": source["vertical_datum"], "license": source["license"],
        "overview": "3600 x 1800, area means of native 6 x 6 samples, rounded once in metres; south first",
        "detail": "Native 60 arc-seconds between 60 S and 60 N; 1 degree pages with a shared geographic sample at each edge",
        "page_selection": "Every page touching terrain at or above -500 m. Heights below -512 m are clamped for land shading.",
        "pages": count, "maximum_quantization_error_metres": error,
        "sha256": {name: hashlib.sha256(data).hexdigest() for name, data in files.items()}
    }
    outputs = {RESOURCES / name: data for name, data in files.items()}
    outputs[SOURCE / "land-relief.json"] = (json.dumps(metadata, indent=2) + "\n").encode()
    for path, data in outputs.items():
        if args.check:
            if path.read_bytes() != data:
                raise ValueError(f"{path.name} differs from the NOAA bake")
        else:
            path.write_bytes(data)
        print(f"{path.name}: {len(data)} bytes, SHA-256 {hashlib.sha256(data).hexdigest()}")


if __name__ == "__main__":
    main()
