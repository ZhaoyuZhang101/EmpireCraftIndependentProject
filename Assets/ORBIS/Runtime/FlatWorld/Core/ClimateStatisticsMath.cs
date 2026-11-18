using System;

namespace Orbis.Flat
{
    /// <summary>Mergeable complete-case moments with positive reliability or area weights.</summary>
    public sealed class OnlineClimateMoments
    {
        double[] means, meanErrors, covariance;
        double[] nextMeans, nextMeanErrors, nextCovariance;
        readonly double[] scaledDelta;
        // Sum of squared normalized weights, W2 / W^2.
        double squaredWeightFraction;

        public int Dimension { get; }
        public long Count { get; private set; }
        public long RejectedCount { get; private set; }
        public double TotalWeight { get; private set; }
        public double EffectiveSampleSize => Count == 0 ? 0 : 1 / squaredWeightFraction;
        public double CovarianceDenominator { get; private set; }
        public bool HasSampleCovariance => Count >= 2 && CovarianceDenominator > 0;

        public OnlineClimateMoments(int dimension)
        {
            if (dimension <= 0) throw new ArgumentOutOfRangeException(nameof(dimension));
            int matrixLength = checked(dimension * dimension);
            Dimension = dimension;
            means = new double[dimension];
            meanErrors = new double[dimension];
            nextMeans = new double[dimension];
            nextMeanErrors = new double[dimension];
            scaledDelta = new double[dimension];
            covariance = new double[matrixLength];
            nextCovariance = new double[matrixLength];
        }

        // A rejected row changes only RejectedCount, including on arithmetic overflow.
        public bool Add(double[] values, double weight = 1)
        {
            if (values != null && values.Length == Dimension && weight > 0 && Finite(weight))
            {
                int i = 0;
                while (i < Dimension && Finite(values[i])) ++i;
                if (i == Dimension && Combine(values, null, null, 1, weight, 0, 1)) return true;
            }
            AddRejections(1);
            return false;
        }

        public int AddBatch(double[][] observations, double[] weights = null)
        {
            if (observations == null) throw new ArgumentNullException(nameof(observations));
            if (weights != null && weights.Length != observations.Length)
                throw new ArgumentException("One weight is required per observation.", nameof(weights));
            int accepted = 0;
            for (int i = 0; i < observations.Length; ++i)
                if (Add(observations[i], weights == null ? 1 : weights[i])) ++accepted;
            return accepted;
        }

        public void Merge(OnlineClimateMoments other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (other.Dimension != Dimension) throw new ArgumentException("Moment dimensions differ.", nameof(other));
            if (other.Count > 0 && !Combine(other.means, other.meanErrors, other.covariance, other.Count,
                other.TotalWeight, other.CovarianceDenominator, other.squaredWeightFraction))
                throw new OverflowException("The combined climate moments exceed the representable range.");
            AddRejections(other.RejectedCount);
        }

        public double Mean(int i) => means[i];

        // Only the lower triangle is accumulated. Both orders read the identical value.
        public double Covariance(int i, int j)
        {
            if ((uint)i >= (uint)Dimension || (uint)j >= (uint)Dimension) throw new IndexOutOfRangeException();
            return i >= j ? covariance[i * Dimension + j] : covariance[j * Dimension + i];
        }

        // Zero denotes an undefined Pearson coefficient on an empty or constant axis.
        public double Correlation(int i, int j)
        {
            double a = Covariance(i, i), b = Covariance(j, j);
            if (a <= 0 || b <= 0) return 0;
            if (i == j) return 1;
            double value = Standardize(Covariance(i, j), Math.Sqrt(a), Math.Sqrt(b));
            return Math.Max(-1, Math.Min(1, value));
        }

        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        internal static double Standardize(double value, double sigmaA, double sigmaB)
        {
            // Dividing by the smaller deviation first avoids forming an extreme product.
            return value / Math.Min(sigmaA, sigmaB) / Math.Max(sigmaA, sigmaB);
        }

        void AddRejections(long rejected)
        {
            RejectedCount = rejected > long.MaxValue - RejectedCount ? long.MaxValue : RejectedCount + rejected;
        }

        // Weighted Chan merge; a one-row right side gives the weighted Welford update.
        // Keeping C = M2 / D directly avoids multiplying huge weight units into M2.
        bool Combine(double[] rightMeans, double[] rightErrors, double[] rightCovariance, long rightCount,
            double rightWeight, double rightDenominator, double rightSquaredWeightFraction)
        {
            if (Count == 0)
            {
                Array.Copy(rightMeans, means, Dimension);
                if (rightErrors != null) Array.Copy(rightErrors, meanErrors, Dimension);
                if (rightCovariance != null) Array.Copy(rightCovariance, covariance, covariance.Length);
                Count = rightCount;
                TotalWeight = rightWeight;
                CovarianceDenominator = rightDenominator;
                squaredWeightFraction = rightSquaredWeightFraction;
                return true;
            }
            double total = TotalWeight + rightWeight;
            if (!Finite(total) || rightCount > long.MaxValue - Count) return false;
            double leftShare = TotalWeight / total, rightShare = rightWeight / total;
            double twiceCross = Math.Min(TotalWeight, rightWeight) * (2 * Math.Max(leftShare, rightShare));
            // D = W - W2 / W. This recurrence avoids subtracting near-equal weights.
            double denominator = leftShare * CovarianceDenominator + rightShare * rightDenominator + twiceCross;
            if (denominator <= 0 || !Finite(denominator)) return false;
            double leftFactor = CovarianceDenominator / denominator, rightFactor = rightDenominator / denominator;
            double crossFraction = .5 * (twiceCross / denominator);
            double deltaScale = crossFraction > 1e-300 ? Math.Sqrt(crossFraction)
                : Math.Sqrt(twiceCross) / Math.Sqrt(denominator) * .7071067811865475244;
            bool baseOnLeft = TotalWeight >= rightWeight;
            for (int i = 0; i < Dimension; ++i)
            {
                double rightError = rightErrors == null ? 0 : rightErrors[i];
                double delta = (rightMeans[i] - means[i]) + (rightError - meanErrors[i]);
                double stepScale = baseOnLeft ? rightShare : -leftShare;
                double step = delta * stepScale;
                scaledDelta[i] = delta * deltaScale;
                if (!Finite(delta))
                {
                    step = rightMeans[i] * stepScale - means[i] * stepScale + (rightError - meanErrors[i]) * stepScale;
                    scaledDelta[i] = rightMeans[i] * deltaScale - means[i] * deltaScale + (rightError - meanErrors[i]) * deltaScale;
                }
                // Save the rounding residual of the mean, so small climate variations
                // on a large absolute offset still contribute to the next centered update.
                double origin = baseOnLeft ? means[i] : rightMeans[i];
                double correction = step + (baseOnLeft ? meanErrors[i] : rightError);
                double updated = origin + correction;
                if (!Finite(updated) || !Finite(scaledDelta[i])) return false;
                nextMeans[i] = updated;
                nextMeanErrors[i] = Math.Abs(origin) >= Math.Abs(correction)
                    ? (origin - updated) + correction : (correction - updated) + origin;
            }
            for (int i = 0; i < Dimension; ++i)
            {
                int offset = i * Dimension;
                for (int j = 0; j <= i; ++j)
                {
                    int k = offset + j;
                    double value = leftFactor * covariance[k] + scaledDelta[i] * scaledDelta[j];
                    if (rightCovariance != null) value += rightFactor * rightCovariance[k];
                    if (!Finite(value)) return false;
                    nextCovariance[k] = value;
                }
            }
            Swap(ref means, ref nextMeans);
            Swap(ref meanErrors, ref nextMeanErrors);
            Swap(ref covariance, ref nextCovariance);
            squaredWeightFraction = leftShare * leftShare * squaredWeightFraction +
                rightShare * rightShare * rightSquaredWeightFraction;
            Count += rightCount;
            TotalWeight = total;
            CovarianceDenominator = denominator;
            return true;
        }

        static void Swap(ref double[] a, ref double[] b)
        {
            double[] temp = a;
            a = b;
            b = temp;
        }
    }

    /// <summary>An immutable Gaussian metric. Callers supply scratch space to score concurrently.</summary>
    public sealed class ClimateGaussian
    {
        const double MachineEpsilon = 2.2204460492503131e-16;
        const double SingularShrinkage = 1e-8;
        readonly double[] means, deviations, lower;
        readonly int[] active;

        public int Dimension => means.Length;
        public int ActiveDimension => active.Length;
        public int UnregularizedRank { get; }
        public double Regularization { get; }
        public bool UsedFallback { get; }
        // Log determinant in the original units of the retained, varying axes.
        public double LogDeterminant { get; }
        public double Mean(int index) => means[index];

        ClimateGaussian(double[] mean, double[] deviations, int[] active, double[] lower, int rank, double shrinkage, bool fallback)
        {
            means = (double[])mean.Clone();
            this.deviations = deviations;
            this.active = active;
            this.lower = lower;
            UnregularizedRank = rank;
            Regularization = shrinkage;
            UsedFallback = fallback;
            double log = 0;
            for (int i = 0; i < active.Length; ++i)
                log += Math.Log(deviations[active[i]]) + Math.Log(lower[i * active.Length + i]);
            LogDeterminant = 2 * log;
        }

        public static bool TryFit(OnlineClimateMoments moments, out ClimateGaussian model, double regularization = .02)
            => TryFitAxes(moments, null, out model, regularization);

        internal static bool TryFitAxes(OnlineClimateMoments moments, int[] axes, out ClimateGaussian model, double regularization = .02)
        {
            model = null;
            if (moments == null || !moments.HasSampleCovariance) return false;
            int n = axes?.Length ?? moments.Dimension;
            var mean = new double[n];
            var cov = new double[n * n];
            for (int i = 0; i < n; ++i)
            {
                int source = axes == null ? i : axes[i];
                mean[i] = moments.Mean(source);
                for (int j = 0; j <= i; ++j)
                    cov[i * n + j] = cov[j * n + i] = moments.Covariance(source, axes == null ? j : axes[j]);
            }
            // Independently rounded entries of a long Welford stream can leave a
            // small residual in an exact null space. Bound the allowance in
            // correlation units; configured reference matrices use only factorization tolerance.
            double accumulatedRoundoff = Math.Min(1e-9, 8 * MachineEpsilon * Math.Sqrt(moments.Count));
            return Create(mean, cov, out model, regularization, accumulatedRoundoff);
        }

        // Shrink the correlation matrix to the identity, preserving each physical variance.
        // Exact zero-variance axes have no scale to learn and are omitted; a wholly
        // constant model is unavailable. No incomplete observation is scored.
        public static bool TryCreate(double[] mean, double[] covarianceRowMajor, out ClimateGaussian model, double regularization = .02)
            => Create(mean, covarianceRowMajor, out model, regularization, 0);

        static bool Create(double[] mean, double[] covarianceRowMajor, out ClimateGaussian model, double regularization, double accumulatedRoundoff)
        {
            model = null;
            if (mean == null || mean.Length == 0 || covarianceRowMajor == null ||
                (long)mean.Length * mean.Length != covarianceRowMajor.Length ||
                !OnlineClimateMoments.Finite(regularization) || regularization < 0 || regularization > 1) return false;
            int n = mean.Length, varying = 0;
            var deviations = new double[n];
            for (int i = 0; i < n; ++i)
            {
                double variance = covarianceRowMajor[i * n + i];
                if (!OnlineClimateMoments.Finite(mean[i]) || !OnlineClimateMoments.Finite(variance) || variance < 0) return false;
                deviations[i] = Math.Sqrt(variance);
                if (variance > 0) ++varying;
            }
            if (varying == 0) return false;
            var active = new int[varying];
            var correlation = new double[varying * varying];
            double tolerance = Math.Max(64 * MachineEpsilon * n, accumulatedRoundoff);
            int row = 0;
            for (int i = 0; i < n; ++i)
            {
                if (deviations[i] > 0) active[row] = i;
                int column = 0;
                for (int j = 0; j < i; ++j)
                {
                    double a = covarianceRowMajor[i * n + j], b = covarianceRowMajor[j * n + i];
                    if (deviations[i] == 0 || deviations[j] == 0)
                    {
                        if (a != 0 || b != 0) return false;
                    }
                    else
                    {
                        a = OnlineClimateMoments.Standardize(a, deviations[i], deviations[j]);
                        b = OnlineClimateMoments.Standardize(b, deviations[i], deviations[j]);
                        if (!OnlineClimateMoments.Finite(a) || !OnlineClimateMoments.Finite(b) ||
                            Math.Abs(a - b) > tolerance || Math.Abs(a) > 1 + tolerance || Math.Abs(b) > 1 + tolerance) return false;
                        double rho = Math.Max(-1, Math.Min(1, .5 * (a + b)));
                        correlation[row * varying + column] = correlation[column * varying + row] = rho;
                    }
                    if (deviations[j] > 0) ++column;
                }
                if (deviations[i] > 0)
                {
                    correlation[row * varying + row] = 1;
                    ++row;
                }
            }
            // Validate before shrinkage: even lambda=1 must not hide an invalid supplied matrix.
            int rank = PositiveSemidefiniteRank(correlation, varying, tolerance);
            if (rank < 0) return false;
            double shrinkage = rank < varying ? Math.Max(regularization, SingularShrinkage) : regularization;
            var lower = new double[correlation.Length];
            if (!Cholesky(correlation, varying, shrinkage, lower))
            {
                shrinkage = Math.Max(shrinkage, SingularShrinkage);
                if (!Cholesky(correlation, varying, shrinkage, lower)) return false;
            }
            bool fallback = varying < n || rank < varying || shrinkage != regularization;
            model = new ClimateGaussian(mean, deviations, active, lower, rank, shrinkage, fallback);
            return true;
        }

        // Pivoted Schur complements distinguish PSD rank loss from an indefinite
        // matrix, including correlations with individually plausible pairwise entries.
        static int PositiveSemidefiniteRank(double[] correlation, int n, double tolerance)
        {
            var work = (double[])correlation.Clone();
            for (int k = 0; k < n; ++k)
            {
                int pivot = k;
                for (int i = k + 1; i < n; ++i)
                    if (work[i * n + i] > work[pivot * n + pivot]) pivot = i;
                double diagonal = work[pivot * n + pivot];
                if (diagonal <= tolerance)
                {
                    for (int i = k; i < n; ++i)
                        for (int j = k; j <= i; ++j)
                            if (Math.Abs(work[i * n + j]) > tolerance) return -1;
                    return k;
                }
                if (pivot != k)
                {
                    for (int j = 0; j < n; ++j) SwapEntries(work, pivot * n + j, k * n + j);
                    for (int i = 0; i < n; ++i) SwapEntries(work, i * n + pivot, i * n + k);
                }
                for (int i = k + 1; i < n; ++i)
                {
                    double factor = work[i * n + k] / diagonal;
                    for (int j = k + 1; j <= i; ++j)
                    {
                        double residual = work[i * n + j] - factor * work[j * n + k];
                        if (!OnlineClimateMoments.Finite(residual)) return -1;
                        work[i * n + j] = work[j * n + i] = residual;
                    }
                }
            }
            return n;
        }

        static void SwapEntries(double[] array, int a, int b)
        {
            double temp = array[a];
            array[a] = array[b];
            array[b] = temp;
        }

        static bool Cholesky(double[] correlation, int n, double shrinkage, double[] lower)
        {
            for (int i = 0; i < n; ++i)
                for (int j = 0; j <= i; ++j)
                {
                    double value = i == j ? 1 : (1 - shrinkage) * correlation[i * n + j];
                    for (int k = 0; k < j; ++k) value -= lower[i * n + k] * lower[j * n + k];
                    if (i == j && !(value > 0)) return false;
                    lower[i * n + j] = i == j ? Math.Sqrt(value) : value / lower[j * n + j];
                }
            return true;
        }

        public bool TryDistanceSquared(double[] values, double[] scratch, out double squared)
        {
            squared = 0;
            if (values == null || values.Length != Dimension || scratch == null || scratch.Length < ActiveDimension) return false;
            for (int i = 0; i < Dimension; ++i)
                if (!OnlineClimateMoments.Finite(values[i])) return false;
            double sum = 0;
            for (int i = 0; i < ActiveDimension; ++i)
            {
                int axis = active[i], offset = i * ActiveDimension;
                double z = (values[axis] - means[axis]) / deviations[axis];
                for (int j = 0; j < i; ++j) z -= lower[offset + j] * scratch[j];
                z /= lower[offset + i];
                scratch[i] = z;
                sum += z * z;
            }
            if (!OnlineClimateMoments.Finite(sum)) return false;
            squared = sum;
            return true;
        }

        public bool TryDistance(double[] values, double[] scratch, out double distance)
        {
            bool available = TryDistanceSquared(values, scratch, out double squared);
            distance = Math.Sqrt(squared);
            return available;
        }
    }
}
