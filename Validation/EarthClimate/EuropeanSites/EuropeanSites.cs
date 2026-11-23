using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Orbis.Flat;

internal static class EuropeanSites
{
    private static int Main(string[] args)
    {
        if (args.Length is < 2 or > 3)
            throw new ArgumentException("Usage from repository root: EuropeanSites regions.json output.json [seed]");
        string output = Path.GetFullPath(args[1]);
        if (output.Equals(Path.GetFullPath(args[0]), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The site report and input must have different paths.");
        using var input = JsonDocument.Parse(File.ReadAllText(args[0]));
        var data = input.RootElement;
        int width = data.GetProperty("width").GetInt32(), height = data.GetProperty("height").GetInt32();
        if (!WorldData.SupportsDimensions(width, height)) throw new InvalidDataException("Invalid Earth dimensions.");
        string seed = args.Length == 3 ? args[2] : "ORBIS-2D-2026";
        const string assets = "Assets/ORBIS/Resources/ORBIS/FlatWorld";
        var world = FlatGenerator.Generate(width, height, seed, TerrainPreset.Earth, default,
            File.ReadAllBytes(Path.Combine(assets, $"Earth{width}.bytes")),
            File.ReadAllBytes(Path.Combine(assets, $"EarthWater{width}.bytes")),
            File.ReadAllBytes(Path.Combine(assets, "EarthRivers.bytes")));

        var sites = data.GetProperty("sites").EnumerateObject().ToArray();
        var tiles = sites.Select(site => site.Value.GetProperty("tile").GetInt32()).ToArray();
        for (int i = 0; i < sites.Length; i++)
        {
            var normals = sites[i].Value.GetProperty("game");
            CheckNormals(world.Temperature, tiles[i], normals.GetProperty("temperature_c"));
            CheckNormals(world.Precipitation, tiles[i], normals.GetProperty("precipitation_mm"));
        }
        // The environmental observer reconstructs days from the generated monthly
        // fields and applies the seeded interannual anomaly. Transient storms are omitted.
        const int years = 30;
        var sampler = new FlatClimateSamples(world, false);
        var summers = sites.Select(_ => new double[years]).ToArray();
        for (int year = 0; year < years; year++)
        for (int day = 150; day < 240; day++)
        for (int i = 0; i < tiles.Length; i++)
            summers[i][year] += sampler.Current(tiles[i], year * 360 + day + .5, null).Precipitation;

        var siteReport = new Dictionary<string, object>();
        for (int i = 0; i < sites.Length; i++)
        {
            var reference = sites[i].Value.GetProperty("observed");
            float[] temperature = reference.GetProperty("temperature_c").Deserialize<float[]>();
            float[] rain = reference.GetProperty("precipitation_mm").Deserialize<float[]>();
            siteReport[sites[i].Name] = new {
                model_koppen = world.Climates[tiles[i]].ToString(),
                observed_koppen = KoppenClassifier.Classify(temperature, rain).ToString(),
                observed_jja_mm = rain.Skip(5).Take(3).Sum(),
                model_30_year_jja_mm = new { min = summers[i].Min(), mean = summers[i].Average(), max = summers[i].Max() }
            };
        }

        // NOAA PSL NCEP/NCAR R1 1991–2020 July 10 m winds at three Gaussian
        // ocean nodes. check_wind_belts.py registers both source archive hashes.
        var referenceWinds = new[] {
            (lon: 24.375, lat: 37.14220047, east: .536808, north: -3.528348),
            (lon: 26.25, lat: 37.14220047, east: .889394, north: -4.553407),
            (lon: 26.25, lat: 35.23749924, east: 3.814265, north: -4.734246)
        };
        var air = world.Moisture;
        var windReport = referenceWinds.Select(point => {
            float u = (float)(point.lon / 360 + .5), v = (float)(point.lat / 180 + .5);
            return new {
                longitude = point.lon, latitude = point.lat,
                model_east_mps = air.Sample(air.East, u, v, 6) * FlatCycloneWeather.ClimateWindMetresPerSecond,
                model_north_mps = air.Sample(air.North, u, v, 6) * FlatCycloneWeather.ClimateWindMetresPerSecond,
                observed_east_mps = point.east, observed_north_mps = point.north
            };
        }).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonSerializer.Serialize(new { seed, width, height, sites = siteReport, july_aegean_wind = windReport },
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        return 0;
    }

    private static void CheckNormals(float[] generated, int tile, JsonElement values)
    {
        if ((uint)tile >= generated.Length / 12 || values.GetArrayLength() != 12)
            throw new InvalidDataException("Invalid site or monthly inputs.");
        for (int month = 0; month < 12; month++)
            if (Math.Abs(generated[tile * 12 + month] - values[month].GetDouble()) > .005)
                throw new InvalidDataException("Site normals do not match this model, geography and seed.");
    }
}
