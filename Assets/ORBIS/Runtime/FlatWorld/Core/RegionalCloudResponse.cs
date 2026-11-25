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

        /// <summary>
        /// 6-hourly synoptic-coupled regional cloud response combining local statistical weather departures
        /// with wind-advected moist uplift growth, post-precipitation / dry-subsidence clearing, and active cyclones.
        /// </summary>
        public static RegionalCloudResponse FromSynoptic(
            in ClimateTileDiagnostics cell, WorldData world, double day, FlatCycloneWeather storms)
        {
            var baseResp = From(in cell);
            if (world == null || cell.Tile < 0 || cell.Tile >= world.Count) return baseResp;
            ref readonly var air = ref cell.Observation;
            double rh = air.Humidity;
            if (!(rh >= 0 && rh <= 1)) return baseResp;

            int tx = cell.Tile % world.Width;
            int ty = cell.Tile / world.Width;
            double u = (tx + 0.5) / world.Width;
            double v = (ty + 0.5) / world.Height;
            float oceanFrac = FlatTerrainAnalysis.Ocean(world.Water[cell.Tile]) ? 1f : 0f;

            var season = new FlatMonthlyForcing(day);
            var m = world.Moisture;
            float we = m != null ? season.Sample(m, m.East, (float)u, (float)v) : 0f;
            float wn = m != null ? season.Sample(m, m.North, (float)u, (float)v) : 0f;
            var cfg = m?.ExoPlaSim?.Config ?? world.PlanetClimate;
            float subDeg = cfg != null && !cfg.IsStandardModernEarth ? cfg.HadleyTerminusDeg : 30f;
            float polDeg = cfg != null && !cfg.IsStandardModernEarth ? cfg.PolarFrontTerminusDeg : 65f;
            float absLat = (float)Math.Abs((v - 0.5) * 180.0);
            float sthzRidge = (float)Math.Exp(-Math.Pow((absLat - subDeg) / 9.5, 2.0));
            float highP = FMath.Clamp((float)((air.Pressure - 1012.0) / 28.0), 0f, 1f);

            FlatSynopticWeather.EvaluateCloudLifecycle(
                FMath.Seed(world.Seed), day, u, v, we, wn, (float)rh, (float)Math.Max(0.0, air.Precipitation),
                oceanFrac, 2.5f, sthzRidge, highP, subDeg, polDeg,
                out float moistUplift, out float postRainClear, out _);

            float cycCore = 0f, cycMoat = 0f, cycWakeCoolC = 0f, cycWakeStab = 0f;
            if (storms != null)
                storms.SampleCloudFootprint(u, v, day, out cycCore, out cycMoat, out cycWakeCoolC, out cycWakeStab);

            double wakeSuppress = (0.28 * cycWakeStab + 0.14 * Math.Min(1.0f, cycWakeCoolC / 2.5f)) * oceanFrac;
            double synCover = (0.42 * moistUplift + 0.72 * cycCore) * rh
                            - (0.46 * postRainClear + 0.45 * cycMoat + wakeSuppress) * (1.0 - 0.45 * rh);
            double cover = Math.Max(-1.0, Math.Min(1.0, baseResp.Cover + synCover));
            double low = cell.WeatherReference != null && cell.Weather.Valid
                ? Math.Min(1.0, Math.Max(0.0, -0.5 * cell.WeatherReference.Standardized(ClimateFeature.Pressure, air)))
                : 0.0;
            double convection = rh > 0.02
                ? Math.Max(0.0, Math.Min(1.0, Math.Max(baseResp.Convection, Math.Max(0.0, cover) * Math.Max(low, 0.55 * moistUplift + 0.85 * cycCore))))
                : 0.0;
            return new RegionalCloudResponse(cover, convection);
        }
    }
}
