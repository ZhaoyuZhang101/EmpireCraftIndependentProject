import unittest

import numpy as np

from check_monsoon_flux import integrated_mean_flow, section_mean


class MoistureTransport(unittest.TestCase):
    def test_constant_specific_humidity_and_wind_in_si_units(self):
        levels = np.array([1000, 925, 850, 700, 600, 500, 400, 300])
        q = np.full((12, 8, 2, 2), 10.)  # g/kg
        u = np.full(q.shape, -8.)         # m/s
        np.testing.assert_allclose(integrated_mean_flow(q, u, levels), -.010 * 8 * 70000 / 9.80665)

    def test_section_uses_face_coordinates_and_partial_endpoints(self):
        lat, lon = np.arange(5.)*2 + 10, np.arange(4.)*3 + 65
        y, x = np.meshgrid(lat, lon, indexing="ij")
        field = (2*x-y)[None]
        measured = section_mean(field, lat, lon, "east", 67.5, 10.75, 17.5)
        self.assertAlmostEqual(measured.item(), 2*67.5 - (10.75+17.5)/2)
        measured = section_mean(field, lat, lon, "north", 13.75, 65.25, 72.5)
        self.assertAlmostEqual(measured.item(), 65.25+72.5-13.75)


if __name__ == "__main__":
    unittest.main()
