using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orbis.Flat;

internal static class CompareKoppen
{
    private const string DataDirectory = "Validation/EarthClimate";
    private const int Width = 256, Height = 128, Months = 12;
    // The independently registered observation footprint also used by score.py.
    private const string HoldoutFootprint = "270c599eac1be55f630b38cf48e19f5268f8a24141e22fbc2b109bee36e5cbfa";
    private const string Usage = "Usage: --compare-koppen export.bin [--split holdout|calibration] [--window west east south north] [--check-target] [--json report.json]";

    public static int Command(string[] args)
    {
        try
        {
            if (args.Length < 2) throw new ArgumentException(Usage);
            string split = "holdout", jsonPath = null;
            Window window = null;
            bool check = false;
            for (int i = 2; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--split" when i + 1 < args.Length: split = args[++i]; break;
                    case "--json" when i + 1 < args.Length: jsonPath = args[++i]; break;
                    case "--window" when i + 4 < args.Length:
                        window = Window.Parse(args, i + 1);
                        i += 4;
                        break;
                    case "--check-target": check = true; break;
                    default: throw new ArgumentException(Usage);
                }
            }
            if (split != "calibration" && split != "holdout") throw new ArgumentException(Usage);
            if (window != null && (split != "calibration" || check))
                throw new ArgumentException("A window is a calibration diagnostic. The target requires the full split.");

            var report = Score(args[1], split, window);
            Print(report);
            if (jsonPath != null)
            {
                string path = Path.GetFullPath(jsonPath);
                if (string.Equals(path, Path.GetFullPath(args[1]), StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Choose a JSON report path distinct from the model export.");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };
                File.WriteAllText(path, JsonSerializer.Serialize(report, options) + Environment.NewLine);
            }
            if (!check && split == "calibration") return 0;
            if (report.FiveGroupPass && report.ExactSubtypePass)
            {
                Console.WriteLine("PASS both area-weighted agreements exceed 90%.");
                return 0;
            }
            Console.Error.WriteLine($"FAIL both agreements must exceed 90%. 5 groups {(report.FiveGroupPass ? "PASS" : "FAIL")}, 30 subtypes {(report.ExactSubtypePass ? "PASS" : "FAIL")}.");
            return 1;
        }
        catch (Exception error) when (error is IOException || error is InvalidDataException ||
            error is ArgumentException || error is FormatException || error is OverflowException || error is Win32Exception)
        {
            Console.Error.WriteLine("Invalid Köppen comparison: " + error.Message);
            return 2;
        }
    }

    private sealed record Report(string Split, string ModelFile, string ModelSha256, int ModelWidth, int ModelHeight, int Cells, int Blocks,
        string FootprintSha256, decimal AreaKm2, decimal FiveGroupAgreement, decimal ExactSubtypeAgreement,
        bool FiveGroupPass, bool ExactSubtypePass, Dictionary<string, ObservedSubtype> ObservedSubtypes, Confusion[] LargestConfusions, Hemisphere[] Hemispheres,
        Agreement WithObservedTemperature, Agreement WithObservedPrecipitation, RainfallAttribution RainfallAttribution,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Window CalibrationWindow);

    private sealed record Window(double West, double East, double South, double North)
    {
        public static Window Parse(string[] args, int start)
        {
            double Degrees(int offset) => double.Parse(args[start + offset], NumberStyles.Float, CultureInfo.InvariantCulture);
            var bounds = new Window(Degrees(0), Degrees(1), Degrees(2), Degrees(3));
            if (!(bounds.West >= -180 && bounds.East <= 180 && bounds.West < bounds.East &&
                  bounds.South >= -90 && bounds.North <= 90 && bounds.South < bounds.North))
                throw new ArgumentException("Expected increasing longitude and latitude bounds in degrees.");
            return bounds;
        }

        public bool Contains(int tile)
        {
            double lon = (tile % Width + .5) * 360 / Width - 180;
            double lat = (tile / Width + .5) * 180 / Height - 90;
            return lon >= West && lon < East && lat >= South && lat < North;
        }
    }

    private sealed record Agreement(decimal FiveGroupAgreement, decimal ExactSubtypeAgreement);
    private sealed record Confusion(string Observed, string Modeled, decimal AreaKm2, decimal ShareOfTotalArea);
    private sealed record RainfallAttribution(int Cells, decimal AreaKm2, Agreement PureSimulation,
        Agreement WithObservedAnnualTotal, Agreement WithObservedMonthlyFractions);

    private sealed class RainfallComparison
    {
        private readonly MatchedArea pure = new(), annual = new(), fractions = new();
        private readonly float[] hybrid = new float[Months];
        private int cells;
        private decimal area;

        public void Add(float[] temperature, float[] modeledRain, float[] observedRain, Koppen modeled,
            string observedClass, decimal weight)
        {
            double modelTotal = modeledRain.Sum(p => (double)p), observedTotal = observedRain.Sum(p => (double)p);
            // A rainless year has no defined seasonal fractions. This exclusion
            // applies only to these paired diagnostics, never to the global score.
            if (modelTotal == 0 || observedTotal == 0) return;
            cells++;
            area += weight;
            pure.Add(modeled, observedClass, weight);
            for (int m = 0; m < Months; m++)
                hybrid[m] = (float)(modeledRain[m] * (observedTotal / modelTotal));
            annual.Add(KoppenClassifier.Classify(temperature, hybrid), observedClass, weight);
            for (int m = 0; m < Months; m++)
                hybrid[m] = (float)(observedRain[m] * (modelTotal / observedTotal));
            fractions.Add(KoppenClassifier.Classify(temperature, hybrid), observedClass, weight);
        }

        public RainfallAttribution Summary() => area == 0
            ? new(0, 0, null, null, null)
            : new(cells, area, pure.Share(area), annual.Share(area), fractions.Share(area));
    }

    private sealed class MatchedArea
    {
        private decimal broad, exact;

        public void Add(Koppen candidate, string observed, decimal area)
        {
            string name = candidate.ToString();
            if (name[0] == observed[0]) broad += area;
            if (name == observed) exact += area;
        }

        public Agreement Share(decimal total) => new(broad / total, exact / total);
    }

    private sealed class ObservedSubtype
    {
        public decimal AreaKm2 { get; private set; }
        private decimal matchedArea;
        public decimal? Recall => AreaKm2 == 0 ? null : matchedArea / AreaKm2;

        public void Add(decimal area, bool matched)
        {
            AreaKm2 += area;
            if (matched) matchedArea += area;
        }
    }

    private static Report Score(string export, string split, Window window)
    {
        var (width, height, modelSha256, temperature, precipitation) = ReadModel(export);
        using var file = File.OpenRead(Path.Combine(DataDirectory, split + ".csv.gz"));
        using var data = new StreamReader(new GZipStream(file, CompressionMode.Decompress));
        var months = Enumerable.Range(1, Months).Select(m => m.ToString("D2", CultureInfo.InvariantCulture)).ToArray();
        var columns = new[] { "tile", "area_km2", "elevation_m" }.Concat(months.Select(m => "t" + m)).Concat(months.Select(m => "p" + m));
        if (data.ReadLine() != string.Join(",", columns)) throw new InvalidDataException("Unexpected CRU monthly columns.");

        var tiles = new SortedSet<ushort>();
        var blocks = new HashSet<int>();
        var north = new SeasonalErrors(true);
        var south = new SeasonalErrors(false);
        var observedT = new float[Months];
        var observedP = new float[Months];
        var localT = new float[Months];
        var localP = new float[Months];
        var withObservedT = new MatchedArea();
        var withObservedP = new MatchedArea();
        var rainfall = new RainfallComparison();
        var subtypes = Enum.GetValues<Koppen>().Where(k => k != Koppen.Unknown)
            .ToDictionary(k => k.ToString(), k => new ObservedSubtype());
        var confusions = new Dictionary<(Koppen Observed, Koppen Modeled), decimal>();
        decimal total = 0, broad = 0, exact = 0;
        string line;
        while ((line = data.ReadLine()) != null)
        {
            var row = line.Split(',');
            if (row.Length != 27 ||
                !int.TryParse(row[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tile) ||
                tile < 0 || tile >= Width * Height ||
                !decimal.TryParse(row[1], NumberStyles.Float, CultureInfo.InvariantCulture, out decimal area) || area < 0)
                throw new InvalidDataException("Invalid CRU tile or area.");
            int y = tile / Width;
            double edge = Math.PI * ((double)y / Height - .5);
            double tileArea = 6371.0088 * 6371.0088 * (2 * Math.PI / Width) * (Math.Sin(edge + Math.PI / Height) - Math.Sin(edge));
            // Eligibility depends only on the registered CRU land area, even on simulated water.
            if ((double)area < .5 * tileArea || (window != null && !window.Contains(tile))) continue;
            if (!tiles.Add((ushort)tile)) throw new InvalidDataException($"Repeated CRU tile {tile}.");
            blocks.Add(y / 16 * (Width / 16) + tile % Width / 16);
            for (int m = 0; m < Months; m++)
            {
                observedT[m] = float.Parse(row[3 + m], CultureInfo.InvariantCulture);
                observedP[m] = float.Parse(row[15 + m], CultureInfo.InvariantCulture);
                CheckValue(observedT[m], false, tile, m, "CRU");
                CheckValue(observedP[m], true, tile, m, "CRU");
            }
            int offset = tile * Months;
            var observed = KoppenClassifier.Classify(observedT, observedP);
            var modeled = KoppenClassifier.Classify(temperature, precipitation, offset);
            var name = observed.ToString();
            // Diagnostic substitutions only. The gate below always scores the
            // normal generator with both of its own monthly fields.
            Array.Copy(temperature, offset, localT, 0, Months);
            Array.Copy(precipitation, offset, localP, 0, Months);
            withObservedT.Add(KoppenClassifier.Classify(observedT, localP), name, area);
            withObservedP.Add(KoppenClassifier.Classify(localT, observedP), name, area);
            rainfall.Add(localT, localP, observedP, modeled, name, area);
            subtypes[name].Add(area, observed == modeled);
            if (observed == modeled) exact += area;
            else
            {
                var pair = (observed, modeled);
                confusions[pair] = confusions.GetValueOrDefault(pair) + area;
            }
            if (name[0] == modeled.ToString()[0]) broad += area;
            total += area;
            (y >= Height / 2 ? north : south).Add((double)area, observedT, observedP, temperature, precipitation, offset);
        }
        if (tiles.Count == 0) throw new InvalidDataException("No eligible CRU cells.");
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        foreach (ushort tile in tiles) writer.Write(tile);
        string digest = Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
        if (split == "holdout" && (tiles.Count != 1806 || blocks.Count != 17 || digest != HoldoutFootprint))
            throw new InvalidDataException("The fixed 1,806-cell, 17-block CRU holdout footprint changed.");
        // Decimal areas preserve the supplied four decimal places, including a score exactly on the threshold.
        return new Report(split, Path.GetFileName(export), modelSha256, width, height, tiles.Count, blocks.Count,
            digest, total, broad / total, exact / total,
            10 * broad > 9 * total, 10 * exact > 9 * total, subtypes,
            confusions.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key.Observed).ThenBy(pair => pair.Key.Modeled)
                .Take(10).Select(pair => new Confusion(pair.Key.Observed.ToString(), pair.Key.Modeled.ToString(), pair.Value, pair.Value / total)).ToArray(),
            new[] { north, south }.Where(h => h.Cells != 0).Select(h => h.Summary()).ToArray(),
            withObservedT.Share(total), withObservedP.Share(total), rainfall.Summary(), window);
    }

    private static (int, int, string, float[], float[]) ReadModel(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        long length = reader.BaseStream.Length;
        if (length < 12 || reader.ReadInt32() != 0x314c4345) throw new InvalidDataException("Expected an ECL1 export.");
        int w = reader.ReadInt32(), h = reader.ReadInt32();
        long count = (long)w * h;
        if (h < 1 || w != 2L * h || (length - 12) % 101 != 0 || (length - 12) / 101 != count)
            throw new InvalidDataException("Invalid ECL1 dimensions or file length.");
        reader.BaseStream.Position = 0;
        string digest = Convert.ToHexString(SHA256.HashData(reader.BaseStream)).ToLowerInvariant();
        if (w == Width)
        {
            reader.BaseStream.Position = 12 + 5 * count;
            return (w, h, digest, ReadField(reader, false), ReadField(reader, true));
        }
        // Use score.py's exact CRU source-land overlap and nested-grid remapping for every other size.
        // Only model fields and the source availability mask are accessed by this subprocess.
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "python" : "python3")
        {
            UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
        };
        start.ArgumentList.Add(Path.Combine(DataDirectory, "score.py"));
        start.ArgumentList.Add(path);
        start.ArgumentList.Add("--emit-koppen-normals");
        using var process = Process.Start(start);
        using var raw = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(raw);
        process.WaitForExit();
        if (process.ExitCode != 0 || raw.Length != 2L * Width * Height * Months * sizeof(float))
            throw new InvalidDataException("CRU grid collocation failed. Noncanonical sizes require Python, NumPy and a nested global grid.");
        raw.Position = 0;
        using var collocated = new BinaryReader(raw);
        return (w, h, digest, ReadField(collocated, false), ReadField(collocated, true));
    }

    private static float[] ReadField(BinaryReader reader, bool rain)
    {
        var field = new float[Width * Height * Months];
        for (int i = 0; i < field.Length; i++)
        {
            field[i] = reader.ReadSingle();
            CheckValue(field[i], rain, i / Months, i % Months, "model");
        }
        return field;
    }

    private static void CheckValue(float value, bool rain, int tile, int month, string source)
    {
        if (!float.IsFinite(value) || (rain && value < 0))
            throw new InvalidDataException($"Invalid {source} {(rain ? "precipitation" : "temperature")} at tile {tile}, month {month + 1}.");
    }

    private sealed record Season(string Name, string Months, double TemperatureMaeC, double PrecipitationMaeMm);
    private sealed record Hemisphere(string Name, int Cells, double AreaKm2, Season[] Seasons, double ObservedWetQuarterMaeMm,
        double ObservedDryQuarterMaeMm, double TemperatureCycleRmseC, double PrecipitationCycleRmseMm);

    private sealed class SeasonalErrors
    {
        private static readonly string[] Names = { "winter", "spring", "summer", "autumn" };
        private static readonly string[] Calendars = { "DJF", "MAM", "JJA", "SON" };
        private readonly bool northern;
        private readonly double[] temperatureError = new double[4], rainError = new double[4];
        private double area, wetError, dryError, temperatureCycleMse, rainCycleMse;
        public int Cells { get; private set; }
        public SeasonalErrors(bool northern) { this.northern = northern; }

        private int Calendar(int season) => (season + (northern ? 0 : 2)) % 4;

        private static double Quarter(float[] values, int offset, int m) =>
            (double)values[offset + m] + values[offset + (m + 1) % Months] + values[offset + (m + 2) % Months];

        private static double CycleMse(float[] observed, float[] modeled, int offset)
        {
            double mean = 0, sum = 0;
            for (int m = 0; m < Months; m++) mean += (double)modeled[offset + m] - observed[m];
            mean /= Months;
            for (int m = 0; m < Months; m++)
            {
                double error = (double)modeled[offset + m] - observed[m] - mean;
                sum += error * error;
            }
            return sum / Months;
        }

        public void Add(double weight, float[] observedT, float[] observedP, float[] modeledT, float[] modeledP, int offset)
        {
            Cells++;
            area += weight;
            for (int season = 0; season < 4; season++)
            {
                int first = (11 + 3 * Calendar(season)) % Months;
                temperatureError[season] += weight * Math.Abs(Quarter(modeledT, offset, first) - Quarter(observedT, 0, first)) / 3;
                rainError[season] += weight * Math.Abs(Quarter(modeledP, offset, first) - Quarter(observedP, 0, first));
            }
            temperatureCycleMse += weight * CycleMse(observedT, modeledT, offset);
            rainCycleMse += weight * CycleMse(observedP, modeledP, offset);
            // Anchor wet and dry quarters to the observed calendar, including year-wrapping windows.
            // Average all equally wet/dry windows so ties do not favor any particular month.
            Span<double> quarters = stackalloc double[Months];
            Span<double> errors = stackalloc double[Months];
            double wet = double.NegativeInfinity, dry = double.PositiveInfinity;
            for (int m = 0; m < Months; m++)
            {
                quarters[m] = Quarter(observedP, 0, m);
                errors[m] = Math.Abs(Quarter(modeledP, offset, m) - quarters[m]);
                wet = Math.Max(wet, quarters[m]);
                dry = Math.Min(dry, quarters[m]);
            }
            int wetCount = 0, dryCount = 0;
            double wetSum = 0, drySum = 0;
            for (int m = 0; m < Months; m++)
            {
                if (quarters[m] == wet) { wetSum += errors[m]; wetCount++; }
                if (quarters[m] == dry) { drySum += errors[m]; dryCount++; }
            }
            wetError += weight * wetSum / wetCount;
            dryError += weight * drySum / dryCount;
        }

        public Hemisphere Summary()
        {
            var seasons = new Season[4];
            for (int s = 0; s < seasons.Length; s++)
                seasons[s] = new Season(Names[s], Calendars[Calendar(s)], temperatureError[s] / area, rainError[s] / area);
            return new Hemisphere(northern ? "north" : "south", Cells, area, seasons, wetError / area, dryError / area,
                Math.Sqrt(temperatureCycleMse / area), Math.Sqrt(rainCycleMse / area));
        }
    }

    private static void Print(Report report)
    {
        if (report.CalibrationWindow is Window w)
            Console.WriteLine(FormattableString.Invariant($"Calibration window: {w.West} to {w.East} longitude, {w.South} to {w.North} latitude."));
        Console.WriteLine(FormattableString.Invariant($"{report.Cells} CRU {report.Split} cells, area-weighted Köppen agreement: 5 groups {report.FiveGroupAgreement:P2}, 30 subtypes {report.ExactSubtypeAgreement:P2}"));
        Console.WriteLine(FormattableString.Invariant($"{report.Blocks} spatial blocks, {report.AreaKm2:F4} km2, reference footprint SHA-256 {report.FootprintSha256}"));
        foreach (var h in report.Hemispheres)
        {
            Console.WriteLine($"{h.Name} hemisphere ({h.Cells} cells), MAE of seasonal mean temperature and total precipitation:");
            foreach (var s in h.Seasons)
                Console.WriteLine(FormattableString.Invariant($"  {s.Name,-6} ({s.Months}) {s.TemperatureMaeC:F2} °C, {s.PrecipitationMaeMm:F2} mm/quarter"));
            Console.WriteLine(FormattableString.Invariant($"  Observed wet/dry quarter MAE {h.ObservedWetQuarterMaeMm:F2}/{h.ObservedDryQuarterMaeMm:F2} mm/quarter"));
            Console.WriteLine(FormattableString.Invariant($"  Demeaned monthly cycle RMSE {h.TemperatureCycleRmseC:F2} °C, {h.PrecipitationCycleRmseMm:F2} mm/month"));
        }
    }
}
