using System;
using System.Collections.Generic;
using System.Threading;

namespace Orbis.Flat
{
    internal static class FlatBasinStorage
    {
        // The priority flood supplies an upper bound. A closed depression needs
        // a water source to occupy any part of that capacity.
        internal static bool Resolve(WorldData w, short[] spill, CancellationToken cancel)
        {
            var reference = w.WaterReference;
            bool model = reference == null;
            if (!model)
                for (int i = 0; i < w.Count && !model; i++)
                    model = w.Elevation[i] != reference.OriginalBed(i);
            short[] modelled = model ? Estimate(w, spill, cancel) : null;
            bool changed = false;
            for (int i = 0; i < w.Count; i++)
            {
                if ((i & 4095) == 0) cancel.ThrowIfCancellationRequested();
                if (FlatTerrainAnalysis.Ocean(w.Water[i])) { w.WaterLevel[i] = 0; continue; }
                short bed = w.Elevation[i], stage = bed;
                bool lake = false;
                if (reference != null)
                {
                    int surveyed = reference.At(i);
                    // A lowered outlet can drain a mapped lake. A raised bed
                    // excludes its displaced water, including an edited dam.
                    stage = (short)Math.Min(surveyed, spill[i]);
                    lake = surveyed >= bed && stage >= bed &&
                        (spill[i] > bed || bed == surveyed);
                }
                if (modelled != null && modelled[i] > bed && (!lake || modelled[i] > stage))
                {
                    stage = modelled[i];
                    lake = true;
                }
                changed |= lake != (w.Water[i] == WaterKind.Lake) || (lake && stage != w.WaterLevel[i]);
                w.Water[i] = lake ? WaterKind.Lake : WaterKind.Land;
                w.WaterLevel[i] = lake ? stage : bed;
            }
            return changed;
        }

        static short[] Estimate(WorldData w, short[] spill, CancellationToken cancel)
        {
            var basin = new int[w.Count];
            var queue = new int[w.Count];
            var groups = new List<List<int>> { null };
            var enabled = new List<bool> { false };
            var raisedOutlet = new List<bool> { false };
            for (int start = 0; start < w.Count; start++)
            {
                if ((start & 4095) == 0) cancel.ThrowIfCancellationRequested();
                if (basin[start] != 0 || spill[start] <= w.Elevation[start] || FlatTerrainAnalysis.Ocean(w.Water[start])) continue;
                var cells = new List<int>();
                bool useClimate = w.WaterReference == null, newSill = false;
                int id = groups.Count, tail = 1;
                basin[start] = id;
                queue[0] = start;
                for (int head = 0; head < tail; head++)
                {
                    int i = queue[head];
                    cells.Add(i);
                    if (w.WaterReference != null)
                        useClimate |= w.Elevation[i] < w.WaterReference.OriginalBed(i) ||
                            w.WaterReference.At(i) == FlatWaterReference.Marine;
                    for (int d = 0; d < 4; d++)
                    {
                        int n = w.SphereNeighbor(i, d);
                        if (w.WaterReference != null && w.Elevation[n] == spill[start] &&
                            w.WaterReference.OriginalBed(n) < w.Elevation[n]) newSill = true;
                        if (basin[n] != 0 || spill[n] != spill[start] || w.Elevation[n] >= spill[n] ||
                            FlatTerrainAnalysis.Ocean(w.Water[n])) continue;
                        basin[n] = id;
                        queue[tail++] = n;
                    }
                }
                groups.Add(cells);
                enabled.Add(useClimate || newSill);
                raisedOutlet.Add(newSill);
            }
            var level = (short[])w.Elevation.Clone();
            if (groups.Count == 1 || !enabled.Contains(true)) return level;
            // Resolve each tile's first receiving depression on the provisional
            // spill tree before charging rainfall to a catchment.
            for (int k = 0; k < w.Count; k++)
            {
                int i = w.DrainOrder[k], parent = w.Downstream[i];
                if (basin[i] == 0 && parent >= 0) basin[i] = basin[parent];
            }
            var available = new double[groups.Count];
            var loss = new double[w.Count];
            for (int y = 0; y < w.Height; y++)
            {
                cancel.ThrowIfCancellationRequested();
                double area = FlatRiverDischarge.RelativeArea(y, w.Height);
                double lat = ((y + .5) / w.Height - .5) * Math.PI;
                var pet = new double[12];
                for (int m = 0; m < 12; m++) pet[m] = FlatRiverDischarge.PetPerDegree(lat, m);
                for (int x = 0; x < w.Width; x++)
                {
                    int i = y * w.Width + x, id = basin[i];
                    if (!enabled[id]) continue;
                    double rain = 0, snow = 0, melt = 0, demand = 0;
                    for (int m = 0; m < 12; m++)
                    {
                        int off = i * 12 + m;
                        var month = new FlatRiverDischarge.ClimateMonth(w.Precipitation[off], w.Temperature[off], pet[m]);
                        rain += month.Rain; snow += month.Snow; melt += month.MeltCapacity; demand += month.PotentialEvaporation;
                    }
                    double liquid = rain + Math.Min(melt, snow);
                    // Exposed land exports climatic excess and a small fraction
                    // of storm rain. Open water instead pays the full PET debt.
                    double runoff = Math.Max(0, liquid - demand) + .08 * Math.Min(liquid, demand);
                    available[id] += area * runoff;
                    loss[i] = area * (runoff + demand - liquid);
                }
            }
            for (int id = 1; id < groups.Count; id++)
            {
                if (!enabled[id] || available[id] <= 0) continue;
                var cells = groups[id];
                cells.Sort((a, b) => w.Elevation[a] != w.Elevation[b] ? w.Elevation[a].CompareTo(w.Elevation[b]) : a.CompareTo(b));
                short stage = spill[cells[0]];
                double balance = available[id];
                for (int k = 0; k < cells.Count;)
                {
                    int first = cells[k];
                    do { balance -= loss[cells[k++]]; }
                    while (k < cells.Count && w.Elevation[cells[k]] == w.Elevation[first]);
                    if (balance < 0) { stage = w.Elevation[first]; break; }
                }
                foreach (int i in cells)
                {
                    short allowed = stage;
                    if (w.WaterReference != null && !raisedOutlet[id])
                    {
                        // Digging one part of an existing depression adds only
                        // its excavated capacity. Other surveyed banks keep
                        // their existing stage until a real outlet changes.
                        allowed = w.WaterReference.At(i) == FlatWaterReference.Marine
                            ? Math.Min(stage, (short)0) : Math.Min(stage, w.WaterReference.OriginalBed(i));
                    }
                    if (w.Elevation[i] < allowed) level[i] = allowed;
                }
            }
            return level;
        }
    }
}
