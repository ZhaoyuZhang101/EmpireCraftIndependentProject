using System;

namespace Orbis.Flat
{
    // Monthly mean surface circulation. Latitude is in degrees north and the
    // solstitial cosine is +1 in northern summer, -1 in southern summer.
    internal readonly struct FlatWindBelts
    {
        static float SubtropicalRidge(float summer) => 30 + 4 * summer;
        static float PolarFront(float summer) => 65 + 4 * summer;
        const float MarinePolarFront = 68;
        readonly float latitude, solstice, inlandEastStep, marineEastStep;

        internal FlatWindBelts(float latitude, float solstitialCosine)
        {
            this.latitude = latitude;
            solstice = solstitialCosine;
            float summer = FMath.Clamp(latitude / 8, -1, 1) * solstitialCosine;
            float degrees = Math.Abs(latitude);
            float trade = (float)Math.Tanh((degrees - SubtropicalRidge(summer)) / 8);
            float polar = (float)(.5 + .5 * Math.Tanh((degrees - 65) / 8));
            // The winter temperature contrast strengthens westerlies. The
            // polar easterlies are weaker than tropical trades on average.
            float EastStep(float front)
            {
                float belt = trade * (float)Math.Tanh((front - degrees) / 8);
                return .24f * belt * (1 - .55f * polar) * (1 - .55f * summer * Math.Max(0, belt));
            }
            inlandEastStep = EastStep(PolarFront(summer));
            // Ocean thermal inertia damps the seasonal shift of the subpolar
            // front. The two regimes blend inland over the maritime fetch.
            marineEastStep = EastStep(MarinePolarFront);
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
            float ridge = Math.Max(SubtropicalRidge(side * solstice), equator + 8);
            if (position < ridge)
                north = -side * .10f * Lobe((position - equator) / (ridge - equator));
            else
                north = side * FMath.Lerp(PolarReturn(position, ridge, PolarFront(side * solstice)),
                    PolarReturn(position, ridge, MarinePolarFront), maritime);
        }

        static float PolarReturn(float position, float ridge, float front) => position < front
            ? .055f * Lobe((position - ridge) / (front - ridge))
            : -.045f * Lobe((position - front) / (90 - front));
    }
}
