using System;
using System.Collections.Generic;
using System.Threading;

namespace Orbis.Flat
{
    // One-off fluvial & hillslope landscape evolution for newly generated procedural worlds.
    // Couples Braun & Willett (2013) implicit stream-power incision with Gleba's:
    //   1. Tectonic lithology resistance (soft foreland/alluvial basins vs hard craton shields & LIP traps),
    //   2. Planetary moisture-weighted runoff proxy (ITCZ + storm-track windward vs subtropical arid belts),
    //   3. Continental Rift Graben & deep tectonic lake preservation (Baikal / Tanganyika analogs),
    //   4. Critical-angle talus thermal relaxation (`erosion::thermal_erosion`) + valley-relief hypsometry blend.
    // Runs at procedural world creation only, never on Earth or brush/save rebuilds.
    public static class FlatErosion
    {
        public const int Iterations = 80;
        const float Erodibility = .0038f, AreaExponent = .5f, Diffusion = .052f, MinimumSlope = .75f;
        const float KeepLakeDepth = 335f, KeepLakeArea = 10f, KeepLakeMaxArea = 155f;
        const float KeepRiftDepth = 155f, KeepRiftArea = 1.5f, KeepRiftMaxArea = 175f;
        const float Jitter = 42f, Hardness = .55f, Epsilon = 1e-3f;

        public static void Apply(WorldData w, CancellationToken cancel)
        {
            int n = w.Count, width = w.Width, height = w.Height;
            var h = new float[n];
            var fixedCell = new bool[n];
            var surface = new float[n];
            for (int i = 0; i < n; i++) h[i] = w.Elevation[i];

            var rowScale = new float[height];
            var zonalMoisture = new float[height];
            var eastStride = new int[height];
            var eastWeight = new float[height];
            double tileAngle = Math.PI / height;
            double nominalTileMeters = 20015086.0 / height;
            float talusMaxDropM = (float)(nominalTileMeters * Math.Tan(3.1 * Math.PI / 180.0)); // Gleba 3.0 deg angle of repose

            for (int y = 0; y < height; y++)
            {
                double latRad = ((y + .5) / height - .5) * Math.PI;
                double absLatDeg = Math.Abs(latRad) * (180.0 / Math.PI);
                rowScale[y] = (float)Math.Cos(latRad);

                // Smooth planetary precipitation proxy (ITCZ peak + midlatitude storm tracks - subtropical STHZ dry belt)
                double itcz = Math.Exp(-(absLatDeg * absLatDeg) / (2.0 * 11.5 * 11.5));
                double storm = Math.Exp(-((absLatDeg - 50.0) * (absLatDeg - 50.0)) / (2.0 * 13.0 * 13.0));
                double sthzDry = Math.Exp(-((absLatDeg - 26.0) * (absLatDeg - 26.0)) / (2.0 * 9.5 * 9.5));
                double polarDry = absLatDeg > 66.0 ? (absLatDeg - 66.0) / 24.0 : 0.0;
                zonalMoisture[y] = FMath.Clamp((float)(0.78 + 0.72 * itcz + 0.42 * storm - 0.36 * sthzDry - 0.32 * polarDry), 0.38f, 1.58f);

                double offset = 2 * Math.Asin(Math.Min(1, Math.Sin(tileAngle * .5) / rowScale[y])) / tileAngle;
                int dx = eastStride[y] = Math.Max(1, (int)Math.Round(offset));
                double lengthEW = 2 * Math.Asin(rowScale[y] * Math.Sin(dx * tileAngle * .5)) / tileAngle;
                eastWeight[y] = (float)(1 / (lengthEW * lengthEW));
            }

            var isOcean = FlatOcean.Find(w, cancel);
            int oceanCount = 0;
            for (int i = 0; i < n; i++)
            {
                if (isOcean[i]) { fixedCell[i] = true; surface[i] = 0f; oceanCount++; }
            }
            if (oceanCount == 0 || oceanCount == n) return;

            // Preserve sub-datum inland seas, major tectonic basins, and segmented Continental Rift Grabens (Baikal/Tanganyika)
            var tec = w.Tectonics;
            var seen = new bool[n];
            var queue = new Queue<int>();
            float areaScale = (width / 512f) * (width / 512f);
            var fill = Flood(w, h, fixedCell, surface, 0f);
            var basin = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (seen[i] || fixedCell[i] || fill[i] - h[i] <= .5f) continue;
                basin.Clear();
                seen[i] = true;
                queue.Enqueue(i);
                float depth = 0f;
                double area = 0, riftArea = 0;
                bool sea = false;
                while (queue.Count > 0)
                {
                    int c = queue.Dequeue();
                    basin.Add(c);
                    sea |= h[c] <= 0f;
                    depth = Math.Max(depth, fill[c] - h[c]);
                    double wCell = rowScale[c / width];
                    area += wCell;
                    if (tec != null && ((tec.Province[c] & GlebaTectonicProvince.RiftGraben) != 0 || tec.Setting[c] == TectonicSetting.Rift))
                        riftArea += wCell;
                    for (int d = 0; d < 4; d++)
                    {
                        int m = w.SphereNeighbor(c, d);
                        if (m >= 0 && !seen[m] && !fixedCell[m] && fill[m] - h[m] > .5f)
                        {
                            seen[m] = true;
                            queue.Enqueue(m);
                        }
                    }
                }

                bool isRiftBasin = riftArea >= 0.25 * area &&
                                   depth >= KeepRiftDepth &&
                                   area >= Math.Max(0.35f, KeepRiftArea * areaScale) &&
                                   area <= KeepRiftMaxArea * areaScale;
                bool isMajorLake = depth >= KeepLakeDepth &&
                                   area >= KeepLakeArea * areaScale &&
                                   area <= KeepLakeMaxArea * areaScale;
                if (sea || isRiftBasin || isMajorLake)
                {
                    foreach (int c in basin) { fixedCell[c] = true; surface[c] = fill[c]; }
                }
            }

            var hypsometry = new List<float>(n - oceanCount);
            for (int i = 0; i < n; i++) if (!fixedCell[i]) hypsometry.Add(h[i]);
            hypsometry.Sort();
            for (int i = 0; i < n; i++) if (fixedCell[i]) h[i] = surface[i];

            // Couple rock erodibility to Gleba tectonic provinces + 3D bedding roughness
            uint seed = FMath.Seed(w.Seed) ^ 0x5e0d1a7bu;
            var strength = new float[n];
            var runoffWeight = new float[n];
            for (int i = 0; i < n; i++)
            {
                int y = i / width;
                if (fixedCell[i]) continue;
                double lat = ((y + .5) / height - .5) * Math.PI, lon = ((i % width + .5) / width - .5) * 2 * Math.PI;
                float radius = (float)(width / (2 * Math.PI));
                float x = (float)Math.Cos(lon) * rowScale[y] * radius;
                float yy = (float)Math.Sin(lon) * rowScale[y] * radius;
                float z = (float)Math.Sin(lat) * radius;

                float fbmHard = (float)Math.Exp(Hardness * FlatTectonics.Fbm(x / 16f + .31f, yy / 16f + .43f, z / 16f + .21f, seed, 4));
                float rockRes = (tec != null && tec.RockResistance != null) ? tec.RockResistance[i] : 1.0f;
                // Inverse resistance: soft foreland/alluvial sediments incise smooth trunk channels; craton shields & LIPs resist
                strength[i] = FMath.Clamp(fbmHard / Math.Max(.52f, rockRes), .38f, 2.15f);

                float jitterScale = (tec != null && (tec.Province[i] & GlebaTectonicProvince.ForelandBasin) != 0) ? .45f : 1.0f;
                h[i] = Math.Max(1f, h[i] + Jitter * jitterScale * FlatTectonics.Fbm(x / 4f + .37f, yy / 4f + .19f, z / 4f + .53f, seed ^ 0x9e3779b9u, 4));

                float orogRain = 1f + .32f * FMath.Clamp(h[i] / 2600f, 0f, 1.2f);
                runoffWeight[i] = rowScale[y] * zonalMoisture[y] * orogRain;
            }

            var receiver = new int[n];
            var length = new float[n];
            var area2 = new float[n];
            var order = new int[n];
            var donors = new int[n];
            var donorStart = new int[n + 1];
            var donorCount = new int[n];
            var smooth = new float[n];

            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                cancel.ThrowIfCancellationRequested();
                var filled = Flood(w, h, fixedCell, surface, Epsilon);
                for (int i = 0; i < n; i++) if (!fixedCell[i]) h[i] = filled[i];

                // Steepest admissible descent matching FlatHydrology's diagonal bank rule
                for (int i = 0; i < n; i++)
                {
                    receiver[i] = i; length[i] = 1f;
                    if (fixedCell[i]) continue;
                    int y = i / width; float best = 0f;
                    for (int d = 0; d < 8; d++)
                    {
                        int m = Neighbor(w, i, d);
                        if (m < 0) continue;
                        if (d >= 4)
                        {
                            int across = y * width + m % width, along = (m / width) * width + i % width;
                            float side = h[across] + h[along];
                            if (side < 2f * Math.Min(h[i], h[m]) || side > 2f * Math.Max(h[i], h[m])) continue;
                        }
                        float run = d < 2 ? rowScale[y] : d < 4 ? 1f : (float)Math.Sqrt(rowScale[y] * rowScale[y] + 1f);
                        float slope = (h[i] - h[m]) / run;
                        if (slope > best) { best = slope; receiver[i] = m; length[i] = run; }
                    }
                }

                Array.Clear(donorCount, 0, n);
                for (int i = 0; i < n; i++) if (receiver[i] != i) donorCount[receiver[i]]++;
                donorStart[0] = 0;
                for (int i = 0; i < n; i++) donorStart[i + 1] = donorStart[i] + donorCount[i];
                Array.Clear(donorCount, 0, n);
                for (int i = 0; i < n; i++) if (receiver[i] != i) { int r = receiver[i]; donors[donorStart[r] + donorCount[r]++] = i; }

                int count = 0;
                for (int i = 0; i < n; i++) if (receiver[i] == i) order[count++] = i;
                for (int k = 0; k < count; k++) { int c = order[k]; for (int j = donorStart[c]; j < donorStart[c + 1]; j++) order[count++] = donors[j]; }

                for (int i = 0; i < n; i++) area2[i] = fixedCell[i] ? 0f : runoffWeight[i];
                for (int k = count - 1; k >= 0; k--) { int c = order[k]; if (receiver[c] != c) area2[receiver[c]] += area2[c]; }

                // Implicit moisture- & lithology-coupled stream power (n = 1: unconditionally stable)
                for (int k = 0; k < count; k++)
                {
                    int c = order[k], r = receiver[c];
                    if (fixedCell[c] || r == c) continue;
                    float f = Erodibility * strength[c] * (float)Math.Pow(area2[c] * 100f, AreaExponent) / length[c];
                    h[c] = Math.Max((h[c] + f * h[r]) / (1f + f), Math.Max(1f, h[r] + MinimumSlope * length[c]));
                }

                // Hillslope diffusion + Gleba 3.0-deg angle-of-repose talus thermal relaxation on steep alpine crests
                Array.Copy(h, smooth, n);
                bool thermalStep = (iteration & 7) == 7;
                for (int i = 0; i < n; i++)
                {
                    if (fixedCell[i]) continue;
                    int row = i / width, x = i % width, dx = eastStride[row];
                    float ew = eastWeight[row];
                    int wNb = w.Index(x - dx, row), eNb = w.Index(x + dx, row);
                    int sNb = w.SphereNeighbor(i, 2), nNb = w.SphereNeighbor(i, 3);
                    float horizontal = smooth[wNb] + smooth[eNb];
                    float vertical = smooth[sNb] + smooth[nNb];
                    float average = (horizontal * ew + vertical) / (2f * (1f + ew));

                    float diff = Diffusion;
                    if (thermalStep)
                    {
                        float eSelf = Math.Max(0f, smooth[i]);
                        float maxDrop = Math.Max(
                            Math.Max(Math.Abs(eSelf - Math.Max(0f, smooth[wNb])), Math.Abs(eSelf - Math.Max(0f, smooth[eNb]))),
                            Math.Max(Math.Abs(eSelf - Math.Max(0f, smooth[sNb])), Math.Abs(eSelf - Math.Max(0f, smooth[nNb]))));
                        if (maxDrop > talusMaxDropM) diff = .32f; // Gleba `thermal_erosion` talus slump
                    }
                    h[i] = Math.Max(1f, smooth[i] + diff * (average - smooth[i]));
                }
            }

            // Remap onto tectonic source hypsometry (preserving global elevation quantiles, snowlines & plains share)
            // while blending 12% of the incised fluvial profile so major river valleys sit genuinely below interfluves.
            var final = Flood(w, h, fixedCell, surface, Epsilon);
            var land = new List<int>(hypsometry.Count);
            for (int i = 0; i < n; i++) if (!fixedCell[i]) land.Add(i);
            land.Sort((a, b) => final[a] != final[b] ? final[a].CompareTo(final[b]) : a.CompareTo(b));

            var level = new int[n];
            var ranked = new float[land.Count];
            for (int k = 0; k < land.Count; k++)
            {
                int idx = land[k];
                float blended = .88f * hypsometry[k] + .12f * Math.Min(hypsometry[k], final[idx]);
                level[idx] = Math.Max(1, (int)Math.Round(blended));
                ranked[k] = final[idx];
            }

            var shift = new int[n];
            for (int i = 0; i < n; i++)
            {
                if (!fixedCell[i]) continue;
                if (isOcean[i]) { level[i] = 0; continue; }
                int k = Array.BinarySearch(ranked, surface[i]);
                if (k < 0) k = ~k;
                float mapped = land.Count > 0 ? hypsometry[Math.Min(Math.Max(k, 0), land.Count - 1)] : surface[i];
                shift[i] = (int)Math.Round(mapped - surface[i]);
                level[i] = (int)Math.Ceiling(mapped);
            }

            Descend(w, level, fixedCell);
            for (int i = 0; i < n; i++)
            {
                if (!fixedCell[i]) w.Elevation[i] = (short)FMath.Clamp(level[i], 1, HeightBands.MaximumBed);
                else if (shift[i] != 0) w.Elevation[i] = (short)FMath.Clamp(w.Elevation[i] + shift[i], HeightBands.MinimumBed, HeightBands.MaximumBed);
            }
        }

        static int Neighbor(WorldData w, int i, int direction)
        {
            if (direction < 4) return w.SphereNeighbor(i, direction);
            int x = i % w.Width, y = i / w.Width, ny = y + (direction < 6 ? -1 : 1);
            if (ny < 0 || ny >= w.Height) return -1;
            return ny * w.Width + FMath.Wrap(x + ((direction & 1) == 0 ? -1 : 1), w.Width);
        }

        static float[] Flood(WorldData w, float[] h, bool[] fixedCell, float[] surface, float eps)
        {
            int n = w.Count;
            var f = new float[n];
            var done = new bool[n];
            var heap = new FloodHeap();
            for (int i = 0; i < n; i++) if (fixedCell[i]) { f[i] = surface[i]; done[i] = true; }
            for (int i = 0; i < n; i++)
            {
                if (!fixedCell[i]) continue;
                for (int d = 0; d < 4; d++)
                {
                    int m = w.SphereNeighbor(i, d);
                    if (m < 0 || done[m]) continue;
                    done[m] = true;
                    f[m] = Math.Max(h[m], surface[i] + eps);
                    heap.Push(m, Key(f[m]));
                }
            }
            while (heap.Count > 0)
            {
                int c = heap.Pop(out _);
                for (int d = 0; d < 4; d++)
                {
                    int m = w.SphereNeighbor(c, d);
                    if (m < 0 || done[m]) continue;
                    done[m] = true;
                    f[m] = Math.Max(h[m], f[c] + eps);
                    heap.Push(m, Key(f[m]));
                }
            }
            for (int i = 0; i < n; i++) if (!done[i]) f[i] = h[i];
            return f;
        }

        static long Key(float height) { return (long)Math.Round(height * 1000.0); }

        static void Descend(WorldData w, int[] h, bool[] fixedCell)
        {
            int n = w.Count;
            var done = new bool[n];
            var heap = new FloodHeap();
            for (int i = 0; i < n; i++)
            {
                if (!fixedCell[i]) continue;
                done[i] = true;
                for (int d = 0; d < 4; d++)
                {
                    int m = w.SphereNeighbor(i, d);
                    if (m < 0 || fixedCell[m] || done[m]) continue;
                    done[m] = true;
                    h[m] = Math.Max(h[m], Math.Max(1, h[i] + 1));
                    heap.Push(m, h[m]);
                }
            }
            while (heap.Count > 0)
            {
                int c = heap.Pop(out _);
                for (int d = 0; d < 4; d++)
                {
                    int m = w.SphereNeighbor(c, d);
                    if (m < 0 || done[m]) continue;
                    done[m] = true;
                    h[m] = Math.Max(h[m], h[c] + 1);
                    heap.Push(m, h[m]);
                }
            }
        }
    }
}
