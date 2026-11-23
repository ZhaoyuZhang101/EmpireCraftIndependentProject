"""Check moving glyphs in three native, identically framed circulation captures.

Pass a frame, a later frame (same map/date/camera), and a frame with only the
circulation readiness flag off. Static shorelines and atmospheric fronts do
not count as moving marks. Run at native resolution, before GIF/video encoding.
"""
import argparse

import cv2
import numpy as np
from PIL import Image


def check(first, later, disabled):
    images = [np.asarray(Image.open(path).convert("RGB"), dtype=np.int16)
              for path in (first, later, disabled)]
    if len({image.shape for image in images}) != 1:
        raise ValueError("Camera captures must have the same dimensions")
    first, later, disabled = images
    ink = (first - disabled).mean(axis=2) > 30
    moving = np.abs(first - later).max(axis=2) > 5
    _, labels, stats, _ = cv2.connectedComponentsWithStats(ink.astype(np.uint8), connectivity=8)
    ids = np.unique(labels[moving])
    areas = stats[ids[ids != 0], cv2.CC_STAT_AREA]
    if len(areas) < 12:
        raise AssertionError(f"Only {len(areas)} moving marks in view")
    flecks = np.mean(areas <= 3)
    resolved = np.mean(areas >= 14)
    print(f"{len(areas)} moving marks, {flecks:.1%} at most 3 pixels, {resolved:.1%} at least 14 pixels")
    assert flecks < .35 and resolved > .50, "Moving ink fragments before the shaft and head can resolve"


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("first", "later", "disabled"):
        parser.add_argument(name)
    check(**vars(parser.parse_args()))
