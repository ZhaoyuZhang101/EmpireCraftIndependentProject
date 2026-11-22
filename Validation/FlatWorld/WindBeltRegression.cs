using System;
using System.IO;
using Orbis;
using Orbis.Flat;

internal static class WindBeltRegression
{
    // A featureless aquaplanet isolates the circulation from terrain and
    // monsoon effects. A lake strip then acts as the only vapour source.
    public static void Run(string output = null)
    {
        const int width = 128, height = width / 2;
        var world = new WorldData(width, height, "WIND-BELTS", TerrainPreset.Continents);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = world.Index(x, y);
            world.Water[i] = WaterKind.DeepOcean;
            for (int m = 0; m < 12; m++) world.Temperature[i*12 + m] = 18;
        }
        var air = FlatMoisture.Build(world, default);
        int At(double latitude, int x, int month) =>
            world.Index(x, (int)((latitude / 180 + .5) * height)) * 12 + month;
        foreach (int side in new[] {-1, 1})
        {
            int summer = side > 0 ? 6 : 0, winter = (summer + 6) % 12;
            foreach (int month in new[] {summer, winter})
            {
                int trade = At(side * 15, 0, month), mid = At(side * 47, 0, month), polar = At(side * 76, 0, month);
                if (air.East[trade] >= 0 || air.East[mid] <= 0 || air.East[polar] >= 0 ||
                    side * air.North[trade] >= -.09f || side * air.North[mid] <= .05f || side * air.North[polar] >= -.03f)
                    throw new Exception("Wrong direction in the trade, midlatitude, or polar wind belt");
            }
            if (air.East[At(side * 30, 0, summer)] >= 0 || air.East[At(side * 30, 0, winter)] <= 0)
                throw new Exception("The subtropical wind reversal did not migrate poleward in summer");
            if (side * air.North[At(-side, 0, summer)] <= .05f)
                throw new Exception("Cross-equatorial trades did not flow towards summer convergence");
        }
        for (int y = 0; y < height; y++)
        for (int m = 0; m < 12; m++)
        {
            int i = world.Index(0,y)*12+m, j = world.Index(0,height-1-y)*12+(m+6)%12;
            if (Math.Abs(air.East[i] - air.East[j]) > .0001f || Math.Abs(air.North[i] + air.North[j]) > .0001f)
                throw new Exception("Mirroring latitude and shifting six months changed the circulation");
        }
        world.Moisture = air;
        foreach (int month in new[]{0, 6})
        {
            world.Day = month * 30 + 15;
            var model = new FlatWeatherSimulation(world).Model;
            const int column = 11;
            double lon = ((column + .5) / FlatWeatherSimulation.Width - .5) * 2 * Math.PI;
            var east = new AtmosVector(Math.Cos(lon), 0, -Math.Sin(lon));
            foreach (int side in new[]{-1, 1})
            {
                int row = (int)((side * 30.0 / 180 + .5) * FlatWeatherSimulation.Height);
                double velocity = AtmosVector.Dot(model.Wind[row*FlatWeatherSimulation.Width+column], east);
                bool summer = month == (side > 0 ? 6 : 0);
                if (summer ? velocity >= 0 : velocity <= 0)
                    throw new Exception("The layered weather solver lost the seasonal surface wind reversal");
            }
        }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = world.Index(x,y);
            world.Water[i] = x >= 30 && x < 34 ? WaterKind.Lake : WaterKind.Land;
            world.CoastDistance[i] = width;
        }
        var plume = FlatMoisture.Build(world, default);
        foreach (int side in new[]{-1,1})
        {
            int winter = side > 0 ? 0 : 6, polarCoast = At(side * 65,90,winter);
            if (air.East[polarCoast] <= 0 || plume.East[polarCoast] >= 0)
                throw new Exception("Maritime westerlies and inland polar easterlies should diverge at the winter front");
        }
        foreach (int latitude in new[]{-46, -15, 15, 46})
        {
            int west = At(latitude,25,3), east = At(latitude,38,3);
            float wet = Math.Abs(latitude) < 30 ? plume.Rain[west] : plume.Rain[east];
            float dry = Math.Abs(latitude) < 30 ? plume.Rain[east] : plume.Rain[west];
            Console.WriteLine($"Wind belt {latitude} degrees, downwind / upwind monthly rain {wet:F2} / {dry:F2} mm");
            if (!(wet > 1.1f * dry && wet > 1))
                throw new Exception("The isolated water source did not supply its downwind shore");
        }
        foreach (double residual in plume.BudgetError)
            if (residual >= 2e-6 || double.IsNaN(residual)) throw new Exception("Wind transport lost column water");
        Console.WriteLine("PASS seasonal wind reversal, hemispheric symmetry, layered weather forcing and downwind vapour supply");
        if (output != null) ExportEarth(output);
    }

    // Export both wind prescriptions with the same Earth bed, temperature,
    // pressure and marine physics. The observation files are read by Python.
    static void ExportEarth(string path)
    {
        const string assets = "Assets/ORBIS/Resources/ORBIS/FlatWorld";
        var world = FlatGenerator.Generate(256,128,"ORBIS-2D-2026",TerrainPreset.Earth,default,
            File.ReadAllBytes(Path.Combine(assets,"Earth256.bytes")),
            File.ReadAllBytes(Path.Combine(assets,"EarthWater256.bytes")),
            File.ReadAllBytes(Path.Combine(assets,"EarthRivers.bytes")));
        var winds = world.Moisture;
        var ocean = FlatOceanCirculation.Build(world,FlatWorkerPolicy.Options(default));
        var reference = FlatMoisture.BuildWithCirculation(world,default,0,true,ocean,seasonalWinds:false);
        string output = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        using var file = new BinaryWriter(File.Create(output));
        file.Write(new byte[]{(byte)'O',(byte)'W',(byte)'B',(byte)'2'});
        file.Write(winds.Width);
        file.Write(winds.Height);
        for (int y = 0; y < winds.Height; y++)
        for (int x = 0; x < winds.Width; x++)
        {
            bool oceanCell = true;
            for (int sy = y*world.Height/winds.Height; sy < (y+1)*world.Height/winds.Height; sy++)
            for (int sx = x*world.Width/winds.Width; sx < (x+1)*world.Width/winds.Width; sx++)
                oceanCell &= FlatTerrainAnalysis.Ocean(world.Water[world.Index(sx,sy)]);
            file.Write((byte)(oceanCell ? 1 : 0));
        }
        foreach (var field in new[]{reference.East, reference.North, winds.East, winds.North})
            foreach (float component in field) file.Write(component);
        foreach (var field in new[]{ocean.SeaAnomaly, ocean.AirAnomaly, ocean.East, ocean.North})
            foreach (float value in field) file.Write(value);
        Console.WriteLine($"Exported {winds.Width}x{winds.Height} monthly normalized wind pairs to {output}");
    }
}
