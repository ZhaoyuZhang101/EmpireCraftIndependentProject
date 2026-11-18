using System;

namespace Orbis.Flat
{
    [Flags]
    public enum ClimateSignals
    {
        None=0, Hot=1, Cold=2, LowPressure=4, Wet=8, Dry=16, HighWind=32, Compound=64,
        SoilDry=128, GroundwaterLow=256, RunoffHigh=512, RunoffLow=1024, HighFlow=2048,
        DroughtRisk=4096, FloodPotential=8192
    }

    // Owned by a single caller. Size the arrays once for all supported profiles.
    public sealed class ClimateScoreWorkspace
    {
        readonly double[][] rows = new double[2 * ClimateFeatureSet.FeatureCount + 1][];
        internal readonly double[] Solve = new double[ClimateFeatureSet.FeatureCount];
        public ClimateScoreWorkspace()
        {
            for (int n = 1; n < rows.Length; n++) rows[n] = new double[n];
        }
        internal double[] Row(int n) => rows[n];
    }

    public readonly struct ClimateScore
    {
        public readonly bool Valid;
        public readonly double SquaredDistance, Reliability;
        public readonly ClimateSignals Signals;
        public double Distance => Valid ? Math.Sqrt(SquaredDistance) : double.NaN;
        internal ClimateScore(double squared, double reliability, ClimateSignals signals)
        {
            Valid = true;
            SquaredDistance = squared;
            Reliability = reliability;
            Signals = signals;
        }
    }

    // Moments have one row per accepted observation, with native axes first and
    // any transformed axes appended. A missing or overflowing axis rejects the
    // whole row atomically, keeping the physical-unit heatmap and metric paired.
    public sealed class ClimateReference
    {
        public readonly ClimateFeatureSet Features;
        public readonly bool WaterBudget;
        public readonly double StepWindowDays;
        readonly OnlineClimateMoments moments;
        readonly int[] encoded;
        readonly double[] deviation;
        ClimateGaussian model;
        public long Count => moments.Count;
        public bool Ready => model != null;
        public int ActiveDimensions => model?.ActiveDimension ?? 0;
        public double EffectiveCount => moments.EffectiveSampleSize;
        public double Reliability { get; private set; }
        public double Covariance(int i, int j) => moments.Covariance(i,j);
        public double Correlation(int i, int j) => moments.Correlation(i,j);
        public double Mean(int i) => moments.Mean(i);

        public ClimateReference(ClimateFeatureSet features, bool waterBudget = false, double stepWindowDays = 0)
        {
            Features = features ?? throw new ArgumentNullException(nameof(features));
            if (!OnlineClimateMoments.Finite(stepWindowDays) || stepWindowDays < 0 || (waterBudget && stepWindowDays > 0))
                throw new ArgumentOutOfRangeException(nameof(stepWindowDays));
            WaterBudget = waterBudget;
            StepWindowDays = stepWindowDays;
            int n = features.Dimension, columns = n;
            encoded = new int[n];
            deviation = new double[n];
            for (int i = 0; i < n; i++)
                encoded[i] = features[i].Transform == ClimateTransform.Identity && features[i].Scale == 1 ? i : columns++;
            moments = new OnlineClimateMoments(columns);
        }

        public double Value(in FlatClimateObservation row, ClimateFeature feature)
        {
            if (WaterBudget)
            {
                if (feature == ClimateFeature.Precipitation) return row.WaterBudgetPrecipitation;
                if (feature == ClimateFeature.Discharge) return row.WaterBudgetDischarge;
            }
            return ClimateFeatureSet.Value(row, feature);
        }

        bool Compatible(in FlatClimateObservation row)
        {
            if (StepWindowDays > 0)
                return row.Source == FlatClimateObservationSource.LayeredStep &&
                    Math.Abs(row.PrecipitationWindowDays - StepWindowDays) <= StepWindowDays * 1e-8;
            if (row.Source != FlatClimateObservationSource.MonthlyNormal && row.Source != FlatClimateObservationSource.RegionalForcing)
                return false;
            return WaterBudget ? row.WaterBudgetDays == FlatClimateSamples.MonthDays :
                Features.IndexOf(ClimateFeature.Precipitation) < 0 || row.PrecipitationWindowDays == FlatClimateSamples.MonthDays;
        }

        public bool Add(in FlatClimateObservation row, double weight, ClimateScoreWorkspace work)
        {
            if (work == null || !Compatible(row)) return false;
            var values = work.Row(moments.Dimension);
            for (int i = 0; i < Features.Dimension; i++)
            {
                values[i] = Value(row, Features[i].Feature);
                if (encoded[i] != i) values[encoded[i]] = Features[i].Encode(values[i]);
            }
            if (!moments.Add(values, weight)) return false;
            model = null;
            Reliability = 0;
            return true;
        }

        public void Merge(ClimateReference partition)
        {
            if (partition == null || !Features.Matches(partition.Features) || StepWindowDays != partition.StepWindowDays ||
                WaterBudget != partition.WaterBudget) throw new ArgumentException("Incompatible climate references.");
            moments.Merge(partition.moments);
            model = null;
            Reliability = 0;
        }

        public void Fit()
        {
            int n = Features.Dimension;
            model = null;
            Reliability = 0;
            if (EffectiveCount < n + 2 || !ClimateGaussian.TryFitAxes(moments, encoded, out model)) return;
            for (int i = 0; i < n; i++)
                deviation[i] = Math.Sqrt(Math.Max(0, moments.Covariance(encoded[i], encoded[i])));
            // Support index: interpolation/reliability weights do not create independent evidence.
            Reliability = Math.Min(1, EffectiveCount / (4 * (n + 1))) * model.UnregularizedRank / n *
                (model.UsedFallback ? .5 : 1);
        }

        public ClimateScore Score(in FlatClimateObservation row, ClimateScoreWorkspace work, double threshold = 2)
        {
            if (work == null || !(threshold > 0) || !OnlineClimateMoments.Finite(threshold) || model == null || !Compatible(row))
                return default;
            int n = Features.Dimension;
            var x = work.Row(n);
            for (int i = 0; i < n; i++) x[i] = Features[i].Encode(Value(row, Features[i].Feature));
            if (!model.TryDistanceSquared(x, work.Solve, out double squared)) return default;
            int unusual = 0;
            double largestZ2 = 0;
            ClimateSignals flags = 0;
            for (int i = 0; i < n; i++)
            {
                if (!(deviation[i] > 0)) continue;
                double z = (x[i] - model.Mean(i)) / deviation[i];
                largestZ2 = Math.Max(largestZ2, z*z);
                if (Math.Abs(z) < threshold) continue;
                unusual++;
                flags |= Direction(Features[i].Feature, z);
            }
            double cutoff = threshold * threshold;
            // A broken correlation can be a large joint departure even when all
            // marginals look normal. A single independent extreme alone is univariate.
            if (model.ActiveDimension >= 2 && squared >= cutoff * model.ActiveDimension &&
                (unusual >= 2 || squared - largestZ2 >= cutoff)) flags |= ClimateSignals.Compound;
            if ((flags & ClimateSignals.Dry) != 0 &&
                (flags & (ClimateSignals.SoilDry | ClimateSignals.RunoffLow | ClimateSignals.GroundwaterLow)) != 0)
                flags |= ClimateSignals.DroughtRisk;
            if ((flags & ClimateSignals.RunoffHigh) != 0 && (flags & (ClimateSignals.Wet | ClimateSignals.HighFlow)) != 0)
                flags |= ClimateSignals.FloodPotential;
            return new ClimateScore(squared, Reliability, flags);
        }

        static ClimateSignals Direction(ClimateFeature feature, double z)
        {
            switch (feature)
            {
                case ClimateFeature.Temperature: return z > 0 ? ClimateSignals.Hot : ClimateSignals.Cold;
                case ClimateFeature.Precipitation: return z > 0 ? ClimateSignals.Wet : ClimateSignals.Dry;
                case ClimateFeature.Runoff: return z > 0 ? ClimateSignals.RunoffHigh : ClimateSignals.RunoffLow;
                case ClimateFeature.Pressure: return z < 0 ? ClimateSignals.LowPressure : 0;
                case ClimateFeature.WindSpeed: return z > 0 ? ClimateSignals.HighWind : 0;
                case ClimateFeature.SoilMoisture: return z < 0 ? ClimateSignals.SoilDry : 0;
                case ClimateFeature.Groundwater: return z < 0 ? ClimateSignals.GroundwaterLow : 0;
                case ClimateFeature.Discharge: return z > 0 ? ClimateSignals.HighFlow : 0;
                default: return 0;
            }
        }
    }
}
