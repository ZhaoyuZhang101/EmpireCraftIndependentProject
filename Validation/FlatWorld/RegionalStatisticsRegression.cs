using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Orbis.Flat;

public static class RegionalStatisticsRegression
{
    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    public static void Run()
    {
        var world = FlatAtlasTerrain.Read(File.ReadAllBytes("Assets/ORBIS/Resources/ORBIS/WorldAtlas/Default.bytes"), "ORBIS-2D-2026", default);
        new FlatSimulation(world);
        var original = FlatSave.Encode(world);
        var timer = Stopwatch.StartNew();
        var atlas = new FlatClimateStatistics(world);
        atlas.Refresh(world.Day, world.Cyclones, default);
        timer.Stop();
        double cold = timer.Elapsed.TotalMilliseconds;
        Check(atlas.Width == 64 && atlas.Height == 32, "bounded statistics grid");
        int valid = 0, budgets = 0;
        foreach (var cell in atlas.Cells)
        {
            if (cell.Weather.Valid) valid++;
            if (cell.Water.Valid) budgets++;
        }
        Check(valid > atlas.Cells.Length * .8 && budgets > 50, "physical weather and terrestrial water coverage");
        AtmosphereField(world, atlas, "imported World Atlas");
        int built = atlas.RegionFits;
        timer.Restart();
        long startBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i=0;i<1000;i++) atlas.Refresh(world.Day, world.Cyclones, default);
        long warmBytes = GC.GetAllocatedBytesForCurrentThread()-startBytes;
        timer.Stop();
        Check(atlas.RegionFits == built && warmBytes == 0, "unchanged view must neither refit nor allocate");
        double hit = timer.Elapsed.TotalMilliseconds;
        int sample = world.Index(world.Width/2,world.Height/2);
        atlas.InvalidateRegion(sample);
        timer.Restart();atlas.Refresh(world.Day,world.Cyclones,default);timer.Stop();
        double incremental = timer.Elapsed.TotalMilliseconds;
        Check(atlas.RegionFits > built && atlas.RegionFits-built < atlas.RegionCount, "bounded regional invalidation");
        var local = atlas.Inspect(sample,world.Day,world.Cyclones);
        Check(local.Tile == sample && local.Observation.Elevation == world.Elevation[sample], "native tile lookup");
        var after = FlatSave.Encode(world);
        Check(System.Linq.Enumerable.SequenceEqual(original,after), "diagnostics modified authoritative save bytes");
        var restored = FlatSave.Decode(original);
        var repeated = new FlatClimateStatistics(restored);
        var savedCell = repeated.Inspect(sample,restored.Day,restored.Cyclones);
        Check(savedCell.Weather.SquaredDistance.Equals(local.Weather.SquaredDistance) && savedCell.Flags == local.Flags,
            "weather statistics changed after load");
        int warmFits = atlas.RegionFits;
        // Warm up the storm-free branch before measuring it.
        atlas.Refresh(1,null);
        timer.Restart();
        startBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i=1; i<=32; i++) atlas.Refresh(1+i*.25,null);
        long tickBytes = GC.GetAllocatedBytesForCurrentThread()-startBytes;
        timer.Stop();
        double tickCost = timer.Elapsed.TotalMilliseconds/32;
        Check(atlas.RegionFits == warmFits && tickBytes == 0, "new ticks score without refitting or allocating");
        local = atlas.Inspect(sample,9.125,null);
        timer.Restart();
        startBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i=0; i<1000; i++) local = atlas.Inspect(sample,9.125,null);
        long probeBytes = GC.GetAllocatedBytesForCurrentThread()-startBytes;
        timer.Stop();
        double probeCost = timer.Elapsed.TotalMilliseconds;
        Check(probeBytes == 0 && local.Weather.Valid, "native local query is allocation free");
        world.Revision++;
        atlas.Refresh(9.125,null);
        Check(atlas.RegionFits == warmFits, "civilisation ticks do not invalidate climate");
        atlas.Refresh(16,null);
        var invalid = atlas.Inspect(-1, double.NaN, null);
        Check(!invalid.Weather.Valid && invalid.Region < 0 && double.IsNaN(invalid.Observation.Temperature) &&
            !atlas.Refresh(double.PositiveInfinity,null), "malformed diagnostics do not disrupt a valid world");
        Check(atlas.RegionFits == warmFits + atlas.RegionCount, "nearest weather climatology switches at mid month");
        atlas.Refresh(30-1e-10,null);
        Check(atlas.RegionFits == warmFits + atlas.RegionCount, "roundoff cannot move a budget into the next month");
        int river = Array.FindIndex(world.Water, w => w == WaterKind.River);
        var gauged = atlas.Inspect(river,16,null);
        Check(gauged.WaterReference != null && gauged.WaterReference.Features.IndexOf(ClimateFeature.Discharge)>=0,
            "real river uses its gauged profile even when effective regional support is low");
        if (gauged.WaterReference.EffectiveCount < gauged.WaterReference.Features.Dimension+2)
            Check(!gauged.Water.Valid, "insufficient gauged sites must be reported as unavailable");
        while (!gauged.Water.Valid && ++river < world.Count)
            if (world.Water[river] == WaterKind.River) gauged = atlas.Inspect(river,16,null);
        Check(gauged.Water.Valid && !double.IsNaN(gauged.Observation.WaterBudgetDischarge), "supported native river station");
        world.TerrainRevision++;
        atlas.Refresh(16,null);
        Check(atlas.RegionFits == warmFits+2*atlas.RegionCount, "new terrain epoch replaces reference and biome caches");
        atlas.InvalidateRegion(0);
        bool canceled=false;
        try {atlas.Refresh(16,null,new CancellationToken(true));} catch (OperationCanceledException) {canceled=true;}
        Check(canceled, "cancelled refresh");
        atlas.Refresh(16,null);
        Console.WriteLine($"PASS default atlas {world.Width}x{world.Height} / diagnostic {atlas.Width}x{atlas.Height}: " +
            $"weather {valid}, water budgets {budgets}, cold including biome calibration {cold:F2} ms, " +
            $"1000 cached refreshes {hit:F3} ms / {warmBytes} bytes, one regional invalidation {incremental:F2} ms, " +
            $"mean six-hour tick {tickCost:F3} ms / {tickBytes} bytes across 32 ticks, 1000 native probes {probeCost:F3} ms / {probeBytes} bytes");
        AnomalyCases();
        WindowAndWaterCases();
        LayeredHistory(world);
        var native = FlatGenerator.Generate(512, 256, "ORBIS-2D-2026", TerrainPreset.Continents, default);
        var nativeAtlas = new FlatClimateStatistics(native);
        nativeAtlas.Refresh(native.Day, native.Cyclones);
        AtmosphereField(native, nativeAtlas, "main scene default");
    }

    static void AtmosphereField(WorldData world, FlatClimateStatistics atlas, string source)
    {
        var timer = Stopwatch.StartNew();
        var air = new FlatClimateStatistics(world, atmosphereOnly:true);
        air.Refresh(world.Day, world.Cyclones);
        double cold = timer.Elapsed.TotalMilliseconds;
        int moister = 0, drier = 0;
        for (int i = 0; i < air.Cells.Length; i++)
        {
            var cell = air.Cells[i];
            Check(cell.Tile == atlas.Cells[i].Tile && cell.Weather.Valid == atlas.Cells[i].Weather.Valid &&
                  cell.Weather.SquaredDistance.Equals(atlas.Cells[i].Weather.SquaredDistance) &&
                  cell.Weather.Reliability.Equals(atlas.Cells[i].Weather.Reliability), "sky must use the atlas weather model at the same geographic site");
            Check(cell.WaterReference == null && double.IsNaN(cell.Observation.SoilWater), "sky incurred a water budget");
            var response = RegionalCloudResponse.From(cell);
            Check(Math.Abs(response.Cover) <= 1 && response.Convection >= 0 && response.Convection <= 1, "unbounded cloud response");
            if (response.Cover > .02) moister++;
            if (response.Cover < -.02) drier++;
        }
        Check(moister > 20 && drier > 20, "real-world regional scores must produce both thicker and thinner cloud");
        var unsupported = RegionalCloudResponse.From(default);
        Check(unsupported.Cover == 0 && unsupported.Convection == 0, "missing evidence must preserve neutral weather");
        var dry = air.Cells[0];
        dry.Observation.Humidity = 0;
        dry.Observation.Precipitation = 0;
        dry.Observation.Pressure = 920;
        dry.Weather = dry.WeatherReference.Score(dry.Observation, new ClimateScoreWorkspace());
        var responseDry = RegionalCloudResponse.From(dry);
        Check(responseDry.Cover <= 0 && responseDry.Convection == 0, "dry low-pressure outliers must not increase cloud or convection");
        timer.Restart();
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int tick = 1; tick <= 32; tick++)
        {
            air.Refresh(tick * .25, null);
            for (int i = 0; i < air.Cells.Length; i++) RegionalCloudResponse.From(air.Cells[i]);
        }
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Check(bytes == 0, "regional sky extraction allocates between seasonal fits");
        Console.WriteLine($"PASS regional air ({source}): {moister} positive and {drier} negative responses, " +
            $"cold {cold:F2} ms, six-hour update plus cloud response {timer.Elapsed.TotalMilliseconds/32:F2} ms, {bytes} hot bytes");
    }

    static void AnomalyCases()
    {
        var features = ClimateFeatureSet.Weather;
        var baseline = new ClimateReference(features);
        var work = new ClimateScoreWorkspace();
        for (int i=0;i<200;i++)
        {
            var row = new FlatClimateObservation {Source=FlatClimateObservationSource.MonthlyNormal,PrecipitationWindowDays=30,
                Temperature=20+Math.Sin(i*.67), Humidity=.66+.04*Math.Cos(i*.31),
                Pressure=1011+2*Math.Sin(i*.43), Precipitation=3+.7*Math.Cos(i*.57), WindSpeed=6+Math.Sin(i*.87)
            };
            baseline.Add(row,1,work);
        }
        baseline.Fit();
        var weather = new FlatClimateObservation {Source=FlatClimateObservationSource.RegionalForcing,PrecipitationWindowDays=30,
            Temperature=37,Humidity=.4,Pressure=978,Precipitation=20,WindSpeed=30};
        var score=baseline.Score(weather,work);
        var expected=ClimateSignals.Hot|ClimateSignals.LowPressure|ClimateSignals.Wet|ClimateSignals.HighWind|ClimateSignals.Compound;
        Check(score.Valid && (score.Signals&expected)==expected && score.SquaredDistance>0,"hot/low/wet/windy compound departure");
        weather.Temperature=-2;weather.Precipitation=0;
        score=baseline.Score(weather,work);
        Check((score.Signals&(ClimateSignals.Cold|ClimateSignals.Dry))==(ClimateSignals.Cold|ClimateSignals.Dry),"cold and low precipitation");
        weather.Temperature=double.NaN;
        Check(!baseline.Score(weather,work).Valid,"missing weather cannot produce an event");
        bool rejected=false;
        try {new ClimateFeatureSet(new ClimateAxis(ClimateFeature.WindSpeed),new ClimateAxis(ClimateFeature.WindSpeed));}
        catch(ArgumentException) {rejected=true;}
        Check(rejected,"duplicate feature");
        Console.WriteLine("PASS directional, compound and missing-data anomaly diagnostics");
    }

    static void WindowAndWaterCases()
    {
        var work = new ClimateScoreWorkspace();
        var water = new ClimateReference(ClimateFeatureSet.RiverWater, waterBudget:true);
        var month = FlatClimateObservation.Missing;
        month.Source = FlatClimateObservationSource.MonthlyNormal;
        month.WaterBudgetDays = month.PrecipitationWindowDays = 30;
        for (int i=0; i<150; i++)
        {
            month.WaterBudgetPrecipitation = 4+.6*Math.Sin(i*.07);
            month.SoilMoisture = .5+.05*Math.Sin(i*.11);
            month.Evaporation = 3+.4*Math.Sin(i*.13);
            month.Runoff = 2+.4*Math.Sin(i*.23);
            month.Groundwater = 80+10*Math.Sin(i*.29);
            month.WaterBudgetDischarge = 20+3*Math.Sin(i*.31);
            Check(water.Add(month, 1, work), "complete month at a routed gauge");
        }
        water.Fit();
        month.Source = FlatClimateObservationSource.RegionalForcing;
        month.Precipitation = 55; month.Discharge = 900;
        month.WaterBudgetPrecipitation = .1; month.SoilMoisture = .04; month.Groundwater = 4; month.Runoff = .2;
        var drought = water.Score(month, work);
        Check(drought.Valid && (drought.Signals & ClimateSignals.DroughtRisk) != 0 &&
            (drought.Signals & ClimateSignals.FloodPotential) == 0, "drought uses the budget's actual rain input");
        month.WaterBudgetPrecipitation = 16; month.Runoff = 11; month.WaterBudgetDischarge = 80;
        Check((water.Score(month, work).Signals & ClimateSignals.FloodPotential) != 0, "runoff and wetness co-occur");
        month.WaterBudgetDays = 1;
        Check(!water.Score(month, work).Valid, "reject incompatible water budget duration");
        month.WaterBudgetDays = 30; month.Source = FlatClimateObservationSource.LayeredStep;
        Check(!water.Score(month, work).Valid, "short-lived solver does not enter the hydrological reference");
        var coupled = new ClimateReference(new ClimateFeatureSet(new ClimateAxis(ClimateFeature.Temperature),
            new ClimateAxis(ClimateFeature.Humidity)));
        month.Source = FlatClimateObservationSource.MonthlyNormal;
        for (int i=0; i<100; i++)
        {
            month.Temperature = i; month.Humidity = i*.01;
            coupled.Add(month, 1, work);
        }
        coupled.Fit();
        month.Temperature = 75; month.Humidity = .25;
        Check(coupled.Score(month, work).Signals == ClimateSignals.Compound,
            "broken correlation detected though both individual variables remain within one standard deviation");
        long before = coupled.Count;
        month.Temperature = double.MaxValue;
        Check(!coupled.Add(month, 1, work) && coupled.Count == before && coupled.Ready, "overflow-safe paired moments");
        Console.WriteLine("PASS compound covariance departure, time-window separation, water budget drought / high runoff");
    }

    static void LayeredHistory(WorldData world)
    {
        var solver = new FlatWeatherSimulation(world, 2);
        var history = new ClimateWeatherHistory(solver, minimumFrames:3, refitFrames:2, minimumPhysicalSeconds:0);
        for (int i=0; i<3; i++) solver.Step(.1, i*.25);
        Check(history.Frames == 3 && !Array.Exists(history.Latest, s=>s.Valid), "prior frames required for actual weather scores");
        solver.Step(.1, .75);
        Check(history.Frames == 4 && Array.Exists(history.Latest, s=>s.Valid), "live solver feeds regional prior automatically");
        solver.Step(.1, .77);
        solver.Step(.05, 1);
        Check(history.Frames == 4, "duplicate synoptic slot and unequal accumulation period excluded");
        solver.Step(.1, 35);
        Check(!Array.Exists(history.Latest, s=>s.Valid), "distinct month starts without a false reference");
        solver.Step(.1, 0);
        Check(history.Frames == 1 && !Array.Exists(history.Latest, s=>s.Valid), "calendar rewind discards future observations");
        history.Dispose();
        solver.Step(.1, 1);
        Check(history.Frames == 1, "detached history receives no further work");
        Console.WriteLine("PASS optional layered solver history: prior-only scoring, temporal support, calendar, disposal");
    }

    // Accept original binary fixtures as arguments so this can run on users' old
    // saves without checking any private world into source control.
    public static void CheckSaves(string[] args)
    {
        Check(args.Length > 1, "Supply one or more .orbis fixtures");
        for (int i=1; i<args.Length; i++)
        {
            var input = File.ReadAllBytes(args[i]);
            int version = BitConverter.ToInt32(input, "ORBIS-FLAT-01".Length + sizeof(int) + 32);
            var world = FlatSave.Decode(input);
            var before = FlatSave.Encode(world);
            var atlas = new FlatClimateStatistics(world);
            atlas.Refresh(world.Day,world.Cyclones);
            var after = FlatSave.Encode(world);
            Check(System.Linq.Enumerable.SequenceEqual(before,after), "diagnostics changed a legacy world");
            var restored = FlatSave.Decode(after);
            Check(System.Linq.Enumerable.SequenceEqual(after,FlatSave.Encode(restored)), "v3 reopen changed canonical bytes");
            if(version==FlatSave.Version) Check(System.Linq.Enumerable.SequenceEqual(input,after), "changed original current save");
            Check(world.Day == restored.Day && world.Wildlife.Count == restored.Wildlife.Count, "loaded calendar and wildlife");
            var reloaded = new FlatClimateStatistics(restored);
            reloaded.Refresh(restored.Day,restored.Cyclones);
            int valid = 0;
            for (int k=0; k<atlas.Cells.Length; k++)
            {
                var a=atlas.Cells[k]; var b=reloaded.Cells[k];
                Check(a.Weather.Valid==b.Weather.Valid && a.Water.Valid==b.Water.Valid && a.Flags==b.Flags &&
                    a.Weather.SquaredDistance.Equals(b.Weather.SquaredDistance) &&
                    a.Water.SquaredDistance.Equals(b.Water.SquaredDistance), "regional metric drift after loading");
                if(a.Weather.Valid) valid++;
            }
            Check(valid > atlas.Cells.Length*.8, $"legacy geographic coverage {valid}/{atlas.Cells.Length}");
            Console.WriteLine($"PASS v{version} save ({input.Length} bytes), {world.Width}x{world.Height}, {world.Wildlife.Count} animals, " +
                $"{valid}/{atlas.Cells.Length} valid weather scores and identical canonical {after.Length}-byte world through diagnostics and round trip");
        }
    }
}
