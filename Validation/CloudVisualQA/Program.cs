using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Orbis;
using Orbis.Editor.CloudVisualQA;
using Orbis.Flat;

public static class Program
{
    static int assertions;

    static void Check(bool cond, string msg)
    {
        assertions++;
        if (!cond) throw new InvalidOperationException("Lightweight QA FAIL: " + msg);
    }

    public static int Main(string[] args)
    {
        try
        {
            string repoRoot = FindRepoRoot();
            string outDir = Path.Combine(repoRoot, "ValidationOutput", "CloudVisualQA_Lightweight");
            Directory.CreateDirectory(outDir);

            Console.WriteLine("=== ORBIS Climate-Cloud Visual QA — CPU-Only Lightweight Verification ===");
            Console.WriteLine("Repo root: " + repoRoot);

            var md = new StringBuilder(4096);
            md.AppendLine("# ORBIS Climate-Cloud Visual QA — Lightweight CPU-Only Verification Report");
            md.AppendLine();
            md.AppendLine($"- **Timestamp (UTC)**: `{DateTime.UtcNow:O}`");
            md.AppendLine("- **Scope**: Pure-C# `Orbis.Flat.Core` forcing/circulation/cyclone paired audit + anti-decorative-noise adversarial detector tests + cyclone spiral chirality tests + real `79e6fa2` left-column PNG baseline calibration (zero Unity/GPU execution).");
            md.AppendLine();

            RunCloudTopModelChecks(md);
            RunAntiDecorativeNoiseSelfTests(md);
            RunCycloneSpiralSelfTests(md);
            RunRealEarthPairedForcingAudit(repoRoot, md);
            Run79e6fa2BaselinePngAudit(repoRoot, outDir, md);

            md.AppendLine($"## Summary");
            md.AppendLine($"- **Total Lightweight Assertions Passed**: `{assertions}`");
            File.WriteAllText(Path.Combine(outDir, "lightweight-qa-report.md"), md.ToString(), Encoding.UTF8);

            Console.WriteLine($"ALL LIGHTWEIGHT QA SUITES PASSED ({assertions} assertions). Report: {Path.Combine(outDir, "lightweight-qa-report.md")}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    static string FindRepoRoot()
    {
        string dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 6; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "Assets", "ORBIS")) &&
                Directory.Exists(Path.Combine(dir, "Documentation", "CloudEarth80Showcase", "NasaUpgrade3Col")))
                return dir;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate ORBIS repository root from " + Directory.GetCurrentDirectory());
    }

    static void RunCloudTopModelChecks(StringBuilder md)
    {
        Console.WriteLine("\n[1/5] Checking OrbisCloudTopModel (OrbisHPVolumeCloud vertical-span mirror)...");
        OrbisCloudTopModel.VerticalSpanMeters(0.07f, 0.70f, out float scBase, out float scTop);
        OrbisCloudTopModel.VerticalSpanMeters(0.25f, 0.60f, out float cuBase, out float cuTop);
        OrbisCloudTopModel.VerticalSpanMeters(0.45f, 0.70f, out float midBase, out float midTop);
        OrbisCloudTopModel.VerticalSpanMeters(0.88f, 0.90f, out float cbBase, out float cbTop);

        Check(scTop > 1800f && scTop < 2500f, $"Marine Sc top out of range: {scTop}");
        Check(cuTop > scTop + 500f && cuTop < 3600f, $"Trade Cu top out of range: {cuTop}");
        Check(midTop > cuTop + 800f && midTop < 5600f, $"Congestus/Ac top out of range: {midTop}");
        Check(cbTop > 10500f && cbTop <= 12500f, $"Cb convective top out of range: {cbTop}");
        Check(cbBase < cuBase, $"Cb base ({cbBase}) should sit below elevated Trade Cu LCL ({cuBase})");
        Console.WriteLine($"  PASS vertical tiers: Sc={scBase:F0}..{scTop:F0} m | Cu={cuBase:F0}..{cuTop:F0} m | Mid={midBase:F0}..{midTop:F0} m | Cb={cbBase:F0}..{cbTop:F0} m");

        md.AppendLine("## 1. Cloud-Top Altitude Tier Mirror (`OrbisCloudTopModel`)");
        md.AppendLine($"- PASS Marine Sc `{scBase:F0}..{scTop:F0} m` < Trade Cu `{cuBase:F0}..{cuTop:F0} m` < Mid Ac `{midBase:F0}..{midTop:F0} m` < Deep Cb `{cbBase:F0}..{cbTop:F0} m`");
        md.AppendLine();
    }

    static void RunAntiDecorativeNoiseSelfTests(StringBuilder md)
    {
        Console.WriteLine("\n[2/5] Running adversarial anti-decorative-noise detector self-tests...");
        var roi = new GeoBox(-20, 20, -15, 15);
        var ctrl = new GeoBox(80, 120, -15, 15);
        const int w = 64, h = 32;

        // Build base forcing & genuine physical response grids
        var forcA = new Float4Grid(w, h);
        var forcB = new Float4Grid(w, h);
        var colA = new Float4Grid(w, h);
        var colB = new Float4Grid(w, h);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            double lon = ((x + 0.5) / w - 0.5) * 360.0;
            double lat = ((y + 0.5) / h - 0.5) * 180.0;
            int i = y * w + x;
            forcA.R[i] = 0.72f; colA.R[i] = 0.65f;
            if (roi.Contains(lon, lat))
            {
                // Spatially structured dry-down inside ROI (varies slightly with lon/lat so PearsonR is well-defined)
                float drop = 0.30f + 0.15f * (float)Math.Cos(lon * Math.PI / 40.0) * (float)Math.Cos(lat * Math.PI / 30.0);
                forcB.R[i] = forcA.R[i] - drop;
                colB.R[i] = colA.R[i] - drop * 0.85f;
            }
            else
            {
                forcB.R[i] = forcA.R[i];
                colB.R[i] = colA.R[i];
            }
        }

        // Case 1: Genuine physical decrease -> MUST PASS
        var fd = ClimateCloudQaMetrics.ComputeFieldDelta(forcA, forcB, 0, roi, ctrl);
        var cd = ClimateCloudQaMetrics.ComputeFieldDelta(colA, colB, 0, roi, ctrl);
        double rTrue = ClimateCloudQaMetrics.ComputeDeltaPearsonR(forcA, forcB, 0, colA, colB, 0, roi);
        var goodScreen = new ScreenRoiDeltaStats
        {
            TargetPixels = 1000, ControlPixels = 1000,
            SignedLumaDeltaDn = -18.5, MeanAbsLumaDeltaDn = 20.0, DirectionalCoherence = 0.925,
            ControlMeanAbsLumaDeltaDn = 0.4, ControlLeakageRatio = 0.02
        };
        var vPass = ClimateCloudQaMetrics.EvaluateAntiNoiseBinding(
            PairExpectationKind.MonotonicDecrease, fd, cd, cd, cd, rTrue, goodScreen);
        Check(vPass.Passed, "Genuine physical response should PASS: " + string.Join("; ", vPass.Failures));

        // Case 2: Zero-mean random decorative noise shuffle in ColumnTexture & image -> MUST FAIL
        var colNoise = new Float4Grid(w, h);
        var rng = new Random(12345);
        for (int i = 0; i < w * h; i++)
        {
            float jitter = (float)((rng.NextDouble() * 2.0 - 1.0) * 0.25);
            colNoise.R[i] = Math.Max(0f, Math.Min(1f, colA.R[i] + jitter));
        }
        var cdNoise = ClimateCloudQaMetrics.ComputeFieldDelta(colA, colNoise, 0, roi, ctrl);
        double rNoise = ClimateCloudQaMetrics.ComputeDeltaPearsonR(forcA, forcB, 0, colA, colNoise, 0, roi);
        var noiseScreen = new ScreenRoiDeltaStats
        {
            TargetPixels = 1000, ControlPixels = 1000,
            SignedLumaDeltaDn = -0.3, MeanAbsLumaDeltaDn = 14.0, DirectionalCoherence = 0.021,
            ControlMeanAbsLumaDeltaDn = 13.8, ControlLeakageRatio = 0.98
        };
        var vNoise = ClimateCloudQaMetrics.EvaluateAntiNoiseBinding(
            PairExpectationKind.MonotonicDecrease, fd, cdNoise, cdNoise, cdNoise, rNoise, noiseScreen);
        Check(!vNoise.Passed, "Zero-mean decorative noise shuffle MUST FAIL");

        // Case 3: Render-only decorative 3D noise (image churns 12 DN while ColumnTexture is static) -> MUST FAIL
        var cdFlat = ClimateCloudQaMetrics.ComputeFieldDelta(colA, colA, 0, roi, ctrl);
        var vRenderOnly = ClimateCloudQaMetrics.EvaluateAntiNoiseBinding(
            PairExpectationKind.MonotonicDecrease, fd, cdFlat, cdFlat, cdFlat, 0.0,
            new ScreenRoiDeltaStats
            {
                TargetPixels = 1000, ControlPixels = 1000,
                SignedLumaDeltaDn = -6.0, MeanAbsLumaDeltaDn = 12.0, DirectionalCoherence = 0.50,
                ControlMeanAbsLumaDeltaDn = 0.5, ControlLeakageRatio = 0.04
            });
        Check(!vRenderOnly.Passed && vRenderOnly.Failures.Exists(s => s.Contains("DECORATIVE_NOISE_RENDER_ONLY")),
            "Render-only decorative noise MUST FAIL with DECORATIVE_NOISE_RENDER_ONLY");

        // Case 4: Unbound sky cache (ColumnTexture changes cleanly but rendered screenshot stays flat) -> MUST FAIL
        var vUnbound = ClimateCloudQaMetrics.EvaluateAntiNoiseBinding(
            PairExpectationKind.MonotonicDecrease, fd, cd, cd, cd, rTrue,
            new ScreenRoiDeltaStats
            {
                TargetPixels = 1000, ControlPixels = 1000,
                SignedLumaDeltaDn = -0.1, MeanAbsLumaDeltaDn = 0.2, DirectionalCoherence = 0.50,
                ControlMeanAbsLumaDeltaDn = 0.1, ControlLeakageRatio = 0.50
            });
        Check(!vUnbound.Passed && vUnbound.FailureCode == "UNBOUND_SKY_OR_WRONG_IMAGE_SIGN",
            "Unbound sky cache MUST FAIL with UNBOUND_SKY_OR_WRONG_IMAGE_SIGN");

        // Case 5: Global decorative leak (unperturbed control ROI shifts just as much as target ROI) -> MUST FAIL
        var colGlobal = new Float4Grid(w, h);
        for (int i = 0; i < w * h; i++) colGlobal.R[i] = Math.Max(0f, colA.R[i] - 0.30f);
        var cdGlobal = ClimateCloudQaMetrics.ComputeFieldDelta(colA, colGlobal, 0, roi, ctrl);
        var vLeak = ClimateCloudQaMetrics.EvaluateAntiNoiseBinding(
            PairExpectationKind.MonotonicDecrease, fd, cdGlobal, cdGlobal, cdGlobal, rTrue, goodScreen);
        Check(!vLeak.Passed && vLeak.Failures.Exists(s => s.Contains("GLOBAL_NOISE_LEAKAGE")),
            "Global uniform drift leaking into control ROI MUST FAIL with GLOBAL_NOISE_LEAKAGE");

        // Case 6: Inverted physical sign (dry produces MORE clouds than moist) -> MUST FAIL
        var cdInv = ClimateCloudQaMetrics.ComputeFieldDelta(colB, colA, 0, roi, ctrl);
        var vInv = ClimateCloudQaMetrics.EvaluateAntiNoiseBinding(
            PairExpectationKind.MonotonicDecrease, fd, cdInv, cdInv, cdInv, -rTrue,
            new ScreenRoiDeltaStats
            {
                TargetPixels = 1000, ControlPixels = 1000,
                SignedLumaDeltaDn = +16.0, MeanAbsLumaDeltaDn = 17.0, DirectionalCoherence = 0.94,
                ControlMeanAbsLumaDeltaDn = 0.2, ControlLeakageRatio = 0.01
            });
        Check(!vInv.Passed && vInv.FailureCode == "WRONG_SIGN_OR_WEAK_COLUMN",
            "Inverted physical sign MUST FAIL");

        // Case 7: Locked zero-drift control with visual-clock drift -> MUST FAIL
        var vDrift = ClimateCloudQaMetrics.EvaluateAntiNoiseBinding(
            PairExpectationKind.ZeroDriftControl, fd, cdNoise, cdNoise, cdNoise, 0.0, noiseScreen);
        Check(!vDrift.Passed && vDrift.FailureCode == "CLOCK_OR_SEED_DRIFT",
            "Locked control with clock/seed drift MUST FAIL with CLOCK_OR_SEED_DRIFT");

        Console.WriteLine("  PASS all 7 anti-decorative-noise tests (1 true signal PASS + 6 decorative/unbound/leak/inverted/drift FAILs caught)");
        md.AppendLine("## 2. Anti-Decorative-Noise Detector Adversarial Self-Tests");
        md.AppendLine("- PASS genuine physical decrease (`r=" + rTrue.ToString("F3", CultureInfo.InvariantCulture) + "`, coherence=`" + cd.DirectionalCoherence.ToString("F3", CultureInfo.InvariantCulture) + "`) -> `PASS`");
        md.AppendLine("- PASS caught zero-mean random noise shuffle (`coherence=" + cdNoise.DirectionalCoherence.ToString("F3", CultureInfo.InvariantCulture) + "`) -> `FAIL`");
        md.AppendLine("- PASS caught render-only 3D decorative noise (`DECORATIVE_NOISE_RENDER_ONLY`) -> `FAIL`");
        md.AppendLine("- PASS caught unbound sky cache (`UNBOUND_SKY_OR_WRONG_IMAGE_SIGN`) -> `FAIL`");
        md.AppendLine("- PASS caught global decorative leakage into unperturbed control ROI (`GLOBAL_NOISE_LEAKAGE`) -> `FAIL`");
        md.AppendLine("- PASS caught inverted physical sign (`WRONG_SIGN_OR_WEAK_COLUMN`) -> `FAIL`");
        md.AppendLine("- PASS caught locked control clock/seed drift (`CLOCK_OR_SEED_DRIFT`) -> `FAIL`");
        md.AppendLine();
    }

    static void RunCycloneSpiralSelfTests(StringBuilder md)
    {
        Console.WriteLine("\n[3/5] Running cyclone position, hemispheric spiral chirality & signed-phase self-tests...");
        const int w = 512, h = 256;

        Float4Grid SynthesizeVortex(double lonDeg, double latDeg, int spin, double rotPhase, double strength)
        {
            var g = new Float4Grid(w, h);
            double latR = latDeg * Math.PI / 180.0, lonR = lonDeg * Math.PI / 180.0;
            double ax = Math.Cos(latR) * Math.Sin(lonR), ay = Math.Sin(latR), az = Math.Cos(latR) * Math.Cos(lonR);
            double ex = Math.Cos(lonR), ey = 0, ez = -Math.Sin(lonR);
            double nx = ay * ez - az * ey, ny = az * ex - ax * ez, nz = ax * ey - ay * ex;
            double extent = 0.08 + 0.08 * strength;

            for (int y = 0; y < h; y++)
            {
                double v = (y + 0.5) / h, lat = (v - 0.5) * Math.PI;
                double cl = Math.Cos(lat), sl = Math.Sin(lat);
                for (int x = 0; x < w; x++)
                {
                    double u = (x + 0.5) / w, lon = (u - 0.5) * 2.0 * Math.PI;
                    double px = cl * Math.Sin(lon), py = sl, pz = cl * Math.Cos(lon);
                    double dist = Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay) + (pz - az) * (pz - az));
                    double r = dist / extent;
                    int idx = y * w + x;
                    g.R[idx] = 0.12f;
                    g.B[idx] = 0.20f;
                    if (r >= 1.35) continue;
                    double lx = (px * ex + py * ey + pz * ez) / extent;
                    double ly = (px * nx + py * ny + pz * nz) / extent;
                    double bearing = Math.Atan2(ly, lx + 1e-7);
                    double phase = (bearing - rotPhase) + spin * 3.1 * Math.Log(1.0 + 4.8 * r);
                    double mainArm = Math.Max(0.0, Math.Cos(phase));
                    double eye = Math.Min(1.0, Math.Max(0.0, (r - 0.03) / 0.05));
                    double wall = Math.Exp(-Math.Pow((r - 0.18) / 0.10, 2.0));
                    double fade = 1.0 - Math.Min(1.0, Math.Max(0.0, (r - 0.55) / 0.65));
                    double cover = eye * Math.Max(wall, 0.85 * mainArm) * fade;
                    g.R[idx] = (float)Math.Max(0.12, Math.Min(1.0, cover));
                    g.B[idx] = (float)Math.Min(1.0, 0.25 + 0.72 * wall * fade);
                    g.A[idx] = (float)(eye * wall * fade);
                }
            }
            return g;
        }

        // NH CCW vortex translating WNW and rotating CCW (+phase)
        double u0 = (-72.0 + 180.0) / 360.0, v0 = (13.0 + 90.0) / 180.0;
        double u1 = (-77.0 + 180.0) / 360.0, v1 = (16.0 + 90.0) / 180.0;
        var g0 = SynthesizeVortex(-72.0, 13.0, +1, 1.0, 0.60);
        var g1 = SynthesizeVortex(-77.0, 16.0, +1, 1.9, 0.88);
        double shiftRad = Math.PI / 3.0;
        var g1Rot = SynthesizeVortex(-77.0, 16.0, +1, 1.9 + shiftRad, 0.88);

        var m = ClimateCloudQaMetrics.MeasureCycloneCloudEvolution(
            g0, g1, u0, v0, 32, 0.60, 1.0, u1, v1, 48, 0.88, 1.9, g1Rot, shiftRad);
        Check(m.ObservedPositionError1Deg < 0.8 && m.ShiftAlignmentCos > 0.98,
            $"NH cyclone tracking error too high: err1={m.ObservedPositionError1Deg:F2} cos={m.ShiftAlignmentCos:F3}");
        Check(m.SpiralChiralityRatio < 0.85,
            $"NH CCW spiral chirality failed: ratio={m.SpiralChiralityRatio:F3}");
        Check(m.RotationSignedError < m.RotationSameError * 0.25 && m.RotationSignedError < m.RotationOppositeError * 0.25,
            $"NH signed rotation failed: signed={m.RotationSignedError:F2} same={m.RotationSameError:F2} opp={m.RotationOppositeError:F2}");
        Check(m.FarFieldLeakageRatio < 0.01, $"Far-field leakage too high: {m.FarFieldLeakageRatio:F4}");

        // Wrong-hemisphere (CW spin in NH) MUST be rejected by chirality check
        var gWrong = SynthesizeVortex(-77.0, 16.0, -1, 1.9, 0.88);
        var mWrong = ClimateCloudQaMetrics.MeasureCycloneCloudEvolution(
            g0, gWrong, u0, v0, 32, 0.60, 1.0, u1, v1, 48, 0.88, 1.9);
        Check(mWrong.SpiralChiralityRatio > 1.05,
            $"Wrong-hemisphere chirality should exceed 1.05, got {mWrong.SpiralChiralityRatio:F3}");

        Console.WriteLine($"  PASS NH CCW spiral: posErr={m.ObservedPositionError1Deg:F2} deg, shiftCos={m.ShiftAlignmentCos:F3}, chirality={m.SpiralChiralityRatio:F3} (wrong-hemi={mWrong.SpiralChiralityRatio:F3}), rotErr signed/same/opp={m.RotationSignedError:F2}/{m.RotationSameError:F2}/{m.RotationOppositeError:F2}");
        md.AppendLine("## 3. Cyclone Tracking, Spiral Chirality & Signed Rotation Evaluator Self-Tests");
        md.AppendLine($"- PASS NH CCW spiral tracking (`posErr={m.ObservedPositionError1Deg:F2} deg`, `shiftCos={m.ShiftAlignmentCos:F3}`, `inward/outward={m.SpiralChiralityRatio:F3}`, `signed/same/opp={m.RotationSignedError:F2}/{m.RotationSameError:F2}/{m.RotationOppositeError:F2}`)");
        md.AppendLine($"- PASS rejected wrong-hemisphere chirality (`inward/outward={mWrong.SpiralChiralityRatio:F3} > 1.05`)");
        md.AppendLine();
    }

    static void RunRealEarthPairedForcingAudit(string repoRoot, StringBuilder md)
    {
        Console.WriteLine("\n[4/5] Running real Earth256 paired forcing & natural cyclone audit through Orbis.Flat.Core...");
        string resDir = Path.Combine(repoRoot, "Assets", "ORBIS", "Resources", "ORBIS", "FlatWorld");
        var baseEarth = ClimateCloudQaScenarios.BuildRealEarth(
            256,
            File.ReadAllBytes(Path.Combine(resDir, "Earth256.bytes")),
            File.ReadAllBytes(Path.Combine(resDir, "EarthWater256.bytes")),
            File.ReadAllBytes(Path.Combine(resDir, "EarthRivers.bytes")));

        var scenarios = ClimateCloudQaScenarios.BuildAllPairedScenarios(baseEarth);
        Check(scenarios.Count == 7, $"Expected 7 paired scenarios, got {scenarios.Count}");

        md.AppendLine("## 4. Real Earth (`Earth256`) Paired Forcing & Natural Cyclone Core Audit");
        md.AppendLine("| Scenario | E2E Coverage | Forcing A -> B | Coherence | Control Leakage | Extra Metric | Verdict |");
        md.AppendLine("| :--- | :--- | :---: | :---: | :---: | :--- | :---: |");

        foreach (var spec in scenarios)
        {
            var audit = ClimateCloudQaScenarios.AuditCoreForcing(spec);
            Console.WriteLine($"  [{spec.Id}] ({spec.CoverageLevel}) forcing={audit.ForcingMeanA:F3}->{audit.ForcingMeanB:F3} coh={audit.ForcingCoherence:F3} leak={audit.ForcingControlLeakage:F4} => {(audit.Passed ? "PASS" : "FAIL")}");
            foreach (var note in audit.Notes) Console.WriteLine("     " + note);
            Check(audit.Passed, $"Core forcing audit failed for {spec.Id}: " + string.Join("; ", audit.Failures));

            string extra = string.IsNullOrEmpty(audit.ExtraMetricLabel)
                ? "-"
                : string.Format(CultureInfo.InvariantCulture, "{0}: {1:F2} / {2:F2}", audit.ExtraMetricLabel, audit.ExtraMetricA, audit.ExtraMetricB);
            md.AppendFormat(CultureInfo.InvariantCulture,
                "| `{0}` | `{1}` | `{2:F3} -> {3:F3}` | `{4:F3}` | `{5:F4}` | `{6}` | **PASS** |\n",
                spec.Id, spec.CoverageLevel, audit.ForcingMeanA, audit.ForcingMeanB,
                audit.ForcingCoherence, audit.ForcingControlLeakage, extra);
        }
        md.AppendLine();
    }

    static void Run79e6fa2BaselinePngAudit(string repoRoot, string outDir, StringBuilder md)
    {
        Console.WriteLine("\n[5/5] Auditing all 5 real NasaUpgrade3Col 79e6fa2 left-column baselines vs aaa9625 middle column...");
        var views = ClimateCloudQaScenarios.Build79e6fa2BaselineViews();
        int aaa9625FlaggedCount = 0;

        md.AppendLine("## 5. Real `79e6fa2` Left-Column Baseline Calibration (`NasaUpgrade3Col` PNG Audit)");
        md.AppendLine("| View | Group | Clear (79 -> aaa9625) | Thin Veil (79 -> aaa9625) | Top3 Span (79 -> aaa9625) | Speckles (79 -> aaa9625) | Sharpness (79 -> aaa9625) | Storm Prom. | Self 79 | aaa9625 vs 79 |");
        md.AppendLine("| :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |");

        foreach (var v in views)
        {
            string pngPath = Path.Combine(repoRoot, v.ReferencePngRelPath.Replace('/', Path.DirectorySeparatorChar));
            Check(File.Exists(pngPath), "Missing NasaUpgrade3Col baseline PNG: " + pngPath);
            var raster = OrbisRasterCodec.Read(File.ReadAllBytes(pngPath));
            var full = new RgbaImage(raster.width, raster.height, raster.rgba);
            var left79 = full.Crop(ClimateCloudQaMetrics.NasaFullDiskLeftX, ClimateCloudQaMetrics.NasaFullDiskTopY,
                ClimateCloudQaMetrics.NasaFullDiskSize, ClimateCloudQaMetrics.NasaFullDiskSize);
            var midAaa = full.Crop(ClimateCloudQaMetrics.NasaFullDiskMidX, ClimateCloudQaMetrics.NasaFullDiskTopY,
                ClimateCloudQaMetrics.NasaFullDiskSize, ClimateCloudQaMetrics.NasaFullDiskSize);

            var m79 = ClimateCloudQaMetrics.AnalyzeFullDisk(left79, v.Id + "_79e6fa2", v.CheckStormReadability, 0.0, 0.21);
            var mAaa = ClimateCloudQaMetrics.AnalyzeFullDisk(midAaa, v.Id + "_aaa9625", v.CheckStormReadability, 0.0, 0.21);

            // 79e6fa2 compared to itself MUST pass 100%
            var selfVerdict = ClimateCloudQaMetrics.CompareAgainst79e6fa2(v.Id, m79, m79, v.CheckStormReadability);
            Check(selfVerdict.Passed, $"79e6fa2 self-baseline must pass for {v.Id}: " + string.Join("; ", selfVerdict.Failures));

            // Compare aaa9625 middle column against 79e6fa2 left column
            var aaaVerdict = ClimateCloudQaMetrics.CompareAgainst79e6fa2(v.Id, m79, mAaa, v.CheckStormReadability);
            if (!aaaVerdict.Passed) aaa9625FlaggedCount++;

            Console.WriteLine($"  [{v.Id}] 79e6fa2: clear={m79.ClearSkyFraction:F3} thin={m79.ThinVeilFraction:F3} span={m79.Top3MeanSpan:F3} speck={m79.SpeckleCount} sharp={m79.TransitionEdgeSharpness:F4}" +
                (v.CheckStormReadability ? $" stormProm={m79.StormProminence:F3}" : ""));
            Console.WriteLine($"     aaa9625: clear={mAaa.ClearSkyFraction:F3} thin={mAaa.ThinVeilFraction:F3} span={mAaa.Top3MeanSpan:F3} speck={mAaa.SpeckleCount} sharp={mAaa.TransitionEdgeSharpness:F4} => {(aaaVerdict.Passed ? "PASS" : "FLAGGED (" + aaaVerdict.Failures.Count + " diffs)")}");
            foreach (var f in aaaVerdict.Failures) Console.WriteLine("       DIFF: " + f);

            // Verify contact-sheet PNG composer round-trips cleanly on the first view
            if (v.Id == "01_pacific_epic_apr")
            {
                var sheet = ClimateCloudQaMetrics.ComposeBaselineSheet(left79, midAaa, aaaVerdict.Passed);
                var encoded = OrbisRasterCodec.RGBA8(sheet.Width, sheet.Height, sheet.Rgba);
                var decoded = OrbisRasterCodec.Read(encoded);
                Check(decoded.width == sheet.Width && decoded.height == sheet.Height, "Baseline sheet PNG round-trip failed");
                File.WriteAllBytes(Path.Combine(outDir, "sample_baseline_sheet_01_pacific.png"), encoded);

                var pairSheet = ClimateCloudQaMetrics.ComposePairSheet(left79, midAaa, true);
                var pEnc = OrbisRasterCodec.RGBA8(pairSheet.Width, pairSheet.Height, pairSheet.Rgba);
                var pDec = OrbisRasterCodec.Read(pEnc);
                Check(pDec.width == pairSheet.Width && pDec.height == pairSheet.Height, "Pair sheet PNG round-trip failed");
            }

            // Negative test on hurricane view: erasing the major storm MUST trigger zoomed-out storm readability FAIL!
            if (v.CheckStormReadability)
            {
                var erasedRgba = (byte[])left79.Rgba.Clone();
                int cx = left79.Width / 2, cy = (int)(left79.Height / 2 + 0.21 * 0.88 * left79.Height / 2);
                int rErase = (int)(0.18 * 0.88 * left79.Height / 2);
                for (int y = Math.Max(0, cy - rErase); y < Math.Min(left79.Height, cy + rErase); y++)
                for (int x = Math.Max(0, cx - rErase); x < Math.Min(left79.Width, cx + rErase); x++)
                {
                    int p = (y * left79.Width + x) * 4;
                    erasedRgba[p] = 74; erasedRgba[p + 1] = 152; erasedRgba[p + 2] = 198; // Clear ocean blue
                }
                var mErased = ClimateCloudQaMetrics.AnalyzeFullDisk(new RgbaImage(left79.Width, left79.Height, erasedRgba), "erased_storm", true, 0.0, 0.21);
                var vErased = ClimateCloudQaMetrics.CompareAgainst79e6fa2(v.Id, m79, mErased, true);
                Check(!vErased.Passed && vErased.Failures.Exists(s => s.Contains("zoomed-out storm readability")),
                    "Erased major storm MUST fail zoomed-out storm readability check");
                Console.WriteLine($"     PASS negative storm-loss test caught missing storm (prominence {m79.StormProminence:F3} -> {mErased.StormProminence:F3})");
            }

            md.AppendFormat(CultureInfo.InvariantCulture,
                "| `{0}` | {1} | `{2:F3} -> {3:F3}` | `{4:F3} -> {5:F3}` | `{6:F3} -> {7:F3}` | `{8} -> {9}` | `{10:F4} -> {11:F4}` | `{12}` | PASS | {13} |\n",
                v.Id, v.HemisphereGroup,
                m79.ClearSkyFraction, mAaa.ClearSkyFraction,
                m79.ThinVeilFraction, mAaa.ThinVeilFraction,
                m79.Top3MeanSpan, mAaa.Top3MeanSpan,
                m79.SpeckleCount, mAaa.SpeckleCount,
                m79.TransitionEdgeSharpness, mAaa.TransitionEdgeSharpness,
                v.CheckStormReadability ? $"{m79.StormProminence:F2}/{mAaa.StormProminence:F2}" : "-",
                aaaVerdict.Passed ? "PASS" : $"FLAGGED ({aaaVerdict.Failures.Count})");
        }

        // Verify that our 79e6fa2 visual baseline detector indeed detects the documented visual regression
        // in aaa9625 (Pacific over-clearing/fragmentation & shorter bands compared to user-preferred 79e6fa2).
        Check(aaa9625FlaggedCount >= 3,
            $"Expected 79e6fa2 baseline audit to flag aaa9625 fragmentation/over-clearing in >=3 views, got {aaa9625FlaggedCount}");
        Console.WriteLine($"  PASS 79e6fa2 baseline audit: 5/5 79e6fa2 self-pass, {aaa9625FlaggedCount}/5 aaa9625 visual regressions cleanly surfaced, storm-loss negative test caught!");
        md.AppendLine();
    }
}
