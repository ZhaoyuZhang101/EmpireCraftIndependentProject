using System;
using System.Collections.Generic;
using Orbis.Flat;

internal static class PolarTerrainRegression
{
    const int Width = 256, Height = Width / 2;
    static readonly string[] Seeds = { "MERIDIAN", "SOUTHERN OCEAN", "ORBITAL RIDGES", "POLAR SHELF",
        "POLAR-LAND-13", "POLAR-LAND-17", "POLAR-LAND-25", "POLAR-LAND-26" };

    // Mean absolute elevation differences on equally long great-circle chords.
    // Include only land or only seabed endpoints when measuring that material.
    sealed class Changes
    {
        double first, second;
        public int Count { get; private set; }
        public void Add(double a, double b)
        {
            first += Math.Abs(a);
            second += Math.Abs(b);
            Count++;
        }
        public string Example;
        public int HighestInterior;
        public double Ratio => first / second;
        public double MeanChange => (first + second) / (2 * Count);
    }

    sealed class Sample
    {
        public readonly Changes[] Terrain = { new Changes(), new Changes() };
        public readonly Changes[] Pole = { new Changes(), new Changes() };
        public readonly Changes Seam = new Changes(), Equator = new Changes();
        public double Area, Land, CoastLength, Near, NearLand, Far, FarLand, HighArea, HighLand;
    }

    static void Measure(WorldData world, Sample sample, Changes[] land, Changes[] bed)
    {
        double step = Math.PI / Height;
        for (int y = 0; y < Height; y++)
        {
            double lat = (y + .5) * step - Math.PI / 2;
            double weight = Math.Cos(lat);
            double northEdge = step * Math.Cos(lat + step / 2);
            int hemisphere = y < Height / 2 ? 0 : 1;
            bool high = Math.Abs(lat) >= 75 * Math.PI / 180 && Math.Abs(lat) <= 85 * Math.PI / 180;
            // Choose the closest pair of cell centres on this parallel, then
            // correct for the residual distance caused by rounding to cells.
            int dx = high ? (int)Math.Round(Math.Asin(Math.Sin(step) / weight) / step) : 1;
            double ewDistance = 2 * Math.Asin(weight * Math.Sin(dx * step));
            var height = world.Elevation;
            int row = y * Width;
            double seam = height[row] - height[row + Width - 1];
            sample.Seam.Add(seam, .5 * (Math.Abs(height[row + 1] - height[row]) +
                Math.Abs(height[row + Width - 1] - height[row + Width - 2])));
            for (int x = 0; x < Width; x++)
            {
                int i = row + x;
                bool dry = height[i] > 0;
                sample.Area += weight;
                if (dry) sample.Land += weight;
                if (dry != (height[world.Index(x + 1, y)] > 0)) sample.CoastLength += step;
                if (y + 1 < Height && dry != (height[i + Width] > 0)) sample.CoastLength += northEdge;
                double centreDot = Math.Cos((x + .5 - Width / 2) * step) * weight;
                if (centreDot > .9) { sample.Near += weight; if (dry) sample.NearLand += weight; }
                if (centreDot < -.5) { sample.Far += weight; if (dry) sample.FarLand += weight; }
                if (y == 0 || y == Height - 1)
                {
                    int opposite = world.SphereNeighbor(i, hemisphere == 0 ? 2 : 3);
                    int nextRow = i + (hemisphere == 0 ? Width : -Width);
                    sample.Pole[hemisphere].Add(height[opposite] - height[i], height[nextRow] - height[i]);
                }
                if (y == Height / 2 - 1)
                    sample.Equator.Add(height[i + Width] - height[i], .5 * (Math.Abs(height[i] - height[i - Width]) +
                        Math.Abs(height[i + 2 * Width] - height[i + Width])));
                if (!high) continue;
                sample.HighArea += weight;
                if (dry) sample.HighLand += weight;
                int west = height[world.Index(x - dx, y)], east = height[world.Index(x + dx, y)];
                int south = height[i - Width], north = height[i + Width];
                double ew = (east - west) * (2 * step / ewDistance), ns = north - south;
                sample.Terrain[hemisphere].Add(ew, ns);
                int interior = Math.Min(height[i], Math.Min(Math.Min(west, east), Math.Min(south, north)));
                if (interior > 0)
                {
                    var material = land[hemisphere];
                    material.Add(ew, ns);
                    if (interior > material.HighestInterior)
                    {
                        material.HighestInterior = interior;
                        double lon = ((x + .5) / Width - .5) * 360;
                        material.Example = $"{world.Seed} {world.Preset} tile {i} ({lon:F3}, {lat * 180 / Math.PI:F3}) elev {height[i]} m";
                    }
                }
                if (!dry && west < 0 && east < 0 && south < 0 && north < 0) bed[hemisphere].Add(ew, ns);
            }
        }
    }

    static void Range(List<string> failures, string label, double value, double min, double max)
    {
        if (!(value >= min && value <= max)) failures.Add($"{label} {value:F3} outside [{min:F2}, {max:F2}]");
    }

    static void VolcanoFootprints(List<string> failures)
    {
        var world = new WorldData(Width, Height, "POLAR-VOLCANO", TerrainPreset.Continents);
        var bed = new float[world.Count];
        foreach (int row in new[] { 0, 6, Height - 7, Height - 1 })
        {
            Array.Clear(bed, 0, bed.Length);
            int tile = world.Index(0, row); // Centre on the dateline.
            FlatTectonics.Cone(world, bed, tile, 1000f, 2f);
            double step = Math.PI / Height, lat = (row + .5) * step - Math.PI / 2;
            // Equal angular distance east/west and towards the equator. In the
            // last half-cell ring the first sample lies across the physical pole.
            int dx = (int)Math.Round(2 * Math.Asin(Math.Min(1, Math.Sin(step / 2) / Math.Cos(lat))) / step);
            double ground = 2 * Math.Asin(Math.Cos(lat) * Math.Sin(dx * step / 2)) / step;
            double expected = 1000 * Math.Exp(-1.8 * ground * ground / 4);
            foreach (int offset in new[] { dx, -dx })
                Range(failures, "Volcano at row " + row + " geodesic flank", bed[world.Index(offset, row)] / expected, .995, 1.005);
            Range(failures, "Volcano radial flank", bed[world.Index(0, row < Height / 2 ? row + 1 : row - 1)] / (1000 * Math.Exp(-1.8 / 4)), .995, 1.005);
        }
    }

    public static void Run()
    {
        var land = new[] { new Changes(), new Changes() };
        var bed = new[] { new Changes(), new Changes() };
        var failures = new List<string>();
        var coasts = new Dictionary<TerrainPreset, double>();
        VolcanoFootprints(failures);
        foreach (TerrainPreset preset in FlatGenerator.ProceduralPresets)
        {
            var sample = new Sample();
            foreach (string seed in Seeds)
                Measure(FlatGenerator.Generate(Width, Height, seed, preset, default), sample, land, bed);
            double dry = sample.Land / sample.Area, near = sample.NearLand / sample.Near, far = sample.FarLand / sample.Far;
            coasts[preset] = sample.CoastLength / sample.Land;
            Console.WriteLine($"{preset,-14} EW/NS at 75-85 deg S/N {sample.Terrain[0].Ratio:F2}/{sample.Terrain[1].Ratio:F2}, " +
                $"cross-pole/interior S/N {sample.Pole[0].Ratio:F2}/{sample.Pole[1].Ratio:F2}, seam {sample.Seam.Ratio:F2}, " +
                $"equator {sample.Equator.Ratio:F2}, area-weighted land {dry:P1} (centre {near:P1}, far side {far:P1}, 75-85 deg {sample.HighLand / sample.HighArea:P1})");
            for (int h = 0; h < 2; h++)
            {
                string where = preset + (h == 0 ? " south" : " north");
                Range(failures, where + " terrain EW/NS", sample.Terrain[h].Ratio, .55, 1.8);
                Range(failures, where + " polar closure", sample.Pole[h].Ratio, .45, 2.25);
            }
            // The meridian and equator should join as smoothly as their direct neighbours.
            Range(failures, preset + " dateline", sample.Seam.Ratio, .55, 1.8);
            Range(failures, preset + " equator", sample.Equator.Ratio, .55, 1.8);
            double target = preset == TerrainPreset.Archipelago ? .12 : preset == TerrainPreset.GreatContinent ? .33 :
                preset == TerrainPreset.InlandSea ? .40 : .29;
            Range(failures, preset + " area-weighted land fraction", dry, target - .055, target + .055);
            // The supercontinent's centre follows the seeded plate poles. Only
            // InlandSea prescribes an enclosed basin on the prime meridian.
            if (sample.HighLand / sample.HighArea >= dry) failures.Add(preset + " lost its polar ocean bias.");
            if (preset == TerrainPreset.InlandSea)
            {
                Range(failures, "Inland sea central ocean", near, 0, .2);
                if (far <= dry) failures.Add("Inland sea far side must be drier than the world as a whole.");
            }
        }
        if (coasts[TerrainPreset.Archipelago] < 1.35 * coasts[TerrainPreset.Continents])
            failures.Add("Archipelago must have more coastline per unit of land than the broad continents preset.");
        for (int h = 0; h < 2; h++)
        {
            string hemisphere = h == 0 ? "South" : "North";
            Console.WriteLine($"{hemisphere} equal-distance land EW/NS {land[h].Ratio:F2} ({land[h].Count} points), " +
                $"seabed {bed[h].Ratio:F2} ({bed[h].Count} points); mean land/seabed change {land[h].MeanChange:F0}/{bed[h].MeanChange:F0} m");
            Console.WriteLine(hemisphere + " reproducible interior: " + land[h].Example);
            Range(failures, hemisphere + " land EW/NS", land[h].Ratio, .6, 1.65);
            Range(failures, hemisphere + " seabed EW/NS", bed[h].Ratio, .6, 1.65);
            if (land[h].Count < 200 || bed[h].Count < 200) failures.Add(hemisphere + " lacks independent high-latitude land or bed coverage.");
            if (land[h].MeanChange < 35 || bed[h].MeanChange < 35)
                failures.Add(hemisphere + " loses its land or ocean relief over a 2.8-degree arc.");
        }
        if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
        Console.WriteLine("PASS generated elevations, seabed, preset shapes, equator, dateline and both poles");
    }
}
