"""Audit the installed worldwide strait resources against the original ocean polygons and V5 grids."""
import argparse
import hashlib
import sys
from pathlib import Path

import numpy as np
import shapely
from shapely.geometry import Polygon

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/"Tools/EarthScale100"))
from bake_global_shore import new_channels
from bake_player_presets import RESOURCES, WIDTHS
from bake_player_water import features, polygons
from bake_straits import GLOBAL_GATES
from global_seaways import Coast
from verify_strait_shore import Atlas, verify_passages, verify_seams

# Released OWR4 resource digests. A saved V5 world requests precisely this
# prefix when rebuilding its authored terrain under the V6 player.
V5_SHA = {
    256: "8e7af046d21a3a49eeabe5121365007bb1951dd964d6e51fab51229bf906c570",
    512: "47c61b4e6a181ff6fa7acb39ddb8a1a5870a70e64ea393ad71e2bdc40aa6ec3f",
    1024: "e90f00678de70f522e34e9bde8f1969f4ce80663a910ae90965231862ee03e53",
    1536: "64f799f8f392863414dba64894c2f64635102e56e82a1a4563ee7f5da56e31af",
    2048: "75dc5fb95934dc785955ab94411b90e2366a391cbb0961a7e68e566a577f768e",
    4096: "bc2f4e22a758cb8da6bec266eca93adef41f595a19d199188f498df372e96c2b",
}


def audit_cells(coast,atlas,width):
    path = RESOURCES/f"EarthWater{width}.bytes"
    raw = path.read_bytes()
    _,cells = new_channels(path)
    legacy = b"OWR4" + raw[4:len(raw)-4-len(cells)*4]
    assert hashlib.sha256(legacy).hexdigest() == V5_SHA[width], f"V5 {width} baseline changed"
    assert len(cells) and np.all(np.diff(cells)>0)
    y,x = np.divmod(cells,width)
    step = 360/width
    west,north = x.astype(float)*step-180,90-y.astype(float)*step
    bucket = ((north+90)//10).astype(int)*36+((west+180)//10).astype(int)
    low_fraction = 1
    for key in np.unique(bucket):
        ids = np.flatnonzero(bucket==key)
        w,n = west[ids],north[ids]
        local = coast.local((w.min(),n.min()-step,w.max()+step,n.max()))
        tiles = shapely.box(w,n-step,w+step,n)
        fractions = shapely.area(shapely.intersection(tiles,local))/step**2
        assert np.min(fractions)>0, (width,int(cells[ids[np.argmin(fractions)]]),"no source ocean")
        low_fraction = min(low_fraction,float(np.min(fractions)))
    tested = 0
    for u,v in ((.5,.5),(.125,.125),(.875,.125),(.125,.875),(.875,.875)):
        lon,lat = west+step*u,north-step*v
        km,weight = atlas.sample(lon,lat)
        assert np.min(weight)>.99, (width,np.min(weight),"incomplete chart coverage")
        # Independent geographic sign beyond the uncertainty of a shore sample.
        reliable = np.abs(km)>.5
        assert np.all((km[reliable]>0)==shapely.contains_xy(coast.ocean,lon[reliable],lat[reliable])), width
        tested += np.count_nonzero(reliable)
    print(f"{width}: V5 SHA matched, {len(cells)} additional cells have surveyed water, "
          f"minimum source water fraction {low_fraction:.6f}, {tested} chart classifications checked")


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache",type=Path,default=ROOT/"ValidationOutput/BasinWater")
    args=parser.parse_args()
    ocean=[p for f in features("ocean",args.cache) for p in polygons(f)]
    shell,*holes=max(ocean,key=lambda p:np.ptp(np.array(p[0])[:,0]))
    coast=Coast(Polygon(shell,holes))
    shapely.prepare(coast.ocean)
    atlas=Atlas(RESOURCES/"EarthGlobalStraitShore.bytes")
    for width in WIDTHS:
        audit_cells(coast,atlas,width)
    print(f"{verify_seams(atlas)} adjacent detail page pairs agree; "
          f"{verify_passages(atlas,GLOBAL_GATES)} additional displayed passages pass a four-neighbour flood")
