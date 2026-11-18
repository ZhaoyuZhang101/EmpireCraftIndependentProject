using System;
using System.Diagnostics;
using System.IO;
using Orbis.Flat;

public static class CycloneRegression
{
    public static void Run()
    {
        WithoutGenesis(Basin(17, 260, .9f, .5f), "cold ocean");
        WithoutGenesis(Basin(30, 260, .3f, .5f), "dry column");
        WithoutGenesis(Basin(30, 0, .9f, .5f), "no rain");
        WithoutGenesis(Basin(30, 260, .9f, 0), "windless warm ocean");
        var outflow = Basin(30, 260, .9f, .5f);
        for (int i = 0; i < outflow.Count; i++)
        {
            double lat = outflow.Latitude(i) * Math.PI / 180;
            for (int m = 0; m < 12; m++)
            {
                // u*cos(lat) is constant, so the zonal flow has no relative curl.
                // v*cos(lat) increases northward in the tropics: the air diverges.
                outflow.Moisture.East[i * 12 + m] = (float)(.55 / Math.Max(.5, Math.Cos(lat)));
                outflow.Moisture.North[i * 12 + m] = FMath.Clamp((float)(.3 * Math.Tan(lat)), -.8f, .8f);
            }
        }
        WithoutGenesis(outflow, "warm ocean with strong, divergent noncyclonic flow");
        var curl = Basin(31, 270, .92f, 0);
        var anticyclonic = curl.Clone();
        for (int i = 0; i < curl.Count; i++)
        {
            double lat = curl.Latitude(i) * Math.PI / 180;
            float zonal = FMath.Clamp((float)(2.8 * (.31 - Math.Abs(lat)) / Math.Max(.5, Math.Cos(lat))), -1, 1);
            for (int m = 0; m < 12; m++)
            {
                // Same moisture and speed, zero divergence; reverse the curl alone.
                curl.Moisture.East[i * 12 + m] = zonal;
                anticyclonic.Moisture.East[i * 12 + m] = -zonal;
            }
        }
        WithoutGenesis(anticyclonic, "wrong-signed tropical curl");
        curl.Cyclones = new FlatCycloneWeather();
        new FlatSimulation(curl).Step(2);
        Check(curl.Cyclones.North.WindSpeed > 0 && curl.Cyclones.South.WindSpeed > 0,
            "cyclonic environmental curl must assist the corresponding hemisphere");
        var lake = Basin(30, 260, .9f, .5f);
        Array.Fill(lake.Water, WaterKind.Lake);
        WithoutGenesis(lake, "freshwater");

        var world = Basin(31, 270, .92f, .5f);
        world.Cyclones = new FlatCycloneWeather();
        var sim = new FlatSimulation(world);
        sim.Step(.25f);
        var initial = world.Cyclones.North;
        Check(initial.Tracked && world.Cyclones.South.Tracked && initial.Strength == 0 &&
            initial.PressureDeficit == 0 && initial.WindSpeed == 0, "genesis must start from rest");
        sim.Step(.75f);
        var early = world.Cyclones.North;
        Check(early.WindSpeed > 0 && early.WindSpeed < 18 && early.PressureDeficit > 0,
            "initial convection must deepen the low gradually");
        sim.Step(3);
        var mature = world.Cyclones.North;
        Check(mature.PressureDeficit > early.PressureDeficit && mature.WindSpeed > early.WindSpeed + 6 &&
            mature.Strength > early.Strength && mature.OceanCooling > 0, "surface flux, pressure, wind and wake must develop");
        Check(mature.Phase > 0 && world.Cyclones.South.Phase < 0, "hemispheres must rotate in opposite directions");
        Check(FlatScrollMath.Shortest(mature.U - initial.U, 1) > .014, "tracks must follow eastward steering");
        var reverse = Basin(31, 270, .92f, -.5f);
        reverse.Cyclones = new FlatCycloneWeather();
        var reversed = new FlatSimulation(reverse);
        reversed.Step(.25f);
        double from = reverse.Cyclones.North.U;
        reversed.Step(3.75f);
        Check(FlatScrollMath.Shortest(reverse.Cyclones.North.U - from, 1) < -.014, "wind reversal must reverse translation");

        var hundredths = Basin(31, 270, .92f, .5f);
        hundredths.Cyclones = new FlatCycloneWeather();
        var fractional = new FlatSimulation(hundredths);
        for (int part = 0; part < 25; part++) fractional.Step(.01f);
        Check(hundredths.Cyclones.StepIndex == 1 && hundredths.Cyclones.North.Equals(initial) &&
            FlatCycloneWeather.StepAt(.24999) == 0, "roundoff in decimal float steps must not lose a completed quarter day");

        var divided = world.Clone();
        var partitioned = new FlatSimulation(divided);
        sim.Step(6.375f);
        for (int i = 0; i < 51; i++) partitioned.Step(.125f);
        Check(world.Cyclones.StepIndex == divided.Cyclones.StepIndex &&
            world.Cyclones.North.Equals(divided.Cyclones.North) && world.Cyclones.South.Equals(divided.Cyclones.South) &&
            world.Cyclones.PreviousNorth.Equals(divided.Cyclones.PreviousNorth) && world.Cyclones.PreviousSouth.Equals(divided.Cyclones.PreviousSouth),
            "weather must agree across different simulation step partitions");
        var unchanged = divided.Cyclones.Clone();
        var noStep = new FlatSimulation(divided);
        for (int i = 0; i < 100; i++) divided.Cyclones.Sample(true, divided.Day);
        Check(noStep.World.Cyclones.North.Equals(unchanged.North) && noStep.World.Cyclones.StepIndex == unchanged.StepIndex,
            "sampling and attaching to an existing world must not advance weather");

        double atLandfall = world.Cyclones.North.WindSpeed;
        Array.Fill(world.Water, WaterKind.Land);
        Array.Fill(divided.Temperature, 14f);
        sim.Step(2);
        partitioned.Step(2);
        Check(world.Cyclones.North.WindSpeed < atLandfall * .6, "landfall must weaken winds");
        Check(divided.Cyclones.North.WindSpeed < atLandfall * .8, "cold water must weaken winds");
        sim.Step(12);
        partitioned.Step(12);
        Check(!world.Cyclones.North.Tracked && !divided.Cyclones.North.Tracked, "unfueled lows must dissipate");

        // The layer is wet and windless in both worlds. Only the established low
        // can maintain inflow and raise the surface enthalpy flux in calm air.
        var feedback = Basin(31, 270, .92f, 0);
        feedback.Cyclones = new FlatCycloneWeather { North = new FlatCycloneTrack
            { Tracked = true, U = .5, V = .6, PressureDeficit = 18, WindSpeed = 23 } };
        var mixed = feedback.Clone();
        mixed.Cyclones.North.OceanCooling = 6;
        new FlatSimulation(feedback).Step(1);
        new FlatSimulation(mixed).Step(1);
        Check(feedback.Cyclones.North.PressureDeficit > 18 && feedback.Cyclones.North.WindSpeed > 23,
            "established pressure/wind must sustain moist inflow without a new environmental trigger");
        Check(mixed.Cyclones.North.PressureDeficit < feedback.Cyclones.North.PressureDeficit &&
            mixed.Cyclones.North.WindSpeed < feedback.Cyclones.North.WindSpeed, "the cooled wake must reduce the heat supply");

        var crossing = Basin(31, 270, .92f, 0);
        Array.Fill(crossing.Moisture.North, -1f);
        crossing.Cyclones = new FlatCycloneWeather { North = new FlatCycloneTrack
            { Tracked = true, U = .4, V = .5001, PressureDeficit = 40, WindSpeed = 35 } };
        new FlatSimulation(crossing).Step(.25f);
        Check(crossing.Cyclones.North.Tracked && crossing.Cyclones.North.V < .5 && crossing.Cyclones.North.WindSpeed > 10,
            "a low carried across the equator must spin down continuously");

        var seam = new FlatCycloneWeather { StepIndex = 12, PreviousNorth = mature, North = mature };
        seam.PreviousNorth.U = .995;
        seam.North.U = .005;
        seam.PreviousNorth.Phase = 6.2;
        seam.North.Phase = 6.4;
        var midway = seam.Sample(true, 3.125);
        Check(Math.Abs(FlatScrollMath.Shortest(midway.U, 1)) < 1e-10 && Math.Abs(midway.Phase - 6.3) < 1e-10,
            "sample must cross the longitude and angle seams continuously");
        Console.WriteLine($"PASS cyclone formation from calm, opposite rotation, steering, landfall/cold decay, partition agreement and seam; early/mature wind {early.WindSpeed:F1}/{mature.WindSpeed:F1} m/s");

        var atlas = FlatAtlasTerrain.Read(File.ReadAllBytes("Assets/ORBIS/Resources/ORBIS/WorldAtlas/Default.bytes"), "ORBIS-2D-2026", default);
        var timer = Stopwatch.StartNew();
        var forecast = new FlatSimulation(atlas);
        double maxWind = 0;
        int stormDays = 0, formations = 0;
        for (int day = 0; day < 360; day++)
        {
            bool storm = false;
            for (int quarter = 0; quarter < 4; quarter++)
            {
                forecast.Step(.25f);
                var state = atlas.Cyclones;
                if (state.North.Tracked && !state.PreviousNorth.Tracked) formations++;
                if (state.South.Tracked && !state.PreviousSouth.Tracked) formations++;
                double wind = Math.Max(state.North.WindSpeed, state.South.WindSpeed);
                storm |= wind >= 17.5;
                maxWind = Math.Max(maxWind, wind);
            }
            if (storm) stormDays++;
        }
        timer.Stop();
        Check(formations > 0 && stormDays > 0 && stormDays < 360, "default climate must produce intermittent tropical storms");
        Console.WriteLine($"PASS default atlas 360 days: {formations} disturbances, {stormDays} days with tropical-storm winds, peak {maxWind:F1} m/s; core advancement {timer.Elapsed.TotalMilliseconds:F1} ms");
    }

    static WorldData Basin(float temperature, float rain, float humidity, float steering)
    {
        var world = new WorldData(64, 32, "CYCLONE-DYNAMICS", TerrainPreset.Continents);
        var columns = world.Moisture = new FlatMoistureState(64, 32);
        for (int i = 0; i < world.Count; i++)
        {
            world.Water[i] = WaterKind.DeepOcean;
            world.Elevation[i] = -2000;
            double latitude = world.Latitude(i);
            float band = (float)Math.Tanh((Math.Abs(latitude) - 18) / 8);
            for (int m = 0; m < 12; m++)
            {
                int j = i * 12 + m;
                world.Temperature[j] = temperature;
                world.Precipitation[j] = columns.Rain[j] = rain;
                columns.Humidity[j] = humidity;
                columns.Condensation[j] = rain / 30;
                if (steering == 0) continue;
                columns.East[j] = steering - .15f * band;
                columns.North[j] = -.45f * Math.Sign(latitude) * band;
            }
        }
        return world;
    }

    static void WithoutGenesis(WorldData world, string environment)
    {
        var sim = new FlatSimulation(world);
        Check(world.Cyclones != null && !world.Cyclones.North.Tracked && !world.Cyclones.South.Tracked, "no spin-up storm over " + environment);
        for (int i = 0; i < 20 * 4; i++)
        {
            sim.Step(.25f);
            Check(!world.Cyclones.North.Tracked && !world.Cyclones.South.Tracked, "no genesis over " + environment);
        }
    }

    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
