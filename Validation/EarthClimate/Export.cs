using System;
using System.IO;
using System.Text;
using Orbis.Flat;

internal static class Export
{
    private static int Main(string[] args)
    {
#if !LEGACY_EARTH_DEM
        if (args.Length > 0 && args[0] == "--compare-koppen")
            return CompareKoppen.Command(args);
#endif
#if CLIMATE_CIRCULATION
        string circulation = null;
        if (args.Length >= 2 && args[0] == "--circulation")
        {
            circulation = args[1];
            if (circulation != "coupled" && circulation != "fixed-winds" && circulation != "still-ocean" && circulation != "control")
                throw new ArgumentException("Expected coupled, fixed-winds, still-ocean or control.");
            args = args[2..];
        }
#endif
        if (args.Length < 1 || args.Length > 3)
            throw new ArgumentException("Usage from repository root: Export output.bin [width] [seed]");
        int width = args.Length > 1 ? int.Parse(args[1]) : 256;
        if (width < 2 || width % 2 != 0) throw new ArgumentException("Expected an even grid width.");
        string seed = args.Length > 2 ? args[2] : "ORBIS-2D-2026";
        const string assets = "Assets/ORBIS/Resources/ORBIS/FlatWorld";
        var elevation = File.ReadAllBytes(Path.Combine(assets, $"Earth{width}.bytes"));
#if LEGACY_EARTH_DEM
        var world = FlatGenerator.Generate(width, width / 2, seed, TerrainPreset.Earth, default, elevation);
#else
        var water = File.ReadAllBytes(Path.Combine(assets, $"EarthWater{width}.bytes"));
        var rivers = File.ReadAllBytes(Path.Combine(assets, "EarthRivers.bytes"));
        var world = FlatGenerator.Generate(width, width / 2, seed, TerrainPreset.Earth, default, elevation, water, rivers);
#endif
#if CLIMATE_CIRCULATION
        // Same generated terrain, coast and lakes in all four counterfactuals.
        if (circulation != null)
            FlatClimate.Build(world, default, 0, circulation != "fixed-winds" && circulation != "control",
                circulation != "still-ocean" && circulation != "control");
#endif
        string path = Path.GetFullPath(args[0]);
        string directory = Path.GetDirectoryName(path);
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, Path.GetRandomFileName());
        var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write);
        try
        {
            using (var writer = new BinaryWriter(new BufferedStream(output)))
            {
                writer.Write(Encoding.ASCII.GetBytes("ECL1"));
                writer.Write(width);
                writer.Write(world.Height);
                foreach (short metres in world.Elevation) writer.Write(metres);
                foreach (ushort distance in world.CoastDistance) writer.Write(distance);
                foreach (WaterKind kind in world.Water) writer.Write((byte)kind);
                foreach (float value in world.Temperature) writer.Write(value);
                foreach (float value in world.Precipitation) writer.Write(value);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        Console.WriteLine($"{world.Width}x{world.Height}, 12 monthly normals, seed {seed}: {path}");
        return 0;
    }
}
