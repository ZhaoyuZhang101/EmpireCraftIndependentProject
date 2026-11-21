"""Synthetic controls for matching satellite river transects to mapped courses."""
from pathlib import Path
import sys
import unittest

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Tools/EarthScale100"))
from river_widths import RiverWidths, chainage, complete_widths


class RiverWidthTest(unittest.TestCase):
    def test_crossing_and_ambiguous_parallel_reaches(self):
        index = RiverWidths([[[-.03, 0], [.03, 0]], [[0, -.03], [0, .03]]], [300, 1500])
        course = np.array([[-.01, 0], [0, 0], [.01, 0]])
        np.testing.assert_equal(index.match(course), [300, 300, 300])
        np.testing.assert_equal(index.match(course[:, ::-1]), [1500, 1500, 1500])
        parallel = RiverWidths([[[-.03, -.001], [.03, -.001]], [[-.03, .001], [.03, .001]]],
                               [150, 1500])
        self.assertTrue(np.isnan(parallel.match(course)).all())
        self.assertTrue(np.isnan(index.match(course + [1.0, 0])).all())

    def test_short_gaps_and_dateline(self):
        course = np.array([[179.98, 60], [-179.99, 60], [-179.95, 60]])
        index = RiverWidths([[[179.97, 60], [-179.94, 60]]], [420])
        np.testing.assert_equal(index.match(course), [420, 420, 420])
        self.assertAlmostEqual(chainage(course)[-1], 3.8918, places=3)
        np.testing.assert_equal(complete_widths(course, [420., np.nan, 420.], 100), [420]*3)
        longer = np.column_stack((np.linspace(0, 2, 5), np.zeros(5)))
        np.testing.assert_equal(complete_widths(longer, [1000., np.nan, np.nan, np.nan, 1000.], 200),
                                [1000, 200, 200, 200, 1000])
