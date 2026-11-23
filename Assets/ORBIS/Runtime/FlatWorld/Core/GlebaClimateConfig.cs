using System;

namespace Orbis.Flat
{
    /// <summary>
    /// Gleba v0.3.6 reference climate configuration, piecewise-linear lookup tables,
    /// and scalar operators reverse-engineered from model.ron and gleba_godot.dll.
    /// Pure C# 9.0 (noEngineReferences) for deterministic runtime & validation use.
    /// </summary>
    public sealed class GlebaClimateConfig
    {
        public readonly struct CurvePoint
        {
            public readonly float X;
            public readonly float Y;
            public CurvePoint(float x, float y)
            {
                X = x;
                Y = y;
            }
        }

        public static readonly GlebaClimateConfig Default = new GlebaClimateConfig();

        // --- Core solver iteration & step parameters (common/model.ron) ---
        public int BlurIterations = 8;
        public float BlurStrength = 0.5f;
        public int AdvectionIterations = 128;
        public float AdvectionDt = 0.0055f;
        public float TemperatureAdvectionDt = 0.001f;
        public float RainfallAdvectionDt = 0.0015f;

        // --- Wind vector synthesis weights ---
        public float GeostrophicWindWeight = 0.5f;
        public float PressureGradientWindWeight = 0.5f;
        public float PrevailingWindWeight = 2.0f;

        // --- Circulation band & latitude moderation factors ---
        public float TemperatureLatitudeModerationFactor = 0.33f;
        public float RainfallLatitudeModerationFactor = 0.75f;
        public float ContinentalityDrynessFactor = 0.35f;
        public float PressureZoneRainfallFactor = 0.75f;

        // --- ITCZ & STHZ anchor latitudes (degrees) ---
        public float ItczLandSummerPosDegrees = 32.5f;
        public float ItczSeaSummerPosDegrees = 4.0f;
        public float SthzLandSummerPosDegrees = 33.0f;
        public float SthzLandWinterPosDegrees = 30.0f;
        public float SthzSeaSummerPosDegrees = 30.0f;
        public float SthzSeaWinterPosDegrees = 28.0f;
        public float MaxSthzItczDistanceForMonsoonTunerDegrees = 5.0f;

        // --- Binary constants from gleba_godot.dll (.rdata) ---
        public float MountainBarrierOnsetMeters = 1500.0f;
        public float MountainBarrierFullMeters = 3500.0f;
        public int RainShadowAdvectionIterations = 20;
        public float DeepInteriorDrynessOnsetKm = 2000.0f;
        public float DeepInteriorDrynessSlopePer1000Km = 0.15f;
        public float CoastalTempSeaThresholdKm = 100.0f;
        public float CoastalRainSeaThresholdKm = 500.0f;
        public float MicroclimateLapseRatePerKm = 4.3f;
        public float MicroclimateLowlandBonusAt100m = 0.43f;

        // --- Verbatim model.ron lookup curves ---
        public static readonly CurvePoint[] DefaultContinentalityFunction = new CurvePoint[]
        {
            new CurvePoint(0.0f, 0.0f),
            new CurvePoint(0.75f, 0.05f),
            new CurvePoint(1.0f, 1.0f)
        };

        // From the point of view of Northern Hemisphere summer (+lat = summer, -lat = winter)
        public static readonly CurvePoint[] DefaultTemperatureLandContinental = new CurvePoint[]
        {
            new CurvePoint(-90.0f, -40.0f),
            new CurvePoint(-80.0f, -25.0f),
            new CurvePoint(-70.0f, -12.0f),
            new CurvePoint(-60.0f, -10.0f),
            new CurvePoint(-50.0f, 0.0f),
            new CurvePoint(-40.0f, 15.0f),
            new CurvePoint(-30.0f, 16.0f),
            new CurvePoint(-20.0f, 18.5f),
            new CurvePoint(-10.0f, 24.0f),
            new CurvePoint(0.0f, 25.0f),
            new CurvePoint(10.0f, 26.0f),
            new CurvePoint(20.0f, 27.0f),
            new CurvePoint(30.0f, 26.0f),
            new CurvePoint(35.0f, 25.0f),
            new CurvePoint(40.0f, 24.0f),
            new CurvePoint(50.0f, 22.0f),
            new CurvePoint(60.0f, 15.0f),
            new CurvePoint(70.0f, 12.0f),
            new CurvePoint(76.0f, 5.0f),
            new CurvePoint(77.0f, -1.0f),
            new CurvePoint(80.0f, -15.0f),
            new CurvePoint(90.0f, -25.0f)
        };

        public static readonly CurvePoint[] DefaultTemperatureLandOceanic = new CurvePoint[]
        {
            new CurvePoint(-90.0f, -30.0f),
            new CurvePoint(-80.0f, -20.0f),
            new CurvePoint(-70.0f, -10.0f),
            new CurvePoint(-60.0f, -4.0f),
            new CurvePoint(-50.0f, 5.0f),
            new CurvePoint(-40.0f, 13.0f),
            new CurvePoint(-30.0f, 17.5f),
            new CurvePoint(-20.0f, 22.0f),
            new CurvePoint(-10.0f, 24.5f),
            new CurvePoint(0.0f, 28.5f),
            new CurvePoint(10.0f, 30.0f),
            new CurvePoint(20.0f, 27.5f),
            new CurvePoint(30.0f, 25.0f),
            new CurvePoint(40.0f, 22.5f),
            new CurvePoint(50.0f, 18.5f),
            new CurvePoint(60.0f, 10.5f),
            new CurvePoint(70.0f, 10.0f),
            new CurvePoint(80.0f, -2.5f),
            new CurvePoint(90.0f, -12.0f)
        };

        public static readonly CurvePoint[] DefaultTemperatureSea = new CurvePoint[]
        {
            new CurvePoint(-90.0f, -30.0f),
            new CurvePoint(-80.0f, -20.0f),
            new CurvePoint(-70.0f, -10.0f),
            new CurvePoint(-60.0f, -4.0f),
            new CurvePoint(-50.0f, 5.0f),
            new CurvePoint(-40.0f, 13.0f),
            new CurvePoint(-30.0f, 18.5f),
            new CurvePoint(-20.0f, 22.0f),
            new CurvePoint(-10.0f, 24.5f),
            new CurvePoint(0.0f, 28.5f),
            new CurvePoint(10.0f, 30.0f),
            new CurvePoint(20.0f, 27.5f),
            new CurvePoint(30.0f, 25.0f),
            new CurvePoint(40.0f, 22.5f),
            new CurvePoint(50.0f, 18.5f),
            new CurvePoint(60.0f, 12.5f),
            new CurvePoint(70.0f, 10.0f),
            new CurvePoint(80.0f, -2.5f),
            new CurvePoint(90.0f, -12.0f)
        };

        public static readonly CurvePoint[] DefaultRainfallLandContinental = new CurvePoint[]
        {
            new CurvePoint(-90.0f, 10.0f),
            new CurvePoint(-80.0f, 30.0f),
            new CurvePoint(-70.0f, 55.0f),
            new CurvePoint(-40.0f, 40.5f),
            new CurvePoint(-30.0f, 10.0f),
            new CurvePoint(-20.0f, 5.0f),
            new CurvePoint(-10.0f, 10.0f),
            new CurvePoint(-5.0f, 75.0f),
            new CurvePoint(0.0f, 200.0f),
            new CurvePoint(5.0f, 175.0f),
            new CurvePoint(10.0f, 160.0f),
            new CurvePoint(14.5f, 10.0f),
            new CurvePoint(19.5f, 5.0f),
            new CurvePoint(20.0f, 0.5f),
            new CurvePoint(30.0f, 1.0f),
            new CurvePoint(35.0f, 50.0f),
            new CurvePoint(40.0f, 65.0f),
            new CurvePoint(70.0f, 65.0f),
            new CurvePoint(80.0f, 15.0f),
            new CurvePoint(90.0f, 10.0f)
        };

        public static readonly CurvePoint[] DefaultRainfallLandOceanic = new CurvePoint[]
        {
            new CurvePoint(-90.0f, 10.0f),
            new CurvePoint(-80.0f, 30.0f),
            new CurvePoint(-70.0f, 75.0f),
            new CurvePoint(-45.0f, 95.0f),
            new CurvePoint(-40.0f, 125.0f),
            new CurvePoint(-35.0f, 120.0f),
            new CurvePoint(-30.0f, 55.0f),
            new CurvePoint(-20.0f, 40.0f),
            new CurvePoint(-10.0f, 60.0f),
            new CurvePoint(-5.0f, 200.0f),
            new CurvePoint(0.0f, 350.0f),
            new CurvePoint(5.0f, 325.0f),
            new CurvePoint(10.0f, 250.0f),
            new CurvePoint(20.0f, 175.5f),
            new CurvePoint(30.0f, 135.0f),
            new CurvePoint(35.0f, 45.0f),
            new CurvePoint(40.0f, 50.0f),
            new CurvePoint(70.0f, 50.0f),
            new CurvePoint(80.0f, 15.0f),
            new CurvePoint(90.0f, 10.0f)
        };

        public static readonly CurvePoint[] DefaultRainfallSea = new CurvePoint[]
        {
            new CurvePoint(-90.0f, 50.0f),
            new CurvePoint(-5.0f, 300.0f),
            new CurvePoint(0.0f, 350.0f),
            new CurvePoint(5.0f, 300.0f),
            new CurvePoint(90.0f, 50.0f)
        };

        public static readonly CurvePoint[] DefaultAdhocItczRain = new CurvePoint[]
        {
            new CurvePoint(-8.0f, 0.0f),
            new CurvePoint(-6.0f, 150.0f),
            new CurvePoint(0.0f, 300.0f),
            new CurvePoint(6.0f, 150.0f),
            new CurvePoint(8.0f, 0.0f)
        };

        // Humidity curves from 05_climate_simulation_main.asm (0x1822d4dd0..0x1822d4e00)
        public static readonly CurvePoint[] HumidityRainfallCurve = new CurvePoint[]
        {
            new CurvePoint(0.0f, 0.0f),
            new CurvePoint(35.0f, 0.1f),
            new CurvePoint(60.0f, 0.5f),
            new CurvePoint(120.0f, 1.0f)
        };

        public static readonly CurvePoint[] HumidityTemperatureCurve = new CurvePoint[]
        {
            new CurvePoint(0.0f, 2.0f),
            new CurvePoint(30.0f, 1.0f),
            new CurvePoint(50.0f, 0.75f)
        };

        public CurvePoint[] ContinentalityFunction = DefaultContinentalityFunction;
        public CurvePoint[] TemperatureLandContinental = DefaultTemperatureLandContinental;
        public CurvePoint[] TemperatureLandOceanic = DefaultTemperatureLandOceanic;
        public CurvePoint[] TemperatureSea = DefaultTemperatureSea;
        public CurvePoint[] RainfallLandContinental = DefaultRainfallLandContinental;
        public CurvePoint[] RainfallLandOceanic = DefaultRainfallLandOceanic;
        public CurvePoint[] RainfallSea = DefaultRainfallSea;
        public CurvePoint[] AdhocItczRain = DefaultAdhocItczRain;

        /// <summary>
        /// Clamped piecewise-linear interpolation matching Gleba's inlined curve evaluator.
        /// </summary>
        public static float EvaluateCurve(CurvePoint[] curve, float x)
        {
            if (curve == null || curve.Length == 0) return 0f;
            if (x <= curve[0].X) return curve[0].Y;
            int last = curve.Length - 1;
            for (int i = 0; i < last; i++)
            {
                if (x < curve[i + 1].X)
                {
                    float span = curve[i + 1].X - curve[i].X;
                    if (span <= 1e-12f) return curve[i].Y;
                    float t = FMath.Clamp((x - curve[i].X) / span, 0f, 1f);
                    return curve[i].Y + (curve[i + 1].Y - curve[i].Y) * t;
                }
            }
            return curve[last].Y;
        }

        /// <summary>
        /// Ken Perlin C2 quintic smoothstep u^3 * (6u^2 - 15u + 10) used for Gleba seasonal blending.
        /// </summary>
        public static float QuinticSmoothstep(float x)
        {
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            double u = x;
            return (float)(u * u * u * (u * (u * 6.0 - 15.0) + 10.0));
        }

        /// <summary>
        /// Maps true geographic latitude (radians in [-pi/2, +pi/2]) piecewise-linearly across
        /// the 5 dynamic circulation zone boundaries [PF_S, STHZ_S, ITCZ, STHZ_N, PF_N] onto [-90, +90] degrees.
        /// </summary>
        public static float MapEffectiveLatitudeDegrees(
            float latRad, float pfSouthRad, float sthzSouthRad, float itczRad, float sthzNorthRad, float pfNorthRad)
        {
            const float HalfPi = 1.57079633f;
            if (latRad < pfSouthRad)
                return -90f + 30f * SafeRatio(latRad + HalfPi, pfSouthRad + HalfPi);
            if (latRad < sthzSouthRad)
                return -60f + 30f * SafeRatio(latRad - pfSouthRad, sthzSouthRad - pfSouthRad);
            if (latRad < itczRad)
                return -30f + 30f * SafeRatio(latRad - sthzSouthRad, itczRad - sthzSouthRad);
            if (latRad < sthzNorthRad)
                return 30f * SafeRatio(latRad - itczRad, sthzNorthRad - itczRad);
            if (latRad < pfNorthRad)
                return 30f + 30f * SafeRatio(latRad - sthzNorthRad, pfNorthRad - sthzNorthRad);
            return 60f + 30f * SafeRatio(latRad - pfNorthRad, HalfPi - pfNorthRad);
        }

        static float SafeRatio(float num, float den)
        {
            if (den <= 1e-6f) return 0.5f;
            return FMath.Clamp(num / den, 0f, 1f);
        }

        /// <summary>
        /// Gleba humidity index in [0, 1] from monthly rainfall (mm) and temperature (C).
        /// </summary>
        public static float ComputeHumidityIndex(float monthlyRainfallMm, float temperatureC)
        {
            float r = EvaluateCurve(HumidityRainfallCurve, monthlyRainfallMm);
            float t = EvaluateCurve(HumidityTemperatureCurve, temperatureC);
            return FMath.Clamp(r * t, 0f, 1f);
        }

        /// <summary>
        /// Gleba TileData::get_temperature_at_time microclimate elevation adjustment:
        /// +0.43 C over 0..100 m, and -4.3 C/km above 100 m.
        /// </summary>
        public static float ApplyMicroclimateElevationLapse(float seaLevelTempC, float elevationMeters)
        {
            float elevPos = Math.Max(elevationMeters, 0f);
            float lowBand = Math.Min(elevPos, 100f);
            float highBand = Math.Max(elevPos - 100f, 0f);
            return seaLevelTempC - 4.3f * (highBand / 1000f) + 0.43f * (lowBand / 100f);
        }
    }
}
