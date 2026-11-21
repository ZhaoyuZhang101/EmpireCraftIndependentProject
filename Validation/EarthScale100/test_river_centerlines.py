"""Survey geometry controls with independent endpoints, branches and gaps."""
from pathlib import Path
import sys
import unittest

import numpy as np
from shapely.geometry import LineString, Point

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Tools/EarthScale100"))
from river_centerlines import RiverCenterlines
from river_widths import RiverWidths


def sections(*runs):
    index = RiverWidths.__new__(RiverWidths)
    index.run_ids = [("test", i, 1, len(run)-1) for i, run in enumerate(runs)]
    a = np.concatenate([p[:-1] for p in runs])
    b = np.concatenate([p[1:] for p in runs])
    index.set_segments(a, b, np.full(len(a), 800.))
    return RiverCenterlines(index)


class CentrelineTest(unittest.TestCase):
    def test_observed_bend_and_fixed_junction_in_both_directions(self):
        x = np.linspace(0, .24, 241)
        y = .012 * np.sin(np.pi*np.clip((x-.035)/.07, 0, 1))**2
        y += .010 * np.sin(np.pi*np.clip((x-.14)/.065, 0, 1))**2
        measured = np.column_stack((x, y))
        natural = np.column_stack((np.linspace(0, .24, 13), np.zeros(13)))
        protected = np.zeros(13, dtype=bool)
        protected[[0, 6, -1]] = True
        for source in (natural, natural[::-1]):
            result = sections(measured).refine(source, protected)
            line = LineString(result)
            self.assertLess(line.distance(Point(measured[70])), .0008)
            self.assertLess(line.distance(Point(measured[172])), .0008)
            np.testing.assert_equal(result[[0, -1]], source[[0, -1]])
            self.assertTrue(np.any(np.all(result == source[6], axis=1)))

    def test_ambiguous_parallel_channels_and_unobserved_gap(self):
        x = np.linspace(0, .18, 181)
        source = np.column_stack((x[::20], np.zeros(len(x[::20]))))
        parallel = np.column_stack((x, np.full(len(x), .0015)))
        untouched = sections(parallel, parallel*[1, -1]).refine(source)
        np.testing.assert_equal(untouched, source)
        y = .002*np.sin(x*100)
        measured = np.column_stack((x, y))
        separated = sections(measured[:60], measured[120:]).refine(source)
        mid = separated[(separated[:, 0] >= .06) & (separated[:, 0] <= .12)]
        np.testing.assert_equal(mid, source[(source[:, 0] >= .06) & (source[:, 0] <= .12)])


if __name__ == "__main__":
    unittest.main()
