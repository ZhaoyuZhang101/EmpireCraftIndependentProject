"""Verify the shipped land elevations, shared geographic samples and source accuracy."""
import argparse
import struct
import zlib
from pathlib import Path

import numpy as np


ROOT = Path(__file__).resolve().parents[2]
RESOURCES = ROOT / "Assets/ORBIS/Resources/ORBIS/FlatWorld"


def payload(name, tag):
    path = RESOURCES / name
    assert path.is_file(), f"Missing measured land relief resource {name}"
    file = path.read_bytes()
    magic, width, height = struct.unpack_from("<4sII", file)
    assert magic == tag
    return width, height, zlib.decompress(file[12:], -15)


def verify(source):
    width, height, data = payload("EarthLandOverview.bytes", b"OLO1")
    assert (width, height) == (3600, 1800)
    overview = np.frombuffer(data, "<u2").reshape(height, width).astype(int) - 32768
    width, height, data = payload("EarthLandDetail.bytes", b"OLD1")
    assert width == 8192 and height <= 8192 and height % 62 == 0
    header = 360 * 120 * 4
    directory = np.frombuffer(data[:header], "<u2").reshape(120, 360, 2)
    atlas = np.frombuffer(data[header:], "u1").reshape(height, width)
    slots = directory[:, :, 0].astype(int)
    occupied = slots > 0
    assert np.array_equal(np.sort(slots[occupied]), np.arange(1, occupied.sum() + 1))
    pages_per_row = width // 62

    def page(row, column):
        column %= 360
        slot = slots[row, column] - 1
        assert slot >= 0
        encoded = int(directory[row, column, 1])
        base, step = (encoded & 8191) - 1024, 1 << (encoded >> 13)
        y, x = divmod(slot, pages_per_row)
        codes = atlas[y * 62:(y + 1) * 62, x * 62:(x + 1) * 62]
        return base + codes.astype(np.int32) * step

    east = north = seam = 0
    for row, col in zip(*np.where(occupied)):
        if occupied[row, (col + 1) % 360]:
            first, other = page(row, col), page(row, col + 1)
            assert np.array_equal(first[:, -2:], other[:, :2]), (int(row), int(col), "east")
            east += 1
            seam += int(col == 359)
        if row < 119 and occupied[row + 1, col]:
            first, other = page(row, col), page(row + 1, col)
            assert np.array_equal(first[-2:], other[:2]), (int(row), int(col), "north")
            north += 1
    assert east > 10000 and north > 10000 and seam > 0
    print(f"{occupied.sum()} native pages, {east} shared east edges ({seam} at 180 degrees), {north} north edges")

    if source:
        import rasterio
        from rasterio.windows import Window

        sites = ((121, 31, "Shanghai"), (115, 31, "Dabie"), (118, 32, "Lower Yangtze"),
                 (86, 27, "Himalayas"), (7, 46, "Alps"), (22, 36, "Taygetus and Laconian Gulf"),
                 (24, 35, "Cretan mountains"), (18, 42, "Dinaric Alps and Bay of Kotor"),
                 (0, 42, "Pyrenees"), (121, 36, "Haiyang and Yellow Sea shelf"),
                 (-72, -14, "Andes"), (35, 31, "Jordan rift"), (36, -2, "Kenya"),
                 (178, -18, "Fiji"), (140, -34, "Murray basin"))
        with rasterio.open(source) as tiff:
            for lon, lat, label in sites:
                west, top = (lon + 180) * 60, (89 - lat) * 60
                elevations = tiff.read(1, window=Window(west, top, 60, 60))[::-1]
                assert np.all(np.isfinite(elevations)) and elevations.min() > -20000
                reference = np.maximum(elevations, -512)
                stored = page(lat + 60, lon + 180)[1:-1, 1:-1]
                errors = np.abs(reference - stored)
                assert np.max(errors) <= 32.01, (label, errors.max())
                if label == "Shanghai":
                    assert np.quantile(errors, .99) <= 1
                averaged = np.rint(elevations.reshape(10, 6, 10, 6).mean(axis=(1, 3), dtype=np.float64))
                chunk = overview[(lat + 90) * 10:(lat + 91) * 10, (lon + 180) * 10:(lon + 181) * 10]
                assert np.array_equal(averaged, chunk), label
                print(f"{label}: native p99 error {np.quantile(errors, .99):.2f} m, maximum {errors.max():.2f} m")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, help="compare representative native tiles to the original NOAA GeoTIFF")
    verify(parser.parse_args().source)
