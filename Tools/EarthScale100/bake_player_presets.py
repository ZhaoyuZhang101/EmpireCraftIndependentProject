"""Bake offline player maps from the shipped ETOPO 2022 overview, or run --check."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Examples/EarthScale100/global"
RESOURCES = ROOT / "Assets/ORBIS/Resources/ORBIS/FlatWorld"
WIDTHS = tuple(map(int, (RESOURCES / "PlayerWidths.txt").read_text().split()))


def weighted_sums(rows, count):
    """Integrate pixel footprints. One source pixel spans 'count' integer ticks."""
    row_count, source_count = rows.shape
    edges, fractions = np.divmod(np.arange(count + 1) * source_count, count)
    prefix = np.zeros((row_count, source_count + 1), dtype=np.int64)
    np.cumsum(rows, axis=1, dtype=np.int64, out=prefix[:, 1:])
    integrals = prefix[:, edges] * count
    integrals[:, :-1] += rows[:, edges[:-1]].astype(np.int64) * fractions[:-1]
    return np.diff(integrals, axis=1)


def bake(source, width):
    height = width // 2
    horizontal = np.empty((source.shape[0], width), dtype=np.int64)
    for start in range(0, source.shape[0], 64):
        block = slice(start, start + 64)
        horizontal[block] = weighted_sums(source[block], width)
    sums = weighted_sums(horizontal.T, height).T
    # The mean of int16 source samples remains in range. Round only once, in metres.
    return np.rint(sums / source.size).astype("<i2")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="verify existing maps without writing")
    args = parser.parse_args()

    meta = json.loads((SOURCE / "metadata.json").read_text(encoding="utf-8"))["overview"]
    raw = (SOURCE / meta["file"]).read_bytes()
    if hashlib.sha256(raw).hexdigest() != meta["sha256"]:
        raise ValueError("The source elevation file differs from its recorded SHA-256.")
    source = np.frombuffer(raw, dtype="<i2").reshape(meta["dimensions"][::-1])
    for width in WIDTHS:
        grid = bake(source, width)
        payload = grid.tobytes(order="C")
        path = RESOURCES / f"Earth{width}.bytes"
        if args.check:
            if path.read_bytes() != payload:
                raise ValueError(f"{path.name} differs from the reference terrain.")
        else:
            path.write_bytes(payload)
        print(f"{path.name}: {grid.shape[1]} x {grid.shape[0]}, sha256 {hashlib.sha256(payload).hexdigest()}")


if __name__ == "__main__":
    main()
