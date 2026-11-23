"""Bake the extra marine coast at all corrected Earth-grid cells into the OSH2 atlas."""
import argparse
import hashlib
import struct
import zlib
from collections import defaultdict
from math import ceil, cos, floor, radians
from pathlib import Path

import numpy as np
import shapely
from scipy.spatial import cKDTree
from shapely.geometry import Polygon, box

from bake_major_lakes import unit_vectors
from bake_player_presets import RESOURCES, ROOT, WIDTHS
from bake_player_water import features, polygons
from bake_strait_shore import coordinates, coverage, distances, pack
from bake_straits import GLOBAL_APPROACHES, GLOBAL_GATES
from global_seaways import Coast

COLS, ROWS = 1440, 720
FEATHER = .35


def legacy_pages(path):
    raw = path.read_bytes()
    tag,cw,ch,fw,fh = struct.unpack_from("<4s4i",raw)
    if (tag,cw,fw) != (b"OSH1",2048,4096):
        raise ValueError("Expected the V5 Earth shoreline.")
    values = np.frombuffer(zlib.decompress(raw[20:],-15), np.uint8)
    coarse = values[:2*cw*ch].reshape(ch,cw,2)
    fine = values[2*cw*ch:].reshape(fh,fw)
    directory = coarse.view("<u2").ravel()[:ROWS*COLS]
    pages,detail,middle = {},{},{}
    for key in np.flatnonzero(directory):
        row,col = divmod(int(directory[key])-1,cw//18)
        x,y = col*18,507+row*19
        pages[key] = coarse[y+1:y+19,x:x+18]
        f,m = (int(i) for i in coarse[y,x:x+2].view("<u2").ravel())
        if f:
            row,col = divmod(f-1,fw//130)
            detail[key] = fine[row*130:(row+1)*130,col*130:(col+1)*130]
        elif m:
            row,col = divmod((m-1)//2,cw//66)
            y,x = ch-(row+1)*66,col*66
            middle[key] = coarse[y:y+66,x:x+66,(m-1)%2]
    return pages,detail,middle


def new_channels(path):
    raw = path.read_bytes()
    tag,width,runs = struct.unpack_from("<4sii",raw)
    if tag != b"OWR5":
        raise ValueError(f"Bake global player water before the shoreline atlas ({path.name}).")
    p = 12 + 6*runs
    for stride in (4,6,4):
        p += 4+stride*struct.unpack_from("<i",raw,p)[0]
    count = struct.unpack_from("<i",raw,p)[0]
    return width,np.frombuffer(raw,"<i4",count=count,offset=p+4)


def windows(directory):
    # Larger simulation cells must restore both banks even where their
    # centre lies on land. All fine samples use the original ocean polygon.
    areas = []
    for width in WIDTHS:
        _,cells = new_channels(directory/f"EarthWater{width}.bytes")
        step = 360/width
        for cell in cells:
            y,x = divmod(int(cell),width)
            w,n = x*step-180,90-y*step
            areas.append(((w,n-step,w+step,n),False))
    # The Seto Inland Sea contains bends and island chains between its gates.
    # Chart the entire basin so the player can pick any visible water point.
    areas.append(((130.55,33.15,135.43,34.80),True))
    for _,a,b,pad in GLOBAL_GATES+GLOBAL_APPROACHES:
        areas.append(((min(a[0],b[0])-pad,min(a[1],b[1])-pad,
                       max(a[0],b[0])+pad,max(a[1],b[1])+pad),True))
    pages = defaultdict(list)
    for bounds,high in areas:
        w,s,e,n = bounds
        margin = FEATHER + .02  # Includes both gutters at 16 samples per page.
        for y in range(max(0,floor((s-margin+90)*4)), min(ROWS,ceil((n+margin+90)*4))):
            for x in range(floor((w-margin+180)*4),ceil((e+margin+180)*4)):
                offset = 360*(x//COLS)
                pages[y*COLS+x%COLS].append(((w-offset,s,e-offset,n),high))
    return pages


def bake(directory, cache):
    source = [p for f in features("ocean",cache) for p in polygons(f)]
    shell,*holes = max(source,key=lambda p:np.ptp(np.array(p[0])[:,0]))
    coast = Coast(Polygon(shell,holes))
    regional,detail,middle = legacy_pages(RESOURCES/"EarthStraitShore.bytes")
    new = windows(directory)
    blocks = defaultdict(list)
    for key in sorted(new):
        y,x = divmod(key,COLS)
        blocks[(y//16,x//16)].append(key)
    for (row,col),keys in sorted(blocks.items()):
        w,s = col*4-180,row*4-90
        latitude = min(89.,max(abs(s),abs(s+4)))
        # 63.5 km is the maximum representable regional signed distance.
        margin = min(180.,1.4/max(.015,cos(radians(latitude))))
        sample_bounds = (w-margin,max(-89.999999,s-1.4),w+4+margin,min(89.999999,s+5.4))
        local = coast.local((sample_bounds[0]-.1,max(-90,sample_bounds[1]-.1),
                             sample_bounds[2]+.1,min(90,sample_bounds[3]+.1)))
        lines = local.boundary.intersection(box(*sample_bounds))
        vertices = shapely.get_coordinates(shapely.segmentize(lines,.0008))
        tree = cKDTree(unit_vectors(vertices[:,0],vertices[:,1]))
        shapely.prepare(local)
        for key in keys:
            y,x = divmod(key,COLS)
            lon,lat = coordinates(x,16,180),coordinates(y,16,90)
            rectangles = new[key]
            amount = coverage([bounds for bounds,_ in rectangles],lon,lat,FEATHER)
            if not np.any(amount):
                continue
            if key in regional:
                field,old = np.moveaxis(regional[key],-1,0)
                weight = np.maximum(old,amount)
            else:
                field = distances(local,tree,lon,lat,2)
                weight = amount
            regional[key] = np.stack((field,weight),axis=-1)
            if key in detail:
                continue
            near = np.abs(field.astype(int)-128) < 14
            high = [bounds for bounds,is_high in rectangles if is_high]
            if high and np.any(near & (coverage(high,lon,lat,FEATHER)>=247)):
                detail[key] = distances(local,tree,coordinates(x,128,180),coordinates(y,128,90),16)
                middle.pop(key,None)
            elif key not in middle and np.any(near & (amount>=247)):
                middle[key] = distances(local,tree,coordinates(x,64,180),coordinates(y,64,90),16)
    # Smallest supported dimensions keeping both textures at or below 8192.
    fw = next(w for w in (4096,8192) if ceil(len(detail)/(w//130))*130<=8192)
    cw = next(w for w in (2048,4096,8192) if ceil(COLS*ROWS/w)+19*(
        ceil(len(regional)/(w//18))+ceil(66*ceil(len(middle)/(2*(w//66)))/19))<=8192)
    return pack(regional,detail,middle,cw,fw,global_atlas=True),len(regional),len(detail),len(middle)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--water",type=Path,default=RESOURCES)
    parser.add_argument("--cache",type=Path,default=ROOT/"ValidationOutput/BasinWater")
    parser.add_argument("--output",type=Path,default=RESOURCES/"EarthGlobalStraitShore.bytes")
    parser.add_argument("--check",action="store_true")
    args = parser.parse_args()
    payload,regions,detail,middle = bake(args.water,args.cache)
    if args.check:
        if args.output.read_bytes() != payload:
            raise ValueError("Installed global marine shore differs from its sources.")
    else:
        args.output.write_bytes(payload)
    print(f"{regions} regional, {detail} high resolution and {middle} medium resolution pages, "
          f"{len(payload)} bytes, sha256 {hashlib.sha256(payload).hexdigest()}")


if __name__ == "__main__":
    main()
