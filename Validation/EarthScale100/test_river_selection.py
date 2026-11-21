"""Geographic regressions against the shipped ORV1 player asset. Runs offline."""
import math
import struct
import unittest
from collections import Counter
from pathlib import Path

from shapely import STRtree
from shapely.affinity import scale
from shapely.geometry import LineString, Point


ASSET = (Path(__file__).resolve().parents[2] / "Assets/ORBIS/Resources/ORBIS"
         / "FlatWorld/EarthRivers.bytes")


class RiverSelectionTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        data = ASSET.read_bytes()
        magic, count = struct.unpack_from("<4sI", data)
        if magic != b"ORV1":
            raise ValueError("Invalid river signature")
        cls.junctions = Counter()
        cls.lines = []
        offset = 8
        for _ in range(count):
            rank, size = struct.unpack_from("<BH", data, offset)
            if rank > 10 or size < 2:
                raise ValueError("Invalid river part")
            start = offset + 3
            offset = start + 4 * size
            xy = list(struct.iter_unpack("<HH", data[start:offset]))
            cls.junctions.update(set(xy))
            cls.lines.append(LineString([(x / 65535 * 360 - 180, y / 65535 * 180 - 90)
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

    def test_simplification_preserves_surveyed_junctions(self):
        for name, lon, lat in [
            ("Tocantins / Araguaia", -48.723134, -5.355401),
            ("Nile delta branches", 31.145274, 30.175320),
            ("Saskatchewan / Nelson", -97.913645, 53.518622),
            ("Lake Baikal outlet", 104.866954, 51.870185),
            ("Svir inflow to Lake Ladoga", 32.789480, 60.471137),
        ]:
            xy = round((lon + 180) / 360 * 65535), round((lat + 90) / 180 * 65535)
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
