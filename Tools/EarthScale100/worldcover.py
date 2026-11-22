"""Open a pinned native ESA WorldCover 2021 land-cover tile from the bake cache."""
from contextlib import contextmanager
import hashlib
from pathlib import Path
import shutil
import tempfile
import urllib.request

import rasterio


def check_digest(path, expected):
    with path.open("rb") as source:
        if hashlib.file_digest(source, "sha256").hexdigest() != expected:
            raise ValueError(f"WorldCover checksum mismatch: {path.name}")


@contextmanager
def open_worldcover(cache, tile, checksum):
    path = cache / f"ESA_WorldCover_10m_2021_v200_{tile}_Map.tif"
    if path.exists():
        check_digest(path, checksum)
    else:
        url = "https://esa-worldcover.s3.eu-central-1.amazonaws.com/v200/2021/map/" + path.name
        with tempfile.NamedTemporaryFile(dir=cache, delete=False) as temp:
            scratch = Path(temp.name)
        try:
            with urllib.request.urlopen(url, timeout=180) as response, scratch.open("wb") as output:
                shutil.copyfileobj(response, output)
            check_digest(scratch, checksum)
            scratch.replace(path)
        finally:
            scratch.unlink(missing_ok=True)
    with rasterio.open(path) as source:
        if source.crs.to_epsg() != 4326 or source.shape != (36000, 36000):
            raise ValueError(f"Unexpected WorldCover grid: {tile}")
        yield source
