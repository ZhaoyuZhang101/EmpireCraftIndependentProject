using System;
using System.Collections.Generic;

namespace Orbis.Flat
{
    public enum FlatClimateObservationSource { Unavailable, MonthlyNormal, RegionalForcing, LayeredStep }

    // Temperatures in Celsius, RH and soil saturation as fractions, mean
    // sea-level pressure in hPa, horizontal wind in m/s, terrain bed in metres.
    // Give rates in mm/day water equivalent and storage depths in mm. Discharge
    // retains the native routed volume unit defined by FlatRiverDischarge.
    public struct FlatClimateObservation
    {
        public double Temperature, Humidity, Pressure, Precipitation, WindSpeed, Elevation;
        public double SoilMoisture, Evaporation, Runoff, Discharge, Groundwater, SoilWater, SnowWater;
        public double PrecipitationWindowDays, WaterBudgetDays;
        // Containing calendar month: mm/day input to the land/lake column,
        // relative mean gauge rate, and end minus beginning total storage in mm.
        public double WaterBudgetPrecipitation, WaterBudgetDischarge, WaterStorageChange;
        public FlatClimateObservationSource Source;
        // Footprint projected from the saved centre deficit and peak wind.
        // A sampled track with a zero contribution is distinct from no history.
        public bool StormTrackSampled;
        public double CyclonePressureDrop, CycloneWindSpeed;

        public static FlatClimateObservation Missing
        {
            get
            {
                return new FlatClimateObservation
                {
                    Temperature = double.NaN, Humidity = double.NaN, Pressure = double.NaN,
                    Precipitation = double.NaN, WindSpeed = double.NaN, Elevation = double.NaN,
                    SoilMoisture = double.NaN, Evaporation = double.NaN, Runoff = double.NaN,
                    Discharge = double.NaN, Groundwater = double.NaN, SoilWater = double.NaN, SnowWater = double.NaN,
                    PrecipitationWindowDays = double.NaN, WaterBudgetDays = double.NaN,
                    WaterBudgetPrecipitation = double.NaN, WaterBudgetDischarge = double.NaN, WaterStorageChange = double.NaN,
                    CyclonePressureDrop = double.NaN, CycloneWindSpeed = double.NaN
                };
            }
        }
    }

    // A diagnostic sampler is owned by one worker/inspector, for one built
    // world. Recreate it when replacing terrain, climate normals or seed.
    public sealed class FlatClimateSamples
    {
        public const int MonthDays = 30, HydrologyHistoryMonths = 24;
        // One native site and one gauge per 64x32 weather cell, plus local probes.
        public const int StationCapacity = 4352;
        const int Months = 12;
        const double Turn = 2 * Math.PI;
        readonly WorldData world;
        readonly uint seed;
        readonly double[] sinLon, cosLon, sinLat, cosLat, pet;
        readonly PressureField pressure;
        readonly Dictionary<int, int> slots;
        readonly Station[] stations;
        readonly FlatRiverDischarge.WaterMonth[] normals;
        readonly FlatRiverDischarge.ClimateMonth[] scratch = new FlatRiverDischarge.ClimateMonth[Months];
        readonly FlatClimateYear[] waterYears = new FlatClimateYear[HydrologyHistoryMonths + 1];
        long waterYearMonth = long.MinValue;
        int nextSlot;
        double previousDay = double.NaN;
        FlatClimateYear currentYear;
        FlatMonthlyForcing season;
        public int CachedWaterStations => slots?.Count ?? 0;

        struct Station
        {
            internal int Tile;
            internal bool Valid;
            internal FlatRiverDischarge.WaterColumn Template;
            internal long CurrentMonth;
            internal FlatRiverDischarge.WaterMonth Current;
        }

        public FlatClimateSamples(WorldData world, bool includeWaterBudget = true)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            seed = FMath.Seed(world.Seed);
            sinLon = new double[world.Width]; cosLon = new double[world.Width];
            sinLat = new double[world.Height]; cosLat = new double[world.Height];
            for (int x = 0; x < world.Width; x++)
            {
                double lon = ((x + .5) / world.Width - .5) * Turn;
                sinLon[x] = Math.Sin(lon); cosLon[x] = Math.Cos(lon);
            }
            for (int y = 0; y < world.Height; y++)
            {
                double lat = ((y + .5) / world.Height - .5) * Math.PI;
                sinLat[y] = Math.Sin(lat); cosLat[y] = Math.Cos(lat);
            }
            pressure = new PressureField(world);
            if (!includeWaterBudget) return;
            pet = new double[world.Height * Months];
            for (int y = 0; y < world.Height; y++)
            {
                double lat = ((y + .5) / world.Height - .5) * Math.PI;
                for (int m = 0; m < Months; m++) pet[y * Months + m] = FlatRiverDischarge.PetPerDegree(lat, m);
            }
            int capacity = Math.Min(StationCapacity, world.Count);
            slots = new Dictionary<int, int>(capacity);
            stations = new Station[capacity];
            normals = new FlatRiverDischarge.WaterMonth[capacity * Months];
        }

        public FlatClimateObservation Normal(int tile, int month)
        {
            if ((uint)tile >= (uint)world.Count) return FlatClimateObservation.Missing;
            month = FMath.Wrap(month, Months);
            int x = tile % world.Width, y = tile / world.Width, index = tile * Months + month;
            float u = (x + .5f) / world.Width, v = (y + .5f) / world.Height;
            var value = FlatClimateObservation.Missing;
            value.Source = FlatClimateObservationSource.MonthlyNormal;
            value.Elevation = world.Elevation[tile];
            value.Temperature = world.Temperature[index];
            Rain(ref value, world.Precipitation[index]);
            value.Pressure = pressure.Normal(PressureField.Cell(x, y, world), month);
            var air = world.Moisture;
            if (air != null)
            {
                value.Humidity = air.Sample(air.Humidity, u, v, month);
                double east = air.Sample(air.East, u, v, month) * FlatCycloneWeather.ClimateWindMetresPerSecond;
                double north = air.Sample(air.North, u, v, month) * FlatCycloneWeather.ClimateWindMetresPerSecond;
                value.WindSpeed = Math.Sqrt(east * east + north * north);
            }
            if (slots == null) return value;
            var gauge = world.Facts?.RiverDischarge;
            if (gauge != null && gauge.HasStation(tile)) value.Discharge = value.WaterBudgetDischarge = gauge.Monthly(tile, month);
            if (!FlatTerrainAnalysis.Ocean(world.Water[tile]))
            {
                int slot = StationFor(tile);
                if (stations[slot].Valid) Water(ref value, stations[slot].Template, normals[slot * Months + month]);
            }
            return value;
        }

        public FlatClimateObservation Current(int tile, double day) { return Current(tile, day, world.Cyclones); }

        // Pass a clone captured with the world clock when working in the
        // background. Explicit null requests only the environmental forcing.
        public FlatClimateObservation Current(int tile, double day, FlatCycloneWeather storms)
        {
            if ((uint)tile >= (uint)world.Count || !OrbisAtmosphereCore.Finite(day))
                return FlatClimateObservation.Missing;
            if (day != previousDay)
            {
                var nextSeason = new FlatMonthlyForcing(day);
                if (nextSeason.AbsoluteMonth <= long.MinValue || nextSeason.AbsoluteMonth >= long.MaxValue)
                    return FlatClimateObservation.Missing;
                season = nextSeason;
                currentYear = FlatClimateYear.At(seed, day);
                previousDay = day;
            }
            long absoluteMonth = (long)season.AbsoluteMonth;
            int m = season.Month, x = tile % world.Width, y = tile / world.Width, start = tile * Months;
            float u = (x + .5f) / world.Width, v = (y + .5f) / world.Height;
            var direction = new AtmosVector(sinLon[x] * cosLat[y], sinLat[y], cosLon[x] * cosLat[y]);
            currentYear.Offsets((float)direction.X, (float)direction.Y, (float)direction.Z, out float degrees, out float fraction);
            var value = FlatClimateObservation.Missing;
            value.Source = FlatClimateObservationSource.RegionalForcing;
            value.Elevation = world.Elevation[tile];
            value.Temperature = season.Sample(world.Temperature, start) + degrees;
            Rain(ref value, season.Sample(world.Precipitation, start, ClimateRange.Nonnegative) * (1 + fraction));
            value.Pressure = pressure.Current(PressureField.Cell(x, y, world), day, season, currentYear);
            var air = world.Moisture;
            double east = double.NaN, north = double.NaN;
            if (air != null)
            {
                value.Humidity = season.Sample(air, air.Humidity, u, v, ClimateRange.Fraction);
                east = season.Sample(air, air.East, u, v) * FlatCycloneWeather.ClimateWindMetresPerSecond;
                north = season.Sample(air, air.North, u, v) * FlatCycloneWeather.ClimateWindMetresPerSecond;
            }
            if (storms != null && storms.StepIndex == FlatCycloneWeather.StepAt(day))
            {
                var tangentEast = new AtmosVector(cosLon[x], 0, -sinLon[x]);
                var tangentNorth = AtmosVector.Cross(direction, tangentEast);
                double drop = 0, stormEast = 0, stormNorth = 0;
                AddTrack(storms.Sample(true, day), 1, direction, tangentEast, tangentNorth, ref drop, ref stormEast, ref stormNorth);
                AddTrack(storms.Sample(false, day), -1, direction, tangentEast, tangentNorth, ref drop, ref stormEast, ref stormNorth);
                value.StormTrackSampled = true;
                value.CyclonePressureDrop = drop;
                value.CycloneWindSpeed = Math.Sqrt(stormEast * stormEast + stormNorth * stormNorth);
                value.Pressure -= drop;
                east += stormEast; north += stormNorth;
            }
            value.WindSpeed = Math.Sqrt(east * east + north * north);
            if (slots == null) return value;
            var gauge = world.Facts?.RiverDischarge;
            if (gauge != null && gauge.HasStation(tile))
            {
                value.Discharge = gauge.Sample(tile, day);
                value.WaterBudgetDischarge = gauge.Monthly(tile, m);
            }
            if (!FlatTerrainAnalysis.Ocean(world.Water[tile]))
            {
                int slot = StationFor(tile);
                if (stations[slot].Valid)
                {
                    if (stations[slot].CurrentMonth != absoluteMonth) Replay(slot, absoluteMonth, direction);
                    Water(ref value, stations[slot].Template, stations[slot].Current);
                }
            }
            return value;
        }

        static void Rain(ref FlatClimateObservation value, double precipitation)
        {
            if (precipitation < 0 || !OrbisAtmosphereCore.Finite(precipitation)) return;
            value.Precipitation = precipitation / MonthDays;
            value.PrecipitationWindowDays = MonthDays;
        }

        // The saved tracker has a centre deficit and eyewall wind, not a solved
        // gridded wind field. A radial pressure deficit 1/(1+r²) gives the
        // corresponding normalized gradient-wind shape 2r/(1+r²), with its
        // maximum at the tracker's physical core radius. Taper the footprint
        // between six and ten core radii. Combine velocities as tangent vectors.
        static void AddTrack(FlatCycloneTrack track, int spin, AtmosVector site, AtmosVector east, AtmosVector north,
            ref double drop, ref double stormEast, ref double stormNorth)
        {
            if (!track.Tracked || (track.PressureDeficit <= 0 && track.WindSpeed <= 0)) return;
            var centre = FlatWeatherSimulation.Direction(track.U, track.V);
            double chord = (site - centre).Length;
            const double coreAngle = FlatCycloneWeather.CoreRadiusMetres / FlatCycloneWeather.EarthRadius;
            if (chord >= 10 * coreAngle) return;
            double r = 2 * Math.Asin(Math.Min(1, chord * .5)) / coreAngle;
            if (r >= 10) return;
            double t = Math.Max(0, (r - 6) / 4);
            double weight = (1 - t * t * (3 - 2 * t)) / (1 + r * r);
            drop += track.PressureDeficit * weight;
            if (r < 1e-8) return;
            var circulation = AtmosVector.Cross(centre, site).Normalized;
            double speed = spin * track.WindSpeed * 2 * r * weight;
            stormEast += speed * AtmosVector.Dot(circulation, east);
            stormNorth += speed * AtmosVector.Dot(circulation, north);
        }

        int StationFor(int tile)
        {
            if (slots.TryGetValue(tile, out int slot)) return slot;
            slot = nextSlot;
            nextSlot = (nextSlot + 1) % stations.Length;
            if (slots.Count == stations.Length) slots.Remove(stations[slot].Tile);
            slots.Add(tile, slot);
            var station = new Station { Tile = tile, Valid = true, CurrentMonth = long.MinValue,
                Template = FlatRiverDischarge.ColumnAt(world, tile) };
            int start = tile * Months, latitude = (tile / world.Width) * Months;
            for (int m = 0; m < Months; m++)
            {
                float rain = world.Precipitation[start + m], temperature = world.Temperature[start + m];
                if (!OrbisAtmosphereCore.Finite(rain) || rain < 0 || !OrbisAtmosphereCore.Finite(temperature)) station.Valid = false;
                scratch[m] = new FlatRiverDischarge.ClimateMonth(rain, temperature, pet[latitude + m]);
            }
            if (station.Valid) FlatRiverDischarge.NormalYear(station.Template, scratch, slot * Months, null, 0, normals);
            stations[slot] = station;
            return slot;
        }

        void Replay(int slot, long absoluteMonth, AtmosVector direction)
        {
            long first = absoluteMonth - HydrologyHistoryMonths;
            if (absoluteMonth != waterYearMonth)
            {
                for (int h = 0; h < waterYears.Length; h++) waterYears[h] = FlatClimateYear.At(seed, (first + h) * (double)MonthDays);
                waterYearMonth = absoluteMonth;
            }
            ref var station = ref stations[slot];
            var column = station.Template;
            int m = FMath.Wrap((int)(first % Months), Months);
            var initial = normals[slot * Months + (m + 11) % Months];
            column.Snow = initial.Snow; column.Soil = initial.Soil; column.Aquifer = initial.Aquifer;
            int start = station.Tile * Months, latitude = (station.Tile / world.Width) * Months;
            for (int h = 0; h < waterYears.Length; h++)
            {
                waterYears[h].Offsets((float)direction.X, (float)direction.Y, (float)direction.Z, out float degrees, out float fraction);
                var forcing = new FlatRiverDischarge.ClimateMonth(world.Precipitation[start + m] * (1 + fraction),
                    world.Temperature[start + m] + degrees, pet[latitude + m]);
                double storage = column.Snow + column.Soil + column.Aquifer;
                double yield = column.Advance(forcing, out double evaporation);
                if (h == waterYears.Length - 1)
                    station.Current = new FlatRiverDischarge.WaterMonth(column, forcing, storage, evaporation, yield);
                m = (m + 1) % Months;
            }
            station.CurrentMonth = absoluteMonth;
        }

        static void Water(ref FlatClimateObservation value, FlatRiverDischarge.WaterColumn template, FlatRiverDischarge.WaterMonth month)
        {
            value.Evaporation = month.Evaporation / MonthDays;
            value.Runoff = month.Yield / MonthDays; // local quickflow, excess, springs and baseflow, before routing
            value.SnowWater = month.Snow;
            value.WaterBudgetDays = MonthDays;
            value.WaterBudgetPrecipitation = month.Precipitation / MonthDays;
            value.WaterStorageChange = month.StorageChange;
            if (!double.IsNaN(template.SoilCapacity))
            {
                value.SoilWater = month.Soil;
                value.SoilMoisture = month.Soil / template.SoilCapacity;
                value.Groundwater = month.Aquifer;
            }
        }

        // Native forcing centres, geographic land fractions, row thermal means
        // and pressure equation match FlatWeatherSimulation.Forcing. This cache
        // never creates a layered atmosphere or steps a weather simulation.
        sealed class PressureField
        {
            const int Width = FlatWeatherSimulation.Width, Height = FlatWeatherSimulation.Height;
            readonly WorldData source;
            readonly int[] tiles = new int[Width * Height];
            readonly AtmosVector[] directions = new AtmosVector[Width * Height];
            readonly double[] groundCorrection = new double[Width * Height], latitudes = new double[Height];
            readonly double[] normals = new double[Width * Height * Months], current = new double[Width * Height];
            readonly double[] rowDays = new double[Height], thermalRow = new double[Width];
            readonly float[] land, glebaPressureOffset;

            internal PressureField(WorldData world)
            {
                source = world;
                glebaPressureOffset = FlatWeatherSimulation.BuildGlebaPressureOffsets(world);
                var localLand = new float[tiles.Length];
                for (int y = 0; y < Height; y++)
                {
                    rowDays[y] = double.NaN;
                    for (int x = 0; x < Width; x++)
                    {
                        int i = y * Width + x;
                        double u = (x + .5) / Width, v = (y + .5) / Height;
                        int tile = tiles[i] = world.Index((int)(u * world.Width), (int)(v * world.Height));
                        directions[i] = FlatWeatherSimulation.Direction(u, v);
                        groundCorrection[i] = FlatClimate.SurfaceHeight(world, tile) * .0065;
                        localLand[i] = FlatTerrainAnalysis.AreaWater(world.Water[tile]) ? 0
                            : FlatMoisture.Continentality(world.CoastDistance[tile], world.Width);
                    }
                    latitudes[y] = Math.Asin(directions[y * Width].Y) * 180 / Math.PI;
                }
                land = FlatMoisture.RegionalContinentality(localLand, Width, Height);
                for (int y = 0; y < Height; y++)
                for (int m = 0; m < Months; m++)
                {
                    double sum = 0;
                    for (int x = 0; x < Width; x++)
                    {
                        int i = y * Width + x;
                        thermalRow[x] = world.Temperature[tiles[i] * Months + m] + groundCorrection[i];
                        sum += thermalRow[x];
                    }
                    for (int x = 0; x < Width; x++)
                    {
                        int i = y * Width + x;
                        double glebaOff = glebaPressureOffset == null ? 0 : glebaPressureOffset[i * Months + m];
                        normals[i * Months + m] = FlatWeatherSimulation.SeaLevelPressure(latitudes[y], m, land[i], thermalRow[x] - sum / Width) + glebaOff;
                    }
                }
            }

            internal static int Cell(int x, int y, WorldData world)
            {
                return (2 * y + 1) * Height / (2 * world.Height) * Width + (2 * x + 1) * Width / (2 * world.Width);
            }
            internal double Normal(int cell, int month) { return normals[cell * Months + month]; }
            internal double Current(int cell, double day, FlatMonthlyForcing season, FlatClimateYear year)
            {
                int y = cell / Width;
                if (rowDays[y] == day) return current[cell];
                int first = y * Width;
                double sum = 0;
                for (int x = 0; x < Width; x++)
                {
                    int i = first + x, start = tiles[i] * Months;
                    var p = directions[i];
                    year.Offsets((float)p.X, (float)p.Y, (float)p.Z, out float degrees, out _);
                    thermalRow[x] = season.Sample(source.Temperature, start)
                        + degrees + groundCorrection[i];
                    sum += thermalRow[x];
                }
                for (int x = 0; x < Width; x++)
                {
                    int i = first + x;
                    double glebaOff = glebaPressureOffset == null ? 0 : season.Sample(glebaPressureOffset, i * Months);
                    current[i] = FlatWeatherSimulation.SeaLevelPressure(latitudes[y], season.SeasonalMonth, land[i], thermalRow[x] - sum / Width) + glebaOff;
                }
                rowDays[y] = day;
                return current[cell];
            }
        }
    }
}
