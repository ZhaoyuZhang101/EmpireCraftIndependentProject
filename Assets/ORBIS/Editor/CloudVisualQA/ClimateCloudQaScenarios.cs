using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Orbis.Flat;

namespace Orbis.Editor.CloudVisualQA
{
    public enum E2ECoverageLevel
    {
        FullEndToEnd,
        PartialWithExplicitNote
    }

    public sealed class LockedViewConfig
    {
        public double CenterLonDeg;
        public double CenterLatDeg;
        public float OrbitalDistance = 2.85f;
        public double SunLonDeg;
        public double SunLatDeg;
        public bool UseFixedSunVector = true;
        public double FixedVisualClock = 24.0;
        public float FixedExposure = 1.0f;
        public string NoiseSeed = "ORBIS-2D-2026";
        public double LockedSpinDay = 105.5;
    }

    public sealed class PairedScenarioSpec
    {
        public string Id;
        public string Title;
        public string PhysicalAxis;
        public string StateALabel;
        public string StateBLabel;
        public PairExpectationKind Expectation;
        public E2ECoverageLevel CoverageLevel;
        public string CoverageNote;
        public LockedViewConfig Camera;
        public GeoBox TargetRoi;
        public GeoBox SecondaryRoi;       // East flank for orographic dipole, or North/South band for migration
        public GeoBox ControlRoi;         // Unperturbed far-field / in-frame control region
        public bool HasSecondaryRoi;
        public int PrimaryForcingChannel; // 0=synLift, 1=itczConv, 2=stormTrack, 3=moistEnv, 4=marineSc, 5=orogCrest, 6=leeShadow
        public bool UseAuxForForcing;     // true -> auxRgba (marineSc/orog/lee), false -> forcingRgba
        public bool EvaluateConvectionTop;
        public WorldData WorldA;
        public WorldData WorldB;
        public FlatCycloneTrack NaturalTrack0;
        public FlatCycloneTrack NaturalTrack1;
        public bool HasNaturalCyclone;
    }

    public sealed class Nasa79e6fa2ViewSpec
    {
        public string Id;
        public string Title;
        public string HemisphereGroup; // "Pacific", "OtherHemisphere", "ZoomedOutStorm"
        public string ReferencePngRelPath;
        public double CalendarDay;
        public double CenterLonDeg;
        public double CenterLatDeg;
        public float OrbitalDistance = 2.85f;
        public double SunLonOffsetDeg = 16.0;
        public bool UseNaturalStormPeak;
        public bool CheckStormReadability;
    }

    public sealed class NaturalCyclonePairResult
    {
        public bool Found;
        public bool NorthernHemisphere;
        public double Day0, Day1;
        public FlatCycloneTrack Track0, Track1;
        public WorldData WorldT0, WorldT1;
    }

    public sealed class CoreForcingAuditSummary
    {
        public string ScenarioId;
        public E2ECoverageLevel CoverageLevel;
        public string CoverageNote;
        public bool Passed;
        public double ForcingMeanA, ForcingMeanB, ForcingDelta, ForcingCoherence, ForcingControlLeakage;
        public double ExtraMetricA, ExtraMetricB;
        public string ExtraMetricLabel;
        public double HadleyDegA, HadleyDegB, PolarDegA, PolarDegB, RossbyKA, RossbyKB;
        public double RegionalCoverMeanA, RegionalCoverMeanB;
        public readonly List<string> Notes = new List<string>();
        public readonly List<string> Failures = new List<string>();
    }

    /// <summary>
    /// Real-data-driven paired scenario builders and pure-C# forcing/circulation audits.
    /// Shared by both the Unity Editor real-machine acceptance entry point and the
    /// standalone CPU-only lightweight verification runner.
    /// </summary>
    public static class ClimateCloudQaScenarios
    {
        public const string DefaultSeed = "ORBIS-2D-2026";
        public const double DefaultVisualClock = 24.0; // Non-zero so wind advection & dual-phase LIC are active yet 100% locked
        public const float DefaultExposure = 1.0f;
        public const int ForcingGridWidth = 256;
        public const int ForcingGridHeight = 128;

        public static WorldData BuildRealEarth(int width, byte[] earthHeights, byte[] earthWater, byte[] earthRivers, string seed = DefaultSeed)
        {
            return FlatGenerator.Generate(width, width / 2, seed, TerrainPreset.Earth, default, earthHeights, earthWater, earthRivers);
        }

        /// <summary>
        /// Deep-clones a WorldData including the Moisture.Gleba, Moisture.ExoPlaSim, and
        /// internal Moisture.OceanCurrents arrays (FlatMoistureState.Clone shares those references).
        /// </summary>
        public static WorldData DeepCloneWorld(WorldData source)
        {
            var copy = source.Clone();
            if (copy.Moisture != null)
            {
                if (copy.Moisture.Gleba != null)
                {
                    var glebaClone = DeepCloneReferenceArrays(copy.Moisture.Gleba);
                    typeof(FlatMoistureState).GetProperty("Gleba", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?.SetValue(copy.Moisture, glebaClone);
                }
                if (copy.Moisture.ExoPlaSim != null)
                {
                    var exoClone = DeepCloneReferenceArrays(copy.Moisture.ExoPlaSim);
                    typeof(FlatMoistureState).GetProperty("ExoPlaSim", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?.SetValue(copy.Moisture, exoClone);
                }
                var ocProp = typeof(FlatMoistureState).GetProperty("OceanCurrents", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var oc = ocProp?.GetValue(copy.Moisture);
                if (oc != null)
                {
                    var ocClone = DeepCloneReferenceArrays(oc);
                    ocProp.SetValue(copy.Moisture, ocClone);
                }
            }
            return copy;
        }

        static object DeepCloneReferenceArrays(object obj)
        {
            if (obj == null) return null;
            var cloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
            object clone = cloneMethod.Invoke(obj, null);
            var fields = obj.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var f in fields)
            {
                if (!f.FieldType.IsArray) continue;
                var arr = f.GetValue(obj) as Array;
                if (arr != null) f.SetValue(clone, arr.Clone());
            }
            return clone;
        }

        /// <summary>
        /// Builds all 7 controlled real-data-driven paired scenarios (1 zero-drift lock control +
        /// 5 climate forcing / configuration pairs + 1 natural evolving cyclone pair).
        /// </summary>
        public static List<PairedScenarioSpec> BuildAllPairedScenarios(WorldData baseEarth)
        {
            var list = new List<PairedScenarioSpec>
            {
                Build00LockControl(baseEarth),
                Build01MoistWarmVsDryStable(baseEarth),
                Build02FlowReversalOrographic(baseEarth),
                Build03SstColdVsWarmMarineSc(baseEarth),
                Build04aSeasonJanVsJulItcz(baseEarth),
                Build04bRotation1xVs045xDoubleCell(baseEarth),
                Build05NaturalCycloneEvolution(baseEarth)
            };
            return list;
        }

        public static List<Nasa79e6fa2ViewSpec> Build79e6fa2BaselineViews()
        {
            return new List<Nasa79e6fa2ViewSpec>
            {
                new Nasa79e6fa2ViewSpec
                {
                    Id = "01_pacific_epic_apr",
                    Title = "Pacific Full-Disk (Apr 15 Equinox) — Long Bands, ITCZ & Natural Clear Gyres",
                    HemisphereGroup = "Pacific",
                    ReferencePngRelPath = "Documentation/CloudEarth80Showcase/NasaUpgrade3Col/compare_01_pacific_epic_apr.png",
                    CalendarDay = 105.823,
                    CenterLonDeg = -132.4,
                    CenterLatDeg = 17.0,
                    OrbitalDistance = 2.85f,
                    SunLonOffsetDeg = 16.0
                },
                new Nasa79e6fa2ViewSpec
                {
                    Id = "02_pacific_epic_jul",
                    Title = "West/Central Pacific Full-Disk (Jul 15) — SPCZ, Double ITCZ & Band Continuity",
                    HemisphereGroup = "Pacific",
                    ReferencePngRelPath = "Documentation/CloudEarth80Showcase/NasaUpgrade3Col/compare_02_pacific_epic_jul.png",
                    CalendarDay = 196.994,
                    CenterLonDeg = 166.0,
                    CenterLatDeg = 14.5,
                    OrbitalDistance = 2.85f,
                    SunLonOffsetDeg = 16.0
                },
                new Nasa79e6fa2ViewSpec
                {
                    Id = "03_africa_southernocean_oct",
                    Title = "Other Hemisphere: Africa / Indian / Southern Ocean (Oct 14) — Fronts & Sahara Clear Skies",
                    HemisphereGroup = "OtherHemisphere",
                    ReferencePngRelPath = "Documentation/CloudEarth80Showcase/NasaUpgrade3Col/compare_03_africa_southernocean_oct.png",
                    CalendarDay = 287.372,
                    CenterLonDeg = 30.2,
                    CenterLatDeg = -1.9,
                    OrbitalDistance = 2.85f,
                    SunLonOffsetDeg = 16.0
                },
                new Nasa79e6fa2ViewSpec
                {
                    Id = "04_south_atlantic_fronts_jan",
                    Title = "Other Hemisphere: South/North Atlantic Fronts (Jan 15) — Continuous Sweeping Fronts",
                    HemisphereGroup = "OtherHemisphere",
                    ReferencePngRelPath = "Documentation/CloudEarth80Showcase/NasaUpgrade3Col/compare_04_south_atlantic_fronts_jan.png",
                    CalendarDay = 15.548,
                    CenterLonDeg = -33.2,
                    CenterLatDeg = -24.0,
                    OrbitalDistance = 2.85f,
                    SunLonOffsetDeg = 16.0
                },
                new Nasa79e6fa2ViewSpec
                {
                    Id = "08_atlantic_hurricane_globe",
                    Title = "Zoomed-Out Major Storm Full-Disk — Natural Cyclone Readability from Orbit",
                    HemisphereGroup = "ZoomedOutStorm",
                    ReferencePngRelPath = "Documentation/CloudEarth80Showcase/NasaUpgrade3Col/compare_08_atlantic_hurricane_globe.png",
                    CalendarDay = 256.648,
                    CenterLonDeg = -75.3,
                    CenterLatDeg = 22.4,
                    OrbitalDistance = 2.85f,
                    SunLonOffsetDeg = 16.0,
                    UseNaturalStormPeak = true,
                    CheckStormReadability = true
                }
            };
        }

        static PairedScenarioSpec Build00LockControl(WorldData baseEarth)
        {
            var a = DeepCloneWorld(baseEarth);
            var b = DeepCloneWorld(baseEarth);
            a.Day = b.Day = 105.0;
            a.Cyclones = b.Cyclones = null;
            return new PairedScenarioSpec
            {
                Id = "00_lock_control",
                Title = "Zero-Drift Repeatability Control (Locked Camera, Sun, Exposure, Seed & Visual Clock)",
                PhysicalAxis = "None (identical inputs A == B)",
                StateALabel = "Locked Earth Apr 15 (Pass 1)",
                StateBLabel = "Locked Earth Apr 15 (Pass 2)",
                Expectation = PairExpectationKind.ZeroDriftControl,
                CoverageLevel = E2ECoverageLevel.FullEndToEnd,
                CoverageNote = "Full end-to-end lock check proving camera, sun, exposure, noise seed, and FlatSkyRenderer visual clock do not drift.",
                Camera = new LockedViewConfig
                {
                    CenterLonDeg = -132.4, CenterLatDeg = 17.0, OrbitalDistance = 2.85f,
                    SunLonDeg = -116.4, SunLatDeg = 10.0,
                    FixedVisualClock = DefaultVisualClock, FixedExposure = DefaultExposure, NoiseSeed = baseEarth.Seed
                },
                TargetRoi = new GeoBox(-170, -95, -20, 45),
                ControlRoi = new GeoBox(20, 80, -20, 20),
                PrimaryForcingChannel = 0,
                WorldA = a,
                WorldB = b
            };
        }

        static PairedScenarioSpec Build01MoistWarmVsDryStable(WorldData baseEarth)
        {
            var moist = DeepCloneWorld(baseEarth);
            var dry = DeepCloneWorld(baseEarth);
            moist.Day = dry.Day = 195.0; // Jul 15
            moist.Cyclones = dry.Cyclones = null;

            var perturbBox = new GeoBox(94, 138, -12, 14);

            // Tile-level precipitation & temperature
            for (int y = 0; y < moist.Height; y++)
            {
                double lat = ((y + 0.5) / moist.Height - 0.5) * 180.0;
                if (lat < perturbBox.SouthLat || lat > perturbBox.NorthLat) continue;
                for (int x = 0; x < moist.Width; x++)
                {
                    double lon = ((x + 0.5) / moist.Width - 0.5) * 360.0;
                    if (!perturbBox.Contains(lon, lat)) continue;
                    float w = EdgeWeight(lon, lat, perturbBox, 2.5);
                    int tile = y * moist.Width + x;
                    for (int m = 0; m < 12; m++)
                    {
                        int j = tile * 12 + m;
                        float baseRain = moist.Precipitation[j];
                        float baseTemp = moist.Temperature[j];
                        moist.Precipitation[j] = FMath.Lerp(baseRain, Math.Max(baseRain, 320f), w);
                        moist.Temperature[j] = baseTemp + 3.0f * w;
                        dry.Precipitation[j] = FMath.Lerp(baseRain, 4f, w);
                        dry.Temperature[j] = baseTemp - 2.0f * w;
                    }
                }
            }

            // Moisture humidity, rain & condensation (wind East/North stays 100% untouched!)
            var mA = moist.Moisture;
            var mB = dry.Moisture;
            for (int y = 0; y < mA.Height; y++)
            {
                double lat = ((y + 0.5) / mA.Height - 0.5) * 180.0;
                if (lat < perturbBox.SouthLat || lat > perturbBox.NorthLat) continue;
                for (int x = 0; x < mA.Width; x++)
                {
                    double lon = ((x + 0.5) / mA.Width - 0.5) * 360.0;
                    if (!perturbBox.Contains(lon, lat)) continue;
                    float w = EdgeWeight(lon, lat, perturbBox, 2.5);
                    int cell = y * mA.Width + x;
                    for (int m = 0; m < 12; m++)
                    {
                        int j = cell * 12 + m;
                        mA.Humidity[j] = FMath.Lerp(mA.Humidity[j], Math.Max(mA.Humidity[j], 0.90f), w);
                        mA.Rain[j] = FMath.Lerp(mA.Rain[j], Math.Max(mA.Rain[j], 320f), w);
                        mA.Condensation[j] = mA.Rain[j] / 30f;

                        mB.Humidity[j] = FMath.Lerp(mB.Humidity[j], 0.22f, w);
                        mB.Rain[j] = FMath.Lerp(mB.Rain[j], 4f, w);
                        mB.Condensation[j] = mB.Rain[j] / 30f;
                    }
                }
            }

            // Thermodynamic stability & subsidence (Gleba PressureHpa/RainfallImpact + ExoPlaSim CAPE/CIN/Soil)
            if (mA.Gleba != null && mB.Gleba != null)
            {
                int gw = mA.Gleba.Width, gh = mA.Gleba.Height;
                for (int y = 0; y < gh; y++)
                {
                    double lat = ((y + 0.5) / gh - 0.5) * 180.0;
                    if (lat < perturbBox.SouthLat || lat > perturbBox.NorthLat) continue;
                    for (int x = 0; x < gw; x++)
                    {
                        double lon = ((x + 0.5) / gw - 0.5) * 360.0;
                        if (!perturbBox.Contains(lon, lat)) continue;
                        float w = EdgeWeight(lon, lat, perturbBox, 2.5);
                        int cell = y * gw + x;
                        for (int m = 0; m < 12; m++)
                        {
                            int j = cell * 12 + m;
                            mA.Gleba.PressureHpa[j] = FMath.Lerp(mA.Gleba.PressureHpa[j], Math.Min(mA.Gleba.PressureHpa[j], 1002f), w);
                            mA.Gleba.RainfallImpact[j] = FMath.Lerp(mA.Gleba.RainfallImpact[j], Math.Max(mA.Gleba.RainfallImpact[j], 0.45f), w);
                            mB.Gleba.PressureHpa[j] = FMath.Lerp(mB.Gleba.PressureHpa[j], Math.Max(mB.Gleba.PressureHpa[j], 1024f), w);
                            mB.Gleba.RainfallImpact[j] = FMath.Lerp(mB.Gleba.RainfallImpact[j], Math.Min(mB.Gleba.RainfallImpact[j], -0.55f), w);
                        }
                    }
                }
            }

            if (mA.ExoPlaSim != null && mB.ExoPlaSim != null)
            {
                int ew = mA.ExoPlaSim.Width, eh = mA.ExoPlaSim.Height;
                for (int y = 0; y < eh; y++)
                {
                    double lat = ((y + 0.5) / eh - 0.5) * 180.0;
                    if (lat < perturbBox.SouthLat || lat > perturbBox.NorthLat) continue;
                    for (int x = 0; x < ew; x++)
                    {
                        double lon = ((x + 0.5) / ew - 0.5) * 360.0;
                        if (!perturbBox.Contains(lon, lat)) continue;
                        float w = EdgeWeight(lon, lat, perturbBox, 2.5);
                        int cell = y * ew + x;
                        for (int m = 0; m < 12; m++)
                        {
                            int j = cell * 12 + m;
                            mA.ExoPlaSim.CapeJkg[j] = FMath.Lerp(mA.ExoPlaSim.CapeJkg[j], Math.Max(mA.ExoPlaSim.CapeJkg[j], 2200f), w);
                            mA.ExoPlaSim.EntropyDeficit[j] = FMath.Lerp(mA.ExoPlaSim.EntropyDeficit[j], 0.16f, w);
                            mA.ExoPlaSim.DeepSoil[j] = FMath.Lerp(mA.ExoPlaSim.DeepSoil[j], Math.Max(mA.ExoPlaSim.DeepSoil[j], 0.85f), w);

                            mB.ExoPlaSim.CapeJkg[j] = FMath.Lerp(mB.ExoPlaSim.CapeJkg[j], 30f, w);
                            mB.ExoPlaSim.EntropyDeficit[j] = FMath.Lerp(mB.ExoPlaSim.EntropyDeficit[j], 0.94f, w);
                            mB.ExoPlaSim.DeepSoil[j] = FMath.Lerp(mB.ExoPlaSim.DeepSoil[j], 0.04f, w);
                            mB.ExoPlaSim.ShallowSoil[j] = FMath.Lerp(mB.ExoPlaSim.ShallowSoil[j], 0.04f, w);
                        }
                    }
                }
            }

            moist.TerrainRevision++;
            dry.TerrainRevision++;

            return new PairedScenarioSpec
            {
                Id = "01_moist_warm_vs_dry_stable",
                Title = "Moist/Warm Unstable vs Dry/Stable Subsidence — Cloud Cover & Convective Cloud Top",
                PhysicalAxis = "Moisture + Convective Instability (RH, Rain, T, SLP, CAPE/CIN); wind, terrain, SST & circulation locked",
                StateALabel = "Moist/Warm Unstable (High RH/CAPE, Low SLP)",
                StateBLabel = "Dry/Stable Subsidence (Low RH/CAPE, High SLP/CIN)",
                Expectation = PairExpectationKind.MonotonicDecrease, // Dry/Stable B < Moist/Warm A
                CoverageLevel = E2ECoverageLevel.FullEndToEnd,
                CoverageNote = "Full end-to-end through FlatSkyRenderer (ClimateTex + GlebaForcing + RegionalCloudField -> FlatSkyBake -> HPCloudColumn -> Volume Clouds). Note: FlatSkyBake uses moisture/CAPE/CIN/SLP for cover & convective cloud-top tier (weather.b -> OrbisCloudVerticalSpan), while Temperature gates thermal convection in BuildCloudSynopticFields and rain/snow freezing in HPCloudColumn.",
                Camera = new LockedViewConfig
                {
                    CenterLonDeg = 122.0, CenterLatDeg = 2.0, OrbitalDistance = 1.90f,
                    SunLonDeg = 138.0, SunLatDeg = 10.0,
                    FixedVisualClock = DefaultVisualClock, FixedExposure = DefaultExposure, NoiseSeed = baseEarth.Seed
                },
                TargetRoi = new GeoBox(98, 134, -9, 11),
                ControlRoi = new GeoBox(146, 166, -12, 12), // In-viewport unperturbed western Pacific control
                PrimaryForcingChannel = 1, // itczConv (also verifies synLift ch 0)
                UseAuxForForcing = false,
                EvaluateConvectionTop = true,
                WorldA = moist,
                WorldB = dry
            };
        }

        static PairedScenarioSpec Build02FlowReversalOrographic(WorldData baseEarth)
        {
            var westerly = DeepCloneWorld(baseEarth);
            var easterly = DeepCloneWorld(baseEarth);
            westerly.Day = easterly.Day = 195.0; // Jul 15
            westerly.Cyclones = easterly.Cyclones = null;

            var flowBox = new GeoBox(-88, -54, -52, -14);
            var mA = westerly.Moisture;
            var mB = easterly.Moisture;
            for (int y = 0; y < mA.Height; y++)
            {
                double lat = ((y + 0.5) / mA.Height - 0.5) * 180.0;
                if (lat < flowBox.SouthLat || lat > flowBox.NorthLat) continue;
                for (int x = 0; x < mA.Width; x++)
                {
                    double lon = ((x + 0.5) / mA.Width - 0.5) * 360.0;
                    if (!flowBox.Contains(lon, lat)) continue;
                    float w = EdgeWeight(lon, lat, flowBox, 2.5);
                    int cell = y * mA.Width + x;
                    for (int m = 0; m < 12; m++)
                    {
                        int j = cell * 12 + m;
                        mA.East[j] = FMath.Lerp(mA.East[j], +0.95f, w);
                        mA.North[j] = FMath.Lerp(mA.North[j], 0.0f, w);
                        mB.East[j] = FMath.Lerp(mB.East[j], -0.95f, w);
                        mB.North[j] = FMath.Lerp(mB.North[j], 0.0f, w);
                    }
                }
            }
            westerly.TerrainRevision++;
            easterly.TerrainRevision++;

            return new PairedScenarioSpec
            {
                Id = "02_flow_reversal_orographic_andes",
                Title = "Actual Flow Field Reversal across Andes — Windward/Leeward Cloud Dipole",
                PhysicalAxis = "Surface wind flow vector ONLY (Moisture.East: +0.95 Westerly vs -0.95 Easterly)",
                StateALabel = "Westerly Flow (+E: West Flank Windward, East Flank Leeward)",
                StateBLabel = "Easterly Flow (-E: East Flank Windward, West Flank Leeward)",
                Expectation = PairExpectationKind.OrographicDipole,
                CoverageLevel = E2ECoverageLevel.PartialWithExplicitNote,
                CoverageNote = "PARTIAL E2E IN aaa9625: Mutating Moisture.East/North directly flips wind-slope orogCrest (aux.g), dynamic leeDownslope inside leeShadow (aux.b), _ClimateWindTex advection/LIC combing, and _ScrollCloudWindTex 3D drift through FlatSkyRenderer. GAPS NOT YET DYNAMICALLY RE-COUPLED IN aaa9625: (1) Gleba.RainShadowiness and WorldData.Precipitation remain pre-baked climatology unless FlatMoisture re-solves; (2) GlebaGeographySupplement.BuildCloudSynopticFields line 3298 overwrites the windConv-coupled synopticLift computed on line 3168; (3) _GlebaUpperWindTex (GlebaRossbyWaveField) depends only on world.Seed/calendarDay, not Moisture.East/North.",
                Camera = new LockedViewConfig
                {
                    CenterLonDeg = -69.5, CenterLatDeg = -32.0, OrbitalDistance = 1.75f,
                    SunLonDeg = -53.5, SunLatDeg = -18.0,
                    FixedVisualClock = DefaultVisualClock, FixedExposure = DefaultExposure, NoiseSeed = baseEarth.Seed
                },
                TargetRoi = new GeoBox(-76, -70, -44, -22),    // West flank (windward under Westerly A)
                SecondaryRoi = new GeoBox(-69, -63, -44, -22), // East flank (windward under Easterly B)
                HasSecondaryRoi = true,
                ControlRoi = new GeoBox(-48, -36, -42, -22),   // In-viewport unperturbed South Atlantic
                PrimaryForcingChannel = 1, // aux.g = orogCrest
                UseAuxForForcing = true,
                WorldA = westerly,
                WorldB = easterly
            };
        }

        static PairedScenarioSpec Build03SstColdVsWarmMarineSc(WorldData baseEarth)
        {
            var cold = DeepCloneWorld(baseEarth);
            var warm = DeepCloneWorld(baseEarth);
            cold.Day = warm.Day = 285.0; // Oct 12 (matches NasaUpgrade3Col compare_05_peru_marine_sc_local)
            cold.Cyclones = warm.Cyclones = null;

            var sstBox = new GeoBox(-104, -71, -33, -7);

            // Apply single coherent SST anomaly (-2.6 C cold upwelling vs +2.6 C warm anomaly)
            // to both internal OceanCurrents.SeaAnomaly and ocean-tile surface Temperature.
            ApplySstAnomaly(cold, sstBox, -2.6f);
            ApplySstAnomaly(warm, sstBox, +2.6f);
            cold.TerrainRevision++;
            warm.TerrainRevision++;

            return new PairedScenarioSpec
            {
                Id = "03_sst_cold_vs_warm_marine_sc",
                Title = "Cold Upwelling vs Warm SST (Peru/Humboldt) — Marine Stratocumulus Deck & Cloud-Top Tier",
                PhysicalAxis = "Ocean SST anomaly ONLY (SeaAnomaly & ocean T: -2.6 C Cold Upwelling vs +2.6 C Warm)",
                StateALabel = "Cold Humboldt Upwelling SST (-2.6 C -> Low Marine Sc Deck)",
                StateBLabel = "Warm Subtropical SST (+2.6 C -> Marine Sc Suppressed)",
                Expectation = PairExpectationKind.MonotonicDecrease, // Marine Sc cover drops in Warm B < Cold A
                CoverageLevel = E2ECoverageLevel.PartialWithExplicitNote,
                CoverageNote = "PARTIAL E2E IN aaa9625: Cold vs Warm OceanCurrents.SeaAnomaly drives marineSc (aux.r: ~0.59 -> ~0.14), scProtect/scDeckDirect, and low-altitude inversion tier (weather.b ~0.06..0.18 -> 3D Voronoi marine Sc) end-to-end through FlatSkyRenderer. WIRING BOUNDARY NOTE: In aaa9625, FlatMoistureState.OceanCurrents is internal to Orbis.Flat.Core and coolUpwelling reads SeaAnomaly directly; modifying WorldData.Temperature alone without OceanCurrents.SeaAnomaly will NOT trigger the marine Sc response.",
                Camera = new LockedViewConfig
                {
                    CenterLonDeg = -89.0, CenterLatDeg = -20.5, OrbitalDistance = 1.85f,
                    SunLonDeg = -73.0, SunLatDeg = -10.0,
                    FixedVisualClock = DefaultVisualClock, FixedExposure = DefaultExposure, NoiseSeed = baseEarth.Seed
                },
                TargetRoi = new GeoBox(-95, -75, -28, -12),
                ControlRoi = new GeoBox(-122, -108, -28, -12), // In-viewport unperturbed South Pacific west of SST box
                PrimaryForcingChannel = 0, // aux.r = marineSc
                UseAuxForForcing = true,
                EvaluateConvectionTop = true,
                WorldA = cold,
                WorldB = warm
            };
        }

        static void ApplySstAnomaly(WorldData world, GeoBox box, float targetAnomalyC)
        {
            var ocProp = typeof(FlatMoistureState).GetProperty("OceanCurrents", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var oc = ocProp?.GetValue(world.Moisture);
            if (oc != null)
            {
                var ocType = oc.GetType();
                int ocW = (int)ocType.GetField("Width", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(oc);
                int ocH = (int)ocType.GetField("Height", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(oc);
                var sea = (float[])ocType.GetField("SeaAnomaly", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(oc);
                for (int y = 0; y < ocH; y++)
                {
                    double lat = ((y + 0.5) / ocH - 0.5) * 180.0;
                    if (lat < box.SouthLat || lat > box.NorthLat) continue;
                    for (int x = 0; x < ocW; x++)
                    {
                        double lon = ((x + 0.5) / ocW - 0.5) * 360.0;
                        if (!box.Contains(lon, lat)) continue;
                        float w = EdgeWeight(lon, lat, box, 2.5);
                        int idx = y * ocW + x;
                        sea[idx] = FMath.Lerp(sea[idx], targetAnomalyC, w);
                    }
                }
            }
            for (int y = 0; y < world.Height; y++)
            {
                double lat = ((y + 0.5) / world.Height - 0.5) * 180.0;
                if (lat < box.SouthLat || lat > box.NorthLat) continue;
                for (int x = 0; x < world.Width; x++)
                {
                    double lon = ((x + 0.5) / world.Width - 0.5) * 360.0;
                    if (!box.Contains(lon, lat)) continue;
                    int tile = y * world.Width + x;
                    if (!FlatTerrainAnalysis.Ocean(world.Water[tile])) continue;
                    float w = EdgeWeight(lon, lat, box, 2.5);
                    for (int m = 0; m < 12; m++)
                        world.Temperature[tile * 12 + m] += targetAnomalyC * w;
                }
            }
        }

        static PairedScenarioSpec Build04aSeasonJanVsJulItcz(WorldData baseEarth)
        {
            var jan = DeepCloneWorld(baseEarth);
            var jul = DeepCloneWorld(baseEarth);
            jan.Day = 15.0;  // Jan 15 (Boreal winter / Austral summer)
            jul.Day = 195.0; // Jul 15 (Boreal summer / Austral winter)
            jan.Cyclones = jul.Cyclones = null;

            return new PairedScenarioSpec
            {
                Id = "04a_season_jan_vs_jul_itcz",
                Title = "Same Region Seasonal Migration (Africa–Indian Ocean, Jan 15 vs Jul 15) — ITCZ & Monsoon Shift",
                PhysicalAxis = "Calendar season ONLY (Day 15 Jan vs Day 195 Jul); camera, planet-local sun, exposure, seed & visual clock locked",
                StateALabel = "January 15 (Austral Summer ITCZ South)",
                StateBLabel = "July 15 (Boreal Summer ITCZ / Monsoon North)",
                Expectation = PairExpectationKind.NorthwardMigration,
                CoverageLevel = E2ECoverageLevel.FullEndToEnd,
                CoverageNote = "Full end-to-end real Earth climate snapshot pair (zero synthetic edits): tests seasonal ITCZ & monsoon migration across FlatMonthlyForcing, Gleba bands, ExoPlaSim CAPE, FlatRegionalCloudField, FlatSkyBake, HPCloudColumn, and volume rendering at locked equatorial sun.",
                Camera = new LockedViewConfig
                {
                    CenterLonDeg = 42.0, CenterLatDeg = 2.0, OrbitalDistance = 2.85f,
                    SunLonDeg = 58.0, SunLatDeg = 0.0, // Equatorial sun locked identically for both Jan and Jul
                    FixedVisualClock = DefaultVisualClock, FixedExposure = DefaultExposure, NoiseSeed = baseEarth.Seed
                },
                TargetRoi = new GeoBox(0, 100, -34, 34),
                SecondaryRoi = new GeoBox(0, 100, 8, 28), // Northern monsoon band (brightens in Jul)
                HasSecondaryRoi = true,
                ControlRoi = new GeoBox(-170, -150, -70, -60),
                PrimaryForcingChannel = 1, // itczConv
                UseAuxForForcing = false,
                WorldA = jan,
                WorldB = jul
            };
        }

        static PairedScenarioSpec Build04bRotation1xVs045xDoubleCell(WorldData baseEarth)
        {
            var rot1x = DeepCloneWorld(baseEarth);
            var rot045x = baseEarth.CloneForTerrainRebuild();
            var slowCfg = ExoPlaSimPlanetConfig.CreatePreset(ExoPlaSimPreset.EarthModern);
            slowCfg.RotationRateRatio = 0.45f; // ONLY rotation rate changed (1.0x ThreeCell -> 0.45x DoubleCell)
            rot045x.PlanetClimate = slowCfg;
            FlatGenerator.Rebuild(rot045x, default);

            rot1x.Day = rot045x.Day = 105.0; // Apr 15 equinox
            rot1x.Cyclones = rot045x.Cyclones = null;

            return new PairedScenarioSpec
            {
                Id = "04b_rotation_1x_vs_045x_doublecell",
                Title = "Planetary Rotation Rate (1.0x ThreeCell vs 0.45x DoubleCell) — Hadley & Convective Belt Expansion",
                PhysicalAxis = "PlanetClimate.RotationRateRatio ONLY (1.0x -> 0.45x via full FlatGenerator.Rebuild; obliquity/CO2/solar constant locked)",
                StateALabel = "1.0x Earth Rotation (ThreeCell: Hadley 30 deg, Rossby k=2.6)",
                StateBLabel = "0.45x Slow Rotator (DoubleCell: Hadley 42 deg, Rossby k=1.6)",
                Expectation = PairExpectationKind.PolewardExpansion,
                CoverageLevel = E2ECoverageLevel.PartialWithExplicitNote,
                CoverageNote = "PARTIAL E2E IN aaa9625: FlatWindBelts, FlatClimate, FlatMoisture, ExoPlaSimSupplement, GetCloudCirculationParams (Hadley 30->42 deg, Polar 65->84.2 deg, Rossby k 2.6->1.6, Regime 0->1), and BuildCloudSynopticFields tropical Hadley/ITCZ poleward expansion ARE wired end-to-end. GAP IN aaa9625: GlebaClimateSupplement.Build does not yet scale SthzNorth/SouthLatRad or PolarNorth/SouthLatRad with RotationRateRatio, and FlatSkyBake.shader reads polarLatRad from aux.a rather than _GlebaCirculationParams.y, so subpolar storm-track shift to ~84 deg is only partially coupled.",
                Camera = new LockedViewConfig
                {
                    CenterLonDeg = -132.4, CenterLatDeg = 17.0, OrbitalDistance = 2.85f,
                    SunLonDeg = -116.4, SunLatDeg = 10.0,
                    FixedVisualClock = DefaultVisualClock, FixedExposure = DefaultExposure, NoiseSeed = baseEarth.Seed
                },
                TargetRoi = new GeoBox(-180, 180, -45, 45),
                SecondaryRoi = new GeoBox(-180, 180, 12, 26), // Subtropical Hadley expansion band
                HasSecondaryRoi = true,
                ControlRoi = new GeoBox(-180, 180, -88, -80),
                PrimaryForcingChannel = 1, // itczConv
                UseAuxForForcing = false,
                WorldA = rot1x,
                WorldB = rot045x
            };
        }

        /// <summary>
        /// Steps the real natural FlatSimulation forward on <paramref name="baseEarth"/> to find
        /// the strongest naturally-formed mature tropical cyclone and captures two moments (t0, t1)
        /// of that exact same tracked storm with significant translation and phase rotation.
        /// </summary>
        public static NaturalCyclonePairResult FindNaturalCyclonePair(WorldData baseEarth, int maxDays = 360)
        {
            var simWorld = DeepCloneWorld(baseEarth);
            simWorld.Day = 0;
            simWorld.Cyclones = FlatCycloneWeather.SpinUp(simWorld);
            var sim = new FlatSimulation(simWorld);

            var history = new List<(double day, FlatCycloneTrack north, FlatCycloneTrack south)>(maxDays * 4 + 4);
            double bestWind = 0;
            int bestIdx = -1;
            bool bestIsNorth = true;

            for (int step = 0; step < maxDays * 4; step++)
            {
                sim.Step(0.25f);
                var n = simWorld.Cyclones.North;
                var s = simWorld.Cyclones.South;
                history.Add((simWorld.Day, n, s));
                if (n.Tracked && n.WindSpeed > bestWind && n.AgeDays >= 1.5)
                {
                    bestWind = n.WindSpeed; bestIdx = step; bestIsNorth = true;
                }
                if (s.Tracked && s.WindSpeed > bestWind && s.AgeDays >= 1.5)
                {
                    bestWind = s.WindSpeed; bestIdx = step; bestIsNorth = false;
                }
            }

            if (bestIdx < 0)
                return new NaturalCyclonePairResult { Found = false };

            FlatCycloneTrack TrackAt(int i) => bestIsNorth ? history[i].north : history[i].south;
            var peak = TrackAt(bestIdx);
            double peakDay = history[bestIdx].day;

            // Walk backward along the EXACT same storm track (continuous AgeDays) to find an earlier
            // organized moment t0 with >= 2.0 deg displacement and >= 0.45 rad phase advance.
            int t0Idx = -1;
            for (int back = 4; back <= 12 && bestIdx - back >= 0; back++)
            {
                int idx = bestIdx - back;
                var earlier = TrackAt(idx);
                if (!earlier.Tracked) break;
                double dt = peakDay - history[idx].day;
                if (Math.Abs((peak.AgeDays - earlier.AgeDays) - dt) > 0.15) break; // different storm
                if (earlier.Strength < 0.30f) continue;
                double disp = ClimateCloudQaMetrics.SphericalDistanceDeg(
                    (earlier.U - 0.5) * 360.0, (earlier.V - 0.5) * 180.0,
                    (peak.U - 0.5) * 360.0, (peak.V - 0.5) * 180.0);
                double dPhase = Math.Abs(peak.Phase - earlier.Phase);
                if (disp >= 2.0 && dPhase >= 0.45)
                {
                    t0Idx = idx;
                    if (back >= 6 && disp >= 3.0) break;
                }
            }
            if (t0Idx < 0)
            {
                // Fallback: pick earliest continuous tracked point >= 0.25 strength within 2.5 days
                for (int back = 4; back <= 10 && bestIdx - back >= 0; back++)
                {
                    var earlier = TrackAt(bestIdx - back);
                    double dt = peakDay - history[bestIdx - back].day;
                    if (earlier.Tracked && Math.Abs((peak.AgeDays - earlier.AgeDays) - dt) <= 0.15 && earlier.Strength >= 0.25f)
                    { t0Idx = bestIdx - back; break; }
                }
            }
            if (t0Idx < 0) t0Idx = Math.Max(0, bestIdx - 4);

            double day0 = history[t0Idx].day;
            double day1 = peakDay;

            // Replay cleanly to exact day0 and day1 so WorldData.Cyclones has authoritative Previous/Current state!
            var w0 = DeepCloneWorld(baseEarth);
            w0.Day = 0;
            w0.Cyclones = FlatCycloneWeather.SpinUp(w0);
            var s0 = new FlatSimulation(w0);
            int steps0 = (int)Math.Round(day0 * 4.0);
            for (int k = 0; k < steps0; k++) s0.Step(0.25f);

            var w1 = DeepCloneWorld(w0);
            var s1 = new FlatSimulation(w1);
            int deltaSteps = (int)Math.Round((day1 - day0) * 4.0);
            for (int k = 0; k < deltaSteps; k++) s1.Step(0.25f);

            var tr0 = bestIsNorth ? w0.Cyclones.North : w0.Cyclones.South;
            var tr1 = bestIsNorth ? w1.Cyclones.North : w1.Cyclones.South;

            // Keep only the tracked hemisphere's storm active in this pair so an unrelated
            // opposite-hemisphere disturbance cannot confound far-field background metrics.
            if (bestIsNorth)
            {
                w0.Cyclones.South = w0.Cyclones.PreviousSouth = default;
                w1.Cyclones.South = w1.Cyclones.PreviousSouth = default;
            }
            else
            {
                w0.Cyclones.North = w0.Cyclones.PreviousNorth = default;
                w1.Cyclones.North = w1.Cyclones.PreviousNorth = default;
            }

            return new NaturalCyclonePairResult
            {
                Found = true,
                NorthernHemisphere = bestIsNorth,
                Day0 = w0.Day,
                Day1 = w1.Day,
                Track0 = tr0,
                Track1 = tr1,
                WorldT0 = w0,
                WorldT1 = w1
            };
        }

        static PairedScenarioSpec Build05NaturalCycloneEvolution(WorldData baseEarth)
        {
            var found = FindNaturalCyclonePair(baseEarth);
            if (!found.Found)
                throw new InvalidOperationException("Natural FlatSimulation produced no tropical cyclone over 360 days on Earth.");

            double lon0 = (found.Track0.U - 0.5) * 360.0, lat0 = (found.Track0.V - 0.5) * 180.0;
            double lon1 = (found.Track1.U - 0.5) * 360.0, lat1 = (found.Track1.V - 0.5) * 180.0;
            double dLon = ClimateCloudQaMetrics.WrapDeltaDeg(lon1 - lon0);
            double midLon = ClimateCloudQaMetrics.WrapDeltaDeg(lon0 + 0.5 * dLon);
            double midLat = 0.5 * (lat0 + lat1);
            double ctrlLon = ClimateCloudQaMetrics.WrapDeltaDeg(midLon + 120.0);

            return new PairedScenarioSpec
            {
                Id = "05_natural_cyclone_t0_vs_t1",
                Title = string.Format(CultureInfo.InvariantCulture,
                    "Same Natural Cyclone Evolution (Day {0:F2} -> Day {1:F2}, {2}) — Tracked Position & Spiral Chirality",
                    found.Day0, found.Day1, found.NorthernHemisphere ? "NH CCW" : "SH CW"),
                PhysicalAxis = "Natural FlatSimulation cyclone time-step ONLY (t0 -> t1 on same tracked storm; camera, planet-local sun, exposure, seed & visual clock locked)",
                StateALabel = string.Format(CultureInfo.InvariantCulture,
                    "Earlier Moment t0 (Day {0:F2}, {1:F1} m/s, lon={2:F1}, lat={3:F1}, phase={4:F2})",
                    found.Day0, found.Track0.WindSpeed, lon0, lat0, found.Track0.Phase),
                StateBLabel = string.Format(CultureInfo.InvariantCulture,
                    "Peak Moment t1 (Day {0:F2}, {1:F1} m/s, lon={2:F1}, lat={3:F1}, phase={4:F2})",
                    found.Day1, found.Track1.WindSpeed, lon1, lat1, found.Track1.Phase),
                Expectation = PairExpectationKind.CycloneEvolution,
                CoverageLevel = E2ECoverageLevel.FullEndToEnd,
                CoverageNote = "Full end-to-end real natural FlatCycloneWeather evolution through FlatSkyRenderer (StageCyclones -> FlatSkyBake Cyclone() -> HPCloudColumn -> ScrollCloudSource ApplyTyphoon3DStadium). Verifies position migration, eyewall contrast, hemispheric logarithmic spiral chirality, signed rotation tracking, and far-field stability.",
                Camera = new LockedViewConfig
                {
                    CenterLonDeg = midLon, CenterLatDeg = midLat, OrbitalDistance = 1.95f,
                    SunLonDeg = ClimateCloudQaMetrics.WrapDeltaDeg(midLon + 16.0), SunLatDeg = midLat * 0.7,
                    FixedVisualClock = DefaultVisualClock, FixedExposure = DefaultExposure, NoiseSeed = baseEarth.Seed
                },
                TargetRoi = new GeoBox(
                    ClimateCloudQaMetrics.WrapDeltaDeg(midLon - 18.0),
                    ClimateCloudQaMetrics.WrapDeltaDeg(midLon + 18.0),
                    Math.Max(-75.0, midLat - 15.0),
                    Math.Min(75.0, midLat + 15.0)),
                ControlRoi = new GeoBox(
                    ClimateCloudQaMetrics.WrapDeltaDeg(ctrlLon - 15.0),
                    ClimateCloudQaMetrics.WrapDeltaDeg(ctrlLon + 15.0),
                    -20.0, 20.0),
                PrimaryForcingChannel = 0,
                WorldA = found.WorldT0,
                WorldB = found.WorldT1,
                NaturalTrack0 = found.Track0,
                NaturalTrack1 = found.Track1,
                HasNaturalCyclone = true
            };
        }

        /// <summary>
        /// Builds the exact Gleba synoptic forcing & auxiliary fields that FlatSkyRenderer.UploadClimate uploads,
        /// as Float4Grids for Layer-1 physical-input binding and standalone CPU-only validation.
        /// </summary>
        public static void BuildForcingGrids(WorldData world, out Float4Grid forcing, out Float4Grid aux)
        {
            var season = new FlatMonthlyForcing(world.Day);
            var fBytes = new byte[ForcingGridWidth * ForcingGridHeight * 4];
            var aBytes = new byte[ForcingGridWidth * ForcingGridHeight * 4];
            GlebaGeographySupplement.BuildCloudSynopticFields(world, season.SegmentStartDay, ForcingGridWidth, ForcingGridHeight, fBytes, aBytes);
            forcing = Float4Grid.FromRgbaBytes(ForcingGridWidth, ForcingGridHeight, fBytes);
            aux = Float4Grid.FromRgbaBytes(ForcingGridWidth, ForcingGridHeight, aBytes);
        }

        /// <summary>
        /// Pure-C# Layer-1 core forcing & regional-statistics audit of a paired scenario.
        /// Runs without Unity or GPU; validates that the physical inputs themselves produce the
        /// exact expected signal and localization before GPU rendering.
        /// </summary>
        public static CoreForcingAuditSummary AuditCoreForcing(PairedScenarioSpec spec)
        {
            var summary = new CoreForcingAuditSummary
            {
                ScenarioId = spec.Id,
                CoverageLevel = spec.CoverageLevel,
                CoverageNote = spec.CoverageNote
            };

            BuildForcingGrids(spec.WorldA, out var fA, out var aA);
            BuildForcingGrids(spec.WorldB, out var fB, out var aB);
            var gA = spec.UseAuxForForcing ? aA : fA;
            var gB = spec.UseAuxForForcing ? aB : fB;

            var delta = ClimateCloudQaMetrics.ComputeFieldDelta(gA, gB, spec.PrimaryForcingChannel, spec.TargetRoi, spec.ControlRoi);
            summary.ForcingMeanA = delta.MeanA;
            summary.ForcingMeanB = delta.MeanB;
            summary.ForcingDelta = delta.SignedDelta;
            summary.ForcingCoherence = delta.DirectionalCoherence;
            summary.ForcingControlLeakage = delta.ControlLeakageRatio;

            GlebaGeographySupplement.GetCloudCirculationParams(spec.WorldA, out float hA, out float pA, out float kA, out _);
            GlebaGeographySupplement.GetCloudCirculationParams(spec.WorldB, out float hB, out float pB, out float kB, out _);
            summary.HadleyDegA = hA * 57.29578; summary.HadleyDegB = hB * 57.29578;
            summary.PolarDegA = pA * 57.29578; summary.PolarDegB = pB * 57.29578;
            summary.RossbyKA = kA; summary.RossbyKB = kB;

            // Also evaluate FlatClimateStatistics -> RegionalCloudResponse for both states
            summary.RegionalCoverMeanA = ComputeRegionalResponseMean(spec.WorldA, spec.TargetRoi);
            summary.RegionalCoverMeanB = ComputeRegionalResponseMean(spec.WorldB, spec.TargetRoi);

            void Check(bool ok, string msg)
            {
                summary.Notes.Add((ok ? "PASS " : "FAIL ") + msg);
                if (!ok) summary.Failures.Add(msg);
            }

            switch (spec.Expectation)
            {
                case PairExpectationKind.ZeroDriftControl:
                    Check(delta.MeanAbsDelta < 1e-6, $"zero forcing drift meanAbs={delta.MeanAbsDelta:G4}");
                    break;

                case PairExpectationKind.MonotonicDecrease:
                    var liftDelta = ClimateCloudQaMetrics.ComputeFieldDelta(fA, fB, 0, spec.TargetRoi, spec.ControlRoi);
                    summary.ExtraMetricLabel = "synLift";
                    summary.ExtraMetricA = liftDelta.MeanA;
                    summary.ExtraMetricB = liftDelta.MeanB;
                    Check(delta.SignedDelta < -0.15, $"primary forcing decreases strongly: {delta.MeanA:F3} -> {delta.MeanB:F3} (d={delta.SignedDelta:+0.000;-0.000})");
                    Check(delta.DirectionalCoherence >= 0.85, $"high forcing directional coherence={delta.DirectionalCoherence:F3} (>=0.85)");
                    Check(delta.ControlLeakageRatio <= 0.02, $"zero control-ROI leakage={delta.ControlLeakageRatio:F4} (<=0.02)");
                    Check(liftDelta.SignedDelta < -0.03, $"synLift also decreases: {liftDelta.MeanA:F3} -> {liftDelta.MeanB:F3}");
                    break;

                case PairExpectationKind.OrographicDipole:
                    var eastOrog = ClimateCloudQaMetrics.ComputeFieldDelta(aA, aB, 1, spec.SecondaryRoi, spec.ControlRoi);
                    var westLee = ClimateCloudQaMetrics.ComputeFieldDelta(aA, aB, 2, spec.TargetRoi, spec.ControlRoi);
                    var eastLee = ClimateCloudQaMetrics.ComputeFieldDelta(aA, aB, 2, spec.SecondaryRoi, spec.ControlRoi);
                    var westLift = ClimateCloudQaMetrics.ComputeFieldDelta(fA, fB, 0, spec.TargetRoi, spec.ControlRoi);
                    var eastLift = ClimateCloudQaMetrics.ComputeFieldDelta(fA, fB, 0, spec.SecondaryRoi, spec.ControlRoi);
                    double orogDipole = (delta.MeanA - delta.MeanB) - (eastOrog.MeanA - eastOrog.MeanB);
                    double leeDipole = (westLee.MeanB - westLee.MeanA) + (eastLee.MeanA - eastLee.MeanB);
                    double liftDipole = (westLift.MeanA - westLift.MeanB) - (eastLift.MeanA - eastLift.MeanB);
                    summary.ExtraMetricLabel = "orogDipole/liftDipole";
                    summary.ExtraMetricA = orogDipole;
                    summary.ExtraMetricB = liftDipole;
                    Check(orogDipole > 0.60, $"Andes orogCrest dipole flip={orogDipole:F3} (west {delta.MeanA:F3}->{delta.MeanB:F3}, east {eastOrog.MeanA:F3}->{eastOrog.MeanB:F3})");
                    Check(leeDipole > 0.20, $"Andes leeShadow dipole flip={leeDipole:F3} (west {westLee.MeanA:F3}->{westLee.MeanB:F3}, east {eastLee.MeanA:F3}->{eastLee.MeanB:F3})");
                    Check(liftDipole > 0.08, $"Andes synLift dipole flip={liftDipole:F3} (west {westLift.MeanA:F3}->{westLift.MeanB:F3}, east {eastLift.MeanA:F3}->{eastLift.MeanB:F3})");
                    Check(delta.ControlLeakageRatio <= 0.02, $"zero control-ROI leakage={delta.ControlLeakageRatio:F4} (<=0.02)");
                    break;

                case PairExpectationKind.NorthwardMigration:
                    double latA = ClimateCloudQaMetrics.ComputeLatitudeCentroid(fA, (r, g, b, a) => g, spec.TargetRoi, false);
                    double latB = ClimateCloudQaMetrics.ComputeLatitudeCentroid(fB, (r, g, b, a) => g, spec.TargetRoi, false);
                    summary.ExtraMetricLabel = "itczLatCentroidDeg";
                    summary.ExtraMetricA = latA;
                    summary.ExtraMetricB = latB;
                    Check(latB - latA > 12.0, $"ITCZ convective centroid shifts north Jan->Jul: {latA:F2} -> {latB:F2} deg (shift={latB - latA:+0.00;-0.00} deg)");
                    break;

                case PairExpectationKind.PolewardExpansion:
                    double absLatA = ClimateCloudQaMetrics.ComputeLatitudeCentroid(fA, (r, g, b, a) => g, spec.TargetRoi, true);
                    double absLatB = ClimateCloudQaMetrics.ComputeLatitudeCentroid(fB, (r, g, b, a) => g, spec.TargetRoi, true);
                    var subA = ClimateCloudQaMetrics.ComputeFieldDelta(fA, fB, 1, spec.SecondaryRoi);
                    summary.ExtraMetricLabel = "itczAbsLatDeg";
                    summary.ExtraMetricA = absLatA;
                    summary.ExtraMetricB = absLatB;
                    Check(summary.HadleyDegB > summary.HadleyDegA + 8.0 && summary.RossbyKB < summary.RossbyKA - 0.5,
                        $"circulation regime shifts 1x->0.45x: Hadley {summary.HadleyDegA:F1}->{summary.HadleyDegB:F1} deg, Polar {summary.PolarDegA:F1}->{summary.PolarDegB:F1} deg, RossbyK {summary.RossbyKA:F1}->{summary.RossbyKB:F1}");
                    Check(absLatB - absLatA > 1.2 && subA.SignedDelta > 0.02,
                        $"tropical convective belt broadens poleward: |lat| {absLatA:F2}->{absLatB:F2} deg, 12..26N itcz {subA.MeanA:F3}->{subA.MeanB:F3}");
                    break;

                case PairExpectationKind.CycloneEvolution:
                    double lon0 = (spec.NaturalTrack0.U - 0.5) * 360.0, lat0 = (spec.NaturalTrack0.V - 0.5) * 180.0;
                    double lon1 = (spec.NaturalTrack1.U - 0.5) * 360.0, lat1 = (spec.NaturalTrack1.V - 0.5) * 180.0;
                    double disp = ClimateCloudQaMetrics.SphericalDistanceDeg(lon0, lat0, lon1, lat1);
                    double dPhase = spec.NaturalTrack1.Phase - spec.NaturalTrack0.Phase;
                    double hemiSign = lat1 >= 0 ? 1.0 : -1.0;
                    summary.ExtraMetricLabel = "trackDispDeg/dPhaseRad";
                    summary.ExtraMetricA = disp;
                    summary.ExtraMetricB = dPhase;
                    Check(spec.NaturalTrack0.Tracked && spec.NaturalTrack1.Tracked, "natural cyclone continuously tracked at t0 and t1");
                    Check(disp >= 1.5, $"natural cyclone translates {disp:F2} deg: ({lon0:F1},{lat0:F1}) -> ({lon1:F1},{lat1:F1})");
                    Check(dPhase * hemiSign > 0.30, $"natural cyclone rotates with hemispheric sign: dPhase={dPhase:+0.00;-0.00} rad (hemisphere={(hemiSign > 0 ? "NH +" : "SH -")})");
                    Check(spec.NaturalTrack1.Strength >= 0.55f, $"peak storm reaches mature strength={spec.NaturalTrack1.Strength:F2} ({spec.NaturalTrack1.WindSpeed:F1} m/s)");
                    break;
            }

            summary.Passed = summary.Failures.Count == 0;
            return summary;
        }

        static double ComputeRegionalResponseMean(WorldData world, GeoBox roi)
        {
            var stats = new FlatClimateStatistics(world, default, 64, atmosphereOnly: true);
            stats.Refresh(world.Day, world.Cyclones, default);
            double sum = 0, wtSum = 0;
            for (int y = 0; y < stats.Height; y++)
            {
                double lat = ((y + 0.5) / stats.Height - 0.5) * 180.0;
                if (lat < roi.SouthLat || lat > roi.NorthLat) continue;
                double wt = Math.Cos(lat * Math.PI / 180.0);
                for (int x = 0; x < stats.Width; x++)
                {
                    double lon = ((x + 0.5) / stats.Width - 0.5) * 360.0;
                    if (!roi.Contains(lon, lat)) continue;
                    var resp = RegionalCloudResponse.From(stats.Cells[y * stats.Width + x]);
                    sum += wt * resp.Cover;
                    wtSum += wt;
                }
            }
            return wtSum > 0 ? sum / wtSum : 0;
        }

        static float EdgeWeight(double lon, double lat, GeoBox box, double marginDeg)
        {
            double dLonMin = Math.Min(Math.Abs(ClimateCloudQaMetrics.WrapDeltaDeg(lon - box.WestLon)),
                                      Math.Abs(ClimateCloudQaMetrics.WrapDeltaDeg(box.EastLon - lon)));
            double dLatMin = Math.Min(lat - box.SouthLat, box.NorthLat - lat);
            double d = Math.Min(dLonMin, dLatMin);
            float t = FMath.Clamp((float)(d / Math.Max(0.1, marginDeg)), 0f, 1f);
            return t * t * (3f - 2f * t);
        }
    }
}
