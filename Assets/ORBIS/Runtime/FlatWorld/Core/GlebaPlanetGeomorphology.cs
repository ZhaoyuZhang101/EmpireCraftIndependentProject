using System;
using System.Collections.Generic;
using System.Threading;

namespace Orbis.Flat
{
    // Post-erosion climate-coupled geomorphology pass for procedural worlds (`preset != TerrainPreset.Earth`),
    // implementing Gleba's Stage 32 (`fjords::generate_fjords`), Stage 45d (`lakes::generate_glacial_lakes`),
    // and Stage 51 (`coral_reefs::generate_coral_reefs`) directly onto the terrain bed before final hydrology & climate.
    public static class GlebaPlanetGeomorphology
    {
        public static void Apply(WorldData w, CancellationToken cancel)
        {
            if (w == null || w.Preset == TerrainPreset.Earth) return;
            int n = w.Count, width = w.Width, height = w.Height;
            float scale = width / 512f;
            float cellKm = 40030f / width;
            uint seed = FMath.Seed(w.Seed) ^ 0x61eba325u;

            var isOcean = FlatOcean.Find(w, cancel);
            var oceanDist = ComputeOceanHopDistance(w, isOcean, cancel);
            var landDist = ComputeLandHopDistance(w, isOcean, cancel);

            var ux = new float[n];
            var uy = new float[n];
            var uz = new float[n];
            var absLatDeg = new float[n];
            var fbm6 = new float[n];
            var estColdestC = new float[n];
            var lgmIceM = new float[n];

            // Respect optional ExoPlaSim planetary orbital/greenhouse forcing if already configured on WorldData
            float globalOffsetC = 0f;
            if (w.PlanetClimate != null)
            {
                float sRatio = FMath.Clamp(w.PlanetClimate.SolarConstantWm2 / 1361f, .5f, 1.8f);
                float co2Ratio = Math.Max(.05f, w.PlanetClimate.Co2Ppm / 280f);
                globalOffsetC = 28f * (sRatio - 1f) + 3.1f * (float)(Math.Log(co2Ratio) / Math.Log(2.0));
            }

            for (int y = 0; y < height; y++)
            {
                if ((y & 15) == 0) cancel.ThrowIfCancellationRequested();
                double lat = ((y + .5) / height - .5) * Math.PI;
                float cosLat = (float)Math.Cos(lat), sinLat = (float)Math.Sin(lat);
                float aLat = (float)(Math.Abs(lat) * (180.0 / Math.PI));
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    double lon = (x + .5) / width * 2 * Math.PI - Math.PI;
                    int i = row + x;
                    float vx = cosLat * (float)Math.Cos(lon);
                    float vy = cosLat * (float)Math.Sin(lon);
                    float vz = sinLat;
                    ux[i] = vx; uy[i] = vy; uz[i] = vz;
                    absLatDeg[i] = aLat;
                    fbm6[i] = GlebaGeographySupplement.SphericalFbm6(vx, vy, vz, 2.43728f, seed ^ 0x13579bdfu);

                    short elev = w.Elevation[i];
                    bool ocean = isOcean[i];
                    float seaKm = ocean ? 0f : oceanDist[i] * cellKm;
                    float cont = FMath.Clamp(seaKm / 1400f, 0f, 1f);

                    // Fast physical temperature & LGM snowline envelope
                    float tMeanSea = 27.8f - 41.5f * (float)Math.Pow(aLat / 90f, 1.55) + globalOffsetC;
                    float seasonalAmp = (3.5f + 19f * Smooth(12f, 65f, aLat)) * (ocean ? .32f : (.48f + .85f * cont));
                    float lapse = (!ocean && elev > 0) ? .0058f * elev : 0f;
                    float tMean = tMeanSea - lapse;
                    float tWarm = tMean + seasonalAmp;
                    float tCold = tMean - seasonalAmp;
                    estColdestC[i] = tCold;

                    if (!ocean && elev > 0)
                    {
                        short maxNb = elev;
                        for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nb = w.Index(x + dx, y + dy);
                            if (nb >= 0 && w.Elevation[nb] > maxNb) maxNb = w.Elevation[nb];
                        }
                        float relief = Math.Max(0f, (float)(maxNb - elev));
                        float rainProxy = (1150f * (float)Math.Exp(-(aLat - 50f) * (aLat - 50f) / (2.0 * 15.0 * 15.0)) + 420f)
                                          * FMath.Clamp(1.15f - .55f * cont, .35f, 1.25f)
                                          * (1f + .35f * FMath.Clamp(maxNb / 1800f, 0f, 1.4f));
                        lgmIceM[i] = GlebaGeographySupplement.ComputeLgmIceMeters(
                            true, elev, relief, aLat, tMean, tWarm, rainProxy);
                    }
                }
            }

            CarveGlacialFjords(w, isOcean, ux, uy, uz, absLatDeg, fbm6, lgmIceM, scale, seed, cancel);
            CarveGlacialShieldLakes(w, isOcean, oceanDist, ux, uy, uz, absLatDeg, fbm6, lgmIceM, scale, seed, cancel);
            ShoalTropicalCoralReefs(w, isOcean, landDist, ux, uy, uz, absLatDeg, fbm6, estColdestC, cellKm, seed, cancel);
        }

        // Gleba Stage 32 (`fjords::generate_fjords`): Carve drowned glacial troughs into subpolar coastal mountain belts
        static void CarveGlacialFjords(
            WorldData w, bool[] isOcean, float[] ux, float[] uy, float[] uz,
            float[] absLatDeg, float[] fbm6, float[] lgmIceM, float scale, uint seed, CancellationToken cancel)
        {
            int n = w.Count, width = w.Width;
            int mountainRadius = Math.Max(1, (int)Math.Ceiling(2.4f * scale));
            int spacingRadius = Math.Max(1, (int)Math.Ceiling(2.2f * scale));
            int maxChannelSteps = FMath.Clamp((int)Math.Round(2.6f * scale), 1, 5);
            int maxFjordTiles = Math.Max(2, n / 280); // <= 0.36% of world tiles so global land share stays rock-solid

            var candidates = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (isOcean[i] || w.Elevation[i] <= 0 || w.Elevation[i] > 950) continue;
                float aLat = absLatDeg[i];
                // Temperate-to-subpolar maritime glacier belt (42-72 deg: Norway, Chile, NZ, SE Alaska, SW Greenland)
                if (aLat < 42f || aLat > 72f) continue;

                bool touchesOpenOcean = false;
                for (int d = 0; d < 4; d++)
                {
                    int nb = w.SphereNeighbor(i, d);
                    if (nb >= 0 && isOcean[nb] && w.Elevation[nb] <= -25) { touchesOpenOcean = true; break; }
                }
                if (!touchesOpenOcean) continue;

                short maxPeak = w.Elevation[i];
                float maxIce = lgmIceM[i];
                foreach (var p in FlatTectonics.SurfaceDisk(w, i, mountainRadius))
                {
                    if (w.Elevation[p.Tile] > maxPeak) maxPeak = w.Elevation[p.Tile];
                    if (lgmIceM[p.Tile] > maxIce) maxIce = lgmIceM[p.Tile];
                }
                if (maxPeak < 760 || maxIce < 25f) continue;
                if (fbm6[i] < -.12f) continue;

                float myScore = FMath.Unit((uint)i ^ (seed + 0x4108du));
                bool localBest = true;
                foreach (var p in FlatTectonics.SurfaceDisk(w, i, spacingRadius))
                {
                    if (p.Tile != i && FMath.Unit((uint)p.Tile ^ (seed + 0x4108du)) > myScore)
                    {
                        localBest = false;
                        break;
                    }
                }
                if (localBest) candidates.Add(i);
            }

            candidates.Sort((a, b) =>
            {
                int cmp = lgmIceM[b].CompareTo(lgmIceM[a]);
                return cmp != 0 ? cmp : a.CompareTo(b);
            });

            int carved = 0;
            for (int k = 0; k < candidates.Count && carved < maxFjordTiles; k++)
            {
                if ((k & 63) == 0) cancel.ThrowIfCancellationRequested();
                int curr = candidates[k];
                if (isOcean[curr]) continue;

                // Carve outer fjord sill -> deeper inner overdeepened glacial trough (`-75 .. -310 m`)
                for (int step = 0; step < maxChannelSteps && carved < maxFjordTiles; step++)
                {
                    uint hStep = FMath.Hash((uint)(curr * 7919 + step * 31) ^ seed);
                    float u = FMath.Unit(hStep);
                    int targetDepth = step == 0
                        ? -(int)Math.Round(70f + 95f * u)   // Outer glacial sill
                        : -(int)Math.Round(135f + 175f * u); // Overdeepened inner fjord basin

                    w.Elevation[curr] = (short)Math.Min((int)w.Elevation[curr], targetDepth);
                    isOcean[curr] = true;
                    carved++;

                    // Advance inland along the steepest incised coastal valley surrounded by higher fjord walls
                    int bestNext = -1;
                    float bestScore = -1e9f;
                    for (int d = 0; d < 4; d++)
                    {
                        int nb = w.SphereNeighbor(curr, d);
                        if (nb < 0 || isOcean[nb] || w.Elevation[nb] <= 0 || w.Elevation[nb] > 1150) continue;
                        if (absLatDeg[nb] > 72.5f) continue;

                        // Avoid merging into another ocean coast (prevent cutting artificial island straits)
                        int oceanTouches = 0;
                        short highestWall = w.Elevation[nb];
                        for (int d2 = 0; d2 < 4; d2++)
                        {
                            int nb2 = w.SphereNeighbor(nb, d2);
                            if (nb2 < 0) continue;
                            if (isOcean[nb2]) oceanTouches++;
                            if (w.Elevation[nb2] > highestWall) highestWall = w.Elevation[nb2];
                        }
                        if (oceanTouches > 1 || highestWall - w.Elevation[nb] < 180) continue;

                        float valleyScore = (highestWall - w.Elevation[nb]) - .65f * w.Elevation[nb] + 90f * fbm6[nb];
                        if (valleyScore > bestScore) { bestScore = valleyScore; bestNext = nb; }
                    }
                    if (bestNext < 0) break;
                    curr = bestNext;
                }
            }
        }

        // Gleba Stage 45d (`lakes::generate_glacial_lakes`): Carve rim-referenced glacial scour lakes on LGM shields
        static void CarveGlacialShieldLakes(
            WorldData w, bool[] isOcean, ushort[] oceanDist, float[] ux, float[] uy, float[] uz,
            float[] absLatDeg, float[] fbm6, float[] lgmIceM, float scale, uint seed, CancellationToken cancel)
        {
            int n = w.Count;
            var tec = w.Tectonics;
            int landCount = 0;
            for (int i = 0; i < n; i++) if (!isOcean[i] && w.Elevation[i] > 0) landCount++;
            if (landCount < 64) return;

            int maxGlacialLakeTiles = Math.Max(1, landCount / 220); // ~0.45% of land tiles on scoured upland shields
            int maxTilesPerLake = FMath.Clamp((int)Math.Round(1.4f * scale * scale + 1.0f), 1, 4);
            var blocked = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (isOcean[i] || w.Elevation[i] <= 0)
                {
                    blocked[i] = true;
                    for (int d = 0; d < 4; d++)
                    {
                        int nb = w.SphereNeighbor(i, d);
                        if (nb >= 0) blocked[nb] = true;
                    }
                }
            }

            var seeds = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (blocked[i] || oceanDist[i] < 3) continue;
                float aLat = absLatDeg[i];
                if (aLat < 46f || aLat > 70f) continue;
                short elev = w.Elevation[i];
                if (elev < 35 || elev > 460 || lgmIceM[i] < 18f) continue;

                // Prefer continental shields, Volga uplands, and raised platforms (`PLATE_OK` in Gleba 0x1808042d0)
                bool shieldOk = true;
                if (tec != null)
                {
                    var s = tec.Setting[i];
                    if (s == TectonicSetting.Collision || s == TectonicSetting.Subduction || s == TectonicSetting.IslandArc)
                        shieldOk = false;
                }
                if (!shieldOk) continue;

                short minNb = elev, maxNb = elev;
                int lowerNeighbors = 0;
                for (int d = 0; d < 4; d++)
                {
                    int nb = w.SphereNeighbor(i, d);
                    if (nb < 0) continue;
                    if (w.Elevation[nb] < minNb) minNb = w.Elevation[nb];
                    if (w.Elevation[nb] > maxNb) maxNb = w.Elevation[nb];
                    if (w.Elevation[nb] < elev) lowerNeighbors++;
                }
                // Place glacial scour lakes on shield interfluve benches (`lowerNeighbors >= 2`) rather than blocking trunk river thalwegs!
                if (maxNb - minNb > 260 || lowerNeighbors < 2) continue;

                float roll = FMath.Unit(FMath.Hash((uint)i ^ (seed + 0x61ac1a1u)));
                float prob = (tec != null && (tec.Province[i] & GlebaTectonicProvince.CratonShield) != 0) ? .085f : .042f;
                if (roll < prob) seeds.Add(i);
            }

            seeds.Sort((a, b) =>
            {
                int cmp = lgmIceM[b].CompareTo(lgmIceM[a]);
                return cmp != 0 ? cmp : a.CompareTo(b);
            });

            int totalCarved = 0;
            var lakeSet = new List<int>(8);
            for (int k = 0; k < seeds.Count && totalCarved < maxGlacialLakeTiles; k++)
            {
                int s = seeds[k];
                if (blocked[s]) continue;

                lakeSet.Clear();
                lakeSet.Add(s);
                short seedElev = w.Elevation[s];
                int targetSize = 1 + (int)(FMath.Unit(FMath.Hash((uint)(s * 4099) ^ seed)) * maxTilesPerLake);

                while (lakeSet.Count < targetSize)
                {
                    int bestNb = -1;
                    float bestPri = -1e9f;
                    for (int idx = 0; idx < lakeSet.Count; idx++)
                    {
                        int u = lakeSet[idx];
                        for (int d = 0; d < 4; d++)
                        {
                            int nb = w.SphereNeighbor(u, d);
                            if (nb < 0 || blocked[nb] || oceanDist[nb] < 3 || lakeSet.Contains(nb)) continue;
                            if (Math.Abs(w.Elevation[nb] - seedElev) > 95) continue;
                            float chord = (ux[nb] - ux[s]) * (ux[nb] - ux[s])
                                        + (uy[nb] - uy[s]) * (uy[nb] - uy[s])
                                        + (uz[nb] - uz[s]) * (uz[nb] - uz[s]);
                            float pri = (fbm6[nb] + 1f) * .35f - (float)Math.Sqrt(chord);
                            if (pri > bestPri) { bestPri = pri; bestNb = nb; }
                        }
                    }
                    if (bestNb < 0) break;
                    lakeSet.Add(bestNb);
                }

                // Rim-referenced bed carving (`min_rim_elev` in Gleba `0x180809320`):
                // Every lake tile is carved strictly below the lowest surrounding non-lake land rim,
                // while staying >= 5 m above sea level so DetectWater never mistakes it for marine water.
                short minRim = short.MaxValue;
                for (int idx = 0; idx < lakeSet.Count; idx++)
                {
                    int u = lakeSet[idx];
                    for (int d = 0; d < 4; d++)
                    {
                        int nb = w.SphereNeighbor(u, d);
                        if (nb >= 0 && !lakeSet.Contains(nb) && w.Elevation[nb] < minRim)
                            minRim = w.Elevation[nb];
                    }
                }
                if (minRim <= 18 || minRim == short.MaxValue) continue;

                for (int idx = 0; idx < lakeSet.Count; idx++)
                {
                    int t = lakeSet[idx];
                    float r = FMath.Unit(FMath.Hash((uint)(t * 13107 + idx) ^ seed));
                    int depth = (int)Math.Round(14f + 42f * r);
                    short carvedBed = (short)Math.Max(5, minRim - depth);
                    if (carvedBed < w.Elevation[t]) w.Elevation[t] = carvedBed;
                    blocked[t] = true;
                    for (int d = 0; d < 4; d++)
                    {
                        int nb = w.SphereNeighbor(t, d);
                        if (nb >= 0) blocked[nb] = true; // Leave >= 1-tile land isthmus between lakes
                    }
                    totalCarved++;
                }
            }
        }

        // Gleba Stage 51 (`coral_reefs::generate_coral_reefs`, `0x180a4b1a0`):
        // Shoals warm tropical submarine shelves & hotspot guyots into shallow limestone reefs (-14..-3 m).
        static void ShoalTropicalCoralReefs(
            WorldData w, bool[] isOcean, ushort[] landDist, float[] ux, float[] uy, float[] uz,
            float[] absLatDeg, float[] fbm6, float[] estColdestC, float cellKm, uint seed, CancellationToken cancel)
        {
            int n = w.Count;
            var tec = w.Tectonics;
            var candidates = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (!isOcean[i]) continue;
                short elev = w.Elevation[i];
                if (elev < -680 || elev > -18) continue;
                if (absLatDeg[i] > 26.5f || estColdestC[i] < 18.0f) continue;
                candidates.Add(i);
            }
            if (candidates.Count == 0) return;

            int numReefs = FMath.Clamp((int)Math.Round(candidates.Count / 85.0), 1, 36);
            int maxBudget = FMath.Clamp((int)Math.Round(1800f / Math.Max(12f, cellKm)), 2, 28);
            var visited = new HashSet<int>();
            var heap = new FloodHeap();

            for (int r = 0; r < numReefs && candidates.Count > 0; r++)
            {
                if ((r & 15) == 0) cancel.ThrowIfCancellationRequested();
                uint hPick = FMath.Hash((uint)(r * 2654435761u) ^ (seed + 0xc08a1u));
                int pick = (int)(hPick % (uint)candidates.Count);
                int reefSeed = candidates[pick];
                candidates[pick] = candidates[candidates.Count - 1];
                candidates.RemoveAt(candidates.Count - 1);

                float seedElev = w.Elevation[reefSeed];
                int budget = 1 + (int)(FMath.Unit(hPick ^ 0x55aau) * maxBudget);
                bool isHotspotGuyot = tec != null && (tec.Province[reefSeed] & GlebaTectonicProvince.HawaiianChain) != 0 && landDist[reefSeed] >= 3;
                bool placedAtollCay = false;

                visited.Clear();
                while (heap.Count > 0) heap.Pop(out _);
                heap.Push(reefSeed, 0);

                while (heap.Count > 0 && visited.Count < budget)
                {
                    int curr = heap.Pop(out _);
                    if (!visited.Add(curr)) continue;

                    uint hTile = FMath.Hash((uint)curr ^ (seed + 0x9eef1u));
                    float targetDepth = -5f + 3.5f * FMath.Unit(hTile); // [-5.0, -1.5) m
                    float elevOld = w.Elevation[curr];
                    // Gleba 97.5% pull toward shallow limestone reef flat (`d_target + 0.025 * (elev_old - d_target)`)
                    float elevNew = targetDepth + .025f * (elevOld - targetDepth);
                    w.Elevation[curr] = (short)FMath.Clamp((int)Math.Round(elevNew), -45, -2);

                    // Rare 1-tile emergent coral cay (`+2..+4 m`) on isolated offshore hotspot atolls (`!was_coast` in Gleba)
                    if (isHotspotGuyot && !placedAtollCay && landDist[curr] >= 4 && elevNew > -10f && FMath.Unit(hTile ^ 0xca7u) < .18f)
                    {
                        bool anyLandNb = false;
                        for (int d = 0; d < 4; d++)
                        {
                            int nb = w.SphereNeighbor(curr, d);
                            if (nb >= 0 && !isOcean[nb]) { anyLandNb = true; break; }
                        }
                        if (!anyLandNb)
                        {
                            w.Elevation[curr] = (short)(2 + (int)(2.5f * FMath.Unit(hTile ^ 0x33u)));
                            isOcean[curr] = false;
                            placedAtollCay = true;
                        }
                    }

                    if (visited.Count >= budget) break;
                    for (int d = 0; d < 4; d++)
                    {
                        int nb = w.SphereNeighbor(curr, d);
                        if (nb < 0 || visited.Contains(nb) || !isOcean[nb]) continue;
                        if (w.Elevation[nb] <= -680 || estColdestC[nb] < 18.0f || absLatDeg[nb] > 27f) continue;

                        float dot = FMath.Clamp(ux[reefSeed] * ux[nb] + uy[reefSeed] * uy[nb] + uz[reefSeed] * uz[nb], -1f, 1f);
                        float distKm = (float)(Math.Acos(dot) * 6371.0);
                        // Gleba contour-following priority: `2.1 * |seed_elev - nb_elev| - dist_km + 2.2 * fbm6`
                        float priority = 2.1f * Math.Abs(seedElev - w.Elevation[nb]) - distKm + 2.2f * fbm6[nb];
                        heap.Push(nb, (long)Math.Round(-priority * 100.0));
                    }
                }
            }
        }

        static ushort[] ComputeOceanHopDistance(WorldData w, bool[] isOcean, CancellationToken cancel)
        {
            int n = w.Count;
            var dist = new ushort[n];
            var q = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                if (isOcean[i]) { dist[i] = 0; q.Enqueue(i); }
                else dist[i] = ushort.MaxValue;
            }
            while (q.Count > 0)
            {
                int u = q.Dequeue();
                int next = Math.Min(65534, dist[u] + 1);
                for (int d = 0; d < 4; d++)
                {
                    int v = w.SphereNeighbor(u, d);
                    if (v >= 0 && dist[v] > next) { dist[v] = (ushort)next; q.Enqueue(v); }
                }
            }
            return dist;
        }

        static ushort[] ComputeLandHopDistance(WorldData w, bool[] isOcean, CancellationToken cancel)
        {
            int n = w.Count;
            var dist = new ushort[n];
            var q = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                if (!isOcean[i] && w.Elevation[i] > 0) { dist[i] = 0; q.Enqueue(i); }
                else dist[i] = ushort.MaxValue;
            }
            while (q.Count > 0)
            {
                int u = q.Dequeue();
                int next = Math.Min(65534, dist[u] + 1);
                for (int d = 0; d < 4; d++)
                {
                    int v = w.SphereNeighbor(u, d);
                    if (v >= 0 && dist[v] > next) { dist[v] = (ushort)next; q.Enqueue(v); }
                }
            }
            return dist;
        }

        static float Smooth(float a, float b, float x)
        {
            float t = FMath.Clamp((x - a) / (b - a), 0f, 1f);
            return t * t * (3f - 2f * t);
        }
    }
}
