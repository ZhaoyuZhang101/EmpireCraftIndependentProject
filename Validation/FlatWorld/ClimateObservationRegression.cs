using System;
using System.Linq;
using Orbis;
using Orbis.Flat;

internal static class ClimateObservationRegression
{
    static void Check(bool value, string reason)
    {
        if (!value) throw new InvalidOperationException("Climate observation: " + reason);
    }

    static void Near(double actual, double expected, double tolerance, string reason)
    {
        Check(Math.Abs(actual - expected) <= tolerance, $"{reason} ({actual:R} versus {expected:R})");
    }

    static WorldData Fixture()
    {
        var world = new WorldData(64, 32, "OBSERVATION-UNITS", TerrainPreset.Continents);
        world.Moisture = new FlatMoistureState(world.Width, world.Height);
        for (int i = 0; i < world.Count; i++)
        for (int m = 0; m < 12; m++)
        {
            int j = i * 12 + m;
            world.Temperature[j] = m < 3 ? -6 : 12 + m;
            world.Precipitation[j] = 30 + 12 * m;
            world.Moisture.Humidity[j] = .7f;
            world.Moisture.East[j] = .6f;
            world.Moisture.North[j] = .8f;
        }
        return world;
    }

    static void SeasonalCalendar()
    {
        var world = Fixture();
        float[] temperature = { -16,-11,-3,6,14,20,24,21,14,7,-3,-12 };
        float[] rain = { 0,6,30,90,150,300,440,320,150,60,0,30 };
        for (int tile = 0; tile < world.Count; tile++)
        for (int month = 0; month < 12; month++)
        {
            int i = tile * 12 + month;
            int season = tile / world.Width < world.Height / 2 ? (month + 6) % 12 : month;
            world.Temperature[i] = temperature[season];
            world.Precipitation[i] = rain[season];
            world.Moisture.Humidity[i] = .25f + .65f * rain[season] / 440;
            world.Moisture.East[i] = .7f + .01f * temperature[season];
            world.Moisture.North[i] = 0;
        }

        var samples = new FlatClimateSamples(world, false);
        uint seed = FMath.Seed(world.Seed);
        foreach (int tile in new[] { world.Index(22, 7), world.Index(22, 24) })
        {
            for (int month = 0; month < 12; month++)
            {
                var normal = samples.Normal(tile, month);
                double sumT = 0, totalRain = 0, sumRH = 0, sumWind = 0;
                for (int d = 0; d < 30; d++)
                {
                    // Each sample represents the centre of one model day.
                    double day = month * 30 + d + .5;
                    var current = samples.Current(tile, day, null);
                    FlatClimateYear.At(seed, day).AtTile(tile, world.Width, world.Height, out float heat, out float wet);
                    sumT += current.Temperature - heat;
                    totalRain += current.Precipitation / (1 + wet);
                    sumRH += current.Humidity;
                    sumWind += current.WindSpeed;
                    Check(current.Precipitation >= 0 && current.Humidity >= 0 && current.Humidity <= 1,
                        "physical rain and humidity throughout the month");
                }
                Near(sumT / 30, normal.Temperature, 2e-6, $"daily forcing preserves month {month + 1} temperature at tile {tile}");
                Near(totalRain, world.Precipitation[tile * 12 + month], 2e-5, "daily rain recovers the saved monthly total");
                Near(sumRH / 30, normal.Humidity, 2e-7, "daily mean column humidity");
                Near(sumWind / 30, normal.WindSpeed, 2e-6, "daily mean prevailing wind in a fixed direction");
            }
            foreach (double boundary in new[] { 0.0, 30.0, 180.0, 360.0 })
            {
                var before = samples.Current(tile, boundary - .00001, null);
                var after = samples.Current(tile, boundary + .00001, null);
                Near(after.Temperature, before.Temperature, .00004, "continuous temperature at month and year boundaries");
                Near(after.Precipitation, before.Precipitation, .00004, "continuous rain at month and year boundaries");
            }
        }
    }

    static void SavedAndCurrent()
    {
        var world = Fixture();
        int tile = world.Index(26, 21);
        world.Elevation[tile] = 242;
        var samples = new FlatClimateSamples(world);
        var normal = samples.Normal(tile, 7);
        Near(normal.Temperature, 19, 0, "saved degrees Celsius");
        Near(normal.Elevation, 242, 0, "terrain metres");
        Near(normal.WindSpeed, 9, 1e-6, "normalized wind vector to m/s");
        Near(normal.Precipitation, 114.0 / 30, 1e-9, "monthly liquid-equivalent depth to mean daily rate");
        Near(normal.Humidity, .7, 1e-6, "column RH fraction");
        Check(normal.Pressure > 990 && normal.Pressure < 1040, "physical sea-level hPa");
        Check(normal.PrecipitationWindowDays == 30 && double.IsNaN(normal.Discharge), "ungauged tile carries an absent flow");
        double day = 7 * 30 + 11.25;
        var current = samples.Current(tile, day, null);
        var climate = FlatClimateYear.At(FMath.Seed(world.Seed), day);
        climate.AtTile(tile, world.Width, world.Height, out float heat, out float fraction);
        // A locally linear series of monthly means crosses the saved mean on day 15.
        Near(current.Temperature, 19 - .125 + heat, 1e-6, "regional interannual heat");
        Near(current.Precipitation, (114 - .125 * 12) * (1 + fraction) / 30.0, 1e-6, "regional interannual precipitation");
        Check(current.WaterBudgetDays == 30 && current.PrecipitationWindowDays == 30, "temporal support is explicit");
        var atMonthStart = FlatClimateYear.At(FMath.Seed(world.Seed), 7 * 30);
        atMonthStart.Weather(world, tile, 7, out _, out float monthlyInput);
        Near(current.WaterBudgetPrecipitation, monthlyInput / 30.0, 1e-9, "hydrology uses only its containing calendar month's forcing");
        Check(Math.Abs(current.WaterBudgetPrecipitation - current.Precipitation) > .01, "distinct stages of a changing seasonal forcing must not alias");
        Near(current.WaterStorageChange, (current.WaterBudgetPrecipitation - current.Evaporation - current.Runoff) * 30,
            1e-10, "the complete current column, including snow and groundwater, balances its own forcing");
        double storage = current.Groundwater;
        Near(samples.Current(tile, day + 2, null).Groundwater, storage, 0, "monthly replay is stable within its month");
        Check(Math.Abs(samples.Current(tile, day + 720, null).Groundwater - storage) > .0001, "interannual forcings reach the aquifer");
        Near(samples.Current(tile, day, null).Groundwater, storage, 0, "calendar rewind produces the same monthly budget");
        Check(double.IsNaN(samples.Normal(-1, 0).Temperature) && double.IsNaN(samples.Current(tile, double.NaN).Pressure), "invalid sample is missing");
        var prior = samples.Current(world.Count - 1, -1e-16, null);
        Check(OrbisAtmosphereCore.Finite(prior.Temperature), "rounding beside the year boundary cannot produce month 12");
        world.Moisture = null;
        var partial = new FlatClimateSamples(world).Normal(tile, 7);
        Check(double.IsNaN(partial.Humidity) && double.IsNaN(partial.WindSpeed), "missing moisture fields are not observed calm or dry weather");
    }

    static void WaterBudget()
    {
        var world = Fixture();
        int tile = world.Index(20, 17), lake = tile + 1, ocean = tile + 2;
        world.Water[lake] = WaterKind.Lake;
        world.Water[ocean] = WaterKind.MiddleOcean;
        var samples = new FlatClimateSamples(world);
        var sources = FlatRiverDischarge.BuildSources(world, default, null, 1);
        double scale = FlatRiverDischarge.RelativeArea(tile / world.Width, world.Height) / 100;
        foreach (int site in new[] { tile, lake })
        {
            var previous = samples.Normal(site, 11);
            for (int month = 0; month < 12; month++)
            {
                var now = samples.Normal(site, month);
                Near(now.Runoff * 30 * scale, sources[site * 12 + month], 1e-6, "same local yield as the routing input");
                if (site == tile)
                {
                    double store = now.SnowWater + now.SoilWater + now.Groundwater;
                    double old = previous.SnowWater + previous.SoilWater + previous.Groundwater;
                    Near(store - old, (now.Precipitation - now.Evaporation - now.Runoff) * 30, .0002, "independent monthly water balance");
                    Near(now.SoilMoisture, now.SoilWater / 165, 1e-12, "root-zone fraction uses physical capacity");
                }
                else Check(double.IsNaN(now.Groundwater) && double.IsNaN(now.SoilMoisture), "a lake has no land soil or soil aquifer");
                previous = now;
            }
        }
        var sea = samples.Normal(ocean, 5);
        Check(double.IsNaN(sea.Evaporation) && double.IsNaN(sea.Runoff) && double.IsNaN(sea.SoilMoisture)
            && double.IsNaN(sea.WaterBudgetPrecipitation), "no land water column over ocean");

        // A real saved gauge can measure zero. Ungauged tiles cannot.
        world.Water[tile] = world.Water[tile + 4] = WaterKind.River;
        world.Runoff[tile] = 7;
        for (int i = 0; i < world.Count; i++) world.DrainOrder[i] = i;
        var route = FlatRiverDischarge.Route(world, sources, default, 1);
        world.Facts = new FlatTerrainFacts(world.Count) { RiverDischarge = route };
        double day = 221.25;
        var river = samples.Current(tile, day, null);
        Near(river.WaterBudgetDischarge, route.Monthly(tile, 7), 0, "same calendar-month gauge for water statistics");
        Near(river.Discharge, route.Sample(tile, day), 0, "continuous climatological gauge for map display");
        Check(Math.Abs(river.Discharge - river.WaterBudgetDischarge) > .001, "monthly river support must survive seasonal interpolation");
        Near(samples.Current(tile, day + 720, null).WaterBudgetDischarge, river.WaterBudgetDischarge, 0, "routing has no invented interannual flood");
        Check(samples.Current(tile + 4, day, null).Discharge == 0 && double.IsNaN(samples.Current(tile + 3, day).Discharge),
            "distinguish dry stations from absent stations");
    }

    static void LightningSeason()
    {
        var world = Fixture();
        for (int i = 0; i < world.Count; i++)
        {
            for (int m = 0; m < 12; m++)
            {
                world.Temperature[i * 12 + m] = 27;
                world.Precipitation[i * 12 + m] = m == 1 ? 450 : 0;
            }
        }
        var lightning = new FlatLightningVisuals(world);
        bool FlashesOn(double day)
        {
            world.Day = day;
            bool struck = false;
            for (int tick = 0; tick < 160; tick++)
            {
                lightning.Tick(.1f, true, 0, 0, false);
                foreach (var flash in lightning.Flashes) struck |= flash.Strength > 0;
            }
            return struck;
        }
        Check(!FlashesOn(29.5), "a dry January does not spawn lightning from February rain");
        Check(FlashesOn(45), "lightning remains active during the wet month");
    }

    static void Tracks()
    {
        var world = Fixture();
        int tile = world.Index(0, 19);
        double u = .5 / world.Width, v = (19 + .5) / world.Height;
        var track = new FlatCycloneTrack { Tracked = true, U = u, V = v, PressureDeficit = 38, WindSpeed = 32 };
        var storms = new FlatCycloneWeather { PreviousNorth = track, North = track };
        var samples = new FlatClimateSamples(world);
        var calm = samples.Current(tile, 0, null);
        var eye = samples.Current(tile, 0, storms);
        Near(calm.Pressure - eye.Pressure, 38, .0001, "saved central pressure deficit applied exactly once");
        Near(eye.WindSpeed, calm.WindSpeed, .0001, "tangential wind vanishes at the cyclone centre");
        Near(eye.Precipitation, calm.Precipitation, 0, "the track does not supply a measured precipitation rate");
        track.U = FlatScrollMath.Wrap(u - 65000 / (6371000 * 2 * Math.PI * Math.Cos((v - .5) * Math.PI)), 1);
        storms.PreviousNorth = storms.North = track;
        var wall = samples.Current(tile, 0, storms);
        Check(wall.WindSpeed > calm.WindSpeed + 15 && wall.Pressure > eye.Pressure && wall.Pressure < calm.Pressure, "resolved vortex crosses the seam at its physical core radius");
        var far = samples.Current(world.Index(32, 19), 0, storms);
        Near(far.Pressure, samples.Current(world.Index(32, 19), 0, null).Pressure, 0, "a cyclone has no footprint on the opposite side of the planet");
        Near(samples.Current(tile, 360, storms).Pressure, samples.Current(tile, 360, null).Pressure, 0, "a stale saved cyclone is not transported into another year");
    }

    static void LiveWeather()
    {
        var world = Fixture();
        for (int i = 0; i < world.Count; i++)
        {
            world.Elevation[i] = (short)(i % 1700);
            world.Water[i] = i % 13 == 0 ? WaterKind.MiddleOcean : WaterKind.Land;
            world.CoastDistance[i] = (ushort)(i % 17);
            for (int m = 0; m < 12; m++) world.Temperature[i * 12 + m] += (float)(Math.Sin(i + m) * 3);
        }
        var sim = new FlatWeatherSimulation(world, 1);
        var samples = new FlatClimateSamples(world);
        int cell = 209;
        Check(double.IsNaN(sim.Instantaneous(cell).Precipitation), "no fictitious timestep before the solver runs");
        int events = 0;
        sim.Stepped += (sender, day) => { if (sender == sim && day == 2.25) events++; };
        // Supply a subfreezing lowest layer so both injected hydrometeors reach the surface.
        sim.Model.Temperature[cell] = 268.15;
        sim.Model.Water[3][cell] += .4;
        sim.Model.Water[4][cell] += .4;
        sim.Step(.1, 2.25);
        for (int i = 0; i < sim.Model.Count; i += 19)
            Near(samples.Current(i, 2.25, null).Pressure, sim.Model.SeaLevelForcingHPa(i), 1e-10,
                "regional pressure equals the actual solver forcing including terrain and year");
        var live = sim.Instantaneous(cell);
        Near(live.Temperature, sim.Model.Temperature[cell] - 273.15, 1e-12, "lowest-layer Kelvin to Celsius");
        Near(live.Pressure, 1013 + sim.Model.PressureAnomaly[cell], 1e-12, "anomaly added to the sea-level reference once");
        Near(live.Precipitation, (sim.Model.RainRate[cell] + sim.Model.SnowRate[cell]) * 24, 1e-12, "solver liquid-equivalent short-step rate expressed as mm/day");
        Check(sim.Model.RainRate[cell] > 0 && sim.Model.SnowRate[cell] > 0, "both liquid and frozen fluxes contribute");
        Near(live.PrecipitationWindowDays, .1 * 45 / 86400, 1e-15, "simulation step to physical days");
        double capacity = OrbisAtmosphereCore.Saturation(sim.Model.Temperature[cell], OrbisAtmosphereCore.Pressure[0]) * OrbisAtmosphereCore.AirMass[0];
        Near(live.Humidity, Math.Min(1, sim.Model.Water[0][cell] / capacity), 1e-12, "prognostic vapour and pressure-level saturation");
        Near(live.WindSpeed, Math.Min(35, sim.Model.Wind[cell].Length / .014 * 10), 1e-12, "wind conversion used by the bulk exchange model");
        Check(events == 1 && double.IsNaN(live.SoilMoisture) && double.IsNaN(live.Evaporation), "no fabricated soil/ET in atmospheric state");
        sim.Model.Restore(sim.Model.Save());
        Check(double.IsNaN(sim.Instantaneous(cell).Temperature), "a restored state at the identical clock has no new completed sampling interval");
        sim.Model.MicrophysicsEnabled = false;
        sim.Step(.1, 2.26);
        Check(double.IsNaN(sim.Instantaneous(cell).Precipitation), "disabled precipitation physics cannot replay an old rain rate");
    }

    static void Cache()
    {
        var world = new WorldData(128, 64, "OBSERVATION-CACHE", TerrainPreset.Continents);
        for (int m = 0; m < 12; m++)
        {
            world.Temperature[m] = 7 + m;
            world.Precipitation[m] = 50 + m * 5;
        }
        var samples = new FlatClimateSamples(world);
        double reference = samples.Current(0, 78, null).Evaporation;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int tile = 1; tile < world.Count; tile++) samples.Normal(tile, 1);
        double after = samples.Current(0, 78, null).Evaporation;
        for (int i = 0; i < 200; i++)
        {
            samples.Normal(0, i % 12);
            samples.Current(0, 78, null);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0 && samples.CachedWaterStations == FlatClimateSamples.StationCapacity,
            $"bounded station replacement and hot samples must allocate zero (observed {allocated})");
        Near(after, reference, 0, "evicting a station cannot change its replay");
    }

    static void GeographicNormals()
    {
        var world = new WorldData(64, 32, "GEOGRAPHIC-NORMALS", TerrainPreset.Continents);
        int coast = world.Index(0, 3), sea = world.Index(63, 3);
        world.Water[sea] = WaterKind.DeepOcean;
        world.Elevation[sea] = -3500;
        world.Elevation[coast] = 800;
        for (int m = 0; m < 12; m++)
        {
            world.Temperature[coast * 12 + m] = 15f + m;
            world.Temperature[sea * 12 + m] = 24f + m;
            world.Precipitation[coast * 12 + m] = 44f - m;
            world.Precipitation[sea * 12 + m] = 12f - m;
        }

        Span<float> temperature = stackalloc float[12], precipitation = stackalloc float[12];
        FlatClimateSamples.SampleNormals(world, 0, 3.5 / 32, 500f, temperature, precipitation);
        for (int m = 0; m < 12; m++)
        {
            Near(temperature[m], 18.85 + m, .00001, "local lapse correction above both sea and land");
            Near(precipitation[m], 28 - m, .00001, "local rain follows the monthly normals");
        }
        float atSeam = temperature[6];
        FlatClimateSamples.SampleNormals(world, 1, 3.5 / 32, 500f, temperature, precipitation);
        Near(temperature[6], atSeam, 0, "local climate wraps across the date line");
        FlatClimateSamples.SampleNormals(world, 0, 3.5 / 32, null, temperature, precipitation);
        Near(temperature[6], 25.5, 0, "island with unresolved height uses atmospheric grid normals");
        Check(KoppenClassifier.Classify(temperature, precipitation) ==
            KoppenClassifier.Classify(temperature.ToArray(), precipitation.ToArray()),
            "point and cell classifiers follow the same Köppen rules");
    }
    public static void Run()
    {
        GeographicNormals();
        SeasonalCalendar();
        LightningSeason();
        SavedAndCurrent();
        WaterBudget();
        Tracks();
        LiveWeather();
        Cache();
        var world = FlatGenerator.Generate(64, 32, "OBSERVATION-PURITY", TerrainPreset.Continents, default);
        byte[] original = FlatSave.Encode(world);
        var samples = new FlatClimateSamples(world);
        for (int tile = 0; tile < world.Count; tile++)
        {
            samples.Normal(tile, 7);
            samples.Current(tile, world.Day);
        }
        Check(original.SequenceEqual(FlatSave.Encode(world)), "diagnostics must leave the complete encoded save byte-identical");
    }
}
