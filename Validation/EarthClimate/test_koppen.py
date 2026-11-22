"""Build and exercise the C# scorer using temporary, artificial monthly normals."""

import csv
import gzip
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import tempfile
import unittest

import numpy as np

from prepare import HEIGHT, WIDTH, area_overlaps, holdout
from score import tile_area


HERE = Path(__file__).resolve().parent
TEMPERATURE = [4, 6, 9, 13, 18, 21, 24, 23, 19, 14, 9, 5]
RAIN = [140, 120, 90, 65, 35, 15, 5, 10, 25, 60, 90, 150]  # Mediterranean


class KoppenScorer(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        build = tempfile.TemporaryDirectory(prefix="orbis-koppen-build-")
        cls.addClassCleanup(build.cleanup)
        directory = Path(build.name)
        subprocess.run(["dotnet", "build", str(HERE / "Export.csproj"), "-c", "Release", "--nologo",
                        "--artifacts-path", str(directory / "artifacts"), "-o", str(directory / "bin")], check=True)
        cls.dll = directory / "bin/Export.dll"

    def setUp(self):
        directory = tempfile.TemporaryDirectory(prefix="orbis-koppen-fixture-")
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.data = self.root / "Validation/EarthClimate"
        self.data.mkdir(parents=True)
        self.set_cells([y * WIDTH + 20 + x for y in (24, 103) for x in range(5)], np.full(10, 9000.0))

    def set_cells(self, ids, areas):
        self.ids, self.areas = np.asarray(ids), np.array(areas)
        phase = np.where(self.ids // WIDTH < HEIGHT // 2, 6, 0)
        self.ot, self.op = [np.stack([np.roll(v, p) for p in phase]).astype("<f4") for v in (TEMPERATURE, RAIN)]
        self.mt, self.mp = self.ot.copy(), self.op.copy()

    def compare(self, code=0, holdout_split=False, strict=True, window=None):
        split = "holdout" if holdout_split else "calibration"
        with gzip.open(self.data / f"{split}.csv.gz", "wt", newline="") as stream:
            rows = csv.writer(stream)
            rows.writerow(["tile", "area_km2", "elevation_m"] + [f"{k}{m:02}" for k in "tp" for m in range(1, 13)])
            for i, area, t, p in zip(self.ids, self.areas, self.ot, self.op):
                rows.writerow([i, f"{area:.4f}", 500, *t, *p])
        model = self.root / "model.bin"
        with model.open("wb") as stream:
            stream.write(struct.pack("<4sII", b"ECL1", WIDTH, HEIGHT))
            stream.write(bytes(4 * WIDTH * HEIGHT))
            stream.write(bytes([1]) * WIDTH * HEIGHT)  # Deliberately simulate water on every observed tile.
            for values in (self.mt, self.mp):
                grid = np.zeros((HEIGHT * WIDTH, 12), dtype="<f4")
                grid[self.ids] = values
                stream.write(grid.tobytes())
        args = [] if holdout_split else ["--split", split]
        if strict and not holdout_split:
            args.append("--check-target")
        if window is not None:
            args.extend(["--window", *map(str, window)])
        run = subprocess.run(["dotnet", str(self.dll), "--compare-koppen", str(model), *args, "--json", "report.json"],
                             cwd=self.root, capture_output=True, encoding="utf-8", errors="replace")
        self.assertEqual(run.returncode, code, run.stdout + run.stderr)
        if code == 2:
            return run.stderr
        report = json.loads((self.root / "report.json").read_text(encoding="utf-8"))
        self.assertEqual(report.get("model_sha256"), hashlib.sha256(model.read_bytes()).hexdigest())
        return report

    def test_area_weighted_strict_threshold_and_subtype_recall(self):
        self.ot[1] = self.mt[1] = -20  # One observed EF, nine Csa. Model mislabels one Csa as Cfa.
        self.mp[0] = 100
        r = self.compare(1)
        self.assertEqual((r["five_group_agreement"], r["exact_subtype_agreement"]), (1, .9))
        self.assertEqual((r["five_group_pass"], r["exact_subtype_pass"]), (True, False))
        self.assertIn("observed_subtypes", r)
        observed = r["observed_subtypes"]
        self.assertEqual(len(observed), 30)
        self.assertEqual(observed["Csa"]["area_km2"], 81000)
        self.assertAlmostEqual(observed["Csa"]["recall"], 8 / 9)
        self.assertEqual(observed["EF"], {"area_km2": 9000, "recall": 1})
        self.assertEqual(observed["Cfa"], {"area_km2": 0, "recall": None})
        self.compare(strict=False)  # Calibration-only diagnostics can finish below target.
        self.mt[0] = -20
        r = self.compare(1)
        self.assertEqual((r["five_group_agreement"], r["exact_subtype_agreement"]), (.9, .9))
        self.assertEqual((r["five_group_pass"], r["exact_subtype_pass"]), (False, False))
        self.areas[0] -= .0001
        self.assertGreater(self.compare()["exact_subtype_agreement"], .9)
        self.areas[0] += .0002
        self.assertLess(self.compare(1)["exact_subtype_agreement"], .9)

    def test_calendar_phase_and_local_seasons(self):
        r = self.compare()
        self.assertEqual((r["five_group_agreement"], r["exact_subtype_agreement"]), (1, 1))
        for h in r["hemispheres"]:
            self.assertEqual((h["temperature_cycle_rmse_c"], h["precipitation_cycle_rmse_mm"]), (0, 0))
            self.assertEqual((h["observed_wet_quarter_mae_mm"], h["observed_dry_quarter_mae_mm"]), (0, 0))
        self.mt, self.mp = [np.roll(v, 6, axis=1) for v in (self.ot, self.op)]
        shifted = self.compare()
        self.assertEqual(shifted["exact_subtype_agreement"], 1)
        for h in shifted["hemispheres"]:
            self.assertAlmostEqual(h["temperature_cycle_rmse_c"], 13.705230145)
            self.assertAlmostEqual(h["precipitation_cycle_rmse_mm"], 96.458108351)
            self.assertEqual((h["observed_wet_quarter_mae_mm"], h["observed_dry_quarter_mae_mm"]), (380, 380))
        self.mt, self.mp = self.ot.copy(), self.op.copy()
        for j, i in enumerate(self.ids):
            summer = [11, 0, 1] if i // WIDTH < HEIGHT // 2 else [5, 6, 7]
            self.mt[j, summer] += 3
            self.mp[j, summer] += 5
        for h in self.compare()["hemispheres"]:
            seasons = {s["name"]: s for s in h["seasons"]}
            self.assertEqual(seasons["summer"]["months"], "JJA" if h["name"] == "north" else "DJF")
            for name, s in seasons.items():
                expected = (3, 15) if name == "summer" else (0, 0)
                self.assertEqual((s["temperature_mae_c"], s["precipitation_mae_mm"]), expected)

    def test_input_attribution_is_separate_from_the_pure_model_gate(self):
        self.mp[0] = 100   # The first Csa becomes Cfa because its summer stays wet.
        self.mt[1] = -20   # A separate Csa becomes polar because its summer stays frozen.
        r = self.compare(1)
        self.assertEqual((r["five_group_agreement"], r["exact_subtype_agreement"]), (.9, .8))
        self.assertEqual(r["with_observed_temperature"], {"five_group_agreement": 1, "exact_subtype_agreement": .9})
        self.assertEqual(r["with_observed_precipitation"], {"five_group_agreement": .9, "exact_subtype_agreement": .9})

    def test_frozen_geographic_footprint_and_default_gate(self):
        # Recover eligible IDs from geometry. Every climate value still comes from the synthetic profiles above.
        mask = np.unpackbits(np.frombuffer(gzip.decompress((HERE / "land-mask.bin.gz").read_bytes()), dtype=np.uint8))
        coverage = np.zeros(WIDTH * HEIGHT)
        for _, tiles, weights in area_overlaps(np.flatnonzero(mask)):
            coverage += np.bincount(tiles, weights=weights, minlength=coverage.size)
        eligible = np.flatnonzero(coverage.round(4) >= .5 * np.repeat(tile_area(HEIGHT), WIDTH))
        ids = [i for i in eligible if holdout(i)]
        self.set_cells(ids, coverage[ids])
        r = self.compare(holdout_split=True)
        self.assertEqual((r["cells"], r["blocks"]), (1806, 17))
        self.assertEqual(r["footprint_sha256"], "270c599eac1be55f630b38cf48e19f5268f8a24141e22fbc2b109bee36e5cbfa")
        self.mp[::3] = 100
        r = self.compare(1, holdout_split=True)
        self.assertTrue(r["five_group_pass"])
        self.assertFalse(r["exact_subtype_pass"])
        present = set(ids)
        j = next(j for j, i in enumerate(ids) if i ^ 1 not in present)
        self.ids[j] ^= 1  # Same size, block and area, different registered tile.
        self.assertIn("footprint", self.compare(2, holdout_split=True))

    def test_observation_coverage_and_invalid_monthly_values(self):
        self.areas[0], self.mp[0] = 1, 100
        self.assertEqual(self.compare()["cells"], 9)
        self.mt[0, 2] = np.nan
        self.assertIn("temperature", self.compare(2))  # A broken export even beyond the eligible sample.
        self.mt[0, 2], self.op[1, 2] = 0, -1
        self.assertIn("precipitation", self.compare(2))

    def test_calibration_window_leaves_global_gate_intact(self):
        window = (-152, -148, 50, 60)
        self.mp[5] = 100
        regional = self.compare(strict=False, window=window)
        self.assertEqual((regional["cells"], regional["blocks"]), (3, 1))
        self.assertAlmostEqual(regional["exact_subtype_agreement"], 2/3)
        self.assertEqual(regional["calibration_window"]["west"], -152)
        self.assertIn("calibration", self.compare(2, holdout_split=True, window=window))
        self.assertIn("calibration", self.compare(2, window=window))


if __name__ == "__main__":
    unittest.main(verbosity=2)
