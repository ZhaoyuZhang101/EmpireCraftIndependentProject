import unittest

import numpy as np

from check_drifter_currents import aggregate, velocity_metrics


class DrifterCurrents(unittest.TestCase):
    def test_component_averages_and_observational_support(self):
        # Same latitude and cell. Opposite instantaneous headings cancel.
        # The antipodal cell has only one valid reading and must be excluded.
        lon = [-177, -175, -173, -171, 3, 5, 7, 9]
        rows = np.column_stack((lon, np.full(8, -12.),
                                [1., -1., 1., -1., .2, np.nan, 1., 1.], np.zeros(8),
                                [5, 5, 5, 5, 1, 1, 0, 0]))
        east, north, supported = aggregate(rows, (8, 16))
        self.assertEqual(supported.sum(), 1)
        self.assertTrue(supported[3, 0])
        self.assertEqual(east[3, 0], 0)
        self.assertEqual(north[3, 0], 0)

    def test_weak_reference_and_zero_prediction(self):
        zeros = np.zeros(4)
        metrics = velocity_metrics(np.array([.1, .1, .1, .01]), zeros,
                                   np.array([.1, 0, -.1, .01]), zeros, np.ones(4))
        self.assertEqual(metrics["direction_cells"], 3)
        self.assertEqual(metrics["calm_predictions_on_direction_cells"], 1)
        self.assertAlmostEqual(metrics["direction_mae_deg"], 90)
        self.assertAlmostEqual(metrics["vector_rmse_ms"], np.sqrt((.1**2 + .2**2) / 4))
        self.assertAlmostEqual(metrics["still_ocean_rmse_ms"], np.sqrt((3*.1**2 + .01**2) / 4))
