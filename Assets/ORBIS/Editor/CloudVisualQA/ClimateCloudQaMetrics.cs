using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Orbis.Editor.CloudVisualQA
{
    /// <summary>
    /// Geographic lon/lat bounding box in degrees. Supports date-line crossing (WestLon > EastLon).
    /// </summary>
    public readonly struct GeoBox
    {
        public readonly double WestLon, EastLon, SouthLat, NorthLat;

        public GeoBox(double westLon, double eastLon, double southLat, double northLat)
        {
            WestLon = westLon;
            EastLon = eastLon;
            SouthLat = southLat;
            NorthLat = northLat;
        }

        public bool Contains(double lonDeg, double latDeg)
        {
            if (latDeg < SouthLat || latDeg > NorthLat) return false;
            if (WestLon <= EastLon) return lonDeg >= WestLon && lonDeg <= EastLon;
            return lonDeg >= WestLon || lonDeg <= EastLon;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[lon {0:F1}..{1:F1}, lat {2:F1}..{3:F1}]", WestLon, EastLon, SouthLat, NorthLat);
        }
    }

    /// <summary>
    /// Pure-C# north-first RGBA8 image container matching OrbisRasterCodec row order.
    /// </summary>
    public sealed class RgbaImage
    {
        public readonly int Width;
        public readonly int Height;
        public readonly byte[] Rgba;

        public RgbaImage(int width, int height, byte[] rgba)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (rgba == null || rgba.Length != width * height * 4)
                throw new ArgumentException("RGBA buffer size does not match dimensions.");
            Width = width;
            Height = height;
            Rgba = rgba;
        }

        public RgbaImage Crop(int x0, int y0, int cropW, int cropH)
        {
            if (x0 < 0 || y0 < 0 || cropW <= 0 || cropH <= 0 || x0 + cropW > Width || y0 + cropH > Height)
                throw new ArgumentOutOfRangeException(nameof(cropW),
                    $"Crop ({x0},{y0},{cropW},{cropH}) exceeds source ({Width}x{Height}).");
            var dst = new byte[cropW * cropH * 4];
            int rowBytes = cropW * 4;
            for (int y = 0; y < cropH; y++)
                Buffer.BlockCopy(Rgba, ((y0 + y) * Width + x0) * 4, dst, y * rowBytes, rowBytes);
            return new RgbaImage(cropW, cropH, dst);
        }

        public RgbaImage ResizeBilinear(int dstW, int dstH)
        {
            if (dstW == Width && dstH == Height) return this;
            var dst = new byte[dstW * dstH * 4];
            for (int y = 0; y < dstH; y++)
            {
                double sy = (y + 0.5) * Height / dstH - 0.5;
                int y0 = Math.Max(0, Math.Min(Height - 1, (int)Math.Floor(sy)));
                int y1 = Math.Min(Height - 1, y0 + 1);
                double fy = Math.Max(0.0, Math.Min(1.0, sy - y0));
                for (int x = 0; x < dstW; x++)
                {
                    double sx = (x + 0.5) * Width / dstW - 0.5;
                    int x0 = Math.Max(0, Math.Min(Width - 1, (int)Math.Floor(sx)));
                    int x1 = Math.Min(Width - 1, x0 + 1);
                    double fx = Math.Max(0.0, Math.Min(1.0, sx - x0));
                    int p00 = (y0 * Width + x0) * 4, p10 = (y0 * Width + x1) * 4;
                    int p01 = (y1 * Width + x0) * 4, p11 = (y1 * Width + x1) * 4;
                    int o = (y * dstW + x) * 4;
                    for (int c = 0; c < 4; c++)
                    {
                        double top = Rgba[p00 + c] * (1.0 - fx) + Rgba[p10 + c] * fx;
                        double bot = Rgba[p01 + c] * (1.0 - fx) + Rgba[p11 + c] * fx;
                        dst[o + c] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(top * (1.0 - fy) + bot * fy)));
                    }
                }
            }
            return new RgbaImage(dstW, dstH, dst);
        }
    }

    /// <summary>
    /// South-first equirectangular 4-channel float grid (matching FlatSkyRenderer.Texture,
    /// FlatSkyRenderer.ColumnTexture, and GlebaGeographySupplement.BuildCloudSynopticFields).
    /// </summary>
    public sealed class Float4Grid
    {
        public readonly int Width;
        public readonly int Height;
        public readonly float[] R, G, B, A;

        public Float4Grid(int width, int height)
        {
            Width = width;
            Height = height;
            int n = width * height;
            R = new float[n];
            G = new float[n];
            B = new float[n];
            A = new float[n];
        }

        public static Float4Grid FromRgbaBytes(int width, int height, byte[] rgba)
        {
            var g = new Float4Grid(width, height);
            int n = width * height;
            const float inv = 1f / 255f;
            for (int i = 0; i < n; i++)
            {
                int p = i * 4;
                g.R[i] = rgba[p] * inv;
                g.G[i] = rgba[p + 1] * inv;
                g.B[i] = rgba[p + 2] * inv;
                g.A[i] = rgba[p + 3] * inv;
            }
            return g;
        }

        public float Channel(int ch, int index)
        {
            switch (ch)
            {
                case 0: return R[index];
                case 1: return G[index];
                case 2: return B[index];
                default: return A[index];
            }
        }

        public void SampleBilinear(double u, double v, out float r, out float g, out float b, out float a)
        {
            u = u - Math.Floor(u);
            v = Math.Max(0.0, Math.Min(1.0, v));
            double px = u * Width - 0.5;
            double py = Math.Max(0.0, Math.Min(Height - 1.0, v * Height - 0.5));
            int x0 = (int)Math.Floor(px), y0 = (int)Math.Floor(py);
            float tx = (float)(px - x0), ty = (float)(py - y0);
            int xw = ((x0 % Width) + Width) % Width;
            int xe = ((x0 + 1) % Width + Width) % Width;
            int ys = Math.Max(0, Math.Min(Height - 1, y0));
            int yn = Math.Max(0, Math.Min(Height - 1, y0 + 1));
            int i00 = ys * Width + xw, i10 = ys * Width + xe, i01 = yn * Width + xw, i11 = yn * Width + xe;
            r = Lerp(Lerp(R[i00], R[i10], tx), Lerp(R[i01], R[i11], tx), ty);
            g = Lerp(Lerp(G[i00], G[i10], tx), Lerp(G[i01], G[i11], tx), ty);
            b = Lerp(Lerp(B[i00], B[i10], tx), Lerp(B[i01], B[i11], tx), ty);
            a = Lerp(Lerp(A[i00], A[i10], tx), Lerp(A[i01], A[i11], tx), ty);
        }

        public float SampleChannelBilinear(int ch, double u, double v)
        {
            SampleBilinear(u, v, out float r, out float g, out float b, out float a);
            switch (ch)
            {
                case 0: return r;
                case 1: return g;
                case 2: return b;
                default: return a;
            }
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }

    /// <summary>
    /// Exact C# mirror of OrbisHPVolumeCloud.cginc::OrbisCloudVerticalSpan(type, coverage).
    /// Converts FlatSkyRenderer weather (convection=weather.b, low=weather.r) into physical cloud-top metres.
    /// </summary>
    public static class OrbisCloudTopModel
    {
        static float Smoothstep(float e0, float e1, float x)
        {
            float t = Math.Max(0f, Math.Min(1f, (x - e0) / Math.Max(1e-6f, e1 - e0)));
            return t * t * (3f - 2f * t);
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        public static void VerticalSpanMeters(float convectionType, float lowCoverage, out float baseMeters, out float topMeters)
        {
            float cov = Math.Max(0f, Math.Min(1f, lowCoverage + 0.12f));
            float sc = 1f - Smoothstep(0.05f, 0.18f, convectionType);
            float cu = Smoothstep(0.10f, 0.22f, convectionType) * (1f - Smoothstep(0.48f, 0.70f, convectionType));
            float cb = Smoothstep(0.50f, 0.96f, convectionType);
            baseMeters = Lerp(760f, 560f, sc);
            baseMeters = Lerp(baseMeters, 1020f, cu * 0.88f);
            baseMeters = Lerp(baseMeters, 510f, cb);

            float lowReach = Lerp(0.76f, 1.0f, Smoothstep(0.14f, 0.82f, cov));
            topMeters = Lerp(2200f, 3150f, Smoothstep(0.06f, 0.34f, convectionType));
            topMeters = Lerp(topMeters, 5200f, Smoothstep(0.34f, 0.60f, convectionType));
            topMeters = Lerp(topMeters, 12350f, cb);
            topMeters = baseMeters + (topMeters - baseMeters) * Lerp(lowReach, 1.0f, cb * 0.55f);
        }
    }

    public sealed class SkyColumnSummary
    {
        public double LowCover;
        public double HighCover;
        public double Convection;
        public double Storm;
        public double EstimatedTopMeters;
        public double CloudWeightedTopMeters;
        public double DeepConvectiveFraction;
        public double MarineScTierFraction;
        public double ColumnLowOpacity;
        public double ColumnHighOpacity;
        public double ColumnSnow;
        public double ColumnRain;
    }

    public sealed class FieldDeltaStats
    {
        public double MeanA;
        public double MeanB;
        public double SignedDelta;
        public double MeanAbsDelta;
        public double DirectionalCoherence;
        public double ControlMeanAbsDelta;
        public double ControlLeakageRatio;
    }

    public sealed class ScreenRoiDeltaStats
    {
        public int TargetPixels;
        public int ControlPixels;
        public double MeanLumaA;
        public double MeanLumaB;
        public double SignedLumaDeltaDn;
        public double MeanAbsLumaDeltaDn;
        public double DirectionalCoherence;
        public double BrightFractionA;
        public double BrightFractionB;
        public double CloudScoreA;
        public double CloudScoreB;
        public double SignedCloudScoreDelta;
        public double ControlMeanAbsLumaDeltaDn;
        public double ControlLeakageRatio;
        public double LatCentroidDegA;
        public double LatCentroidDegB;
        public double DipoleLumaDeltaDn;
    }

    public sealed class CycloneEvolutionMetrics
    {
        public double Track0LonDeg, Track0LatDeg, Track0WindMs, Track0Strength, Track0PhaseRad;
        public double Track1LonDeg, Track1LatDeg, Track1WindMs, Track1Strength, Track1PhaseRad;
        public double TrackDisplacementDeg;
        public double TrackPhaseDeltaRad;
        public double ObservedCloud0LonDeg, ObservedCloud0LatDeg;
        public double ObservedCloud1LonDeg, ObservedCloud1LatDeg;
        public double ObservedPositionError0Deg, ObservedPositionError1Deg;
        public double ShiftAlignmentCos;
        public double EyewallCoverT1, EyeCoverT1, EyewallEyeContrastT1;
        public double SpiralInwardErrorT1, SpiralOutwardErrorT1, SpiralChiralityRatio;
        public double RotationSameError, RotationSignedError, RotationOppositeError;
        public double ScreenShiftAlignmentCos;
        public double StormCorridorMeanAbs, FarFieldMeanAbs, FarFieldLeakageRatio;
    }

    public enum PairExpectationKind
    {
        ZeroDriftControl,
        MonotonicDecrease,   // B < A (e.g. Moist/Warm A -> Dry/Stable B, or Cold SST Sc A -> Warm SST B)
        MonotonicIncrease,   // B > A
        OrographicDipole,    // West flank A>B while East flank B>A
        NorthwardMigration,  // Season Jan (A) -> Jul (B): convective centroid shifts north
        PolewardExpansion,   // Rotation 1.0x (A) -> 0.45x (B): tropical convective belt expands poleward
        CycloneEvolution     // Natural cyclone t0 (A) -> t1 (B): tracks position & signed rotation
    }

    public sealed class AntiNoiseBindingVerdict
    {
        public bool Passed;
        public string FailureCode = "NONE";
        public readonly List<string> Checks = new List<string>();
        public readonly List<string> Failures = new List<string>();

        public void AddCheck(bool ok, string code, string detail)
        {
            Checks.Add((ok ? "PASS " : "FAIL ") + detail);
            if (!ok)
            {
                if (FailureCode == "NONE") FailureCode = code;
                Failures.Add($"[{code}] {detail}");
            }
        }
    }

    public sealed class FullDiskVisualMetrics
    {
        public string Label;
        public double ClearSkyFraction;
        public double ThinVeilFraction;
        public double ThickCloudFraction;
        public double TotalCloudFraction;
        public double LargeBandShare;
        public double Top3BandShare;
        public double Top3MeanSpan;
        public double MaxBandSpan;
        public int SpeckleCount;
        public double TransitionEdgeSharpness;
        public double LargestClearBasinFraction;
        public double StormCoreP95;
        public double StormCoreMean;
        public double StormProminence;
        public double StormEyeContrast;
        public double DiskMeanCloudScore;
    }

    public sealed class BaselineComparisonVerdict
    {
        public string ViewId;
        public bool Passed;
        public FullDiskVisualMetrics Baseline79;
        public FullDiskVisualMetrics Candidate;
        public readonly List<string> Checks = new List<string>();
        public readonly List<string> Failures = new List<string>();
    }

    /// <summary>
    /// Core pure-C# metric evaluators, anti-decorative-noise binding checks, cyclone spiral/rotation
    /// probes, 79e6fa2 full-disk visual baseline analyzer, and PNG comparison sheet composers.
    /// </summary>
    public static class ClimateCloudQaMetrics
    {
        // Exact crop boxes of NasaUpgrade3Col 3-column comparison PNGs (2344x922 full-disk, 2464x662 local).
        public const int NasaFullDiskLeftX = 16;
        public const int NasaFullDiskMidX = 792;
        public const int NasaFullDiskTopY = 130;
        public const int NasaFullDiskSize = 760;

        public const int NasaLocalLeftX = 16;
        public const int NasaLocalTopY = 130;
        public const int NasaLocalWidth = 800;
        public const int NasaLocalHeight = 500;

        public static SkyColumnSummary SummarizeSkyColumn(Float4Grid weather, Float4Grid column, GeoBox roi)
        {
            var s = new SkyColumnSummary();
            double wtSum = 0, cloudWtSum = 0;
            int w = weather.Width, h = weather.Height;
            for (int y = 0; y < h; y++)
            {
                double v = (y + 0.5) / h;
                double lat = (v - 0.5) * 180.0;
                if (lat < roi.SouthLat || lat > roi.NorthLat) continue;
                double wt = Math.Cos(lat * Math.PI / 180.0);
                for (int x = 0; x < w; x++)
                {
                    double u = (x + 0.5) / w;
                    double lon = (u - 0.5) * 360.0;
                    if (!roi.Contains(lon, lat)) continue;
                    int i = y * w + x;
                    float low = weather.R[i], high = weather.G[i], conv = weather.B[i], storm = weather.A[i];
                    OrbisCloudTopModel.VerticalSpanMeters(conv, low, out _, out float topM);
                    wtSum += wt;
                    s.LowCover += wt * low;
                    s.HighCover += wt * high;
                    s.Convection += wt * conv;
                    s.Storm += wt * storm;
                    s.EstimatedTopMeters += wt * topM;
                    double cw = wt * Math.Max(0.0, low - 0.05);
                    cloudWtSum += cw;
                    s.CloudWeightedTopMeters += cw * topM;
                    if (conv >= 0.50f && low >= 0.22f) s.DeepConvectiveFraction += wt;
                    if (conv <= 0.18f && low >= 0.16f) s.MarineScTierFraction += wt;
                    if (column != null)
                    {
                        column.SampleBilinear(u, v, out float cr, out float cg, out float cb, out float ca);
                        s.ColumnLowOpacity += wt * cr;
                        s.ColumnHighOpacity += wt * cg;
                        s.ColumnSnow += wt * cb;
                        s.ColumnRain += wt * ca;
                    }
                }
            }
            if (wtSum > 0)
            {
                s.LowCover /= wtSum;
                s.HighCover /= wtSum;
                s.Convection /= wtSum;
                s.Storm /= wtSum;
                s.EstimatedTopMeters /= wtSum;
                s.DeepConvectiveFraction /= wtSum;
                s.MarineScTierFraction /= wtSum;
                s.ColumnLowOpacity /= wtSum;
                s.ColumnHighOpacity /= wtSum;
                s.ColumnSnow /= wtSum;
                s.ColumnRain /= wtSum;
            }
            s.CloudWeightedTopMeters = cloudWtSum > 1e-6 ? s.CloudWeightedTopMeters / cloudWtSum : s.EstimatedTopMeters;
            return s;
        }

        public static FieldDeltaStats ComputeFieldDelta(Float4Grid a, Float4Grid b, int channel, GeoBox targetRoi, GeoBox? controlRoi = null)
        {
            var stats = new FieldDeltaStats();
            int w = a.Width, h = a.Height;
            double wt = 0, sumA = 0, sumB = 0, absSum = 0;
            double cWt = 0, cAbs = 0;
            for (int y = 0; y < h; y++)
            {
                double v = (y + 0.5) / h;
                double lat = (v - 0.5) * 180.0;
                double cosLat = Math.Cos(lat * Math.PI / 180.0);
                for (int x = 0; x < w; x++)
                {
                    double u = (x + 0.5) / w;
                    double lon = (u - 0.5) * 360.0;
                    int i = y * w + x;
                    float va = a.Channel(channel, i);
                    float vb = (b.Width == w && b.Height == h) ? b.Channel(channel, i) : b.SampleChannelBilinear(channel, u, v);
                    if (targetRoi.Contains(lon, lat))
                    {
                        wt += cosLat;
                        sumA += cosLat * va;
                        sumB += cosLat * vb;
                        absSum += cosLat * Math.Abs(vb - va);
                    }
                    if (controlRoi.HasValue && controlRoi.Value.Contains(lon, lat))
                    {
                        cWt += cosLat;
                        cAbs += cosLat * Math.Abs(vb - va);
                    }
                }
            }
            if (wt > 0)
            {
                stats.MeanA = sumA / wt;
                stats.MeanB = sumB / wt;
                stats.SignedDelta = stats.MeanB - stats.MeanA;
                stats.MeanAbsDelta = absSum / wt;
                stats.DirectionalCoherence = Math.Abs(stats.SignedDelta) / (stats.MeanAbsDelta + 1e-6);
            }
            if (cWt > 0)
            {
                stats.ControlMeanAbsDelta = cAbs / cWt;
                stats.ControlLeakageRatio = stats.ControlMeanAbsDelta / (stats.MeanAbsDelta + 1e-6);
            }
            return stats;
        }

        /// <summary>
        /// Area-weighted Pearson correlation between (forcingB - forcingA) and (outputB - outputA)
        /// inside <paramref name="roi"/>. Pure decorative noise yields r ~ 0.
        /// </summary>
        public static double ComputeDeltaPearsonR(
            Float4Grid forcingA, Float4Grid forcingB, int forcingChannel,
            Float4Grid outputA, Float4Grid outputB, int outputChannel, GeoBox roi)
        {
            int w = forcingA.Width, h = forcingA.Height;
            double wtSum = 0, sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
            for (int y = 0; y < h; y++)
            {
                double v = (y + 0.5) / h;
                double lat = (v - 0.5) * 180.0;
                if (lat < roi.SouthLat || lat > roi.NorthLat) continue;
                double wt = Math.Cos(lat * Math.PI / 180.0);
                for (int x = 0; x < w; x++)
                {
                    double u = (x + 0.5) / w;
                    double lon = (u - 0.5) * 360.0;
                    if (!roi.Contains(lon, lat)) continue;
                    int i = y * w + x;
                    double dx = forcingB.Channel(forcingChannel, i) - forcingA.Channel(forcingChannel, i);
                    double dy = outputB.SampleChannelBilinear(outputChannel, u, v) - outputA.SampleChannelBilinear(outputChannel, u, v);
                    wtSum += wt;
                    sx += wt * dx;
                    sy += wt * dy;
                    sxx += wt * dx * dx;
                    syy += wt * dy * dy;
                    sxy += wt * dx * dy;
                }
            }
            if (wtSum <= 1e-9) return 0.0;
            double mx = sx / wtSum, my = sy / wtSum;
            double vx = Math.Max(0.0, sxx / wtSum - mx * mx);
            double vy = Math.Max(0.0, syy / wtSum - my * my);
            double denom = Math.Sqrt(vx * vy);
            return denom > 1e-9 ? (sxy / wtSum - mx * my) / denom : 0.0;
        }

        public static double ComputeLatitudeCentroid(Float4Grid grid, Func<float, float, float, float, float> weightFn, GeoBox roi, bool absoluteLat = false)
        {
            double num = 0, den = 0;
            int w = grid.Width, h = grid.Height;
            for (int y = 0; y < h; y++)
            {
                double v = (y + 0.5) / h;
                double lat = (v - 0.5) * 180.0;
                if (lat < roi.SouthLat || lat > roi.NorthLat) continue;
                double area = Math.Cos(lat * Math.PI / 180.0);
                double coord = absoluteLat ? Math.Abs(lat) : lat;
                for (int x = 0; x < w; x++)
                {
                    double u = (x + 0.5) / w;
                    double lon = (u - 0.5) * 360.0;
                    if (!roi.Contains(lon, lat)) continue;
                    int i = y * w + x;
                    double val = Math.Max(0.0, weightFn(grid.R[i], grid.G[i], grid.B[i], grid.A[i]));
                    num += area * val * coord;
                    den += area * val;
                }
            }
            return den > 1e-9 ? num / den : 0.0;
        }

        /// <summary>
        /// Evaluates anti-decorative-noise binding across Layer-1 (physical forcing),
        /// Layer-2 (FlatSkyRenderer weather & column caches), and Layer-3 (rendered game-camera pixels).
        /// </summary>
        public static AntiNoiseBindingVerdict EvaluateAntiNoiseBinding(
            PairExpectationKind kind,
            FieldDeltaStats forcingDelta,
            FieldDeltaStats skyLowDelta,
            FieldDeltaStats skyConvDelta,
            FieldDeltaStats columnOpacityDelta,
            double forcingToColumnR,
            ScreenRoiDeltaStats screenStats,
            double latShiftForcingDeg = 0,
            double latShiftColumnDeg = 0,
            double latShiftScreenDeg = 0,
            double dipoleForcing = 0,
            double dipoleColumn = 0,
            double dipoleScreenDn = 0,
            CycloneEvolutionMetrics cyclone = null,
            bool requireStrictScreenControl = true)
        {
            var verdict = new AntiNoiseBindingVerdict();

            if (kind == PairExpectationKind.ZeroDriftControl)
            {
                verdict.AddCheck(skyLowDelta.MeanAbsDelta < 0.002, "CLOCK_OR_SEED_DRIFT",
                    $"locked control FlatSkyRenderer.Texture meanAbs={skyLowDelta.MeanAbsDelta:F5} (<0.002)");
                verdict.AddCheck(columnOpacityDelta.MeanAbsDelta < 0.002, "COLUMN_DRIFT",
                    $"locked control FlatSkyRenderer.ColumnTexture meanAbs={columnOpacityDelta.MeanAbsDelta:F5} (<0.002)");
                if (screenStats != null && screenStats.TargetPixels > 0)
                    verdict.AddCheck(screenStats.MeanAbsLumaDeltaDn < 0.25, "RENDER_DRIFT",
                        $"locked control screenshot meanAbs={screenStats.MeanAbsLumaDeltaDn:F3} DN (<0.25)");
                verdict.Passed = verdict.Failures.Count == 0;
                return verdict;
            }

            if (kind == PairExpectationKind.MonotonicDecrease || kind == PairExpectationKind.MonotonicIncrease)
            {
                double expectedSign = kind == PairExpectationKind.MonotonicIncrease ? 1.0 : -1.0;
                verdict.AddCheck(columnOpacityDelta.SignedDelta * expectedSign > 0.020, "WRONG_SIGN_OR_WEAK_COLUMN",
                    $"ColumnTexture signed delta={columnOpacityDelta.SignedDelta:+0.0000;-0.0000} (expected sign {(expectedSign > 0 ? "+" : "-")}, |d|>0.020)");
                verdict.AddCheck(columnOpacityDelta.DirectionalCoherence >= 0.45, "DECORATIVE_ZERO_MEAN_NOISE",
                    $"ColumnTexture directional coherence={columnOpacityDelta.DirectionalCoherence:F3} (>=0.45; random noise ~0)");
                verdict.AddCheck(columnOpacityDelta.ControlLeakageRatio <= 0.20, "GLOBAL_NOISE_LEAKAGE",
                    $"ColumnTexture control ROI leakage={columnOpacityDelta.ControlLeakageRatio:F3} (<=0.20; ctrlAbs={columnOpacityDelta.ControlMeanAbsDelta:F4})");
                if (Math.Abs(forcingDelta.MeanAbsDelta) > 1e-4)
                    verdict.AddCheck(forcingToColumnR >= 0.25, "UNCORRELATED_NOISE_CHANGE",
                        $"Pearson r(dForcing, dColumn)={forcingToColumnR:F3} (>=0.25)");

                if (screenStats != null && screenStats.TargetPixels > 0)
                {
                    bool renderNoiseOnly = screenStats.MeanAbsLumaDeltaDn >= 2.0 && Math.Abs(columnOpacityDelta.SignedDelta) < 0.004;
                    verdict.AddCheck(!renderNoiseOnly, "DECORATIVE_NOISE_RENDER_ONLY",
                        $"cross-modal non-decorative guard: imgAbs={screenStats.MeanAbsLumaDeltaDn:F2} DN vs colDelta={columnOpacityDelta.SignedDelta:+0.000;-0.000}");
                    verdict.AddCheck(screenStats.SignedLumaDeltaDn * expectedSign > 1.2, "UNBOUND_SKY_OR_WRONG_IMAGE_SIGN",
                        $"rendered ROI signed luma delta={screenStats.SignedLumaDeltaDn:+0.00;-0.00} DN (expected sign {(expectedSign > 0 ? "+" : "-")}, |d|>1.2)");
                    verdict.AddCheck(screenStats.DirectionalCoherence >= 0.30, "RENDER_ZERO_MEAN_NOISE",
                        $"rendered ROI directional coherence={screenStats.DirectionalCoherence:F3} (>=0.30)");
                    if (requireStrictScreenControl && screenStats.ControlPixels > 0)
                        verdict.AddCheck(screenStats.ControlLeakageRatio <= 0.25, "RENDER_CONTROL_LEAKAGE",
                            $"rendered control ROI leakage={screenStats.ControlLeakageRatio:F3} (<=0.25; ctrlAbs={screenStats.ControlMeanAbsLumaDeltaDn:F2} DN)");
                }
                verdict.Passed = verdict.Failures.Count == 0;
                return verdict;
            }

            if (kind == PairExpectationKind.OrographicDipole)
            {
                verdict.AddCheck(dipoleForcing > 0.05, "MISSING_FORCING_DIPOLE",
                    $"forcing windward-leeward dipole={dipoleForcing:+0.000;-0.000} (>0.05)");
                verdict.AddCheck(dipoleColumn > 0.015, "MISSING_COLUMN_DIPOLE",
                    $"ColumnTexture windward-leeward dipole={dipoleColumn:+0.000;-0.000} (>0.015)");
                verdict.AddCheck(columnOpacityDelta.ControlLeakageRatio <= 0.25, "GLOBAL_NOISE_LEAKAGE",
                    $"ColumnTexture control ROI leakage={columnOpacityDelta.ControlLeakageRatio:F3} (<=0.25)");
                if (screenStats != null && screenStats.TargetPixels > 0)
                {
                    verdict.AddCheck(dipoleScreenDn > 0.6, "MISSING_RENDER_DIPOLE",
                        $"rendered screenshot windward-leeward dipole={dipoleScreenDn:+0.00;-0.00} DN (>0.6)");
                    if (requireStrictScreenControl && screenStats.ControlPixels > 0)
                        verdict.AddCheck(screenStats.ControlLeakageRatio <= 0.30, "RENDER_CONTROL_LEAKAGE",
                            $"rendered control ROI leakage={screenStats.ControlLeakageRatio:F3} (<=0.30)");
                }
                verdict.Passed = verdict.Failures.Count == 0;
                return verdict;
            }

            if (kind == PairExpectationKind.NorthwardMigration)
            {
                verdict.AddCheck(latShiftForcingDeg > 6.0, "MISSING_SEASONAL_FORCING_SHIFT",
                    $"ITCZ forcing centroid shift Jan->Jul={latShiftForcingDeg:+0.00;-0.00} deg (>+6.0)");
                verdict.AddCheck(latShiftColumnDeg > 3.0, "MISSING_COLUMN_SEASONAL_SHIFT",
                    $"ColumnTexture cloud centroid shift Jan->Jul={latShiftColumnDeg:+0.00;-0.00} deg (>+3.0)");
                verdict.AddCheck(forcingToColumnR >= 0.30, "UNCORRELATED_SEASONAL_NOISE",
                    $"Pearson r(dSeasonForcing, dColumn)={forcingToColumnR:F3} (>=0.30)");
                if (screenStats != null && screenStats.TargetPixels > 0)
                    verdict.AddCheck(latShiftScreenDeg > 1.5, "MISSING_RENDER_SEASONAL_SHIFT",
                        $"rendered tropical cloud centroid shift Jan->Jul={latShiftScreenDeg:+0.00;-0.00} deg (>+1.5)");
                verdict.Passed = verdict.Failures.Count == 0;
                return verdict;
            }

            if (kind == PairExpectationKind.PolewardExpansion)
            {
                verdict.AddCheck(latShiftForcingDeg > 1.0, "MISSING_HADLEY_FORCING_EXPANSION",
                    $"tropical convective |lat| forcing shift 1x->0.45x={latShiftForcingDeg:+0.00;-0.00} deg (>+1.0)");
                verdict.AddCheck(latShiftColumnDeg > 0.4 || columnOpacityDelta.MeanAbsDelta > 0.02, "MISSING_COLUMN_ROTATION_RESPONSE",
                    $"ColumnTexture convective |lat| shift={latShiftColumnDeg:+0.00;-0.00} deg, meanAbs={columnOpacityDelta.MeanAbsDelta:F4}");
                verdict.AddCheck(forcingToColumnR >= 0.15 || latShiftColumnDeg > 0.5, "UNCORRELATED_ROTATION_NOISE",
                    $"Pearson r(dRotationForcing, dColumn)={forcingToColumnR:F3}, |lat| shift={latShiftColumnDeg:+0.00;-0.00} deg");
                if (screenStats != null && screenStats.TargetPixels > 0)
                    verdict.AddCheck(screenStats.MeanAbsLumaDeltaDn > 1.0 && Math.Abs(columnOpacityDelta.MeanAbsDelta) > 0.01,
                        "UNBOUND_ROTATION_RENDER",
                        $"rendered meanAbs={screenStats.MeanAbsLumaDeltaDn:F2} DN bound to column meanAbs={columnOpacityDelta.MeanAbsDelta:F4}");
                verdict.Passed = verdict.Failures.Count == 0;
                return verdict;
            }

            if (kind == PairExpectationKind.CycloneEvolution && cyclone != null)
            {
                verdict.AddCheck(cyclone.TrackDisplacementDeg >= 1.5, "INSUFFICIENT_TRACK_MOTION",
                    $"natural cyclone track motion={cyclone.TrackDisplacementDeg:F2} deg (>=1.5 deg)");
                verdict.AddCheck(cyclone.ObservedPositionError1Deg <= 3.5 && cyclone.ShiftAlignmentCos >= 0.65,
                    "CYCLONE_POSITION_MISMATCH",
                    $"cloud peak follows track: posErr1={cyclone.ObservedPositionError1Deg:F2} deg (<=3.5), shiftCos={cyclone.ShiftAlignmentCos:F3} (>=0.65)");
                verdict.AddCheck(cyclone.SpiralChiralityRatio < 0.96, "CYCLONE_CHIRALITY_MISMATCH",
                    $"hemispheric spiral winding inward/outward={cyclone.SpiralChiralityRatio:F3} (<0.96)");
                double expectedPhaseSign = cyclone.Track1LatDeg >= 0 ? 1.0 : -1.0;
                verdict.AddCheck(cyclone.TrackPhaseDeltaRad * expectedPhaseSign > 0.20, "CYCLONE_PHASE_SIGN_MISMATCH",
                    $"simulated signed phase delta={cyclone.TrackPhaseDeltaRad:+0.00;-0.00} rad (expected {(expectedPhaseSign > 0 ? "CCW +" : "CW -")})");
                if (cyclone.RotationSameError > 1e-4)
                    verdict.AddCheck(cyclone.RotationSignedError < cyclone.RotationSameError * 0.92 &&
                                     cyclone.RotationSignedError < cyclone.RotationOppositeError,
                        "CYCLONE_ROTATION_UNCOUPLED",
                        $"rainbands follow signed phase: signedErr={cyclone.RotationSignedError:F3} < sameErr={cyclone.RotationSameError:F3} & oppErr={cyclone.RotationOppositeError:F3}");
                verdict.AddCheck(cyclone.FarFieldLeakageRatio <= 0.35, "CYCLONE_GLOBAL_NOISE_LEAK",
                    $"cyclone far-field leakage={cyclone.FarFieldLeakageRatio:F3} (<=0.35; farAbs={cyclone.FarFieldMeanAbs:F4})");
                if (screenStats != null && screenStats.TargetPixels > 0)
                    verdict.AddCheck(cyclone.ScreenShiftAlignmentCos >= 0.55 && screenStats.MeanAbsLumaDeltaDn > 1.0,
                        "CYCLONE_SCREEN_SHIFT_MISMATCH",
                        $"rendered storm shift alignment cos={cyclone.ScreenShiftAlignmentCos:F3} (>=0.55), corridorAbs={screenStats.MeanAbsLumaDeltaDn:F2} DN");
                verdict.Passed = verdict.Failures.Count == 0;
                return verdict;
            }

            verdict.AddCheck(false, "UNKNOWN_EXPECTATION", "Unsupported pair expectation kind: " + kind);
            verdict.Passed = false;
            return verdict;
        }

        /// <summary>
        /// Evaluates cyclone cloud-peak tracking, eyewall-vs-eye contrast, hemispheric logarithmic
        /// spiral chirality (matching ClimateCloudRegression.ValidateCycloneClouds), and localized
        /// far-field stability directly on FlatSkyRenderer.Texture / ColumnTexture.
        /// </summary>
        public static CycloneEvolutionMetrics MeasureCycloneCloudEvolution(
            Float4Grid weatherT0, Float4Grid weatherT1,
            double u0, double v0, double wind0, double str0, double phase0,
            double u1, double v1, double wind1, double str1, double phase1,
            Float4Grid weatherT1PhaseShifted = null, double purePhaseShiftRad = 0.0)
        {
            var m = new CycloneEvolutionMetrics
            {
                Track0LonDeg = (u0 - 0.5) * 360.0,
                Track0LatDeg = (v0 - 0.5) * 180.0,
                Track0WindMs = wind0,
                Track0Strength = str0,
                Track0PhaseRad = phase0,
                Track1LonDeg = (u1 - 0.5) * 360.0,
                Track1LatDeg = (v1 - 0.5) * 180.0,
                Track1WindMs = wind1,
                Track1Strength = str1,
                Track1PhaseRad = phase1,
                TrackPhaseDeltaRad = phase1 - phase0
            };

            double dLon = WrapDeltaDeg(m.Track1LonDeg - m.Track0LonDeg);
            double dLat = m.Track1LatDeg - m.Track0LatDeg;
            double midCos = Math.Cos(0.5 * (m.Track0LatDeg + m.Track1LatDeg) * Math.PI / 180.0);
            m.TrackDisplacementDeg = Math.Sqrt((dLon * midCos) * (dLon * midCos) + dLat * dLat);

            FindStormCloudCentroid(weatherT0, u0, v0, 8.0, out m.ObservedCloud0LonDeg, out m.ObservedCloud0LatDeg);
            FindStormCloudCentroid(weatherT1, u1, v1, 8.0, out m.ObservedCloud1LonDeg, out m.ObservedCloud1LatDeg);

            m.ObservedPositionError0Deg = SphericalDistanceDeg(m.Track0LonDeg, m.Track0LatDeg, m.ObservedCloud0LonDeg, m.ObservedCloud0LatDeg);
            m.ObservedPositionError1Deg = SphericalDistanceDeg(m.Track1LonDeg, m.Track1LatDeg, m.ObservedCloud1LonDeg, m.ObservedCloud1LatDeg);

            double obsDx = WrapDeltaDeg(m.ObservedCloud1LonDeg - m.ObservedCloud0LonDeg) * midCos;
            double obsDy = m.ObservedCloud1LatDeg - m.ObservedCloud0LatDeg;
            double trkDx = dLon * midCos, trkDy = dLat;
            double obsNorm = Math.Sqrt(obsDx * obsDx + obsDy * obsDy);
            double trkNorm = Math.Sqrt(trkDx * trkDx + trkDy * trkDy);
            m.ShiftAlignmentCos = (obsNorm > 1e-4 && trkNorm > 1e-4)
                ? (obsDx * trkDx + obsDy * trkDy) / (obsNorm * trkNorm)
                : 0.0;

            // Hemispheric logarithmic spiral chirality probe around T1 tracked centre.
            int spin = m.Track1LatDeg >= 0 ? 1 : -1;
            double lat1Rad = m.Track1LatDeg * Math.PI / 180.0;
            double lon1Rad = m.Track1LonDeg * Math.PI / 180.0;
            // Tangent basis on unit sphere
            double ax = Math.Cos(lat1Rad) * Math.Sin(lon1Rad);
            double ay = Math.Sin(lat1Rad);
            double az = Math.Cos(lat1Rad) * Math.Cos(lon1Rad);
            double ex = Math.Cos(lon1Rad), ey = 0.0, ez = -Math.Sin(lon1Rad);
            double nx = ay * ez - az * ey, ny = az * ex - ax * ez, nz = ax * ey - ay * ex;
            double nLen = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (nLen > 1e-9) { nx /= nLen; ny /= nLen; nz /= nLen; }

            double SampleSpherical(Float4Grid grid, double radiusRad, double bearing)
            {
                double cb = Math.Cos(bearing), sb = Math.Sin(bearing);
                double cr = Math.Cos(radiusRad), sr = Math.Sin(radiusRad);
                double dx = ax * cr + (ex * cb + nx * sb) * sr;
                double dy = ay * cr + (ey * cb + ny * sb) * sr;
                double dz = az * cr + (ez * cb + nz * sb) * sr;
                double uu = 0.5 + Math.Atan2(dx, dz) / (2.0 * Math.PI);
                double vv = 0.5 + Math.Asin(Math.Max(-1.0, Math.Min(1.0, dy))) / Math.PI;
                return grid.SampleChannelBilinear(0, uu, vv);
            }

            const int samples = 96;
            m.EyeCoverT1 = SampleSpherical(weatherT1, 0.0, 0.0);
            double wallSum = 0, inward = 0, outward = 0;
            var outerBand = new double[samples];
            for (int i = 0; i < samples; i++)
            {
                double a = i * 2.0 * Math.PI / samples;
                wallSum += SampleSpherical(weatherT1, 0.027, a);
                outerBand[i] = SampleSpherical(weatherT1, 0.097, a);
                for (int j = 0; j < 3; j++)
                {
                    double r = 0.067 + j * 0.024;
                    double inner = SampleSpherical(weatherT1, r, a);
                    inward += Math.Abs(inner - SampleSpherical(weatherT1, r + 0.024, a - spin * 0.44));
                    outward += Math.Abs(inner - SampleSpherical(weatherT1, r + 0.024, a + spin * 0.44));
                }
            }
            m.EyewallCoverT1 = wallSum / samples;
            m.EyewallEyeContrastT1 = m.EyewallCoverT1 - m.EyeCoverT1;
            m.SpiralInwardErrorT1 = inward;
            m.SpiralOutwardErrorT1 = outward;
            m.SpiralChiralityRatio = outward > 1e-6 ? inward / outward : 1.0;

            if (weatherT1PhaseShifted != null && Math.Abs(purePhaseShiftRad) > 0.05)
            {
                double same = 0, signedErr = 0, oppErr = 0;
                for (int i = 0; i < samples; i++)
                {
                    double a = i * 2.0 * Math.PI / samples;
                    same += Math.Abs(outerBand[i] - SampleSpherical(weatherT1PhaseShifted, 0.097, a));
                    signedErr += Math.Abs(outerBand[i] - SampleSpherical(weatherT1PhaseShifted, 0.097, a + purePhaseShiftRad));
                    oppErr += Math.Abs(outerBand[i] - SampleSpherical(weatherT1PhaseShifted, 0.097, a - purePhaseShiftRad));
                }
                m.RotationSameError = same;
                m.RotationSignedError = signedErr;
                m.RotationOppositeError = oppErr;
            }

            // Storm corridor vs far-field localization check
            double nearSum = 0, nearWt = 0, farSum = 0, farWt = 0;
            int w = weatherT1.Width, h = weatherT1.Height;
            for (int y = 0; y < h; y++)
            {
                double lat = ((y + 0.5) / h - 0.5) * 180.0;
                double wt = Math.Cos(lat * Math.PI / 180.0);
                for (int x = 0; x < w; x++)
                {
                    double lon = ((x + 0.5) / w - 0.5) * 360.0;
                    double d0 = SphericalDistanceDeg(lon, lat, m.Track0LonDeg, m.Track0LatDeg);
                    double d1 = SphericalDistanceDeg(lon, lat, m.Track1LonDeg, m.Track1LatDeg);
                    double dMin = Math.Min(d0, d1);
                    int idx = y * w + x;
                    double diff = Math.Abs(weatherT1.R[idx] - weatherT0.R[idx]);
                    if (dMin <= 14.0) { nearSum += wt * diff; nearWt += wt; }
                    else if (dMin >= 42.0 && Math.Abs(lat) <= 60.0) { farSum += wt * diff; farWt += wt; }
                }
            }
            m.StormCorridorMeanAbs = nearWt > 0 ? nearSum / nearWt : 0;
            m.FarFieldMeanAbs = farWt > 0 ? farSum / farWt : 0;
            m.FarFieldLeakageRatio = m.FarFieldMeanAbs / (m.StormCorridorMeanAbs + 1e-6);
            return m;
        }

        static void FindStormCloudCentroid(Float4Grid weather, double uCenter, double vCenter, double searchRadiusDeg,
            out double lonOut, out double latOut)
        {
            double cLon = (uCenter - 0.5) * 360.0;
            double cLat = (vCenter - 0.5) * 180.0;
            double sumX = 0, sumY = 0, sumW = 0;
            int w = weather.Width, h = weather.Height;
            for (int y = 0; y < h; y++)
            {
                double lat = ((y + 0.5) / h - 0.5) * 180.0;
                if (Math.Abs(lat - cLat) > searchRadiusDeg) continue;
                for (int x = 0; x < w; x++)
                {
                    double lon = ((x + 0.5) / w - 0.5) * 360.0;
                    double dLon = WrapDeltaDeg(lon - cLon);
                    double d = SphericalDistanceDeg(lon, lat, cLon, cLat);
                    if (d > searchRadiusDeg) continue;
                    int i = y * w + x;
                    // Emphasize deep storm core (storm channel + convective low cloud)
                    double weight = Math.Pow(Math.Max(0.0, weather.A[i] * 0.70 + weather.R[i] * weather.B[i] * 0.30), 2.0)
                                    * Math.Exp(-(d * d) / (searchRadiusDeg * searchRadiusDeg * 0.65));
                    sumX += weight * dLon;
                    sumY += weight * (lat - cLat);
                    sumW += weight;
                }
            }
            lonOut = sumW > 1e-9 ? WrapDeltaDeg(cLon + sumX / sumW) : cLon;
            latOut = sumW > 1e-9 ? cLat + sumY / sumW : cLat;
        }

        public static double WrapDeltaDeg(double deg)
        {
            while (deg > 180.0) deg -= 360.0;
            while (deg < -180.0) deg += 360.0;
            return deg;
        }

        public static double SphericalDistanceDeg(double lon1, double lat1, double lon2, double lat2)
        {
            double r1 = lat1 * Math.PI / 180.0, r2 = lat2 * Math.PI / 180.0;
            double dl = (lon2 - lon1) * Math.PI / 180.0;
            double c = Math.Sin(r1) * Math.Sin(r2) + Math.Cos(r1) * Math.Cos(r2) * Math.Cos(dl);
            return Math.Acos(Math.Max(-1.0, Math.Min(1.0, c))) * 180.0 / Math.PI;
        }

        /// <summary>
        /// Desert-safe RGB cloud whiteness score in [0, 1] for full-disk Unity globe captures
        /// (works identically on 79e6fa2 NasaUpgrade3Col left-column PNGs and live candidate renders).
        /// </summary>
        public static float PixelCloudScore(byte rByte, byte gByte, byte bByte)
        {
            float r = rByte * (1f / 255f), g = gByte * (1f / 255f), b = bByte * (1f / 255f);
            float maxC = Math.Max(r, Math.Max(g, b));
            float minC = Math.Min(r, Math.Min(g, b));
            float sat = maxC > 1e-4f ? (maxC - minC) / maxC : 0f;
            // Warm sand/desert through Rayleigh haze has R >= B - 0.012 while sunlit cloud has cool-neutral B >= R + 0.015 (or bright white minC > 0.80).
            float warmExcess = Math.Max(0f, (r - b) + 0.010f);
            float brightWhiteOverride = Math.Max(0f, Math.Min(1f, (minC - 0.78f) / 0.07f));
            float notDesert = Math.Max(brightWhiteOverride, Math.Max(0f, Math.Min(1f, 1f - warmExcess / 0.045f)));
            float brightness = Math.Max(0f, Math.Min(1f, (minC - 0.38f) / 0.44f));
            float neutral = Math.Max(0f, Math.Min(1f, 1f - Math.Max(0f, sat - 0.12f) / 0.36f));
            return brightness * notDesert * neutral;
        }

        /// <summary>
        /// Analyzes a full-disk square globe image (e.g., cropped 760x760 79e6fa2 left-column panel
        /// or live candidate capture resized to 760x760) for:
        /// 1. Long cloud-band continuity (LargeBandShare, Top3BandShare, Top3MeanSpan, SpeckleCount),
        /// 2. Thin-cloud transition (ThinVeilFraction, TransitionEdgeSharpness),
        /// 3. Natural clear sky (ClearSkyFraction, LargestClearBasinFraction),
        /// 4. Zoomed-out major storm readability (StormCoreP95, StormCoreMean, StormProminence, StormEyeContrast).
        /// </summary>
        public static FullDiskVisualMetrics AnalyzeFullDisk(
            RgbaImage image, string label,
            bool analyzeStorm = false, double stormNormX = 0.0, double stormNormY = 0.18)
        {
            if (image.Width != NasaFullDiskSize || image.Height != NasaFullDiskSize)
                image = image.ResizeBilinear(NasaFullDiskSize, NasaFullDiskSize);

            int w = image.Width, h = image.Height;
            double cx = (w - 1) * 0.5, cy = (h - 1) * 0.5;
            double diskR = 0.88 * (Math.Min(w, h) * 0.5);
            double diskR2 = diskR * diskR;

            var score = new float[w * h];
            var inDisk = new bool[w * h];
            var diskScores = new List<float>(w * h);

            int diskCount = 0, clearCount = 0, thinCount = 0, thickCount = 0;
            double scoreSum = 0;

            for (int y = 0; y < h; y++)
            {
                double dy = y - cy;
                for (int x = 0; x < w; x++)
                {
                    double dx = x - cx;
                    int idx = y * w + x;
                    if (dx * dx + dy * dy > diskR2) continue;
                    inDisk[idx] = true;
                    int p = idx * 4;
                    float cs = PixelCloudScore(image.Rgba[p], image.Rgba[p + 1], image.Rgba[p + 2]);
                    score[idx] = cs;
                    diskScores.Add(cs);
                    diskCount++;
                    scoreSum += cs;
                    if (cs < 0.12f) clearCount++;
                    else if (cs < 0.52f) thinCount++;
                    else thickCount++;
                }
            }

            double edgeGradSum = 0;
            int edgeGradCount = 0;
            for (int y = 0; y < h - 1; y++)
            for (int x = 0; x < w - 1; x++)
            {
                int idx = y * w + x;
                if (!inDisk[idx]) continue;
                float cs = score[idx];
                if (cs < 0.12f || cs >= 0.65f) continue;
                float g = Math.Abs(score[idx + 1] - cs) + Math.Abs(score[idx + w] - cs);
                edgeGradSum += g;
                edgeGradCount++;
            }

            // Downsample 4x for connected-component band continuity & clear-basin openness
            const int step = 4;
            int dw = w / step, dh = h / step;
            var bandMask = new bool[dw * dh];
            var clearMask = new bool[dw * dh];
            int totalBand = 0, totalDiskDs = 0;
            for (int y = 0; y < dh; y++)
            for (int x = 0; x < dw; x++)
            {
                int si = (y * step) * w + (x * step);
                if (!inDisk[si]) continue;
                totalDiskDs++;
                if (score[si] >= 0.18f) { bandMask[y * dw + x] = true; totalBand++; }
                if (score[si] < 0.12f) clearMask[y * dw + x] = true;
            }

            AnalyzeComponents(bandMask, dw, dh, diskR / step,
                out var bandAreas, out var bandSpans);
            AnalyzeComponents(clearMask, dw, dh, diskR / step,
                out var clearAreas, out _);

            int largeSum = 0, speckleCount = 0;
            foreach (int a in bandAreas)
            {
                if (a >= 25) largeSum += a;
                if (a < 12) speckleCount++;
            }
            int top3Area = 0;
            for (int k = 0; k < Math.Min(3, bandAreas.Count); k++) top3Area += bandAreas[k];
            double top3Span = 0;
            int spanN = Math.Min(3, bandSpans.Count);
            for (int k = 0; k < spanN; k++) top3Span += bandSpans[k];
            if (spanN > 0) top3Span /= spanN;

            var res = new FullDiskVisualMetrics
            {
                Label = label,
                ClearSkyFraction = diskCount > 0 ? (double)clearCount / diskCount : 0,
                ThinVeilFraction = diskCount > 0 ? (double)thinCount / diskCount : 0,
                ThickCloudFraction = diskCount > 0 ? (double)thickCount / diskCount : 0,
                TotalCloudFraction = diskCount > 0 ? (double)(thinCount + thickCount) / diskCount : 0,
                LargeBandShare = totalBand > 0 ? (double)largeSum / totalBand : 0,
                Top3BandShare = totalBand > 0 ? (double)top3Area / totalBand : 0,
                Top3MeanSpan = top3Span,
                MaxBandSpan = bandSpans.Count > 0 ? bandSpans[0] : 0,
                SpeckleCount = speckleCount,
                TransitionEdgeSharpness = edgeGradCount > 0 ? edgeGradSum / edgeGradCount : 0,
                LargestClearBasinFraction = (totalDiskDs > 0 && clearAreas.Count > 0) ? (double)clearAreas[0] / totalDiskDs : 0,
                DiskMeanCloudScore = diskCount > 0 ? scoreSum / diskCount : 0
            };

            if (analyzeStorm && diskScores.Count > 0)
            {
                diskScores.Sort();
                float diskP25 = diskScores[diskScores.Count / 4];
                int scx = (int)Math.Round(cx + stormNormX * diskR);
                int scy = (int)Math.Round(cy + stormNormY * diskR);
                int half = (int)Math.Round(0.16 * diskR);
                int x0 = Math.Max(0, scx - half), x1 = Math.Min(w - 1, scx + half);
                int y0 = Math.Max(0, scy - half), y1 = Math.Min(h - 1, scy + half);
                var patch = new List<float>((x1 - x0 + 1) * (y1 - y0 + 1));
                double pSum = 0;
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float s = score[y * w + x];
                    patch.Add(s);
                    pSum += s;
                }
                patch.Sort();
                float p95 = patch.Count > 0 ? patch[(int)(patch.Count * 0.95)] : 0f;
                res.StormCoreP95 = p95;
                res.StormCoreMean = patch.Count > 0 ? pSum / patch.Count : 0;
                res.StormProminence = Math.Max(0.0, p95 - diskP25);

                // Measure local eye-vs-eyewall contrast inside the storm patch (small eye clearing inside bright collar)
                int eyeSearchR = Math.Max(6, (int)(0.055 * diskR));
                float bestEye = 1f;
                int exBest = scx, eyBest = scy;
                for (int y = Math.Max(1, scy - eyeSearchR); y <= Math.Min(h - 2, scy + eyeSearchR); y++)
                for (int x = Math.Max(1, scx - eyeSearchR); x <= Math.Min(w - 2, scx + eyeSearchR); x++)
                {
                    float v = score[y * w + x];
                    if (v < bestEye) { bestEye = v; exBest = x; eyBest = y; }
                }
                double ringSum = 0;
                int ringN = 0;
                int ringR = Math.Max(8, (int)(0.045 * diskR));
                for (int k = 0; k < 24; k++)
                {
                    double ang = k * 2.0 * Math.PI / 24.0;
                    int rx = Math.Max(0, Math.Min(w - 1, (int)Math.Round(exBest + Math.Cos(ang) * ringR)));
                    int ry = Math.Max(0, Math.Min(h - 1, (int)Math.Round(eyBest + Math.Sin(ang) * ringR)));
                    ringSum += score[ry * w + rx];
                    ringN++;
                }
                res.StormEyeContrast = Math.Max(0.0, (ringN > 0 ? ringSum / ringN : 0) - bestEye);
            }

            return res;
        }

        static void AnalyzeComponents(bool[] mask, int dw, int dh, double radiusDs,
            out List<int> areas, out List<double> spans)
        {
            areas = new List<int>();
            spans = new List<double>();
            var visited = new bool[dw * dh];
            var queue = new Queue<int>(256);
            double invDiam = 1.0 / Math.Max(1.0, 2.0 * radiusDs);
            for (int y = 0; y < dh; y++)
            for (int x = 0; x < dw; x++)
            {
                int start = y * dw + x;
                if (!mask[start] || visited[start]) continue;
                visited[start] = true;
                queue.Enqueue(start);
                int area = 0, minX = x, maxX = x, minY = y, maxY = y;
                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    int cy = cur / dw, cx = cur % dw;
                    area++;
                    if (cx < minX) minX = cx;
                    if (cx > maxX) maxX = cx;
                    if (cy < minY) minY = cy;
                    if (cy > maxY) maxY = cy;
                    for (int ny = Math.Max(0, cy - 1); ny <= Math.Min(dh - 1, cy + 1); ny++)
                    for (int nx = Math.Max(0, cx - 1); nx <= Math.Min(dw - 1, cx + 1); nx++)
                    {
                        int ni = ny * dw + nx;
                        if (mask[ni] && !visited[ni])
                        {
                            visited[ni] = true;
                            queue.Enqueue(ni);
                        }
                    }
                }
                areas.Add(area);
                if (area >= 6)
                {
                    double dx = maxX - minX, dy = maxY - minY;
                    spans.Add(Math.Sqrt(dx * dx + dy * dy) * invDiam);
                }
            }
            areas.Sort((a, b) => b.CompareTo(a));
            spans.Sort((a, b) => b.CompareTo(a));
        }

        /// <summary>
        /// Compares a candidate full-disk capture against the user-preferred 79e6fa2 left-column baseline
        /// on long cloud-band continuity, thin-cloud transition, natural clear sky, and zoomed-out storm readability.
        /// </summary>
        public static BaselineComparisonVerdict CompareAgainst79e6fa2(
            string viewId, FullDiskVisualMetrics baseline79, FullDiskVisualMetrics candidate, bool checkStormReadability)
        {
            var v = new BaselineComparisonVerdict { ViewId = viewId, Baseline79 = baseline79, Candidate = candidate };
            void Check(bool ok, string msg)
            {
                v.Checks.Add((ok ? "PASS " : "FAIL ") + msg);
                if (!ok) v.Failures.Add(msg);
            }

            // 1. Long cloud-band continuity
            double minSpan = baseline79.Top3MeanSpan * 0.88;
            Check(candidate.Top3MeanSpan >= minSpan,
                $"long band continuity Top3MeanSpan={candidate.Top3MeanSpan:F3} vs 79e6fa2={baseline79.Top3MeanSpan:F3} (min {minSpan:F3})");
            double minLarge = baseline79.LargeBandShare - 0.035;
            Check(candidate.LargeBandShare >= minLarge,
                $"coherent band area share LargeBandShare={candidate.LargeBandShare:F3} vs 79e6fa2={baseline79.LargeBandShare:F3} (min {minLarge:F3})");
            int maxSpeckles = Math.Max((int)Math.Ceiling(baseline79.SpeckleCount * 1.25), baseline79.SpeckleCount + 20);
            Check(candidate.SpeckleCount <= maxSpeckles,
                $"band fragmentation SpeckleCount={candidate.SpeckleCount} vs 79e6fa2={baseline79.SpeckleCount} (max {maxSpeckles})");

            // 2. Thin-cloud transition (avoid binary hard-cut paper steps & preserve soft veil)
            double minThin = baseline79.ThinVeilFraction * 0.78;
            Check(candidate.ThinVeilFraction >= minThin,
                $"thin-cloud veil ThinVeilFraction={candidate.ThinVeilFraction:F3} vs 79e6fa2={baseline79.ThinVeilFraction:F3} (min {minThin:F3})");
            double maxSharp = baseline79.TransitionEdgeSharpness * 1.12;
            Check(candidate.TransitionEdgeSharpness <= maxSharp,
                $"soft thin-cloud transition EdgeSharpness={candidate.TransitionEdgeSharpness:F4} vs 79e6fa2={baseline79.TransitionEdgeSharpness:F4} (max {maxSharp:F4})");

            // 3. Natural clear sky (prevent both over-blanketing and bald over-clearing vs 79e6fa2)
            double clearDiff = Math.Abs(candidate.ClearSkyFraction - baseline79.ClearSkyFraction);
            Check(clearDiff <= 0.105 && candidate.ClearSkyFraction >= 0.22 && candidate.ClearSkyFraction <= 0.64,
                $"natural clear sky ClearSkyFraction={candidate.ClearSkyFraction:F3} vs 79e6fa2={baseline79.ClearSkyFraction:F3} (|diff|={clearDiff:F3} <=0.105)");
            Check(candidate.LargestClearBasinFraction >= 0.06,
                $"open clear-sky basin LargestClearBasin={candidate.LargestClearBasinFraction:F3} (>=0.06)");

            // 4. Zoomed-out major storm readability
            if (checkStormReadability)
            {
                double minProm = Math.Max(0.55, baseline79.StormProminence * 0.85);
                Check(candidate.StormProminence >= minProm && candidate.StormCoreMean >= 0.50,
                    $"zoomed-out storm readability prominence={candidate.StormProminence:F3} (min {minProm:F3}), coreMean={candidate.StormCoreMean:F3} (79e6fa2={baseline79.StormCoreMean:F3})");
            }

            v.Passed = v.Failures.Count == 0;
            return v;
        }

        /// <summary>
        /// Composes a 3-panel paired review sheet: [State A | State B | Signed Diff Heatmap (Red=+B, Blue=-B)].
        /// </summary>
        public static RgbaImage ComposePairSheet(RgbaImage imgA, RgbaImage imgB, bool passed, float diffGain = 3.0f)
        {
            if (imgB.Width != imgA.Width || imgB.Height != imgA.Height)
                imgB = imgB.ResizeBilinear(imgA.Width, imgA.Height);
            int pw = imgA.Width, ph = imgA.Height;
            const int gap = 12, pad = 12, barH = 14;
            int outW = pad * 2 + pw * 3 + gap * 2;
            int outH = pad * 2 + barH + ph;
            var dst = new byte[outW * outH * 4];
            FillRect(dst, outW, outH, 0, 0, outW, outH, 14, 18, 26, 255);
            // Status accent bar across top
            byte br = passed ? (byte)64 : (byte)220, bg = passed ? (byte)204 : (byte)68, bb = passed ? (byte)128 : (byte)76;
            FillRect(dst, outW, outH, pad, pad, outW - pad * 2, 6, br, bg, bb, 255);

            int yOff = pad + barH;
            Blit(imgA, dst, outW, pad, yOff);
            Blit(imgB, dst, outW, pad + pw + gap, yOff);

            // Signed diff heatmap: neutral slate = unchanged, red/amber = B brighter than A, cyan/blue = A brighter than B
            var heat = new byte[pw * ph * 4];
            for (int i = 0; i < pw * ph; i++)
            {
                int p = i * 4;
                double la = 0.2126 * imgA.Rgba[p] + 0.7152 * imgA.Rgba[p + 1] + 0.0722 * imgA.Rgba[p + 2];
                double lb = 0.2126 * imgB.Rgba[p] + 0.7152 * imgB.Rgba[p + 1] + 0.0722 * imgB.Rgba[p + 2];
                double d = (lb - la) * diffGain;
                byte hr, hg, hb;
                if (d >= 0)
                {
                    double t = Math.Min(1.0, d / 255.0);
                    hr = (byte)(28 + 227 * t);
                    hg = (byte)(34 + 130 * t * t);
                    hb = (byte)(44 * (1.0 - t));
                }
                else
                {
                    double t = Math.Min(1.0, -d / 255.0);
                    hr = (byte)(28 * (1.0 - t));
                    hg = (byte)(34 + 165 * t);
                    hb = (byte)(44 + 211 * t);
                }
                heat[p] = hr; heat[p + 1] = hg; heat[p + 2] = hb; heat[p + 3] = 255;
            }
            Blit(new RgbaImage(pw, ph, heat), dst, outW, pad + (pw + gap) * 2, yOff);
            return new RgbaImage(outW, outH, dst);
        }

        /// <summary>
        /// Composes a 3-panel 79e6fa2 visual baseline sheet:
        /// [79e6fa2 Left-Col Reference | Candidate Full-Disk | Candidate Cloud-Tier Map (Navy=Clear, Cyan=Thin Veil, White=Thick)].
        /// </summary>
        public static RgbaImage ComposeBaselineSheet(RgbaImage ref79, RgbaImage candidate, bool passed)
        {
            if (ref79.Width != NasaFullDiskSize || ref79.Height != NasaFullDiskSize)
                ref79 = ref79.ResizeBilinear(NasaFullDiskSize, NasaFullDiskSize);
            if (candidate.Width != NasaFullDiskSize || candidate.Height != NasaFullDiskSize)
                candidate = candidate.ResizeBilinear(NasaFullDiskSize, NasaFullDiskSize);

            int pw = NasaFullDiskSize, ph = NasaFullDiskSize;
            const int gap = 12, pad = 12, barH = 14;
            int outW = pad * 2 + pw * 3 + gap * 2;
            int outH = pad * 2 + barH + ph;
            var dst = new byte[outW * outH * 4];
            FillRect(dst, outW, outH, 0, 0, outW, outH, 14, 18, 26, 255);
            FillRect(dst, outW, outH, pad, pad, pw, 6, 232, 168, 72, 255); // Amber bar for 79e6fa2 ref
            FillRect(dst, outW, outH, pad + pw + gap, pad, pw, 6, passed ? (byte)64 : (byte)220, passed ? (byte)204 : (byte)68, passed ? (byte)128 : (byte)76, 255);
            FillRect(dst, outW, outH, pad + (pw + gap) * 2, pad, pw, 6, 96, 178, 240, 255);

            int yOff = pad + barH;
            Blit(ref79, dst, outW, pad, yOff);
            Blit(candidate, dst, outW, pad + pw + gap, yOff);

            var map = new byte[pw * ph * 4];
            double cx = (pw - 1) * 0.5, cy = (ph - 1) * 0.5, r2 = Math.Pow(0.88 * pw * 0.5, 2.0);
            for (int y = 0; y < ph; y++)
            for (int x = 0; x < pw; x++)
            {
                int i = y * pw + x, p = i * 4;
                double d2 = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                if (d2 > r2) { map[p] = 10; map[p + 1] = 14; map[p + 2] = 22; map[p + 3] = 255; continue; }
                float cs = PixelCloudScore(candidate.Rgba[p], candidate.Rgba[p + 1], candidate.Rgba[p + 2]);
                if (cs < 0.12f) { map[p] = 22; map[p + 1] = 64; map[p + 2] = 112; }        // Clear sky
                else if (cs < 0.52f) { map[p] = 74; map[p + 1] = 192; map[p + 2] = 224; } // Thin-cloud veil
                else { map[p] = 242; map[p + 1] = 246; map[p + 2] = 252; }                // Thick cloud core
                map[p + 3] = 255;
            }
            Blit(new RgbaImage(pw, ph, map), dst, outW, pad + (pw + gap) * 2, yOff);
            return new RgbaImage(outW, outH, dst);
        }

        static void FillRect(byte[] dst, int w, int h, int x0, int y0, int rw, int rh, byte r, byte g, byte b, byte a)
        {
            for (int y = Math.Max(0, y0); y < Math.Min(h, y0 + rh); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(w, x0 + rw); x++)
            {
                int p = (y * w + x) * 4;
                dst[p] = r; dst[p + 1] = g; dst[p + 2] = b; dst[p + 3] = a;
            }
        }

        static void Blit(RgbaImage src, byte[] dst, int dstW, int x0, int y0)
        {
            int rowBytes = src.Width * 4;
            for (int y = 0; y < src.Height; y++)
                Buffer.BlockCopy(src.Rgba, y * rowBytes, dst, ((y0 + y) * dstW + x0) * 4, rowBytes);
        }

        public static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
