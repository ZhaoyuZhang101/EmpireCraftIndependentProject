"""Physical-distance regressions for the ORBIS export and its shipped bed."""
import hashlib
import json
import struct
import unittest
from pathlib import Path

import numpy as np
from scipy.ndimage import map_coordinates

from world_atlas.core import procedural_planet as planet
from world_atlas.physical.planetary_grid import lon_lat_to_unit_vector


def grid_degrees(height, width):
    latitude = 90.0 - (np.arange(height) + 0.5) * 180.0 / height
    longitude = (np.arange(width) + 0.5) * 360.0 / width - 180.0
    return latitude[:, None], longitude[None, :]


class SphericalReliefTests(unittest.TestCase):
    def test_final_filter_has_a_round_ground_footprint_at_both_poles(self):
        from world_atlas.core.spherical_relief import smooth_field
        height, width = 128, 256
        latitude, longitude = grid_degrees(height, width)
        vectors = lon_lat_to_unit_vector(longitude, latitude)
        for row in (6, height - 7):
            impulse = np.zeros((height, width), dtype=np.float32)
            impulse[row, 0] = 1
            blurred = smooth_field(impulse, radius=4)
            lon, lat = np.radians([longitude[0, 0], latitude[row, 0]])
            east = np.array([-np.sin(lon), np.cos(lon), 0.0])
            north = np.cross(vectors[row, 0], east)
            weights = blurred * np.cos(np.radians(latitude))
            east_moment = np.sum((vectors @ east) ** 2 * weights)
            north_moment = np.sum((vectors @ north) ** 2 * weights)
            self.assertAlmostEqual(np.sqrt(east_moment / north_moment), 1.0, delta=0.10)
            self.assertGreater(blurred[row, -1], 0.0)
        impulse.fill(0)
        impulse[0, 0] = 1
        self.assertGreater(smooth_field(impulse, radius=4)[0, width // 2], 0.0)
        np.testing.assert_allclose(smooth_field(np.ones_like(impulse), 4, 2), 1.0)

    def test_tectonic_distance_crosses_the_poles_and_the_seam(self):
        from world_atlas.core.spherical_relief import distance_to_sources_km
        for sign in (1, -1):
            sources = lon_lat_to_unit_vector([179, -179], [sign * 87, sign * 45])
            queries = lon_lat_to_unit_vector([-1, -178, 179], [sign * 89, sign * 45, sign * 87])
            distance = distance_to_sources_km(queries, sources, 6400)
            expected = [np.radians(4), 2 * np.arcsin(np.cos(np.radians(45)) * np.sin(np.radians(.5))), 0]
            np.testing.assert_allclose(distance, np.array(expected) * 6400, atol=0.002)

    def test_shelf_reaches_the_same_ground_distance_for_a_meridional_coast(self):
        for width in (256, 512, 1024):
            height = width // 2
            latitude, longitude = grid_degrees(height, width)
            shape = (height, width)
            land = np.broadcast_to(longitude < 0, shape)
            floor, _ = planet._visible_ocean_bathymetry(
                land, np.full(shape, 4000), np.full(shape, planet.CRUST_OCEANIC),
                np.full(shape, 190), np.full(shape, 1e6), np.full(shape, 1e6),
                np.zeros(shape, dtype=np.int8), 271, spherical_relief=True,
            )
            depths = []
            for target in (0, 78, -78):
                row = np.argmin(np.abs(latitude[:, 0] - target))
                # Shortest distance to the great-circle meridian at longitude zero.
                lon = np.degrees(np.arcsin(np.sin(235.0 / 6400) / np.cos(np.radians(latitude[row, 0]))))
                depths.append(np.interp(lon, longitude[0], floor[row]))
            self.assertLess(max(depths) - min(depths), 0.065, (width, depths))

    def test_a_ridge_cell_cannot_stamp_a_single_cell_thermal_crest(self):
        shape = (64, 128)
        land = np.zeros(shape, dtype=bool)
        land[30:34, :3] = True
        age = np.full(shape, 95.0)
        def floor():
            return planet._visible_ocean_bathymetry(
                land, np.full(shape, 4000), np.full(shape, planet.CRUST_OCEANIC),
                age, np.full(shape, 1e6), np.full(shape, 1e6),
                np.zeros(shape, dtype=np.int8), 271, spherical_relief=True,
            )[0]
        baseline = floor()
        age[6, 64] = 0.0
        change = np.abs(floor() - baseline)
        self.assertGreater(float(np.sum(change)), 0)
        self.assertLess(change[6, 64], np.sum(change) * 0.5)

    def test_baked_extreme_polar_rings_do_not_have_radial_spokes(self):
        path = Path(__file__).resolve().parents[3] / 'Assets/ORBIS/Resources/ORBIS/WorldAtlas/Default.bytes'
        data = path.read_bytes()
        metadata = json.loads(path.with_suffix('.json').read_text(encoding='utf-8'))
        tag, width, height, seed = struct.unpack_from('<4sIII', data)
        self.assertEqual((tag, width, height, seed), (b'OAT1', 512, 256, metadata['seedHash']))
        self.assertEqual(hashlib.sha256(data).hexdigest(), metadata['sha256'])
        bed = np.frombuffer(data, dtype='<i2', offset=16).reshape(height, width)[::-1].astype(float)
        # Add pole-reflected and periodic samples for bilinear interpolation.
        halo = np.vstack((np.roll(bed[0], width // 2), bed, np.roll(bed[-1], width // 2)))
        halo = np.pad(halo, ((0, 0), (1, 1)), mode='wrap')
        latitude, longitude = grid_degrees(height, width)
        lon = np.radians(longitude)
        east = np.stack((-np.sin(lon), np.cos(lon), np.zeros_like(lon)), axis=-1)
        half_step = 78.0 / (2 * 6400.0)
        for side in (1, -1):
            cap = latitude[side * latitude[:, 0] >= 88]
            center = lon_lat_to_unit_vector(longitude, cap)
            north = np.cross(center, east)
            def mean_change(tangent):
                samples = []
                for direction in (1, -1):
                    point = center * np.cos(half_step) + tangent * (direction * np.sin(half_step))
                    row = (np.pi / 2 - np.arcsin(np.clip(point[..., 2], -1, 1))) * height / np.pi + 0.5
                    col = (np.arctan2(point[..., 1], point[..., 0]) + np.pi) * width / (2 * np.pi) + 0.5
                    samples.append(map_coordinates(halo, [row, col], order=1))
                row_change = np.mean(np.abs(samples[0] - samples[1]), axis=1)
                return np.average(row_change, weights=np.cos(np.radians(cap[:, 0])))
            # Radial spokes change much faster across meridians. A spreading
            # ridge near a pole can legitimately change faster along them.
            across, along = mean_change(east), mean_change(north)
            with self.subTest(hemisphere=side):
                self.assertGreater(min(across, along), 0.0)
                self.assertLess(across / along, 1.5)


if __name__ == '__main__':
    unittest.main()
