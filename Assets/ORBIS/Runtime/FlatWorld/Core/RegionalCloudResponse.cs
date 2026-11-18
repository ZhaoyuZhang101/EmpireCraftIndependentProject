using System;

namespace Orbis.Flat
{
    // Bounded adjustments to the existing cloud parameterization, not rainfall rates.
    public readonly struct RegionalCloudResponse
    {
        public readonly float Cover, Convection;

        RegionalCloudResponse(double cover, double convection)
        {
            Cover = (float)cover;
            Convection = (float)convection;
        }

        public static RegionalCloudResponse From(in ClimateTileDiagnostics cell)
        {
            var reference = cell.WeatherReference;
            var score = cell.Weather;
            if (!score.Valid || reference == null || reference.ActiveDimensions < 1) return default;
            ref readonly var air = ref cell.Observation;
            double rh = air.Humidity;
            if (!(rh >= 0 && rh <= 1)) return default;

            // The joint distance sets the strength. Its sign comes from the local
            // moisture departures; heat or wind alone has no prescribed cloud sign.
            double weight = score.Reliability * Math.Min(1, score.Distance / (2 * Math.Sqrt(reference.ActiveDimensions)));
            double vapor = reference.Standardized(ClimateFeature.Humidity, air);
            double rain = reference.Standardized(ClimateFeature.Precipitation, air);
            double moisture = Math.Min(1, Math.Max(-1, (vapor + rain) * .25));
            double cover = weight * moisture * (moisture > 0 ? rh : 1 - rh);
            double low = Math.Min(1, Math.Max(0, -.5 * reference.Standardized(ClimateFeature.Pressure, air)));
            double convection = Math.Max(0, cover) * low;
            return new RegionalCloudResponse(cover, convection);
        }
    }
}
