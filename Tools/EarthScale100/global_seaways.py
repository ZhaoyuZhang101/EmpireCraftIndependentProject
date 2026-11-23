"""Find island passages lost by geographic aggregation on each player grid."""
from collections import defaultdict
from functools import lru_cache
from math import floor

import numpy as np
import shapely
from rasterio.features import rasterize
from rasterio.transform import from_bounds
from scipy.ndimage import binary_dilation, distance_transform_edt, find_objects, label
from shapely.affinity import translate
from shapely.geometry import LineString, Point, box

from bake_straits import polygon_parts, water_path


class Coast:
    def __init__(self, ocean):
        self.ocean = ocean
        land = list(polygon_parts(box(-180,-90,180,90).difference(ocean)))
        # An island or continent cut by the longitude seam is still one bank.
        identities = list(range(len(land) + 1))
        dateline = LineString([(-180,-90), (-180,90)])
        west = [(i, p.intersection(dateline)) for i,p in enumerate(land,1) if p.bounds[0] == -180]
        for east_id, polygon in enumerate(land,1):
            if polygon.bounds[2] != 180:
                continue
            edge = translate(polygon, xoff=-360).intersection(dateline)
            for west_id, other in west:
                if edge.intersection(other).length > 1e-8:
                    a, b = identities[east_id], identities[west_id]
                    if a != b:
                        identities = [a if n == b else n for n in identities]
        self.land = [(polygon, identities[i]) for i,polygon in enumerate(land,1)]

    @lru_cache(maxsize=256)
    def block(self, west, south, east, north):
        parts = []
        for offset in (-360, 0, 360):
            bounds = (max(-180,west-offset), max(-90,south), min(180,east-offset), min(90,north))
            if bounds[0] >= bounds[2] or bounds[1] >= bounds[3]:
                continue
            part = shapely.clip_by_rect(self.ocean, *bounds)
            if not part.is_valid:
                part = shapely.make_valid(part)
            parts.append(translate(part, xoff=offset) if offset else part)
        return shapely.union_all(parts)

    def local(self, bounds):
        w,s,e,n = bounds
        # Reuse a small geographic crop rather than intersecting every possible
        # strait with all half a million vertices of the world shoreline.
        parent = self.block(10*floor(w/10), 10*floor(s/10),
                            10*(floor(e/10)+1), 10*(floor(n/10)+1))
        return parent.intersection(box(*bounds))

    def island_contacts(self, wet):
        height,width = wet.shape
        land = rasterize(self.land, out_shape=wet.shape, dtype="int32",
                         transform=from_bounds(-180,-90,180,90,width,height))
        # Assign the centres which fall just offshore to their nearest source
        # bank. Only a contact between two *different* represented landmasses
        # proposes a search. No distance transform can authorize a channel.
        nearest = distance_transform_edt(land == 0, return_distances=False, return_indices=True)
        banks = land[tuple(nearest)]
        banks[wet] = 0
        contact = np.zeros_like(wet)
        for dy,dx in ((0,1),(1,0),(1,1),(1,-1)):
            other = np.roll(banks, (dy,dx), axis=(0,1))
            adjacent = (banks != 0) & (other != 0) & (banks != other)
            if dy:
                adjacent[0] = False
            contact |= adjacent | np.roll(adjacent, (-dy,-dx), axis=(0,1))
        return contact


def windows(contact):
    h,w = contact.shape
    y,x = np.nonzero(contact)
    stride = 4
    areas = set()
    for dy,dx in ((0,0),(2,2)):
        starts = ((y-dy)//stride) * (w//stride+1) + ((x-dx) % w)//stride
        for key in np.unique(starts):
            row,col = divmod(int(key), w//stride+1)
            left,top = col*stride+dx-2, row*stride+dy-2
            areas.add((left,max(0,top),left+8,min(h,top+8)))
    # Long, contiguous contacts can hide the entire throat, including all wet
    # seeds in an eight-cell window. Also try the whole local interface.
    groups, _ = label(contact, structure=np.ones((3,3)))
    for group in find_objects(groups):
        ys,xs = group
        if max(ys.stop-ys.start, xs.stop-xs.start) <= 4:
            continue
        area = (xs.start-3, max(0,ys.start-3), xs.stop+3, min(h,ys.stop+3))
        if (area[2]-area[0])*(area[3]-area[1]) <= 600:
            areas.add(area)
    return sorted(areas, key=lambda a: ((a[2]-a[0])*(a[3]-a[1]),a[1],a[0]))


def automatic_channels(coast, wet, allowed):
    """Keep the least-edited path through shared *wet* faces of a source polygon.

    Each mouth must already be a simulated marine component reaching the edge
    of the search window. Two arms of the same fjord or opposite sides of a
    mapped isthmus cannot join through disconnected polygon fragments.
    """
    height,width = wet.shape
    step = 360 / width
    contact = coast.island_contacts(wet)
    # Both sides of some passages belong to one continental landmass (e.g.
    # the Gulf of Corinth). A surveyed sea arm stranded from the open ocean
    # is independent evidence of a potential blockage. Ignore map seams.
    ocean_ids,_ = label(wet)
    links = defaultdict(set)
    for left,right in ((ocean_ids[:,0],ocean_ids[:,-1]),
                       (ocean_ids[0,:width//2],ocean_ids[0,width//2:]),
                       (ocean_ids[-1,:width//2],ocean_ids[-1,width//2:])):
        for a,b in np.unique(np.stack((left,right),axis=-1),axis=0):
            if a and b and a != b:
                links[a].add(b)
                links[b].add(a)
    atlantic = ocean_ids[int((90+25)/step),int((180-25)/step)]
    offshore = {atlantic}
    pending = [atlantic]
    while pending:
        for other in links[pending.pop()]-offshore:
            offshore.add(other)
            pending.append(other)
    stranded = wet & ~np.isin(ocean_ids,list(offshore))
    contact |= stranded
    # Pad longitude periodically for coastlines crossing the date line.
    def coastal_band(mask, radius):
        return binary_dilation(np.pad(mask,((0,0),(radius,radius)),mode="wrap"),iterations=radius)[:,radius:-radius]
    near = coastal_band(contact,2) | coastal_band(stranded,5)
    eligible = (near & allowed) | wet
    changed = set()
    for x0,y0,x1,y1 in windows(contact):
        cols = np.arange(x0,x1) % width
        window = wet[y0:y1,cols]
        components,count = label(window)
        if count < 2:
            continue
        edges = np.r_[components[0], components[-1], components[:,0], components[:,-1]]
        mouths = np.unique(edges[edges != 0])
        if len(mouths) < 2:
            continue
        rows, columns = np.nonzero(np.isin(components,mouths))
        # Use a measured seawater centre for each mouth. At a coastline, a
        # simulated wet cell may have its centre on the other side of a bank.
        longitude = (x0+columns+.5)*step-180
        latitude = 90-(y0+rows+.5)*step
        bounds = (x0*step-180,90-y1*step,x1*step-180,90-y0*step)
        for polygon in polygon_parts(coast.local(bounds)):
            inside = shapely.contains_xy(polygon, longitude, latitude)
            ids = components[rows[inside],columns[inside]]
            choices = np.flatnonzero(inside)
            if not len(choices) or np.min(ids) == np.max(ids):
                continue
            anchors = []
            for m in np.unique(ids):
                available = choices[ids == m]
                radius = (rows[available]-(y1-y0-1)/2)**2+(columns[available]-(x1-x0-1)/2)**2
                best = available[np.argmin(radius)]
                anchors.append(Point(longitude[best],latitude[best]))
            for a,b in zip(anchors,anchors[1:]):
                try:
                    path = water_path(("Island passage",a,b,polygon),width,wet,eligible)
                except ValueError:
                    continue  # No route along this contact without crossing protected cells.
                for x,y in path:
                    x %= width
                    if not wet[y,x]:
                        wet[y,x] = True
                        changed.add(y*width+x)
    return sorted(changed)
