using System;
using Orbis.Flat;

internal static class ClimateMathRegression
{
    public static void Run()
    {
        WeightedBatchAndMerging();
        MissingRowsAndWeightRanges();
        DistanceAndAffineTransformation();
        PhysicalUnitsAndRegularization();
        SingularAndInvalidCovariances();
        LongCollinearStream();
        RepeatedScoring();
        Console.WriteLine("Climate statistics: batch/online/merge, weights, symmetry, PSD, Pearson, affine and unit changes, singularity, invalid inputs and allocation-free scoring passed.");
    }

    static void WeightedBatchAndMerging()
    {
        const int n = 317, d = 4;
        var rows = new double[n][];
        var weights = new double[n];
        var mean = new double[d];
        double w = 0, w2 = 0;
        for (int i = 0; i < n; ++i)
        {
            double t = Math.Sin(i * .17), p = Math.Cos(i * .23);
            rows[i] = new[] { 14 + t, 1200 + 81 * t + 40 * p, 4 - 3 * t, 17.0 };
            weights[i] = .31 + .16 * (i % 11);
            w += weights[i];
            w2 += weights[i] * weights[i];
            for (int j = 0; j < d; ++j) mean[j] += weights[i] * rows[i][j];
        }
        for (int j = 0; j < d; ++j) mean[j] /= w;
        var expected = new double[d, d];
        double denominator = w - w2 / w;
        for (int i = 0; i < n; ++i)
            for (int j = 0; j < d; ++j)
                for (int k = 0; k < d; ++k)
                    expected[j, k] += weights[i] * (rows[i][j] - mean[j]) * (rows[i][k] - mean[k]) / denominator;

        var batch = new OnlineClimateMoments(d);
        Require(batch.AddBatch(rows, weights) == n, "batch accepts every complete row");
        var online = new OnlineClimateMoments(d);
        var shards = new[] { new OnlineClimateMoments(d), new OnlineClimateMoments(d), new OnlineClimateMoments(d) };
        for (int i = 0; i < n; ++i)
        {
            Require(online.Add(rows[i], weights[i]), "online row accepted");
            shards[i % 3].Add(rows[i], weights[i]);
        }
        var merged = new OnlineClimateMoments(d);
        merged.Merge(shards[2]);
        shards[0].Merge(shards[1]);
        merged.Merge(shards[0]);
        merged.Merge(new OnlineClimateMoments(d));
        foreach (var moments in new[] { batch, online, merged })
        {
            Require(moments.Count == n && moments.Dimension == d && moments.HasSampleCovariance, "valid batch size");
            Near(w, moments.TotalWeight, "weight sum");
            Near(w * w / w2, moments.EffectiveSampleSize, "Kish effective sample size");
            Near(denominator, moments.CovarianceDenominator, "unbiased reliability-weight denominator");
            for (int j = 0; j < d; ++j)
            {
                Near(mean[j], moments.Mean(j), "weighted mean");
                for (int k = 0; k < d; ++k)
                {
                    Near(expected[j, k], moments.Covariance(j, k), "independent two-pass covariance");
                    Require(moments.Covariance(j, k) == moments.Covariance(k, j), "exact covariance symmetry");
                }
            }
            Near(-1, moments.Correlation(0, 2), "perfect inverse correlation");
            Near(expected[0, 1] / Math.Sqrt(expected[0, 0] * expected[1, 1]),
                moments.Correlation(0, 1), "Pearson coefficient");
            Require(moments.Correlation(3, 3) == 0 && moments.Correlation(1, 3) == 0, "constant-axis Pearson value");
            for (int i = 0; i < 80; ++i)
            {
                double projected = 0;
                for (int j = 0; j < d; ++j)
                    for (int k = 0; k < d; ++k)
                        projected += Math.Sin((j + 1) * (i + .1)) * moments.Covariance(j, k) * Math.Sin((k + 1) * (i + .1));
                Require(projected >= -1e-10, "nonnegative projected variance");
            }
            Require(ClimateGaussian.TryFit(moments, out var fit) && fit.ActiveDimension == 3 && fit.UnregularizedRank == 2,
                "fit from weighted observations identifies the two independent varying axes");
        }
    }

    static void MissingRowsAndWeightRanges()
    {
        var moments = new OnlineClimateMoments(2);
        Require(!ClimateGaussian.TryFit(moments, out _) && !moments.HasSampleCovariance, "empty fit unavailable");
        Require(moments.Add(new[] { 2.0, 5.0 }), "first row");
        Require(!ClimateGaussian.TryFit(moments, out _) && moments.Covariance(0, 0) == 0, "singleton covariance");
        Require(!moments.Add(null) && !moments.Add(new[] { 1.0 }), "missing or incomplete vector");
        Require(!moments.Add(new[] { 1234.0, double.NaN }) && !moments.Add(new[] { double.PositiveInfinity, 8.0 }),
            "a bad component excludes the entire row");
        var good = new[] { 6.0, -1.0 };
        Require(!moments.Add(good, 0) && !moments.Add(good, -1) && !moments.Add(good, double.NaN) &&
            !moments.Add(good, double.PositiveInfinity), "invalid weights");
        var invalidShard = new OnlineClimateMoments(2);
        invalidShard.Add(null);
        moments.Merge(invalidShard);
        Require(moments.RejectedCount == 9 && moments.Count == 1 && moments.Mean(0) == 2, "rejections survive empty merge");
        moments.Add(good);
        Near(8, moments.Covariance(0, 0), "two equally weighted observations variance");
        Near(-12, moments.Covariance(0, 1), "two equally weighted observations covariance");
        Require(!moments.Add(new[] { double.MaxValue, 1.0 }) && moments.Count == 2 && moments.Mean(0) == 4,
            "unrepresentable update leaves every accepted moment intact");
        Near(-12, moments.Covariance(0, 1), "preserved covariance after overflow");

        foreach (double factor in new[] { 1e-200, 1.0, 1e200 })
        {
            var scaled = new OnlineClimateMoments(2);
            scaled.Add(new[] { 2.0, 5.0 }, 2 * factor);
            scaled.Add(good, 3 * factor);
            Near(4.4, scaled.Mean(0), "mean independent of weight units");
            Near(-12, scaled.Covariance(0, 1), "covariance independent of weight units");
            Near(25.0 / 13, scaled.EffectiveSampleSize, "effective count independent of weight units");
        }
        var unbalanced = new OnlineClimateMoments(1);
        unbalanced.Add(new[] { 2.0 }, 1e200);
        unbalanced.Add(new[] { 6.0 }, 1e-200);
        Near(8, unbalanced.Covariance(0, 0), "unequal weights retain the small contribution in the denominator");
        Near(2e-200, unbalanced.CovarianceDenominator, "small unbiased denominator", 1e-12, 0);
        var ordinary = new OnlineClimateMoments(1);
        var offset = new OnlineClimateMoments(1);
        for (int i = 0; i < 101; ++i)
        {
            double value = i % 7;
            ordinary.Add(new[] { value });
            offset.Add(new[] { 1e15 + value });
        }
        Near(ordinary.Covariance(0, 0), offset.Covariance(0, 0), "small spread on a large offset");
    }

    static void DistanceAndAffineTransformation()
    {
        var mean = new[] { 1.0, -2 };
        var cov = new[] { 4.0, 1, 1, 9 };
        var point = new[] { 4.0, 2 };
        var scratch = new double[2];
        Require(ClimateGaussian.TryCreate(mean, cov, out var model, 0), "known positive-definite Gaussian");
        Require(model.Dimension == 2 && model.ActiveDimension == 2 && !model.UsedFallback && model.Regularization == 0,
            "exact unregularized model");
        Near(Math.Log(35), model.LogDeterminant, "determinant of the physical covariance");
        Require(model.TryDistanceSquared(point, scratch, out double squared), "squared distance available");
        Near(121.0 / 35, squared, "analytic non-diagonal Mahalanobis squared");
        Require(model.TryDistance(point, scratch, out double distance), "distance available");
        Near(11 / Math.Sqrt(35), distance, "distance and squared distance are distinct");
        // Rotation/shear, anisotropic scaling and translation. B C B^T is formed directly.
        var b = new double[,] { { 2, -1 }, { .4, .75 } };
        var shift = new[] { 117.0, -39 };
        var transformedCov = new double[4];
        var transformedMean = (double[])shift.Clone();
        var transformedPoint = (double[])shift.Clone();
        for (int i = 0; i < 2; ++i)
            for (int j = 0; j < 2; ++j)
            {
                transformedMean[i] += b[i, j] * mean[j];
                transformedPoint[i] += b[i, j] * point[j];
                for (int k = 0; k < 2; ++k)
                    for (int l = 0; l < 2; ++l)
                        transformedCov[2 * i + k] += b[i, j] * cov[2 * j + l] * b[k, l];
            }
        Require(ClimateGaussian.TryCreate(transformedMean, transformedCov, out var transformed, 0) &&
            transformed.TryDistanceSquared(transformedPoint, scratch, out distance), "affinely transformed fit");
        Near(squared, distance, "distance invariant to a full-rank nondiagonal affine transformation");
        Near(Math.Log(35) + 2 * Math.Log(1.9), transformed.LogDeterminant, "log determinant under affine transformation");
        mean[0] = double.NaN;
        cov[0] = 0;
        Near(1, model.Mean(0), "external inputs are snapshotted");
        Require(model.TryDistanceSquared(point, scratch, out squared), "cached factor outlives input arrays");
        Near(121.0 / 35, squared, "immutable reference distance");
    }

    static void PhysicalUnitsAndRegularization()
    {
        var scratch = new double[2];
        foreach (double shrinkage in new[] { 0.0, .02, .7, 1.0 })
        {
            Require(ClimateGaussian.TryCreate(new[] { 0.0, 0 }, new[] { 4.0, 1, 1, 9 }, out var baseline, shrinkage),
                "ordinary unit model");
            Require(ClimateGaussian.TryCreate(new[] { 0.0, 0 }, new[] { 4e-280, 1, 1, 9e280 }, out var rescaled, shrinkage),
                "extremely different physical units");
            Require(baseline.TryDistanceSquared(new[] { 3.0, 4 }, scratch, out double regular), "ordinary unit score");
            Require(rescaled.TryDistanceSquared(new[] { 3e-140, 4e140 }, scratch, out double extreme), "extreme unit score");
            Near(regular, extreme, "regularization commutes with physical unit changes");
            Near(baseline.LogDeterminant, rescaled.LogDeterminant, "reciprocal unit changes respect determinant", 1e-12);
        }
        var moments = new OnlineClimateMoments(2);
        moments.Add(new[] { 1e-140, -1e140 });
        moments.Add(new[] { 3e-140, 1e140 });
        Near(2e-280, moments.Covariance(0, 0), "tiny physical variance", 1e-12, 0);
        Near(2e280, moments.Covariance(1, 1), "large physical variance", 1e-12, 0);
        Near(1, moments.Correlation(0, 1), "unit-stable Pearson");
    }

    static void SingularAndInvalidCovariances()
    {
        var zero = new[] { 0.0, 0, 0 };
        var singular = new[] { 4.0, 6, 0, 6, 9, 0, 0, 0, 0 };
        Require(ClimateGaussian.TryCreate(zero, singular, out var model, 0), "rank-deficient positive-semidefinite covariance");
        Require(model.ActiveDimension == 2 && model.UnregularizedRank == 1 && model.UsedFallback && model.Regularization > 0,
            "constant projection and numerical regularization are inspectable");
        var scratch = new double[3];
        Require(model.TryDistanceSquared(new[] { 2.0, 3, 123 }, scratch, out double onLine), "score on supported line");
        Require(model.TryDistanceSquared(new[] { 2.0, -3, 123 }, scratch, out double offLine) && offLine > onLine * 1000,
            "rare direction retained in regularized metric");
        Near(2 / (2 - model.Regularization), onLine, "score on the supported direction");
        Near(Math.Log(36 * model.Regularization * (2 - model.Regularization)), model.LogDeterminant,
            "active-space regularized log determinant", 1e-8);
        Require(!model.TryDistanceSquared(new[] { 2.0, 3, double.NaN }, scratch, out _), "complete-case scoring on constant axes");
        Require(!ClimateGaussian.TryCreate(zero, new double[9], out _), "all-constant reference unavailable");
        Require(!ClimateGaussian.TryCreate(zero, new[] { 1.0, .8, .8, .8, 1, -.8, .8, -.8, 1 }, out _, 1),
            "pairwise-valid indefinite matrix rejected prior to shrinkage");
        Require(!ClimateGaussian.TryCreate(zero, new[] { 1.0, .2, 0, .21, 1, 0, 0, 0, 1 }, out _), "asymmetric reference rejected");
        Require(!ClimateGaussian.TryCreate(new[] { 2.0, 1.0 }, new[] { 0.0, 1e-300, 1e-300, 1.0 }, out _),
            "zero variance cannot have nonzero covariance");
        Require(!ClimateGaussian.TryCreate(new[] { 0.0 }, new[] { -1.0 }, out _), "negative variance");
        Require(!ClimateGaussian.TryCreate(new[] { double.NaN }, new[] { 1.0 }, out _) &&
            !ClimateGaussian.TryCreate(new[] { 0.0 }, new[] { double.PositiveInfinity }, out _), "nonfinite reference");
        Require(!ClimateGaussian.TryCreate(null, singular, out _) && !ClimateGaussian.TryCreate(zero, new double[4], out _),
            "invalid shapes");
        foreach (double invalid in new[] { -.1, 1.1, double.NaN, double.PositiveInfinity })
            Require(!ClimateGaussian.TryCreate(zero, singular, out _, invalid), "invalid shrinkage rejected");
        Require(!model.TryDistanceSquared(null, scratch, out _) && !model.TryDistanceSquared(new[] { 1.0 }, scratch, out _)
            && !model.TryDistanceSquared(zero, new double[1], out _), "incomplete score or insufficient scratch");
        Require(!model.TryDistanceSquared(new[] { double.MaxValue, 0.0, 0.0 }, scratch, out _), "unrepresentable squared distance");
    }

    static void LongCollinearStream()
    {
        var moments = new OnlineClimateMoments(6);
        var row = new double[6];
        for (int i = 0; i < 250000; ++i)
        {
            double t = i % 1003 - 501, p = Math.Sin(.07 * i) * 4;
            row[0] = 1e15 + t; row[1] = 3.57 * t; row[2] = 1e-140 * p;
            row[3] = 1e140 * (7.1 * t + 17.3 * p); row[4] = p * .002; row[5] = 30 * t - 14 * p;
            Require(moments.Add(row, .75 + i % 17), "long stream of finite observations");
        }
        Require(ClimateGaussian.TryFit(moments, out var model, 0) && model.UnregularizedRank == 2,
            "roundoff in a long stream preserves its two independent directions");
    }

    static void RepeatedScoring()
    {
        Require(ClimateGaussian.TryCreate(new[] { 0.0, 0 }, new[] { 3.0, 1, 1, 2 }, out var model), "cached model");
        var values = new[] { 1.0, 2 };
        var scratch = new double[2];
        model.TryDistanceSquared(values, scratch, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        double total = 0;
        for (int i = 0; i < 2048; ++i)
        {
            Require(model.TryDistanceSquared(values, scratch, out double squared), "repeated finite score");
            total += squared;
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(total > 0 && bytes == 0, "repeated model scoring allocated " + bytes + " bytes");
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Climate math: " + message);
    }

    static void Near(double expected, double actual, string message, double relative = 1e-10, double absolute = 1e-11)
    {
        if (double.IsNaN(actual) || double.IsInfinity(actual) ||
            Math.Abs(actual - expected) > Math.Max(absolute, Math.Abs(expected) * relative))
            throw new InvalidOperationException("Climate math: " + message + ". Expected " + expected + ", received " + actual);
    }
}
