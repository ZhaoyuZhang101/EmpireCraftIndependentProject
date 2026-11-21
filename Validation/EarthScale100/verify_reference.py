"""Verify the bundled Earth samples and measure the effect of mesh spacing offline."""
import hashlib
import json
from pathlib import Path

import numpy as np


ROOT = Path(__file__).resolve().parents[2] / "Examples" / "EarthScale100"


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def check_file(path, digest):
    with path.open("rb") as file:
        if hashlib.file_digest(file, "sha256").hexdigest() != digest:
            raise ValueError(f"Unexpected file content: {path}")


def coarse_surface(bed, stride):
    # The viewer uses the NE-to-SW diagonal in each coarse quad.
    coarse = bed[::stride, ::stride].astype(np.float64)
    rows, columns = np.indices(bed.shape)
    x = np.minimum(columns // stride, coarse.shape[1] - 2)
    y = np.minimum(rows // stride, coarse.shape[0] - 2)
    u = columns / stride - x
    v = rows / stride - y
    nw, ne = coarse[y, x], coarse[y, x + 1]
    sw, se = coarse[y + 1, x], coarse[y + 1, x + 1]
    return np.where(u + v <= 1, nw + (ne - nw)*u + (sw - nw)*v,
                    se + (sw - se)*(1 - u) + (ne - se)*(1 - v))


def main():
    manifest = read_json(ROOT / "manifest.json")
    global_meta = read_json(ROOT / "global" / "metadata.json")
    assert manifest["realMetresPerGameMetre"] == manifest["localSpacingMeters"] == 100
    assert manifest["earthMeanRadiusMetres"] == 6371008.8
    global_path = ROOT / manifest["globalFile"]
    check_file(global_path, global_meta["overview"]["sha256"])
    globe = np.fromfile(global_path, "<i2").reshape(manifest["globalHeight"], manifest["globalWidth"])
    assert globe.shape == (2160, 4320) and (globe.min(), globe.max()) == (-10470, 6560)
    legacy = global_meta["legacy_import"]
    check_file(global_path.parent / legacy["file"], legacy["sha256"])

    terrain_path = ROOT / manifest["localFile"]
    meta = read_json(terrain_path.with_suffix(".json"))
    mask_path = terrain_path.parent / meta["sourceMask"]["file"]
    check_file(terrain_path, meta["sha256"])
    check_file(mask_path, meta["sourceMask"]["sha256"])
    bed = np.fromfile(terrain_path, "<i2").reshape(manifest["localHeight"], manifest["localWidth"])
    mask = np.fromfile(mask_path, "u1").reshape(bed.shape)
    bounds = meta["boundsProjectedMeters"]
    assert (bounds["east"] - bounds["west"], bounds["north"] - bounds["south"]) == (102400, 51200)
    assert tuple(bed.shape) == (513, 1025) and (bed.min(), bed.max()) == (-4131, 3644)
    # Mountain west of Hualien, coastal lowland and ocean to the east, north-first rows.
    assert (bed[202, 243], bed[393, 582], bed[233, 933]) == (3367, 10, -1867)
    coarse = coarse_surface(bed, 4)
    np.testing.assert_array_equal(bed[::4, ::4], coarse[::4, ::4])
    difference = (coarse - bed)[(mask == 1) & (bed > 0)]
    rms = np.sqrt(np.mean(difference ** 2))
    assert difference.size == 326632 and abs(rms - 46.35) < 0.01
    print(f"Verified global/local DEMs and game PNG. {difference.size} positive land samples.\n"
          f"400 m vs 100 m: RMS difference {rms:.2f} real m, "
          f"P95 {np.percentile(np.abs(difference), 95):.2f} m. "
          f"Sampled peaks {coarse.max():.0f} / {bed.max()} m.")


if __name__ == "__main__":
    main()
