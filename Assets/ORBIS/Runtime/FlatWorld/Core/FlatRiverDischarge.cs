using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orbis.Flat
{
    // Mean-flow units are the volume of 1,200 mm/year over one mean-area tile.
    // For twelve equal 30-day months, monthly mean rates average to the annual
    // rate. These relative units are independent of an assumed planetary radius.
    public sealed class FlatRiverDischarge
    {
        const int Months = 12;
        const double MillimetresPerMonthPerUnit = 1200.0 / Months;
        readonly int[] slotByTile;
        readonly float[] annual, monthly;

        FlatRiverDischarge(int tileCount, int stationCount)
        {
            slotByTile = new int[tileCount];
            annual = new float[stationCount];
            monthly = new float[stationCount * Months];
        }

        int Slot(int tile)
        {
            return (uint)tile < (uint)slotByTile.Length ? slotByTile[tile] - 1 : -1;
        }

        // An ungauged tile and a measured zero flow have different meanings.
        public bool HasStation(int tile) { return Slot(tile) >= 0; }

        public float Annual(int tile)
        {
            int slot = Slot(tile);
            return slot >= 0 ? annual[slot] : 0;
        }

        public float Monthly(int tile, int month)
        {
            int slot = Slot(tile);
            return slot >= 0 ? monthly[slot * Months + FMath.Wrap(month, Months)] : 0;
        }

        float SampleSlot(int slot, double day)
        {
            double period = day / 30.0;
            double whole = Math.Floor(period);
            int month = FMath.Wrap((int)(whole % Months), Months);
            int start = slot * Months;
            return FMath.Lerp(monthly[start + month], monthly[start + (month + 1) % Months], (float)(period - whole));
        }

        public float Sample(int tile, double day)
        {
            int slot = Slot(tile);
            return slot >= 0 ? SampleSlot(slot, day) : 0;
        }

        public float StageRatio(int tile, double day)
        {
            int slot = Slot(tile);
            return slot >= 0 && annual[slot] > 0 ? SampleSlot(slot, day) / annual[slot] : 1;
        }

        internal static double RelativeArea(int row, int height)
        {
            double south = (row / (double)height - .5) * Math.PI;
            double north = ((row + 1) / (double)height - .5) * Math.PI;
            return (Math.Sin(north) - Math.Sin(south)) * height * .5;
        }

        // Oudin temperature/radiation PET using the mean extraterrestrial solar
        // radiation of the local latitude at the middle of a model month.
        internal static double PetPerDegree(double latitude, int month)
        {
            double declination = .4091 * Math.Cos((month - 5.2) * Math.PI / 6);
            double sunset = Math.Acos(Math.Max(-1, Math.Min(1, -Math.Tan(latitude) * Math.Tan(declination))));
            double radiation = 37.59 * (sunset * Math.Sin(latitude) * Math.Sin(declination)
                + Math.Cos(latitude) * Math.Cos(declination) * Math.Sin(sunset));
            return Math.Max(0, radiation) * 30 / (2.45 * 100);
        }

        internal readonly struct ClimateMonth
        {
            internal readonly double Rain, Snow, MeltCapacity, PotentialEvaporation;
            internal ClimateMonth(double precipitation, double temperature, double petPerDegree)
            {
                double rainFraction = Math.Max(0, Math.Min(1, (temperature + 2) / 5));
                Rain = precipitation * rainFraction;
                Snow = precipitation - Rain;
                // Two mm per positive degree-day; precipitation and all stores
                // are measured in mm of liquid-water equivalent.
                MeltCapacity = 60 * Math.Max(0, temperature);
                PotentialEvaporation = petPerDegree * Math.Max(0, temperature + 5);
            }
        }

        internal struct WaterColumn
        {
            readonly bool lake;
            readonly double steepness, soilCapacity, aquiferCapacity;
            internal double Snow, Soil, Aquifer;
            internal double SoilCapacity { get { return lake ? double.NaN : soilCapacity; } }

            internal WaterColumn(bool isLake, double slope)
            {
                lake = isLake;
                steepness = slope;
                soilCapacity = 165 - 85 * slope;
                aquiferCapacity = 240 - 120 * slope;
                Snow = Soil = Aquifer = 0;
            }

            internal double Advance(ClimateMonth climate, out double evaporation)
            {
                Snow += climate.Snow;
                double melt = Math.Min(Snow, climate.MeltCapacity);
                Snow -= melt;
                double liquid = climate.Rain + melt;
                if (lake)
                {
                    // Direct precipitation over open water can evaporate again.
                    // Storage and attenuation of routed inflows are handled once
                    // at the lake outlet, independently of the path across it.
                    evaporation = Math.Min(liquid, climate.PotentialEvaporation);
                    return liquid - evaporation;
                }

                double saturation = Soil / soilCapacity;
                double quick = liquid * (.025 + .12 * steepness + .28 * saturation * saturation);
                Soil += liquid - quick;
                evaporation = Math.Min(Soil, climate.PotentialEvaporation * Math.Min(1, Soil / (.55 * soilCapacity)));
                Soil -= evaporation;
                double excess = Math.Max(0, Soil - soilCapacity);
                Soil -= excess;
                double recharge = Soil * (.16 - .05 * steepness);
                Soil -= recharge;
                Aquifer += recharge + .3 * excess;
                double spring = Math.Max(0, Aquifer - aquiferCapacity);
                Aquifer -= spring;
                double baseflow = .24 * Aquifer;
                Aquifer -= baseflow;
                return quick + .7 * excess + spring + baseflow;
            }
        }

        internal static WaterColumn ColumnAt(WorldData world, int tile)
        {
            int parent = world.Downstream[tile];
            double drop = parent < 0 ? 0 : Math.Max(0, world.Elevation[tile] - world.WaterLevel[parent]);
            return new WaterColumn(world.Water[tile] == WaterKind.Lake, drop / (drop + 250));
        }

        internal readonly struct WaterMonth
        {
            internal readonly double Snow, Soil, Aquifer, Evaporation, Yield, Precipitation, StorageChange;
            internal WaterMonth(WaterColumn column, ClimateMonth climate, double previousStorage, double evaporation, double yield)
            {
                Snow = column.Snow; Soil = column.Soil; Aquifer = column.Aquifer;
                Evaporation = evaporation; Yield = yield;
                Precipitation = climate.Rain + climate.Snow;
                StorageChange = Snow + Soil + Aquifer - previousStorage;
            }
        }

        // Routing and diagnostic stations share the identical forcing, convergence
        // and snow/soil/aquifer model. Only the selected output is written.
        internal static double NormalYear(WaterColumn column, ClimateMonth[] climate, int start,
            float[] sources, double areaScale, WaterMonth[] budgets)
        {
            double sum = 0;
            for (int year = 0; year < 12; year++)
            {
                double soil = column.Soil, aquifer = column.Aquifer;
                sum = 0;
                if (sources != null)
                {
                    for (int m = 0; m < Months; m++)
                    {
                        sources[start + m] = (float)(column.Advance(climate[m], out _) * areaScale);
                        sum += sources[start + m];
                    }
                }
                else
                {
                    for (int m = 0; m < Months; m++)
                    {
                        double storage = column.Snow + column.Soil + column.Aquifer;
                        double yield = column.Advance(climate[m], out double evaporation);
                        budgets[start + m] = new WaterMonth(column, climate[m], storage, evaporation, yield);
                    }
                }
                // Snowmelt is periodic by year two; positive snow balance
                // remains in perennial ice, as in the saved drainage model.
                if (year >= 1 && Math.Abs(column.Soil - soil) < .0001 && Math.Abs(column.Aquifer - aquifer) < .0001) break;
            }
            return sum;
        }

        // Generation requests local annual yields in addition to the monthly
        // sources. Loading uses the identical normals without writing Runoff.
        internal static float[] BuildSources(WorldData world, CancellationToken cancel, float[] annualSources = null, int workers = 0)
        {
            var sources = new float[world.Count * Months];
            Parallel.For(0, world.Height, FlatWorkerPolicy.Options(cancel, workers), row =>
            {
                var climate = new ClimateMonth[Months];
                var pet = new double[Months];
                double areaScale = RelativeArea(row, world.Height) / MillimetresPerMonthPerUnit;
                double latitude = ((row + .5) / world.Height - .5) * Math.PI;
                for (int m = 0; m < Months; m++) pet[m] = PetPerDegree(latitude, m);
                for (int x = 0; x < world.Width; x++)
                {
                    if ((x & 63) == 0) cancel.ThrowIfCancellationRequested();
                    int tile = row * world.Width + x, start = tile * Months;
                    if (annualSources != null) annualSources[tile] = 0;
                    if (FlatTerrainAnalysis.Ocean(world.Water[tile])) continue;
                    double precipitation = 0;
                    for (int m = 0; m < Months; m++)
                    {
                        float rain = world.Precipitation[start + m];
                        precipitation += rain;
                        climate[m] = new ClimateMonth(rain, world.Temperature[start + m], pet[m]);
                    }
                    if (precipitation == 0) continue;

                    double sum = NormalYear(ColumnAt(world, tile), climate, start, sources, areaScale, null);
                    if (annualSources != null) annualSources[tile] = (float)(sum / Months);
                }
            });
            return sources;
        }

        static bool LakeOutlet(WorldData world, int tile)
        {
            if (world.Water[tile] != WaterKind.Lake) return false;
            int parent = world.Downstream[tile];
            return parent < 0 || world.Water[parent] != WaterKind.Lake || world.WaterLevel[tile] != world.WaterLevel[parent];
        }

        static double Total(float[] values, int start)
        {
            double sum = 0;
            for (int m = 0; m < Months; m++) sum += values[start + m];
            return sum;
        }

        static void FitMean(float[] values, int start, double target, double sum)
        {
            // Older files can contain yield from permanent ice (or a hand-made
            // channel with no climate). Preserve its saved volume as steady
            // release when no modelled month supplies a timing signal.
            if (sum <= 0)
            {
                for (int m = 0; m < Months; m++) values[start + m] = (float)target;
                return;
            }
            double scale = target * Months / sum;
            for (int m = 0; m < Months; m++) values[start + m] = (float)(values[start + m] * scale);
        }

        // Exact monthly solution of dStorage/dt = inflow - Storage/tau for
        // constant mean inflow during a month. Start at the periodic storage
        // of the twelve input months, preserving their annual volume.
        static void AttenuateLake(float[] rates, int start, double residenceMonths)
        {
            double retention = Math.Exp(-1 / residenceMonths);
            double inputFactor = residenceMonths * (1 - retention);
            double storage = 0;
            for (int m = 0; m < Months; m++) storage = retention * storage + inputFactor * rates[start + m];
            storage /= 1 - Math.Pow(retention, Months);
            for (int m = 0; m < Months; m++)
            {
                double incoming = rates[start + m], next = retention * storage + inputFactor * incoming;
                rates[start + m] = (float)Math.Max(0, incoming + storage - next);
                storage = next;
            }
        }

        internal static FlatRiverDischarge Route(WorldData world, float[] sources, CancellationToken cancel, int workers = 0)
        {
            int count = 0;
            for (int i = 0; i < world.Count; i++)
            {
                if ((i & 4095) == 0) cancel.ThrowIfCancellationRequested();
                if (world.Water[i] == WaterKind.River || LakeOutlet(world, i)) count++;
            }
            if (count == 0) return new FlatRiverDischarge(0, 0);
            var result = new FlatRiverDischarge(world.Count, count);
            if (sources == null) sources = BuildSources(world, cancel, null, workers);
            var local = new double[world.Count];
            var lakeStorage = new float[world.Count];
            for (int i = 0; i < world.Count; i++) local[i] = world.Runoff[i];
            for (int i = 0; i < world.Count; i++)
            {
                int p = world.Downstream[i];
                if (p >= 0) local[p] -= world.Runoff[i];
            }
            int slot = 0;
            for (int row = 0; row < world.Height; row++)
            {
                cancel.ThrowIfCancellationRequested();
                double area = RelativeArea(row, world.Height);
                for (int x = 0; x < world.Width; x++)
                {
                    int i = row * world.Width + x, start = i * Months;
                    // Decompose the persisted annual graph into nonnegative
                    // local yields. v1/v2 rainfall-only files consequently
                    // keep their original mean at every gauge and junction.
                    FitMean(sources, start, Math.Max(0, local[i]), Total(sources, start));
                    if (world.Water[i] == WaterKind.Lake)
                    {
                        // Shallow active storage controls outlet response; the
                        // whole deep lake need not turn over to pass a flood.
                        double activeMetres = Math.Min(3, .25 + .08 * Math.Max(0, world.WaterLevel[i] - world.Elevation[i]));
                        lakeStorage[i] = (float)(area * activeMetres * 10);
                    }
                    if (world.Water[i] == WaterKind.River || LakeOutlet(world, i))
                    {
                        result.slotByTile[i] = slot + 1;
                        result.annual[slot] = world.Runoff[i];
                        // Directly authored, partially initialized terrain can
                        // contain stations before a drain order is available.
                        for (int m = 0; m < Months; m++) result.monthly[slot * Months + m] = world.Runoff[i];
                        slot++;
                    }
                }
            }
            for (int k = world.Count - 1; k >= 0; k--)
            {
                if ((k & 4095) == 0) cancel.ThrowIfCancellationRequested();
                int i = world.DrainOrder[k], p = world.Downstream[i], start = i * Months;
                // Some authored/legacy graphs have smaller saved flow below a
                // junction than their inflows. Keep their authoritative annual
                // value, reducing all months in the same proportion.
                double annualRate = world.Runoff[i], sum = Total(sources, start);
                if (sum != annualRate * Months)
                    FitMean(sources, start, annualRate, sum);
                if (world.Water[i] == WaterKind.Lake)
                {
                    if (!LakeOutlet(world, i)) lakeStorage[p] += lakeStorage[i];
                    else if (annualRate > 0)
                        AttenuateLake(sources, start, Math.Max(.15, Math.Min(8, lakeStorage[i] / annualRate)));
                }
                int index = result.Slot(i);
                if (index >= 0) Array.Copy(sources, start, result.monthly, index * Months, Months);
                if (p >= 0)
                    for (int m = 0; m < Months; m++) sources[p * Months + m] += sources[start + m];
            }
            return result;
        }
    }
}
