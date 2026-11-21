using System;

namespace Orbis.Planet
{
    // Hammer's equal-area sphere projection in a 2:1 ellipse. Both coordinate
    // pairs are normalized south-to-north; the two date-line edges stay distinct.
    public static class HammerProjection
    {
        public static void Project(double u, double v, out double x, out double y)
        {
            double halfLon = (u - .5) * Math.PI, latitude = (v - .5) * Math.PI;
            double cosLat = Math.Cos(latitude);
            double denominator = Math.Sqrt(1 + cosLat * Math.Cos(halfLon));
            x = .5 + .5 * cosLat * Math.Sin(halfLon) / denominator;
            y = .5 + .5 * Math.Sin(latitude) / denominator;
        }

        public static bool TryUnproject(double x, double y, out PlanetLocation location)
        {
            x = 2 * x - 1;
            y = 2 * y - 1;
            double radiusSquared = x * x + y * y;
            location = default;
            if (!(radiusSquared <= 1 + 1e-14)) return false;
            double factor = Math.Sqrt(2 - Math.Min(1, radiusSquared));
            double lon = 2 * Math.Atan2(x * factor, Math.Max(0, 1 - radiusSquared));
            double lat = Math.Asin(Math.Max(-1, Math.Min(1, y * factor)));
            location = new PlanetLocation(lon, lat);
            return true;
        }
    }
}
