"""Register local Natural Earth courses to continuous GRWL transect centres."""
import numpy as np
import shapely

from river_widths import KM_PER_DEGREE, chainage


def interpolate(axis, coordinates, distances):
    return np.column_stack([np.interp(distances, axis, component) for component in coordinates.T])


class RiverCenterlines:
    def __init__(self, transects):
        self.sections = transects
        counts = np.asarray([r[3] for r in transects.run_ids])
        self.offset = np.r_[0, np.cumsum(counts)]
        self.owner = np.repeat(np.arange(len(counts)), counts)
        self.distance = np.empty(len(transects.a))
        self.runs = []
        for first, end in zip(self.offset[:-1], self.offset[1:]):
            points = np.vstack((transects.a[first:end], transects.b[end-1]))
            along = chainage(points)
            self.runs.append((points, along))
            self.distance[first:end] = along[:-1]
        self.accepted = 0
        self.kilometres = 0.
        # Pin both survey identities. The geographic key is a Natural Earth
        # part endpoint, followed by the local window in which the named GRWL
        # reach is the same channel. This matters at abandoned Yangtze bends.
        self.registered = {
            ("NH49", 1): ((103.85069, 28.66804), (112.27, 29.60, 112.78, 29.95)),
            ("NH48", 12): ((103.85069, 28.66804), (105.43, 28.67, 106.06, 29.14)),
            ("NH36", 2): ((30.36723, 31.46772), (30.35, 30.35, 31.08, 31.34)),
            ("NH36", 3): ((31.84423, 31.52712), (31.03, 30.33, 31.69, 31.32)),
            ("NJ50", 1): ((119.23476, 37.77714), (117.80, 37.24, 119.10, 37.84)),
        }

    def _anchors(self, points, along, stations):
        """Ambiguous arms and hairpin limbs have no anchor, even at similar width."""
        index = self.sections
        coords = interpolate(along, points, stations)
        east = np.cos(np.deg2rad(coords[:, 1]))
        reach = 1.25 / KM_PER_DEGREE
        x, y = coords.T
        search = shapely.box(x-reach/east, y-reach, x+reach/east, y+reach)
        site, seg = index.tree.query(search)
        ids = np.full(len(stations), -1)
        where = np.zeros(len(stations))
        signs = np.zeros(len(stations))
        ambiguous = np.zeros(len(stations), dtype=bool)
        if not len(seg):
            return ids, where, signs, ambiguous
        scale = np.column_stack((east[site], np.ones(len(site)))) * KM_PER_DEGREE
        direction = (index.b[seg]-index.a[seg])*scale
        length = np.linalg.norm(direction, axis=1)
        direction /= np.maximum(length, 1e-12)[:, None]
        delta = (coords[site]-index.a[seg])*scale
        projection = np.sum(delta*direction, axis=1)
        t = np.clip(projection, 0, length)
        separation = np.linalg.norm(delta-t[:, None]*direction, axis=1)
        tangent = interpolate(along, points, stations+.4)-interpolate(along, points, stations-.4)
        tangent[:, 0] *= east
        tangent /= np.maximum(np.linalg.norm(tangent, axis=1), 1e-12)[:, None]
        dot = np.sum(tangent[site]*direction, axis=1)
        width = index.widths[seg]*.001
        good = (length > 1e-7) & (abs(dot) > .67) & (separation < np.clip(.16+width, .28, 1.25))
        good &= (projection >= -.025) & (projection <= length+.025)
        run = self.owner[seg]
        good &= ~(((seg == self.offset[run]) & (projection < 0)) |
                  ((seg+1 == self.offset[run+1]) & (projection > length)))
        site, seg = site[good], seg[good]
        if not len(seg):
            return ids, where, signs, ambiguous
        cost = separation[good]+.36*(1-abs(dot[good]))
        position = self.distance[seg]+t[good]
        order = np.lexsort((seg, cost, site))
        best = order[np.r_[True, np.diff(site[order]) != 0]]
        picked = site[best]
        ids[picked] = self.owner[seg[best]]
        where[picked] = position[best]
        signs[picked] = np.sign(dot[good][best])
        best_cost = np.full(len(stations), np.inf)
        best_cost[picked] = cost[best]
        competing = (self.owner[seg] != ids[site]) | (abs(position-where[site]) > 2.5)
        ambiguous[site[competing & (cost < best_cost[site]+np.clip(width[good]*.22, .10, .28))]] = True
        ids[ambiguous] = -1
        return ids, where, signs, ambiguous

    def _observed_arc(self, points, along, positions, run, measured):
        observed, survey = self.runs[run]
        sign = np.sign(measured[-1]-measured[0])
        interior = survey[(survey > min(measured[0], measured[-1])) &
                          (survey < max(measured[0], measured[-1]))]
        stations = np.r_[measured[0], interior if sign > 0 else interior[::-1], measured[-1]]
        ground = interpolate(survey, observed, stations)
        source_range = (along > positions[0]) & (along < positions[-1])
        metric = KM_PER_DEGREE*np.array([np.cos(np.deg2rad(ground[:, 1].mean())), 1.])
        measured_arc = shapely.LineString(ground*metric)
        prior = shapely.points(points[source_range]*metric)
        # Some earlier width stations came directly from the Landsat chart.
        # Retain their along-river sampling position on the observed centre
        # when the former position was within one native 30 m pixel of it.
        already_surveyed = prior[shapely.distance(prior, measured_arc) <= .03]
        pinned = np.array([], dtype=int)
        if len(already_surveyed):
            local = np.r_[0., np.cumsum(np.linalg.norm(np.diff(ground*metric, axis=0), axis=1))]
            preserve = np.interp(shapely.line_locate_point(measured_arc, already_surveyed), local, stations)
            stations = sign*np.unique(sign*np.r_[stations, preserve])
            pinned = np.searchsorted(sign*stations, sign*preserve)
            ground = interpolate(survey, observed, stations)
        # Interpolate between paired anchors in the same monotone measure.
        on_source = np.interp(sign*stations, sign*measured, positions)
        original = interpolate(along, points, on_source)
        old = shapely.LineString(np.vstack((original[0], points[source_range], original[-1]))*metric)
        widths = self.sections.widths[self.offset[run]:self.offset[run+1]]
        section = np.clip(np.searchsorted(survey, stations)-1, 0, len(widths)-1)
        distance = shapely.distance(shapely.points(ground*metric), old)
        if np.any(distance > np.clip(1.0+4.0*widths[section]/1000, 1.5, 6.0)):
            return None
        tolerance = np.clip(np.median(widths[section])*.00009, .035, .095)
        if np.max(distance) < tolerance:
            return None
        # Fade the measured displacement in/out along the original course.
        # Both coordinates and the approach direction agree at either tie-in.
        taper = min(1.4, (positions[-1]-positions[0])*.24)
        blend = np.clip(np.minimum(on_source-positions[0], positions[-1]-on_source)/taper, 0, 1)
        blend = blend*blend*(3-2*blend)
        ground = original+blend[:, None]*(ground-original)
        # Bound the spacing for downstream width sampling on straight reaches.
        breaks = np.searchsorted(abs(stations-stations[0]), np.arange(4., abs(stations[-1]-stations[0]), 4.))
        cuts = np.unique(np.r_[0, breaks, pinned, len(ground)-1])
        result = [ground[0]]
        for a, b in zip(cuts, cuts[1:]):
            line = shapely.LineString(ground[a:b+1]*metric)
            result.extend(shapely.get_coordinates(shapely.simplify(line, tolerance, preserve_topology=False))[1:]/metric)
        result[-1] = original[-1]
        return np.asarray(result)

    def _registered_anchors(self, points, along, stations, ids, where, signs, windows):
        """Find a surveyed single channel continuously between two anchor clusters."""
        found = []
        coords = interpolate(along, points, stations)
        for run in np.unique(ids[ids >= 0]):
            window = windows.get(self.sections.run_ids[run][:2])
            if window is None:
                continue
            west, south, east, north = window
            inside = ((coords[:, 0] >= west) & (coords[:, 0] <= east) &
                      (coords[:, 1] >= south) & (coords[:, 1] <= north))
            inside = np.flatnonzero(inside)
            for span in np.split(inside, np.flatnonzero(np.diff(inside) != 1)+1):
                if len(span)<8:
                    continue
                exact = span[ids[span] == run]
                for direction in (-1, 1):
                    matched = exact[signs[exact] == direction]
                    breaks = (np.diff(matched) > 3) | (np.diff(where[matched])*direction <= .02)
                    clusters = [c for c in np.split(matched, np.flatnonzero(breaks)+1) if len(c)>=3]
                    if len(clusters)<2:
                        continue
                    group = clusters[0].tolist()
                    for cluster in clusters[1:]:
                        a, b = group[-1], cluster[0]
                        ds = stations[b]-stations[a]
                        dt = direction*(where[b]-where[a])
                        if ds>50 or dt<=.22 or dt>ds*2.2+1:
                            if len(group)>=6:
                                found.append(np.asarray(group))
                            group = []
                        group.extend(cluster)
                    if len(group)>=6:
                        found.append(np.asarray(group))
        return sorted(found, key=lambda group: -len(group))

    def _span(self, points, windows):
        along = chainage(points)
        if along[-1] < 4:
            return points
        stations = np.arange(.65, along[-1]-.65, .6)
        ids, where, signs, ambiguous = self._anchors(points, along, stations)
        valid = np.flatnonzero(ids >= 0)
        if len(valid) < 3:
            return points
        a, b = valid[:-1], valid[1:]
        old_step = stations[b]-stations[a]
        observed_step = (where[b]-where[a])*signs[a]
        uncertain = np.cumsum(ambiguous)[b] != np.cumsum(ambiguous)[a]
        separate = ((ids[a] != ids[b]) | (signs[a] != signs[b]) | uncertain |
                    (old_step > 14) | (observed_step < np.maximum(.02, old_step*.45-.2)) |
                    (observed_step > old_step*1.9+.35))
        replacements = []
        for anchors in self._registered_anchors(points, along, stations, ids, where, signs, windows):
            pos = stations[anchors]
            arc = self._observed_arc(points, along, pos, ids[anchors[0]], where[anchors])
            if arc is not None and not any(start < pos[-1] and end > pos[0] for start, end, _ in replacements):
                replacements.append((pos[0], pos[-1], arc))
        for group in np.split(valid, np.flatnonzero(separate)+1):
            for start, end, _ in replacements:
                group = group[(stations[group]<start) | (stations[group]>end)]
            if len(group)<3 or stations[group[-1]]-stations[group[0]] < 1.8:
                continue
            # A long span may contain one unregistered bend. Restrict rejection
            # to that gap and retain its independently supported neighbours.
            pending = [group]
            while pending:
                anchors = pending.pop()
                pos = stations[anchors]
                if len(anchors)<3 or pos[-1]-pos[0]<1.8:
                    continue
                interfering = next(((start, end) for start, end, _ in replacements
                                    if start < pos[-1] and end > pos[0]), None)
                if interfering:
                    pending.extend((anchors[pos>interfering[1]], anchors[pos<interfering[0]]))
                    continue
                arc = self._observed_arc(points, along, pos, ids[anchors[0]], where[anchors])
                if arc is None:
                    gaps = np.diff(pos)
                    cut = int(np.argmax(gaps))+1
                    if gaps[cut-1]>1.3:
                        pending.extend((anchors[cut:], anchors[:cut]))
                    continue
                replacements.append((pos[0], pos[-1], arc))
        if not replacements:
            return points
        pieces, cursor = [], 0
        for start, end, arc in sorted(replacements, key=lambda item: item[0]):
            first = np.searchsorted(along, start)
            pieces.extend((points[cursor:first], arc))
            cursor = np.searchsorted(along, end, side="right")
            self.accepted += 1
            self.kilometres += end-start
        pieces.append(points[cursor:])
        return np.concatenate(pieces)

    def refine(self, points, protected=None):
        """Accept ordered WGS84 vertices and a mask of frozen junction vertices."""
        points = np.asarray(points, dtype=float)
        if len(points)<2 or np.any(abs(np.diff(points[:, 0])) > 180):
            return points
        bounds = shapely.box(*(points.min(axis=0)-.04), *(points.max(axis=0)+.04))
        if not len(self.sections.tree.query(bounds)):
            return points
        cuts = np.array([0, len(points)-1]) if protected is None else np.unique(
            np.r_[0, np.flatnonzero(protected), len(points)-1])
        ends = points[[0, -1]]
        windows = {key: window for key, (anchor, window) in self.registered.items()
                   if np.min(np.linalg.norm(ends-anchor, axis=1)) < .001}
        spans = [self._span(points[a:b+1], windows) for a, b in zip(cuts, cuts[1:])]
        return np.vstack([spans[0]]+[span[1:] for span in spans[1:]])
