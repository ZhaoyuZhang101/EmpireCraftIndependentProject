using System;

namespace Orbis.Flat
{
    public enum ExoPlaSimPreset : byte
    {
        EarthModern = 0,
        EarthPreindustrial = 1,
        WarmGreenhouseEocene = 2,
        IcehouseGlacial = 3,
        HighObliquity35 = 4,
        SuperEarthDense = 5,
        TidallyLockedEyeball = 6
    }

    /// <summary>
    /// Planetary orbital, rotational, stellar, and multi-gas atmospheric configuration
    /// modeled after Web-ExoPlaSim-QoL (`radmod.f90` + `calmod.f90` + gas mixture calculator).
    /// Pure C#, deterministic, coordinate-free, and zero-overhead when at EarthModern defaults.
    /// </summary>
    [Serializable]
    public sealed class ExoPlaSimPlanetConfig
    {
        public ExoPlaSimPreset Preset = ExoPlaSimPreset.EarthModern;

        // Orbital & rotational parameters
        public float SolarConstantWm2 = 1361.0f;
        public float StarEffectiveTempK = 5772.0f;
        public float ObliquityDeg = 23.44f;
        public float Eccentricity = 0.0167f;
        public float PerihelionPhaseRad = 0.20f;
        public float RotationRateRatio = 1.0f;      // 1.0 = 24h Earth sidereal rotation
        public bool TidallyLocked = false;
        public float SubstellarLongitudeDeg = 0.0f;
        public float SurfaceGravityMs2 = 9.80665f;

        // Multi-gas partial pressures (bar) & trace greenhouse concentrations (ppm)
        public float N2Bar = 0.7808f;
        public float O2Bar = 0.2095f;
        public float ArBar = 0.0093f;
        public float H2Bar = 0.0f;
        public float HeBar = 0.0f;
        public float Co2Ppm = 360.0f;
        public float Ch4Ppm = 1.75f;
        public bool FixedOzone = true;

        // Land-surface & hydrology physics toggles (Web-ExoPlaSim-QoL Surface & Hydrology tab)
        public float MixedLayerDepthMeters = 50.0f;
        public bool WetSoilAlbedoFeedback = true;
        public bool InteractiveVegetationFeedback = true;
        public bool MaritimeSeaBreezeGuard = true;

        public static readonly ExoPlaSimPlanetConfig DefaultEarth = CreatePreset(ExoPlaSimPreset.EarthModern);

        public ExoPlaSimPlanetConfig Clone() => (ExoPlaSimPlanetConfig)MemberwiseClone();

        public bool IsStandardModernEarth =>
            Preset == ExoPlaSimPreset.EarthModern &&
            !TidallyLocked &&
            Math.Abs(SolarConstantWm2 - 1361.0f) < 0.05f &&
            Math.Abs(ObliquityDeg - 23.44f) < 0.01f &&
            Math.Abs(Eccentricity - 0.0167f) < 0.0005f &&
            Math.Abs(RotationRateRatio - 1.0f) < 0.005f &&
            Math.Abs(TotalPressureBar - 1.013f) < 0.03f &&
            Math.Abs(Co2Ppm - 360.0f) < 5.0f;

        public float TotalNonTraceBar => Math.Max(0.01f, N2Bar + O2Bar + ArBar + H2Bar + HeBar);

        public float Co2PartialBar => TotalNonTraceBar * Math.Max(0.1f, Co2Ppm) * 1e-6f;

        public float TotalPressureBar => TotalNonTraceBar + Co2PartialBar + 0.0134f; // + mean water vapor & trace

        public float PressureRatioEarth => TotalPressureBar / 1.01325f;

        /// <summary>
        /// Mean dry molar mass (g/mol) from partial pressures, matching Web-ExoPlaSim-QoL's gas mixture calculator.
        /// </summary>
        public float MeanMolarMassGmol
        {
            get
            {
                float pTot = TotalNonTraceBar + Co2PartialBar;
                float massSum =
                    N2Bar * 28.0134f +
                    O2Bar * 31.9988f +
                    ArBar * 39.948f +
                    H2Bar * 2.01588f +
                    HeBar * 4.0026f +
                    Co2PartialBar * 44.0095f;
                return massSum / Math.Max(1e-4f, pTot);
            }
        }

        /// <summary>
        /// Specific gas constant R_spec = R_univ / M (J / (kg K)). Earth dry air ~ 287.05 J/(kg K).
        /// </summary>
        public float SpecificGasConstantJkgK => 8314.4626f / Math.Max(2.0f, MeanMolarMassGmol);

        /// <summary>
        /// Dry adiabatic lapse rate g / c_p in K/km (Earth ~ 9.8 K/km; moist environmental ~ 6.5 * g / g_Earth).
        /// </summary>
        public float MoistLapseRateScale => FMath.Clamp(SurfaceGravityMs2 / 9.80665f, 0.35f, 2.5f);

        /// <summary>
        /// Radiative greenhouse forcing (W/m2) relative to modern Earth (360 ppm CO2, 1.013 bar, 5772 K G2V star),
        /// combining logarithmic CO2 band saturation (Myhre / PlaSim `radmod.f90`), CH4, pressure broadening,
        /// and M-dwarf near-IR water/H2 continuum absorption shift.
        /// </summary>
        public float GreenhouseForcingDeltaWm2
        {
            get
            {
                double co2Ratio = Math.Max(1.0, Co2Ppm) / 360.0;
                double dFCo2 = 5.35 * Math.Log(co2Ratio);
                double dFCh4 = 0.45 * (Math.Sqrt(Math.Max(0.0, Ch4Ppm)) - Math.Sqrt(1.75));
                double pBroad = 6.2 * (Math.Pow(Math.Max(0.1, PressureRatioEarth), 0.58) - 1.0);
                double h2Cia = H2Bar > 0.01f ? 14.0 * Math.Log(1.0 + H2Bar * TotalPressureBar) : 0.0;
                double ozoneTerm = FixedOzone ? 0.0 : -1.1;
                return (float)FMath.Clamp((float)(dFCo2 + dFCh4 + pBroad + h2Cia + ozoneTerm), -28.0f, 65.0f);
            }
        }

        /// <summary>
        /// Equilibrium global mean surface temperature offset (deg C) induced by non-Earth stellar flux,
        /// greenhouse gas mixture, pressure broadening, and redder stellar spectrum (lower ice/Rayleigh albedo).
        /// Equals exactly 0.0 °C for DefaultEarth.
        /// </summary>
        public float GlobalEquilibriumTempDeltaC
        {
            get
            {
                if (IsStandardModernEarth) return 0.0f;
                // M/K-dwarf NIR shift lowers ice-snow & Rayleigh bond albedo
                float starRedShift = FMath.Clamp((5772.0f - StarEffectiveTempK) / 2800.0f, -0.3f, 0.9f);
                float effectiveBondAlbedo = FMath.Clamp(0.30f - 0.065f * starRedShift, 0.14f, 0.45f);
                double absorbed = SolarConstantWm2 * (1.0 - effectiveBondAlbedo) * 0.25;
                double absorbedEarth = 1361.0 * (1.0 - 0.30) * 0.25; // 238.175 W/m2
                double dForcing = (absorbed - absorbedEarth) + GreenhouseForcingDeltaWm2;
                // Planck + water-vapor radiative-convective climate sensitivity (~0.72 K per W/m2)
                return FMath.Clamp((float)(0.72 * dForcing), -45.0f, 45.0f);
            }
        }

        /// <summary>
        /// Hadley cell poleward expansion factor (~ Omega^-0.42 in Held-Hou theory). Equals 1.0 at Earth rotation.
        /// </summary>
        public float HadleyExpansionFactor =>
            TidallyLocked ? 2.4f : FMath.Clamp((float)Math.Pow(Math.Max(0.04, RotationRateRatio), -0.42), 0.55f, 2.5f);

        /// <summary>
        /// Computes daily-mean Top-Of-Atmosphere (TOA) incident shortwave flux in W/m2 at (latitudeRad, longitudeRad, month).
        /// Supports arbitrary obliquity, eccentricity, stellar constant, and tidally-locked substellar geometry.
        /// </summary>
        public double ComputeToaSolarFluxWm2(double latitudeRad, double longitudeRad, int month)
        {
            if (TidallyLocked)
            {
                double subLon = SubstellarLongitudeDeg * Math.PI / 180.0;
                double cosZenith = Math.Cos(latitudeRad) * Math.Cos(longitudeRad - subLon);
                return SolarConstantWm2 * Math.Max(0.0, cosZenith);
            }

            double phase = (month - 6) * Math.PI / 6.0;
            double obliqRad = FMath.Clamp(ObliquityDeg, 0.0f, 89.5f) * Math.PI / 180.0;
            double declination = Math.Asin(Math.Max(-1.0, Math.Min(1.0, Math.Sin(obliqRad) * Math.Cos(phase + 0.42))));
            double tanProd = -Math.Tan(latitudeRad) * Math.Tan(declination);
            double hourAngle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, tanProd)));
            double ecc = FMath.Clamp(Eccentricity, 0.0f, 0.65f);
            double distFactor = (1.0 - 2.0 * ecc * Math.Cos(phase + PerihelionPhaseRad)) / Math.Max(0.25, (1.0 - ecc * ecc));
            return (SolarConstantWm2 / Math.PI) * distFactor *
                   (hourAngle * Math.Sin(latitudeRad) * Math.Sin(declination) +
                    Math.Cos(latitudeRad) * Math.Cos(declination) * Math.Sin(hourAngle));
        }

        public static ExoPlaSimPlanetConfig CreatePreset(ExoPlaSimPreset preset)
        {
            var c = new ExoPlaSimPlanetConfig { Preset = preset };
            switch (preset)
            {
                case ExoPlaSimPreset.EarthPreindustrial:
                    c.Co2Ppm = 280.0f;
                    c.Ch4Ppm = 0.72f;
                    break;
                case ExoPlaSimPreset.WarmGreenhouseEocene:
                    c.SolarConstantWm2 = 1355.0f;
                    c.Co2Ppm = 1400.0f;
                    c.Ch4Ppm = 3.5f;
                    c.ObliquityDeg = 23.8f;
                    break;
                case ExoPlaSimPreset.IcehouseGlacial:
                    c.Co2Ppm = 185.0f;
                    c.Ch4Ppm = 0.38f;
                    c.ObliquityDeg = 22.9f;
                    c.Eccentricity = 0.019f;
                    break;
                case ExoPlaSimPreset.HighObliquity35:
                    c.ObliquityDeg = 35.0f;
                    c.Eccentricity = 0.03f;
                    break;
                case ExoPlaSimPreset.SuperEarthDense:
                    c.SurfaceGravityMs2 = 13.8f;
                    c.N2Bar = 1.72f;
                    c.O2Bar = 0.42f;
                    c.ArBar = 0.02f;
                    c.Co2Ppm = 520.0f;
                    c.RotationRateRatio = 0.78f;
                    break;
                case ExoPlaSimPreset.TidallyLockedEyeball:
                    c.TidallyLocked = true;
                    c.SubstellarLongitudeDeg = 15.0f;
                    c.StarEffectiveTempK = 3450.0f;
                    c.SolarConstantWm2 = 1260.0f;
                    c.RotationRateRatio = 0.08f;
                    c.ObliquityDeg = 0.0f;
                    c.Eccentricity = 0.0f;
                    c.Co2Ppm = 450.0f;
                    break;
            }
            return c;
        }

        public static string GetPresetLabel(ExoPlaSimPreset preset)
        {
            switch (preset)
            {
                case ExoPlaSimPreset.EarthModern: return "现代地球 (1.01 bar · 360 ppm CO2)";
                case ExoPlaSimPreset.EarthPreindustrial: return "前工业化地球 (280 ppm CO2)";
                case ExoPlaSimPreset.WarmGreenhouseEocene: return "始新世温室地球 (1400 ppm CO2)";
                case ExoPlaSimPreset.IcehouseGlacial: return "冰盛期冰室地球 (185 ppm CO2)";
                case ExoPlaSimPreset.HighObliquity35: return "高倾角强季风行星 (倾角 35°)";
                case ExoPlaSimPreset.SuperEarthDense: return "稠密大气超级地球 (2.16 bar · 1.4g)";
                case ExoPlaSimPreset.TidallyLockedEyeball: return "红矮星潮汐锁定眼球行星";
                default: return preset.ToString();
            }
        }
    }
}
