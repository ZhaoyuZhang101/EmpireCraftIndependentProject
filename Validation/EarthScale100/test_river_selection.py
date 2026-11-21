"""Geographic regressions against the shipped player river asset. Runs offline."""
import math
import struct
import unittest
from collections import Counter
from pathlib import Path

import numpy as np
from scipy.ndimage import label
import shapely
from shapely import STRtree
from shapely.affinity import scale
from shapely.geometry import LineString, Point, box

from verify_strait_shore import Atlas


ASSET = (Path(__file__).resolve().parents[2] / "Assets/ORBIS/Resources/ORBIS"
         / "FlatWorld/EarthRivers.bytes")


class RiverSelectionTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        data = ASSET.read_bytes()
        magic, count = struct.unpack_from("<4sI", data)
        if magic not in (b"ORV1", b"ORV2"):
            raise ValueError("Invalid river signature")
        fmt, scale = ("<IIH", 16777215) if magic == b"ORV2" else ("<HH", 65535)
        cls.coordinate_scale = scale
        cls.junctions = Counter()
        cls.lines = []
        cls.widths = []
        offset = 8
        for _ in range(count):
            rank, size = struct.unpack_from("<BH", data, offset)
            if rank > 10 or size < 2:
                raise ValueError("Invalid river part")
            start = offset + 3
            offset = start + struct.calcsize(fmt) * size
            stations = list(struct.iter_unpack(fmt, data[start:offset]))
            xy = [(s[0], s[1]) for s in stations]
            if any(max(p) > scale for p in xy):
                raise ValueError("Invalid river coordinate")
            cls.widths.append(np.array([s[2] for s in stations]) if magic == b"ORV2" else None)
            cls.junctions.update(set(xy))
            cls.lines.append(LineString([(x / scale * 360 - 180, y / scale * 180 - 90)
                                         for x, y in xy]))
        if offset != len(data):
            raise ValueError("Invalid river length")
        cls.tree = STRtree(cls.lines)

    def distance_km(self, lon, lat):
        point = Point(lon, lat)
        nearby = self.tree.query(point, predicate="dwithin", distance=.3)
        east = math.cos(math.radians(lat))
        return min((scale(self.lines[i], xfact=east, yfact=1, origin=point).distance(point)
                    * 111.2 for i in nearby), default=math.inf)

    def test_widths_follow_observed_cross_sections(self):
        # GRWL V01.01 raw station positions and widths (200 m medians).
        # A station can lie between two vertices of the simplified asset.
        sites = [
            ("Jingjiang west NH49/1/2559", 112.399534, 29.884233, 1239, 80),
            ("Jingjiang bend NH49/1/4282", 112.645735, 29.743610, 675, 80),
            ("Jingjiang east NH49/1/4879", 112.816990, 29.754091, 834, 80),
            ("Yibin downstream NH48/12/1765", 105.782672, 28.909220, 746, 80),
            ("Huang NJ50/1/602", 119.130103, 37.750552, 318, 80),
            ("Hengmen NF49/31/684", 113.572733, 22.584933, 565, 80),
            ("Rosetta NH36/2/108", 30.390537, 31.441274, 313, 80),
            # First transect at the transition to the fixed tidal outlet.
            ("Damietta NH36/3/0", 31.844416, 31.523682, 243, 180),
        ]
        for name, lon, lat, metres, position_tolerance in sites:
            with self.subTest(name=name):
                part = self.tree.nearest(Point(lon, lat))
                xy = (np.asarray(self.lines[part].coords)-[lon,lat]) * [math.cos(math.radians(lat))*111200,111200]
                span = np.diff(xy, axis=0)
                t = np.clip(-np.sum(xy[:-1]*span, axis=1)/np.maximum(1e-12,np.sum(span*span, axis=1)), 0, 1)
                distance = np.linalg.norm(xy[:-1]+span*t[:,None], axis=1)
                i = distance.argmin()
                self.assertLess(distance[i], position_tolerance)
                widths = self.widths[part]
                actual = (1-t[i])*widths[i]+t[i]*widths[i+1]
                self.assertLess(abs(actual-metres), max(35, metres*.15))
        # These original 30 m stations lie on separate Jingjiang bends where
        # the generalized 1:10m line was over a kilometre from the channel.
        for lon, lat in [(112.667369,29.766890), (112.382576,29.746893)]:
            self.assertLess(self.distance_km(lon, lat), .08)

    def test_surveyed_tidal_currents_face_the_marine_receiver(self):
        for name, lon, lat in [("Hengmen",113.59769,22.59360), ("Modaomen",113.18139,22.54373),
                               ("Rosetta",30.36723,31.46772), ("Damietta",31.84423,31.52712),
                               ("Qiantang",120.20023,30.22262), ("Yangtze",121.85823,31.41490)]:
            with self.subTest(name=name):
                part = self.tree.nearest(Point(lon, lat))
                end = np.asarray(self.lines[part].coords[-1])
                self.assertLess(np.linalg.norm(end-[lon,lat]), .0001)

    def test_principal_rivers_and_lake_links(self):
        # Independent WGS84 vertices from the original Natural Earth 1:10m layer.
        sites = [
            ("Amazonas", -64.596099, -3.387709), ("Nile", 31.157319, 27.218573),
            ("Yangtze", 107.519786, 29.887315), ("Lena", 106.699718, 57.045111),
            ("Yukon", -159.773834, 63.051540), ("Mississippi upstream", -91.403554, 44.007636),
            ("Fort Peck / Missouri", -107.416819, 47.658881), ("Nasser / Nile", 32.421153, 22.672309),
            ("Great Slave / Mackenzie", -115.906606, 61.045925), ("Niagara", -79.052398, 43.147081),
            ("Lake Ladoga / Svir", 32.229015, 60.683051), ("Baikal / Angara", 105.682628, 51.957221),
            ("Angara downstream reservoir", 102.369640, 57.428534), ("Upper Orange", 28.666596, -29.989435),
            ("Upper Tocantins", -48.110911, -8.086358), ("Upper Guaporé", -63.213002, -12.674981),
            ("Salinas / Chixoy", -90.421620, 16.353217), ("lower Usumacinta", -91.482167, 17.779364),
            ("Tonle Sap outlet", 104.844900, 12.164659),
            ("Yangtze estuary near Jiangyin", 120.206, 31.924),
            ("Yangtze south of Chongming", 121.500, 31.483),
        ]
        for name, lon, lat in sites:
            with self.subTest(name=name):
                self.assertLess(self.distance_km(lon, lat), 1.2)

    def test_small_streams_and_distant_namesakes_are_absent(self):
        sites = [
            ("Namekagon", -91.538808, 45.970567), ("Fox, Wisconsin", -88.175201, 44.331814),
            ("Mackenzie, Australia", 148.747813, -23.378920), ("Fraser, Labrador", -63.913645, 56.760850),
            ("Tongariro", 175.836677, -39.049481),
            ("interior of Chongming Island", 121.580, 31.622),
        ]
        for name, lon, lat in sites:
            with self.subTest(name=name):
                self.assertGreater(self.distance_km(lon, lat), 5)

    def test_chinese_and_indian_river_courses(self):
        # Independent vertices on Natural Earth's original centre lines. The
        # Huang mouth comes from the more complete scale-rank edition of 5.1.2.
        routes = {
            "Yellow River at the Bohai": [(118.75869,37.76027), (119.13012,37.74895), (119.23476,37.77714)],
            "Xi / Hengmen tidal outlet": [(113.50978,22.58088), (113.5727,22.5849), (113.5871,22.5917)],
            "Rosetta tidal outlet": [(30.39853,31.44339), (30.37962,31.44795), (30.36723,31.46772)],
            "Han / Wuhan": [(111.4776,32.5678), (112.6909,30.5710), (114.2896,30.5664)],
            "Gan / Lake Poyang": [(114.9182,26.1960), (115.9819,29.2792), (116.1147,29.4925), (116.1975,29.7514)],
            "Jialing / Chongqing": [(106.8816,34.2080), (106.0593,31.5687), (106.5868,29.5856)],
            "Xiang / Lake Dongting": [(110.6170,25.4879), (112.1991,26.4748), (112.9936,29.1475)],
            "Pearl North River": [(112.5288,25.3172), (113.5234,24.6316), (112.8193,23.1529)],
            "Pearl East River": [(114.9519,23.8839), (114.41562,23.10944), (113.78948,23.07986), (113.61777,22.94929)],
            "Qiantang / Hangzhou": [(118.0959,29.6422), (119.7864,29.8729), (120.14894,30.19583)],
            "Krishna / Bay of Bengal": [(79.2334,16.3305), (80.0979,16.8201), (80.8827,16.0137)],
            "Mahanadi delta": [(83.5281,21.7203), (85.2061,20.3819), (86.1587,20.1431)],
            "Narmada / Gulf of Khambhat": [(78.1283,22.8052), (74.28785,21.95002), (73.1318,21.7538)],
        }
        for name, points in routes.items():
            for point in points:
                with self.subTest(name=name, point=point):
                    self.assertLess(self.distance_km(*point), 1.2)

    def test_shared_main_stem_and_delta_junctions_have_no_gap(self):
        # A neighbouring 16-bit sample can be over half a kilometre away. A
        # narrow river needs an actual line intersection to carry water through.
        yangtze_sea = (121.858226, 31.414904)
        routes = [
            ("Upper Han / Yangtze", (108.3125,32.7698), yangtze_sea),
            ("Gan / Poyang / Yangtze", (114.9182,26.196), yangtze_sea),
            ("Xiang / Dongting / Yangtze", (112.1991,26.4748), yangtze_sea),
            ("Jialing / Yangtze", (106.0593,31.5687), yangtze_sea),
            ("Bei / Xi", (113.5234,24.6316), (113.16554,22.57563)),
            ("Middle Huang / Bohai", (112.1514,35.0135), (119.23476,37.77714)),
            ("Ganges / Padma", (87.938,24.806), (90.25,23.462)),
            ("Lower Nile / Rosetta", (31.157319,27.218573), (30.39853,31.44339)),
            ("Lower Nile / Damietta", (31.157319,27.218573), (31.84474,31.52534)),
        ]
        for name, start, end in routes:
            with self.subTest(name=name):
                first, last = self.tree.nearest([Point(start), Point(end)])
                reached, pending = {first}, [first]
                while pending and last not in reached:
                    for part in self.tree.query(self.lines[pending.pop()], predicate="intersects"):
                        if part not in reached:
                            reached.add(part)
                            pending.append(part)
                self.assertIn(last, reached)

    def test_deltas_reach_the_displayed_marine_water(self):
        # Walk contiguous lower channels all the way into an independently
        # located offshore receiver. Water uses the same atlas as the player.
        # Sampling only the final endpoint misses broken intermediate tidal arms.
        atlas = Atlas()
        routes = [
            ("Rosetta", (31.14527,30.17532), (30.38,31.55), [(30.94,30.80)]),
            ("Damietta", (31.14527,30.17532), (31.86,31.57), [(31.52,30.72)]),
            ("Yangtze / Chongming", (120.205719,31.934053), (122.02,31.415), [(121.58,31.622)]),
            ("Yellow", (118.75869,37.76027), (119.29,37.79), [(119.14,37.79)]),
            ("Pearl / Modaomen", (112.950893,22.834932), (113.46,21.95), [(113.38,22.53)]),
            ("Qiantang", (119.784458,29.880265), (121.44,30.41), [(120.42,30.28)]),
            ("Ganges / Meghna", (89.74928,23.84801), (90.62,22.0), [(90.73,22.2)]),
            ("Amazon / Atlantic", (-52.712,-1.584), (-49.75,.6), [(-50.58,-.89)]),
            ("Mississippi", (-89.41356,28.93517), (-89.48,28.88), []),
            ("Mekong / Bassac", (105.83798,10.00499), (106.30,9.47), []),
        ]
        for name, start, sea, banks in routes:
            with self.subTest(name=name):
                # 0.5 km of tolerance accounts for 16-bit coordinate rounding
                # and the flood raster. It cannot bridge a missing river reach.
                step = .003
                bounds = np.vstack([start, sea, *banks])
                lo = np.floor((bounds.min(axis=0)-.19)/step).astype(int)
                hi = np.ceil((bounds.max(axis=0)+.19)/step).astype(int)
                lon, lat = (np.arange(lo[k], hi[k])*step for k in range(2))
                lines = self.tree.query(box(lon[0], lat[0], lon[-1], lat[-1]))
                rivers = shapely.buffer(shapely.union_all([self.lines[i] for i in lines]), .006)
                river_water = shapely.contains_xy(rivers, lon, lat[:, None])
                km, weight = atlas.sample(lon, lat[:, None])
                marine = (weight > .99) & (km > 0)
                def cell(point):
                    return tuple((np.rint(np.array(point)/step).astype(int)-lo)[::-1])
                self.assertTrue(river_water[cell(start)], "Missing mapped upstream reach")
                self.assertTrue(marine[cell(sea)], "Missing mapped marine receiver")
                water, _ = label(river_water | marine, np.ones((3,3)))
                self.assertEqual(water[cell(start)], water[cell(sea)], "Lower watercourse breaks before the open sea")
                for bank in banks:
                    self.assertEqual(water[cell(bank)], 0, f"Dry island or bank flooded at {bank}")

    def test_simplification_preserves_surveyed_junctions(self):
        for name, lon, lat in [
            ("Tocantins / Araguaia", -48.723134, -5.355401),
            ("Nile delta branches", 31.145274, 30.175320),
            ("Saskatchewan / Nelson", -97.913645, 53.518622),
            ("Lake Baikal outlet", 104.866954, 51.870185),
            ("Svir inflow to Lake Ladoga", 32.789480, 60.471137),
        ]:
            xy = round((lon + 180) / 360 * self.coordinate_scale), round((lat + 90) / 180 * self.coordinate_scale)
            with self.subTest(name=name):
                self.assertGreaterEqual(self.junctions[xy], 2)

    def test_courses_connect_across_rank_changes_and_lakes(self):
        routes = [
            ("Baikal to Yenisei mouth", (106.497325, 52.383124), (83.176768, 70.083075)),
            ("upper to lower Mississippi", (-94.637074, 47.440009), (-89.413564, 28.935167)),
            ("Great Slave Lake to Mackenzie delta", (-113.703603, 61.246039), (-135.183013, 69.263129)),
            ("upper to lower Tocantins", (-49.178700, -15.925958), (-49.670400, -2.676446)),
            ("Lake Tonle Sap to Mekong delta", (103.687429, 13.224677), (105.837983, 10.004991)),
            ("Yangtze past Zhenjiang to the estuary", (119.606375, 32.196885), (121.858226, 31.414904)),
        ]
        for name, start, end in routes:
            with self.subTest(name=name):
                self.assertLess(self.distance_km(*start), 1.2)
                self.assertLess(self.distance_km(*end), 1.2)
                first, last = self.tree.nearest([Point(start), Point(end)])
                reached, pending = {first}, [first]
                while pending and last not in reached:
                    line = self.lines[pending.pop()]
                    # Separate source parts can fall on adjacent quantization
                    # points. This geographic tolerance is under 1 km.
                    for part in self.tree.query(line, predicate="dwithin", distance=.008):
                        if part not in reached:
                            reached.add(part)
                            pending.append(part)
                self.assertIn(last, reached)


if __name__ == "__main__":
    unittest.main()
