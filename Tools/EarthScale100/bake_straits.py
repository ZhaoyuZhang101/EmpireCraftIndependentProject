"""Preserve local ocean connectivity when aggregating the Natural Earth 1:10m coast."""
from heapq import heappop, heappush
from math import cos, floor, radians

from shapely.geometry import Point, box


# WGS84 water points on opposite sides, plus a search margin in degrees. The
# points select a passage; its actual course and both banks come from the ocean
# polygon. Short approach legs join the Danish and Malacca routes offshore.
GATES = (
    ("Bosporus", (28.40, 40.81), (29.48, 41.64), .20),
    ("Dardanelles", (25.65, 40.00), (28.40, 40.81), .22),
    ("Kerch", (36.50, 44.85), (36.70, 45.80), .27),
    ("Gibraltar", (-6.18, 35.93), (-4.80, 35.95), .28),
    ("Dover", (-.03, 50.62), (2.00, 51.40), .26),
    ("Messina", (15.455, 38.56), (15.65, 37.77), .24),
    ("Oresund", (12.45, 56.35), (12.88, 55.44), .24),
    ("Great Belt", (10.85, 55.91), (10.99, 54.71), .30),
    ("Qiongzhou", (109.225, 20.06), (111.10, 20.15), .26),
    ("Taiwan", (119.15, 23.37), (120.80, 25.35), .40),
    ("Tsugaru", (139.83, 41.25), (141.60, 41.54), .28),
    ("Bab el Mandeb", (42.88, 12.93), (43.76, 12.37), .27),
    ("Hormuz", (55.70, 26.69), (56.99, 26.02), .34),
    ("Singapore", (103.37, 1.17), (104.55, 1.38), .23),
    ("Sunda", (105.13, -6.52), (106.22, -5.58), .31),
    ("Lombok", (115.70, -8.02), (115.73, -9.06), .28),
    ("Cook", (174.53, -40.84), (174.71, -41.82), .44),
    ("Tiran", (34.63, 28.31), (34.36, 27.67), .20),
)

APPROACHES = (
    ("Belt to Kattegat", (10.85, 55.91), (11.32, 56.55), .30),
    ("Kattegat to Skagerrak", (11.32, 56.55), (10.40, 57.95), .55),
    ("Skagerrak to North Sea", (10.40, 57.95), (7.20, 57.60), .45),
    ("Fehmarn Belt", (10.99, 54.71), (12.36, 54.53), .30),
    ("Baltic approach", (12.36, 54.53), (14.10, 55.25), .30),
    ("Sound to Baltic", (12.88, 55.44), (14.10, 55.25), .25),
    ("Malacca", (100.75, 3.00), (103.37, 1.17), .32),
    ("Singapore eastern approach", (104.55, 1.38), (104.77, 1.72), .15),
    ("Red Sea approach", (42.18, 14.08), (42.88, 12.93), .32),
    ("Persian Gulf approach", (54.82, 26.36), (55.70, 26.69), .30),
    ("Gulf of Oman approach", (56.99, 26.02), (57.09, 25.29), .25),
)

# Regression mouths supplement the worldwide island contact search. In enclosed
# seas its shoreline may run for several grid cells before an open-water seed
# is available. All positions below must share a *single* clipped ocean polygon.
GLOBAL_GATES = (
    ("Kanmon", (130.77,33.96), (131.16,33.92), .16),
    ("Bungo", (131.97,32.97), (132.02,33.52), .17),
    ("Kurushima", (132.82,34.061), (133.09,34.053), .12),
    ("Bisan Seto", (133.50,34.36), (134.18,34.38), .14),
    ("Akashi", (134.86,34.59), (135.13,34.63), .12),
    ("Naruto", (134.60,34.32), (134.70,34.18), .09),
    ("Kitan", (134.99,34.39), (134.99,34.20), .12),
    ("Geiyo", (132.69,34.13), (133.01,34.31), .11),
    ("Little Belt", (9.73,55.55), (9.72,55.36), .11),
    ("Nevelskoy", (141.57,52.52), (141.64,51.98), .18),
    ("Belle Isle", (-56.79,51.51), (-56.28,51.77), .19),
    ("Magellan", (-70.95,-53.45), (-71.42,-53.86), .18),
    ("Pemba", (39.48,-5.40), (39.51,-4.91), .22),
    ("Johnstone", (-126.57,50.51), (-126.02,50.44), .14),
    ("San Bernardino", (124.11,12.63), (124.37,12.56), .17),
    ("Torres", (141.90,-10.52), (142.60,-10.49), .25),
    ("Foveaux", (167.72,-46.65), (168.79,-46.70), .25),
    ("Chacao", (-73.99,-41.84), (-73.43,-41.81), .11),
)

# Local exits connect to open sea rather than ending in isolated wet cells
# in the 256 and 512 Earth presets. The large Seto basin is split at bends.
GLOBAL_APPROACHES = (
    ("Little Belt northern approach", (9.73,55.55), (10.12,55.68), .18),
    ("Little Belt to Kattegat", (10.12,55.68), (10.85,55.91), .16),
    ("Hibiki-nada", (130.58,34.16), (130.77,33.96), .15),
    ("Suo-nada", (131.16,33.92), (132.02,33.52), .20),
    ("Iyo-nada", (132.02,33.52), (132.82,34.061), .22),
    ("Hiuchi-nada", (133.09,34.053), (133.50,34.36), .18),
    ("Harima-nada", (134.18,34.38), (134.86,34.59), .18),
    ("Harima to Naruto", (134.18,34.38), (134.60,34.32), .14),
    ("Osaka Bay", (135.13,34.63), (134.99,34.39), .18),
    ("Kii Channel", (134.70,34.18), (134.88,33.75), .18),
    ("Kitan to Kii", (134.99,34.20), (134.88,33.75), .16),
    ("Bungo Pacific approach", (131.97,32.97), (132.37,32.59), .20),
)

def polygon_parts(geometry):
    if geometry.geom_type == "Polygon":
        if not geometry.is_empty:
            yield geometry
    elif hasattr(geometry, "geoms"):
        for part in geometry.geoms:
            yield from polygon_parts(part)


def passages(ocean, gates=GATES + APPROACHES):
    for name, start, end, pad in gates:
        a, b = Point(start), Point(end)
        bounds = (min(a.x,b.x)-pad, min(a.y,b.y)-pad, max(a.x,b.x)+pad, max(a.y,b.y)+pad)
        clipped = ocean.intersection(box(*bounds))
        channel = next((p for p in polygon_parts(clipped) if p.contains(a) and p.contains(b)), None)
        if channel is None:
            raise ValueError(f"{name}: no continuous surveyed ocean between the two mouths")
        yield name, a, b, channel


def water_path(region, width, wet, eligible):
    """Least altered four-face path; a cell can contain several disjoint sea arms."""
    name, a, b, channel = region
    step = 360 / width
    def tile(x, y):
        return floor((x+180)/step), floor((90-y)/step)
    west, south, east, north = channel.bounds
    x0, y0 = tile(west, north)
    x1, y1 = tile(east, south)
    nodes, shapes, cells = {}, [], []
    for y in range(max(0,y0), min(width//2-1,y1)+1):
        top = 90-y*step
        for x in range(x0, x1+1):
            if not eligible[y,x % width]:
                continue
            left = x*step-180
            for part in polygon_parts(channel.intersection(box(left, top-step, left+step, top))):
                nodes.setdefault((x,y), []).append(len(shapes))
                shapes.append(part)
                cells.append((x,y))

    def mouth(point):
        for node in nodes.get(tile(point.x,point.y), ()):
            if shapes[node].covers(point):
                return node
        raise ValueError(f"{name}: {point} has no eligible ocean fragment at width {width}")

    first, last = mouth(a), mouth(b)
    costs = {first: (0, 0.)}
    previous = {}
    queue = [(0, 0., first)]
    while queue:
        edits, distance, node = heappop(queue)
        if node == last:
            path = [cells[node]]
            while node != first:
                node = previous[node]
                path.append(cells[node])
            return path
        if costs[node] != (edits, distance):
            continue
        x, y = cells[node]
        for col, row in ((x-1,y),(x+1,y),(x,y-1),(x,y+1)):
            for other in nodes.get((col,row), ()):
                # Cost ties favour a short route through the wider water cells.
                length = cos(radians(90-(y+.5)*step)) if y == row else 1.
                cost = edits+int(not wet[row,col % width]), distance+length*(2-shapes[other].area/step**2)
                if other in costs and costs[other] <= cost:
                    continue
                if shapes[node].intersection(shapes[other]).length <= 1e-10:
                    continue  # No shared wet face (corner contact is insufficient).
                costs[other] = cost
                previous[other] = node
                heappush(queue, (*cost, other))
    raise ValueError(f"{name}: no surveyed four-face route at width {width}")


def representative_channels(regions, bed, survey, marine, passage):
    width = bed.shape[1]
    wet = (bed <= 0) & (survey == marine)
    eligible = (survey == marine) | (survey == passage)
    changed = []
    for region in regions:
        for x, y in water_path(region, width, wet, eligible):
            if not wet[y,x]:
                changed.append(y*width+x)
                wet[y,x] = True
    return sorted(changed)
