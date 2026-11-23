using System;
using System.Threading;

namespace Orbis.Flat
{
    public struct ClimateTileDiagnostics
    {
        public int Tile, Region;
        public FlatClimateObservation Observation;
        public ClimateScore Weather, Water;
        public ClimateSignals Flags => Weather.Signals | Water.Signals;
        public FlatBiomeEstimate Biome;
        public ClimateReference WeatherReference, WaterReference;
    }

    // One instance belongs to one worker. Consumers publish completed copies of
    // Cells. All reads below are from terrain-derived arrays and a captured storm.
    public sealed class FlatClimateStatistics
    {
        const int RegionSide = 4;
        enum Group { LandAir, MarineAir, LakeAir, Soil, River, Lake, Outlet, Count }
        readonly WorldData world;
        readonly bool atmosphereOnly;
        readonly FlatBiomeStatistics suppliedBiomes;
        readonly ClimateFeatureSet[] profiles;
        readonly ClimateScoreWorkspace work = new ClimateScoreWorkspace();
        readonly FlatBiomeScratch biomeWork = new FlatBiomeScratch();
        readonly int regionsX, regionsY;
        readonly int[][] neighbors;
        readonly ClimateReference[,] partitions, references;
        readonly bool[] dirtyPartitions, dirtyFits;
        readonly int[] tiles, riverGauges, lakeGauges;
        readonly double[] weights;
        readonly FlatBiomeEstimate[] biomeCells;
        FlatClimateSamples samples;
        FlatBiomeStatistics biomes;
        int terrainRevision, referenceMonth = -1;
        long lastTick = long.MinValue;
        bool dirty, lastHadStorms;
        FlatCycloneTrack north, south, previousNorth, previousSouth;

        public int Width { get; }
        public int Height { get; }
        public int RegionCount => regionsX * regionsY;
        public int RegionFits { get; private set; }
        public ClimateTileDiagnostics[] Cells { get; }

        public FlatClimateStatistics(WorldData world, CancellationToken cancel = default, int width = 64,
            FlatBiomeStatistics biomes = null, ClimateFeatureSet weather = null, ClimateFeatureSet landWater = null,
            ClimateFeatureSet riverWater = null, ClimateFeatureSet lakeWater = null, ClimateFeatureSet lakeOutlet = null,
            bool atmosphereOnly = false)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.atmosphereOnly = atmosphereOnly;
            if (width < 8 || width > Math.Min(128, world.Width) || width % 8 != 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Use a multiple of eight within the source grid, up to 128.");
            Width = width;
            Height = width / 2;
            regionsX = Width / RegionSide;
            regionsY = Height / RegionSide;
            suppliedBiomes = biomes;
            weather = weather ?? ClimateFeatureSet.Weather;
            profiles = new[] {weather, weather, weather, landWater ?? ClimateFeatureSet.LandWater,
                riverWater ?? ClimateFeatureSet.RiverWater, lakeWater ?? ClimateFeatureSet.LakeWater,
                lakeOutlet ?? ClimateFeatureSet.LakeOutlet};
            Cells = new ClimateTileDiagnostics[Width * Height];
            tiles = new int[Cells.Length];
            riverGauges = new int[Cells.Length];
            lakeGauges = new int[Cells.Length];
            biomeCells = new FlatBiomeEstimate[Cells.Length];
            weights = new double[Height];
            int groups = atmosphereOnly ? (int)Group.Soil : (int)Group.Count;
            partitions = new ClimateReference[RegionCount, groups];
            references = new ClimateReference[RegionCount, groups];
            dirtyPartitions = new bool[RegionCount];
            dirtyFits = new bool[RegionCount];
            neighbors = new int[RegionCount][];
            for (int r = 0; r < RegionCount; r++) neighbors[r] = Neighbors(r);
            for (int y = 0; y < Height; y++)
            {
                weights[y] = FlatRiverDischarge.RelativeArea(y, Height);
                for (int x = 0; x < Width; x++)
                    tiles[y*Width+x] = world.Index((2*x+1)*world.Width/(2*Width), (2*y+1)*world.Height/(2*Height));
            }
            RebuildTerrain(cancel);
        }

        int CellOf(int tile)
        {
            int x = (2*(tile % world.Width)+1)*Width/(2*world.Width);
            int y = (2*(tile / world.Width)+1)*Height/(2*world.Height);
            return y*Width+x;
        }

        int RegionOfCell(int cell) => cell / Width / RegionSide * regionsX + cell % Width / RegionSide;

        int[] Neighbors(int region)
        {
            var result = new int[9];
            int n = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int y = region / regionsX + dy, x = region % regionsX + dx;
                if (y < 0 || y >= regionsY)
                {
                    y = y < 0 ? -y-1 : 2*regionsY-y-1;
                    x += regionsX/2;
                }
                int other = y*regionsX + FMath.Wrap(x,regionsX);
                if (other != region && Array.IndexOf(result, other, 0, n) < 0) result[n++] = other;
            }
            Array.Resize(ref result, n);
            return result;
        }

        void RebuildTerrain(CancellationToken cancel)
        {
            // TerrainRevision is distinct from the frequently changing economy revision.
            samples = new FlatClimateSamples(world, includeWaterBudget: !atmosphereOnly);
            if (!atmosphereOnly) biomes = suppliedBiomes ?? FlatBiomeStatistics.Calibrate(world, cancel);
            Array.Fill(riverGauges, -1);
            Array.Fill(lakeGauges, -1);
            var gauge = world.Facts?.RiverDischarge;
            if (gauge != null)
            {
                var riverDistance = new double[Cells.Length];
                var lakeDistance = new double[Cells.Length];
                for (int y = 0; y < world.Height; y++)
                {
                    cancel.ThrowIfCancellationRequested();
                    double cos = Math.Cos(((y+.5)/world.Height-.5)*Math.PI);
                    for (int x = 0; x < world.Width; x++)
                    {
                        int tile = y*world.Width+x;
                        if (!gauge.HasStation(tile)) continue;
                        int cell = CellOf(tile);
                        bool lake = world.Water[tile] == WaterKind.Lake;
                        if (atmosphereOnly && !lake) continue;
                        var stations = lake ? lakeGauges : riverGauges;
                        var distances = lake ? lakeDistance : riverDistance;
                        double dx = ((x+.5)/world.Width - (cell%Width+.5)/Width)*2*cos;
                        double dy = (y+.5)/world.Height - (cell/Width+.5)/Height;
                        double d2 = dx*dx + dy*dy;
                        if (stations[cell] >= 0 && d2 >= distances[cell]) continue;
                        stations[cell] = tile;
                        distances[cell] = d2;
                    }
                }
            }
            for (int i = 0; biomes != null && i < tiles.Length; i++)
            {
                if (i % Width == 0) cancel.ThrowIfCancellationRequested();
                biomeCells[i] = biomes.Estimate(world, tiles[i], biomeWork);
            }
            terrainRevision = world.TerrainRevision;
            InvalidateAll();
        }

        void InvalidateAll()
        {
            dirty = true;
            Array.Fill(dirtyPartitions, true);
            Array.Fill(dirtyFits, true);
            lastTick = long.MinValue;
        }

        public void InvalidateRegion(int nativeTile)
        {
            if ((uint)nativeTile >= (uint)world.Count) throw new ArgumentOutOfRangeException(nameof(nativeTile));
            int region = RegionOfCell(CellOf(nativeTile));
            dirty = true;
            dirtyPartitions[region] = true;
            dirtyFits[region] = true;
            foreach (int other in neighbors[region]) dirtyFits[other] = true;
        }

        static Group Surface(WaterKind water) => FlatTerrainAnalysis.Ocean(water) ? Group.MarineAir :
            water == WaterKind.Lake ? Group.LakeAir : Group.LandAir;

        ClimateReference NewReference(Group group) =>
            new ClimateReference(profiles[(int)group], waterBudget: group >= Group.Soil);

        void Train(int region, Group group, in FlatClimateObservation row, double weight)
        {
            var partition = partitions[region,(int)group];
            if (partition == null) partitions[region,(int)group] = partition = NewReference(group);
            partition.Add(row, weight, work);
        }

        void BuildPartition(int region, int month)
        {
            for (int group = 0; group < partitions.GetLength(1); group++) partitions[region,group] = null;
            int y0 = region / regionsX * RegionSide, x0 = region % regionsX * RegionSide;
            for (int y = y0; y < y0+RegionSide; y++)
            for (int x = x0; x < x0+RegionSide; x++)
            {
                int cell = y*Width+x, tile = tiles[cell];
                double weight = weights[y];
                var air = samples.Normal(tile, month);
                Group surface = Surface(world.Water[tile]);
                Train(region, surface, air, weight);
                int outlet = lakeGauges[cell];
                // Lake outlets also contribute air observations on coasts whose grid centre is dry.
                if (surface != Group.LakeAir && outlet >= 0)
                    Train(region, Group.LakeAir, samples.Normal(outlet, month), weight);
                if (atmosphereOnly) continue;
                if (surface != Group.MarineAir)
                    Train(region, surface == Group.LakeAir ? Group.Lake : Group.Soil, air, weight);
                if (riverGauges[cell] >= 0) Train(region, Group.River, samples.Normal(riverGauges[cell], month), weight);
                if (outlet < 0) continue;
                var lake = samples.Normal(outlet, month);
                Train(region, Group.Outlet, lake, weight);
                // At most one lake site per coarse cell, including cells whose centre is dry.
                if (surface != Group.LakeAir)
                    Train(region, Group.Lake, lake, weight);
            }
        }

        void FitRegion(int region)
        {
            for (int g = 0; g < references.GetLength(1); g++)
            {
                var own = partitions[region,g];
                var fit = NewReference((Group)g);
                if (own != null) fit.Merge(own);
                // Pool only the same surface/site type. A sparse stratum can use the
                // adjacent geographic ring, never a global or cross-surface substitute.
                if (fit.EffectiveCount < 2 * (profiles[g].Dimension+1))
                    foreach (int other in neighbors[region])
                    {
                        var part = partitions[other,g];
                        if (part != null) fit.Merge(part);
                    }
                fit.Fit();
                references[region,g] = fit.Count > 0 ? fit : null;
            }
            RegionFits++;
        }

        // The tracker has at most two lows and publishes new endpoints once per
        // six-hour tick. Value equality also handles replacing a saved snapshot at that tick.
        static bool SameTrack(in FlatCycloneTrack a, in FlatCycloneTrack b) =>
            a.Tracked == b.Tracked && a.U == b.U && a.V == b.V &&
            a.PressureDeficit == b.PressureDeficit && a.WindSpeed == b.WindSpeed;

        public bool Refresh(double day, FlatCycloneWeather captured, CancellationToken cancel = default)
        {
            if (!OnlineClimateMoments.Finite(day) || Math.Abs(day) > 1e15) return false;
            cancel.ThrowIfCancellationRequested();
            if (terrainRevision != world.TerrainRevision) RebuildTerrain(cancel);
            // Preserve the containing calendar month even when the tracker
            // rounds a float time infinitesimally before midnight to its next step.
            long tick = (long)Math.Floor(day * 4);
            bool hasStorms = captured != null && captured.StepIndex == tick;
            bool changed = tick != lastTick || hasStorms != lastHadStorms || (hasStorms &&
                (!SameTrack(captured.North,north) || !SameTrack(captured.South,south) ||
                 !SameTrack(captured.PreviousNorth,previousNorth) || !SameTrack(captured.PreviousSouth,previousSouth)));
            if (!dirty && !changed) return true;
            double tickDay = tick * .25;
            int month = new FlatMonthlyForcing(tickDay).Month;
            if (month != referenceMonth) InvalidateAll();
            try
            {
                if (dirty)
                {
                    for (int r = 0; r < RegionCount; r++) if (dirtyPartitions[r])
                    {
                        cancel.ThrowIfCancellationRequested();
                        BuildPartition(r, month);
                    }
                    for (int r = 0; r < RegionCount; r++) if (dirtyFits[r])
                    {
                        cancel.ThrowIfCancellationRequested();
                        FitRegion(r);
                    }
                }
                for (int i = 0; i < Cells.Length; i++)
                {
                    if (i % Width == 0) cancel.ThrowIfCancellationRequested();
                    int region = RegionOfCell(i);
                    if (changed || dirtyFits[region]) Cells[i] = Diagnose(tiles[i], region, tickDay, captured, biomeCells[i]);
                }
            }
            catch (OperationCanceledException)
            {
                InvalidateAll();
                throw;
            }
            Array.Clear(dirtyPartitions, 0, RegionCount);
            Array.Clear(dirtyFits, 0, RegionCount);
            dirty = false;
            referenceMonth = month;
            lastTick = tick;
            lastHadStorms = hasStorms;
            if (hasStorms)
            {
                north = captured.North; south = captured.South;
                previousNorth = captured.PreviousNorth; previousSouth = captured.PreviousSouth;
            }
            return true;
        }

        public ClimateTileDiagnostics Inspect(int tile, double day, FlatCycloneWeather captured)
        {
            if ((uint)tile >= (uint)world.Count || !Refresh(day, captured))
                return new ClimateTileDiagnostics {Tile=tile, Region=-1, Observation=FlatClimateObservation.Missing,
                    Biome=new FlatBiomeEstimate(FlatBiomeStatus.Unavailable)};
            int region = RegionOfCell(CellOf(tile));
            return Diagnose(tile, region, day, captured, biomes?.Estimate(world, tile, biomeWork) ?? default);
        }

        ClimateTileDiagnostics Diagnose(int tile, int region, double day, FlatCycloneWeather captured, FlatBiomeEstimate biome)
        {
            var observation = samples.Current(tile, day, captured);
            Group surface = Surface(world.Water[tile]);
            var air = references[region, (int)surface];
            ClimateReference water = null;
            if (!atmosphereOnly && surface != Group.MarineAir)
            {
                bool gauged = !double.IsNaN(observation.Discharge);
                Group type = surface == Group.LakeAir ? (gauged ? Group.Outlet : Group.Lake) : (gauged ? Group.River : Group.Soil);
                water = references[region, (int)type];
            }
            return new ClimateTileDiagnostics {Tile=tile, Region=region, Observation=observation, Biome=biome,
                WeatherReference=air, WaterReference=water, Weather=air?.Score(observation,work) ?? default,
                Water=water?.Score(observation,work) ?? default};
        }
    }
}
