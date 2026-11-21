using System;
using System.Threading;

namespace Orbis.Flat
{
    // Diagnostic classes are independent of the serialized Biome and VegetationCover IDs.
    public enum FlatBiomeClass : byte
    {
        TropicalRainforest, TropicalSavanna, TemperateForest, TemperateGrassland,
        Desert, Tundra, BorealForest, Alpine, None = 255
    }

    public enum FlatBiomeStatus : byte { Unavailable, Estimated, Ambiguous, OutsideReference, ProtectedHabitat }

    public enum FlatBiomeFeature
    {
        MeanTemperature, AnnualPrecipitation, TemperatureRange,
        PrecipitationCoefficientOfVariation, AbsoluteLatitude, Elevation
    }

    /// <summary>Mutable calibration data for exchange. Covariance uses row-major order in the native units in Schema.</summary>
    [Serializable]
    public sealed class FlatBiomeReference
    {
        public FlatBiomeClass Type;
        public string Schema = FlatBiomeStatistics.FeatureSchema;
        public string Source;
        public double Prior;
        public double[] Mean, Covariance, Minimum, Maximum;
        // External climatologies may leave both support counts at zero when unknown.
        public long SampleCount;
        public double EffectiveSampleSize;

        internal FlatBiomeReference Copy()
        {
            var copy = (FlatBiomeReference)MemberwiseClone();
            copy.Mean = (double[])Mean.Clone();
            copy.Covariance = (double[])Covariance.Clone();
            copy.Minimum = (double[])Minimum.Clone();
            copy.Maximum = (double[])Maximum.Clone();
            return copy;
        }
    }

    /// <summary>One scratch object per caller or worker. Probabilities are replaced by each Estimate call.</summary>
    public sealed class FlatBiomeScratch
    {
        public readonly double[] Probabilities = new double[FlatBiomeStatistics.ClassCount];
        internal readonly double[] Features = new double[FlatBiomeStatistics.FeatureCount];
        internal readonly double[] Solve = new double[FlatBiomeStatistics.FeatureCount];
        internal readonly double[] LocalSupport = new double[FlatBiomeStatistics.ClassCount];
        internal readonly double[] Scores = new double[FlatBiomeStatistics.ClassCount];
    }

    public readonly struct FlatBiomeEstimate
    {
        public readonly FlatBiomeStatus Status;
        public readonly FlatBiomeClass Best, RunnerUp;
        // Conditional probability among eligible references, including the weak neighborhood prior.
        public readonly double Confidence;
        public readonly double DistanceSquared;

        internal FlatBiomeEstimate(FlatBiomeStatus status, FlatBiomeClass best = FlatBiomeClass.None,
                                   FlatBiomeClass runnerUp = FlatBiomeClass.None, double confidence = 0, double distanceSquared = 0)
        {
            Status = status;
            Best = best;
            RunnerUp = runnerUp;
            Confidence = confidence;
            DistanceSquared = distanceSquared;
        }
    }

    /// <summary>Read-only, optional terrestrial biome diagnostic fitted from long-term local climate and terrain labels.</summary>
    public sealed class FlatBiomeStatistics
    {
        public const int ClassCount = 8, FeatureCount = 6;
        public const string FeatureSchema = "ORBIS biome v1 | annual mean temperature [degC], annual precipitation [mm], " +
            "warmest minus coldest monthly mean [degC], monthly precipitation CV [1], absolute latitude [deg], elevation [m]";
        public const double MinimumEffectiveSamples = 2 * FeatureCount;
        public const double MinimumConfidence = .60;
        // Chi-square(6), 99.9% quantile. This is a diagnostic extrapolation cutoff.
        public const double MaximumDistanceSquared = 22.458;
        // World-only smoothing widths in the six native units. A flat quantized plateau or a nearly
        // constant tropical summer still has a finite resolvable local habitat width.
        static readonly double[] Smoothing = { 1, 60, 1, .05, 1, 100 };
        static readonly double[] PhysicalMinimum = { -100, 0, 0, 0, 0, HeightBands.MinimumBed };
        static readonly double[] PhysicalMaximum = { 100, 120000, 200, Math.Sqrt(11), 90, HeightBands.MaximumBed };
        readonly Entry[] entries = new Entry[ClassCount];
        public int AvailableClasses { get; private set; }

        sealed class Entry
        {
            internal readonly FlatBiomeReference Reference;
            internal readonly ClimateGaussian Gaussian;
            internal double LogPriorDensity;

            internal Entry(FlatBiomeReference reference, ClimateGaussian gaussian)
            {
                Reference = reference;
                Gaussian = gaussian;
            }
        }

        FlatBiomeStatistics() { }

        public static FlatBiomeStatistics Calibrate(WorldData world, CancellationToken cancel = default)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            cancel.ThrowIfCancellationRequested();
            var result = new FlatBiomeStatistics();
            var moments = new OnlineClimateMoments[ClassCount];
            var minimum = new double[ClassCount, FeatureCount];
            var maximum = new double[ClassCount, FeatureCount];
            var features = new double[FeatureCount];
            for (int c = 0; c < ClassCount; c++) moments[c] = new OnlineClimateMoments(FeatureCount);

            for (int row = 0; row < world.Height; row++)
            {
                cancel.ThrowIfCancellationRequested();
                // Equal longitude/latitude cells have area proportional to cos(center latitude),
                // exactly up to a factor shared by every row on this grid.
                double weight = Math.Cos(((row + .5) / world.Height - .5) * Math.PI);
                int end = (row + 1) * world.Width;
                for (int tile = row * world.Width; tile < end; tile++)
                {
                    FlatBiomeClass label = TrainingLabel(world, tile);
                    if (label == FlatBiomeClass.None || !ReadFeatures(world, tile, features, out double cold, out double hot) ||
                        !Eligible(world, tile, label, cold, hot)) continue;
                    int c = (int)label;
                    if (!moments[c].Add(features, weight)) continue;
                    bool first = moments[c].Count == 1;
                    for (int j = 0; j < FeatureCount; j++)
                    {
                        if (first || features[j] < minimum[c, j]) minimum[c, j] = features[j];
                        if (first || features[j] > maximum[c, j]) maximum[c, j] = features[j];
                    }
                }
            }

            string source = "ORBIS model-derived terrain/ecology labels and 12 monthly normals, " +
                $"cos(latitude) surface-area weights, {world.Preset}, seed {world.Seed}, {world.Width}x{world.Height}";
            for (int c = 0; c < ClassCount; c++)
            {
                var m = moments[c];
                if (m.EffectiveSampleSize + 1e-9 < MinimumEffectiveSamples) continue;
                var reference = new FlatBiomeReference
                {
                    Type = (FlatBiomeClass)c, Source = source, Prior = m.TotalWeight,
                    SampleCount = m.Count, EffectiveSampleSize = m.EffectiveSampleSize,
                    Mean = new double[FeatureCount], Covariance = new double[FeatureCount * FeatureCount],
                    Minimum = new double[FeatureCount], Maximum = new double[FeatureCount]
                };
                for (int j = 0; j < FeatureCount; j++)
                {
                    reference.Mean[j] = m.Mean(j);
                    for (int k = 0; k < FeatureCount; k++)
                        reference.Covariance[j * FeatureCount + k] = m.Covariance(j, k);
                    reference.Covariance[j * FeatureCount + j] += Smoothing[j] * Smoothing[j];
                    // Permit overlap at ecotones, with a finite envelope around observed ranges.
                    double padding = Math.Max(2 * Smoothing[j], .5 * Math.Sqrt(m.Covariance(j, j)));
                    reference.Minimum[j] = Math.Max(PhysicalMinimum[j], minimum[c, j] - padding);
                    reference.Maximum[j] = Math.Min(PhysicalMaximum[j], maximum[c, j] + padding);
                }
                result.TryAdd(reference);
            }
            result.NormalizePriors();
            return result;
        }

        /// <summary>Imports a complete replacement set or a subset of classes. Positive prior weights are normalized.</summary>
        public static bool TryFromReferences(FlatBiomeReference[] references, out FlatBiomeStatistics model)
        {
            model = null;
            if (references == null || references.Length == 0 || references.Length > ClassCount) return false;
            var imported = new FlatBiomeStatistics();
            foreach (var reference in references)
                if (!imported.TryAdd(reference)) return false;
            if (!imported.NormalizePriors()) return false;
            model = imported;
            return true;
        }

        public FlatBiomeReference[] ExportReferences()
        {
            var copies = new FlatBiomeReference[AvailableClasses];
            int index = 0;
            foreach (var entry in entries)
                if (entry != null) copies[index++] = entry.Reference.Copy();
            return copies;
        }

        bool TryAdd(FlatBiomeReference r)
        {
            if (r == null || (int)r.Type >= ClassCount || entries[(int)r.Type] != null ||
                r.Schema != FeatureSchema || string.IsNullOrWhiteSpace(r.Source) ||
                !OnlineClimateMoments.Finite(r.Prior) || r.Prior <= 0 ||
                r.SampleCount < 0 || !OnlineClimateMoments.Finite(r.EffectiveSampleSize) ||
                r.EffectiveSampleSize < 0 || r.EffectiveSampleSize > r.SampleCount * (1 + 1e-8) ||
                r.Mean == null || r.Mean.Length != FeatureCount ||
                r.Minimum == null || r.Minimum.Length != FeatureCount ||
                r.Maximum == null || r.Maximum.Length != FeatureCount ||
                r.Covariance == null || r.Covariance.Length != FeatureCount * FeatureCount) return false;
            for (int j = 0; j < FeatureCount; j++)
                if (!OnlineClimateMoments.Finite(r.Minimum[j]) || !OnlineClimateMoments.Finite(r.Maximum[j]) ||
                    r.Mean[j] < r.Minimum[j] || r.Mean[j] > r.Maximum[j]) return false;
            // Comparisons of multivariate densities require all classes to use the same six dimensions.
            if (!ClimateGaussian.TryCreate(r.Mean, r.Covariance, out var gaussian) || gaussian.ActiveDimension != FeatureCount)
                return false;
            entries[(int)r.Type] = new Entry(r.Copy(), gaussian);
            AvailableClasses++;
            return true;
        }

        bool NormalizePriors()
        {
            if (AvailableClasses == 0) return false;
            double largest = 0, sum = 0;
            foreach (var e in entries)
                if (e != null) largest = Math.Max(largest, e.Reference.Prior);
            foreach (var e in entries)
                if (e != null) sum += e.Reference.Prior / largest;
            foreach (var e in entries)
            {
                if (e == null) continue;
                double prior = (e.Reference.Prior / largest) / sum;
                if (prior == 0) return false;
                e.Reference.Prior = prior;
                e.LogPriorDensity = Math.Log(prior) - .5 * e.Gaussian.LogDeterminant;
            }
            return true;
        }

        public FlatBiomeEstimate Estimate(WorldData world, int tile, FlatBiomeScratch work)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if ((uint)tile >= (uint)world.Count) throw new ArgumentOutOfRangeException(nameof(tile));
            if (work == null) throw new ArgumentNullException(nameof(work));
            Array.Clear(work.Probabilities, 0, ClassCount);
            if (Protected(world, tile)) return new FlatBiomeEstimate(FlatBiomeStatus.ProtectedHabitat);
            if (AvailableClasses == 0 || !ReadFeatures(world, tile, work.Features, out double cold, out double hot))
                return new FlatBiomeEstimate(FlatBiomeStatus.Unavailable);
            Neighborhood(world, tile, work.LocalSupport);

            FlatBiomeClass best = FlatBiomeClass.None, runnerUp = FlatBiomeClass.None;
            double bestScore = double.NegativeInfinity, secondScore = double.NegativeInfinity, bestDistance = 0;
            for (int c = 0; c < ClassCount; c++)
            {
                work.Scores[c] = double.NegativeInfinity;
                var e = entries[c];
                if (e == null || !Eligible(world, tile, (FlatBiomeClass)c, cold, hot) ||
                    !InRange(e.Reference, work.Features) ||
                    !e.Gaussian.TryDistanceSquared(work.Features, work.Solve, out double d2) || d2 > MaximumDistanceSquared)
                    continue;
                // Four adjacent labels contribute at most a 1.5x prior multiplier.
                double score = e.LogPriorDensity - .5 * d2 + Math.Log(1 + .125 * work.LocalSupport[c]);
                work.Scores[c] = score;
                if (score > bestScore)
                {
                    runnerUp = best;
                    secondScore = bestScore;
                    best = (FlatBiomeClass)c;
                    bestScore = score;
                    bestDistance = d2;
                }
                else if (score > secondScore)
                {
                    runnerUp = (FlatBiomeClass)c;
                    secondScore = score;
                }
            }
            if (best == FlatBiomeClass.None) return new FlatBiomeEstimate(FlatBiomeStatus.OutsideReference);
            double sum = 0;
            for (int c = 0; c < ClassCount; c++)
            {
                work.Probabilities[c] = Math.Exp(work.Scores[c] - bestScore);
                sum += work.Probabilities[c];
            }
            for (int c = 0; c < ClassCount; c++) work.Probabilities[c] /= sum;
            double confidence = work.Probabilities[(int)best];
            return new FlatBiomeEstimate(confidence >= MinimumConfidence ? FlatBiomeStatus.Estimated : FlatBiomeStatus.Ambiguous,
                                         best, runnerUp, confidence, bestDistance);
        }

        static bool InRange(FlatBiomeReference reference, double[] features)
        {
            for (int j = 0; j < FeatureCount; j++)
                if (features[j] < reference.Minimum[j] || features[j] > reference.Maximum[j]) return false;
            return true;
        }

        static bool ReadFeatures(WorldData w, int tile, double[] output, out double cold, out double hot)
        {
            cold = double.PositiveInfinity;
            hot = double.NegativeInfinity;
            if (w.Climates[tile] < Koppen.Af || w.Climates[tile] > Koppen.EF ||
                w.Elevation[tile] < HeightBands.MinimumBed || w.Elevation[tile] > HeightBands.MaximumBed) return false;
            double temp = 0, rain = 0, rainSquares = 0;
            for (int m = tile * 12; m < (tile + 1) * 12; m++)
            {
                double t = w.Temperature[m], p = w.Precipitation[m];
                // Use the same normal ranges as FlatSave.Validate. The positive checks also reject NaN.
                if (!(t >= -100 && t <= 100 && p >= 0 && p <= 10000)) return false;
                cold = Math.Min(cold, t);
                hot = Math.Max(hot, t);
                temp += t;
                rain += p;
                rainSquares += p * p;
            }
            output[(int)FlatBiomeFeature.MeanTemperature] = temp / 12;
            output[(int)FlatBiomeFeature.AnnualPrecipitation] = rain;
            output[(int)FlatBiomeFeature.TemperatureRange] = hot - cold;
            // Twelve equally long game months. For a completely dry year set seasonality to zero.
            output[(int)FlatBiomeFeature.PrecipitationCoefficientOfVariation] =
                rain > 0 ? Math.Min(PhysicalMaximum[3], Math.Sqrt(Math.Max(0, 12 * rainSquares / (rain * rain) - 1))) : 0;
            output[(int)FlatBiomeFeature.AbsoluteLatitude] = Math.Abs(w.Latitude(tile));
            output[(int)FlatBiomeFeature.Elevation] = w.Elevation[tile];
            return true;
        }

        static bool Protected(WorldData w, int tile)
        {
            var b = w.Biomes[tile];
            if (w.Water[tile] != WaterKind.Land || b == Biome.Water || b == Biome.Beach ||
                b == Biome.Snow || b == Biome.Wetland || w.Climates[tile] == Koppen.EF) return true;
            bool dry = FlatTerrainAnalysis.DryClimate(w.Climates[tile]);
            if (w.Facts != null)
            {
                var cover = w.Facts.Vegetation[tile];
                if (cover == VegetationCover.Marsh || cover == VegetationCover.Ice) return true;
                // Old saves can retain the regional biome while their reconstructed ecology recognizes the local oasis.
                if (dry && (cover == VegetationCover.Grassland ||
                    (cover >= VegetationCover.SeasonalForest && cover <= VegetationCover.TropicalRainforest))) return true;
            }
            // The terrain rules can sustain local grass or trees near persistent water in a dry climate.
            return dry && (b == Biome.Woodland || b == Biome.Grassland);
        }

        static FlatBiomeClass TrainingLabel(WorldData w, int tile)
        {
            if (Protected(w, tile)) return FlatBiomeClass.None;
            var k = w.Climates[tile];
            if (k < Koppen.Af || k > Koppen.EF) return FlatBiomeClass.None;
            var b = w.Biomes[tile];
            bool highland = w.Elevation[tile] >= 2680;
            if (k == Koppen.ET || b == Biome.Tundra)
                return highland ? FlatBiomeClass.Alpine : FlatBiomeClass.Tundra;
            if (w.Facts != null)
            {
                switch (w.Facts.Vegetation[tile])
                {
                    case VegetationCover.TropicalRainforest: return FlatBiomeClass.TropicalRainforest;
                    case VegetationCover.Savanna: return FlatBiomeClass.TropicalSavanna;
                    case VegetationCover.SeasonalForest:
                        return k == Koppen.Am || k == Koppen.Af ? FlatBiomeClass.TropicalRainforest : FlatBiomeClass.TemperateForest;
                    case VegetationCover.TemperateRainforest:
                    case VegetationCover.DeciduousForest: return FlatBiomeClass.TemperateForest;
                    case VegetationCover.Grassland:
                    case VegetationCover.DrySteppe: return FlatBiomeClass.TemperateGrassland;
                    case VegetationCover.BorealForest: return FlatBiomeClass.BorealForest;
                    case VegetationCover.Tundra: return highland ? FlatBiomeClass.Alpine : FlatBiomeClass.Tundra;
                    case VegetationCover.AlpineMeadow: return FlatBiomeClass.Alpine;
                    case VegetationCover.Bare:
                        if (b == Biome.Desert) return FlatBiomeClass.Desert;
                        if (b == Biome.Alpine) return FlatBiomeClass.Alpine;
                        return FlatBiomeClass.None;
                    default: return FlatBiomeClass.None; // Shrubland is outside this eight-class training taxonomy.
                }
            }
            // Imported terrain may have the saved labels before its derived ecology cache is published.
            switch (b)
            {
                case Biome.Rainforest: return FlatBiomeClass.TropicalRainforest;
                case Biome.Grassland: return k == Koppen.Aw ? FlatBiomeClass.TropicalSavanna : FlatBiomeClass.TemperateGrassland;
                case Biome.Steppe: return FlatBiomeClass.TemperateGrassland;
                case Biome.Woodland: return FlatBiomeClass.TemperateForest;
                case Biome.Desert: return FlatBiomeClass.Desert;
                case Biome.Taiga: return FlatBiomeClass.BorealForest;
                case Biome.Alpine: return FlatBiomeClass.Alpine;
                default: return FlatBiomeClass.None;
            }
        }

        static bool Eligible(WorldData w, int tile, FlatBiomeClass type, double cold, double hot)
        {
            // Same growing-season threshold and highland elevation used by FlatTerrainAnalysis and FlatEcology.
            bool alpine = w.Elevation[tile] >= 2680 && hot <= 10;
            if (w.Climates[tile] == Koppen.ET || w.Biomes[tile] == Biome.Tundra || (alpine && w.Biomes[tile] == Biome.Alpine))
                return type == (alpine ? FlatBiomeClass.Alpine : FlatBiomeClass.Tundra) && hot <= 10;
            switch (type)
            {
                case FlatBiomeClass.Alpine: return alpine;
                case FlatBiomeClass.Tundra: return hot <= 10 && !alpine;
                case FlatBiomeClass.TemperateForest:
                case FlatBiomeClass.BorealForest: return hot > 10;
                case FlatBiomeClass.TropicalRainforest:
                case FlatBiomeClass.TropicalSavanna: return cold >= 15;
                default: return true;
            }
        }

        static void Neighborhood(WorldData w, int tile, double[] local)
        {
            Array.Clear(local, 0, ClassCount);
            float inland = FlatMoisture.Continentality(w.CoastDistance[tile], w.Width);
            for (int d = 0; d < 4; d++)
            {
                int n = w.SphereNeighbor(tile, d);
                FlatBiomeClass type = TrainingLabel(w, n);
                if (type == FlatBiomeClass.None) continue;
                double heightDifference = Math.Abs(w.Elevation[tile] - w.Elevation[n]);
                double maritimeDifference = Math.Abs(inland - FlatMoisture.Continentality(w.CoastDistance[n], w.Width));
                local[(int)type] += 1 / (1 + heightDifference / 400 + 2 * maritimeDifference);
            }
        }
    }
}
