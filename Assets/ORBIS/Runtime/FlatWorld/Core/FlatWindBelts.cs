using System;

namespace Orbis.Flat
{
    // Monthly mean surface circulation. Latitude is in degrees north and the
    // solstitial cosine is +1 in northern summer, -1 in southern summer.
    internal readonly struct FlatWindBelts
    {
        static float SubtropicalRidge(float ridgeBase, float summer) => ridgeBase + 4 * summer;
        static float PolarFront(float frontBase, float summer) => Math.Min(89f, frontBase + 4 * summer);
        const float DefaultRidgeBase = 30f;
        const float DefaultPolarFront = 65f;
        const float MarinePolarFront = 68f;
        readonly float latitude, solstice, ridgeBase, frontBase, marineFront, inlandEastStep, marineEastStep;

        internal FlatWindBelts(float latitude, float solstitialCosine)
            : this(latitude, solstitialCosine, null)
        {
        }

        internal FlatWindBelts(float latitude, float solstitialCosine, ExoPlaSimPlanetConfig planet)
        {
            this.latitude = latitude;
            solstice = solstitialCosine;
            bool custom = planet != null && !planet.IsStandardModernEarth;
            ridgeBase = custom ? planet.HadleyTerminusDeg : DefaultRidgeBase;
            frontBase = custom ? planet.PolarFrontTerminusDeg : DefaultPolarFront;
            marineFront = custom
                ? Math.Min(89f, frontBase + 3f * FMath.Clamp((85f - frontBase) / 20f, 0f, 1f))
                : MarinePolarFront;

            float summer = FMath.Clamp(latitude / 8, -1, 1) * solstitialCosine;
            float degrees = Math.Abs(latitude);
            float trade = (float)Math.Tanh((degrees - SubtropicalRidge(ridgeBase, summer)) / 8);
            // In SingleCell / DoubleCell ("双环流") regimes, frontBase >= 79 deg collapses the 3rd polar-easterly cap
            // so the hemisphere consists of an expanded Hadley cell + a single merged mid-to-high-latitude cell.
            float polarCapWeight = custom ? FMath.Clamp((83f - frontBase) / 14f, 0f, 1f) : 1f;
            float polar = polarCapWeight * (float)(.5 + .5 * Math.Tanh((degrees - frontBase) / 8));
            float multiBandMod = custom && planet.CirculationRegime == ExoPlaSimCirculationRegime.MultiBandJets && degrees > frontBase
                ? .35f * (float)Math.Cos((degrees - frontBase) * Math.PI / 16f)
                : 0f;
            // The winter temperature contrast strengthens westerlies. The
            // polar easterlies are weaker than tropical trades on average.
            float EastStep(float front)
            {
                float frontTanh = custom && front >= 84f ? 1f : (float)Math.Tanh((front - degrees) / 8);
                float belt = trade * frontTanh + multiBandMod;
                return .24f * belt * (1 - .55f * polar) * (1 - .55f * summer * Math.Max(0, belt));
            }
            inlandEastStep = EastStep(PolarFront(frontBase, summer));
            // Ocean thermal inertia damps the seasonal shift of the subpolar
            // front. The two regimes blend inland over the maritime fetch.
            marineEastStep = EastStep(marineFront);
        }

        float EastAt(float axis, float maritime)
        {
            float distance = (latitude - axis) / 8;
            return FMath.Lerp(inlandEastStep, marineEastStep, maritime) *
                (1 - .55f * (float)Math.Exp(-distance * distance));
        }

        // Normalized eastward open-ocean wind for the wind stress calculation.
        internal static float Zonal(float latitude, float solstitialCosine)
        {
            var winds = new FlatWindBelts(latitude, solstitialCosine);
            return FMath.Clamp(winds.EastAt(4 * solstitialCosine, 1) / .32f, -1, 1);
        }

        static float Lobe(float fraction) => 4 * fraction * (1 - fraction);

        // East and north displacements per six hours, in 2.8125-degree
        // reference cells. The moist convergence lies between the marine
        // convergence zone and the inland heat low; the two can be far apart.
        internal void Surface(float heatLowLatitude, float maritime, out float east, out float north)
        {
            float axis = FMath.Lerp(4 * solstice, heatLowLatitude, .25f);
            east = EastAt(axis, maritime);
            float side = latitude >= axis ? 1 : -1;
            float position = side * latitude, equator = side * axis;
            float ridge = Math.Max(SubtropicalRidge(ridgeBase, side * solstice), equator + 8);
            if (position < ridge)
                north = -side * .10f * Lobe((position - equator) / Math.Max(4f, ridge - equator));
            else
                north = side * FMath.Lerp(PolarReturn(position, ridge, PolarFront(frontBase, side * solstice)),
                    PolarReturn(position, ridge, marineFront), maritime);
        }

        static float PolarReturn(float position, float ridge, float front) => position < front
            ? .055f * Lobe((position - ridge) / Math.Max(4f, front - ridge))
            : (front >= 86f ? 0f : -.045f * Lobe((position - front) / Math.Max(2f, 90 - front)));

        // Couples Gleba's Saldo-pulled ITCZ, dynamic subpolar front, and segmented 18-oval
        // sea-level pressure geostrophic + cross-isobaric Ekman gyre winds into surface circulation.
        internal void SurfaceWithGleba(
            float heatLowLatitude, float maritime,
            float glebaItczDeg, float glebaPfNorthDeg, float glebaPfSouthDeg,
            float pgEastHpa, float pgNorthHpa, float oceanContrast, float monsoonOverride,
            out float east, out float north)
        {
            if (oceanContrast <= 0f)
            {
                Surface(heatLowLatitude, maritime, out east, out north);
                return;
            }

            float degrees = Math.Abs(latitude);
            float axis = FMath.Lerp(4 * solstice, heatLowLatitude, .25f);
            float subtropGate = 1f - SmoothStep((degrees - 30f) / 11f);
            axis = FMath.Lerp(axis, glebaItczDeg, .52f * maritime * oceanContrast * subtropGate);

            east = EastAt(axis, maritime);

            float side = latitude >= axis ? 1 : -1;
            float position = side * latitude, equator = side * axis;
            float ridge = Math.Max(SubtropicalRidge(ridgeBase, side * solstice), equator + 8);
            if (position < ridge)
                north = -side * .105f * Lobe((position - equator) / Math.Max(4f, ridge - equator));
            else
                north = side * FMath.Lerp(PolarReturn(position, ridge, PolarFront(frontBase, side * solstice)),
                    PolarReturn(position, ridge, marineFront), maritime);

            // Geostrophic + frictional cross-isobaric Ekman wind around Gleba's segmented SLP ovals.
            // Gate meridional pressure-gradient (zonal geoX) to <41 deg so Gleba's 1100 hPa polar cap
            // never reverses the 40-68 deg westerlies, while preserving zonal pressure-gradient (meridional geoY)
            // around both Subtropical Highs and Icelandic/Aleutian Subpolar Lows.
            float wGeo = degrees < 2.5f ? 0f : FMath.Clamp((degrees - 2.5f) / 5f, 0f, 1f);
            float geoX = latitude >= 0f ? +pgNorthHpa : -pgNorthHpa;
            float geoY = latitude >= 0f ? -pgEastHpa : +pgEastHpa;
            float cGeo = FMath.Lerp(.70f, .84f, maritime) * wGeo;
            float cPg = FMath.Lerp(.50f, .34f, maritime);
            float baseScale = .0185f * oceanContrast * (1f - FMath.Clamp(monsoonOverride, 0f, .85f));
            float zonalScale = baseScale * subtropGate;
            float meridWeight = FMath.Lerp(.35f * (1f - SmoothStep((degrees - 68f) / 12f)), 1f, subtropGate);
            float meridScale = baseScale * meridWeight;
            east += FMath.Clamp((cGeo * geoX * subtropGate + cPg * pgEastHpa) * zonalScale, -.16f, .16f);
            north += FMath.Clamp((cGeo * geoY + cPg * pgNorthHpa * subtropGate) * meridScale, -.22f, .22f);
        }

        static float SmoothStep(float t)
        {
            t = FMath.Clamp(t, 0f, 1f);
            return t * t * (3f - 2f * t);
        }
    }
}
