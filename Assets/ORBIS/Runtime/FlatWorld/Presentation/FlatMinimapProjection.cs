using Orbis.Planet;
using UnityEngine;

namespace Orbis.Flat
{
    // Inverts a few points along the viewport at minimap resolution. Picking an individual
    // terrain tile during unfolding uses the full deformed mesh and is too costly for a HUD.
    internal readonly struct FlatMinimapProjection
    {
        const float Tau = 2 * Mathf.PI;
        readonly FlatScrollNavigator localChart;
        readonly WorldData world;
        readonly Vector2 chartCenter, halfSize, poles;
        readonly Vector3 normal, east, north, eye, right, up;
        readonly Vector4 relief;
        readonly Matrix4x4 viewProjection;
        readonly Quaternion spin, inverseSpin;
        readonly float morph, radius, aspect, halfTan;
        internal bool IsFlatAtlas => localChart == null && morph >= 1;

        internal FlatMinimapProjection(Camera camera, WorldData world, FlatScrollNavigator localChart,
            Vector2 chartCenter, float spinRadians, float morph, Vector3 normal, Vector3 east,
            Vector3 north, Vector2 poles, Vector4 relief, float radius)
        {
            this.world = world;
            this.localChart = localChart;
            this.chartCenter = chartCenter;
            this.morph = morph;
            this.normal = normal;
            this.east = east;
            this.north = north;
            this.poles = poles;
            this.relief = relief;
            this.radius = radius;
            aspect = camera.aspect;
            halfTan = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f);
            halfSize = new Vector2(aspect, 1) * camera.orthographicSize;
            eye = camera.transform.position;
            right = camera.transform.right;
            up = camera.transform.up;
            viewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;
            spin = Quaternion.AngleAxis(spinRadians * Mathf.Rad2Deg, Vector3.up);
            inverseSpin = Quaternion.Inverse(spin);
        }

        static Vector2 GeographicUV(PlanetVector direction)
        {
            var location = PlanetCoordinateSystem.FromWorld(direction, default, 1);
            return new Vector2((float)PlanetCoordinateSystem.MapU(location), (float)PlanetCoordinateSystem.MapV(location));
        }

        Vector2 GlobeUV(Vector2 ndc)
        {
            // The viewport corner can see space. Stop at the true tangent in the same
            // screen direction, including when only part of the planet fits the window.
            Vector3 offset = (right * (ndc.x * aspect) + up * ndc.y) * halfTan;
            float distance = eye.magnitude;
            float slope = Mathf.Min(offset.magnitude, 1 / Mathf.Sqrt(distance * distance - 1));
            float angle = Mathf.Atan(slope);
            float arc = Mathf.Asin(Mathf.Clamp01(distance * Mathf.Sin(angle))) - angle;
            Vector3 point = inverseSpin * (eye.normalized * Mathf.Cos(arc) + offset.normalized * Mathf.Sin(arc));
            return GeographicUV(new PlanetVector(point.x, point.y, -point.z));
        }

        static Vector2 SheetOffset(Vector2 radians)
        {
            return new Vector2(radians.x / Tau, radians.y * (float)MarkleyProjection.Aspect / Tau);
        }

        PlanetVector Direction(Vector2 point)
        {
            if (localChart != null) return localChart.LocalDirection(point);
            var uv = chartCenter + SheetOffset(point);
            return MarkleyProjection.Direction(uv.x, uv.y);
        }

        float Height(Vector2 uv)
        {
            return FlatGlobeGeometry.Radius(FlatGlobeGeometry.SurfaceHeight(world, uv), uv.y, world.Height, poles) - 1;
        }

        Vector2 FlatUV(Vector2 projected)
        {
            Vector2 point = projected;
            // Undo the same small relief displacement that shifts shorelines and peaks on
            // the detailed map. The footprint only needs sub-minimap-pixel accuracy.
            for (int i = 0; relief.x > 0 && i < 3; i++)
            {
                float height = (Height(GeographicUV(Direction(point))) - relief.y) * relief.w;
                Vector2 next = projected * (1 - height / FlatGlobeGeometry.ReliefCameraHeight) - Vector2.up * (height * relief.z);
                if ((next - point).sqrMagnitude < 1e-12f) break;
                point = next;
            }
            return GeographicUV(Direction(point));
        }

        Vector2 ProjectFold(Vector2 point)
        {
            var direction = Direction(point);
            var uv = GeographicUV(direction);
            float height = Height(uv);
            float sphereRadius = localChart != null ? localChart.FoldSphereRadius(uv, 1 + height) : 1 + height;
            var sphere = spin * new Vector3((float)direction.X, (float)direction.Y, -(float)direction.Z) * sphereRadius;
            var r = FlatGlobeGeometry.ReliefPosition(point, height, relief);
            var plane = normal * (1 + r.z) + east * r.x + north * r.y;
            return viewProjection.MultiplyPoint(Vector3.Lerp(sphere, plane, morph));
        }

        Vector2 FoldUV(Vector2 ndc, Vector2 guess)
        {
            const float step = .0005f;
            Vector2 point = guess;
            float error = (ProjectFold(point) - ndc).sqrMagnitude;
            for (int i = 0; i < 7 && error > 1e-8f; i++)
            {
                Vector2 screen = ProjectFold(point), offset = ndc - screen;
                Vector2 dx = (ProjectFold(point + Vector2.right * step) - screen) / step;
                Vector2 dy = (ProjectFold(point + Vector2.up * step) - screen) / step;
                float determinant = dx.x * dy.y - dy.x * dx.y;
                if (Mathf.Abs(determinant) < 1e-6f) break;
                var delta = new Vector2(offset.x * dy.y - offset.y * dy.x, dx.x * offset.y - dx.y * offset.x) / determinant;
                delta = Vector2.ClampMagnitude(delta, .45f);
                bool advanced = false;
                for (int j = 0; j < 4; j++, delta *= .5f)
                {
                    var candidate = Vector2.ClampMagnitude(point + delta, radius);
                    float next = (ProjectFold(candidate) - ndc).sqrMagnitude;
                    if (next >= error) continue;
                    error = next;
                    point = candidate;
                    advanced = true;
                    break;
                }
                if (!advanced) break; // At a clipped limb the nearest visible point is the boundary.
            }
            return GeographicUV(Direction(point));
        }

        internal Vector2 Sample(Vector2 ndc)
        {
            if (morph <= 0) return GlobeUV(ndc);
            var point = Vector2.Scale(ndc, halfSize);
            return morph >= 1 ? FlatUV(point) : FoldUV(ndc, point);
        }

        // The strategic atlas repeats and reflects across sheet boundaries. A viewport
        // edge inside another visible copy is not an edge of the geographic footprint.
        internal bool CoveredByAtlasCopy(Vector2 ndcOutside)
        {
            var half = SheetOffset(halfSize);
            var uv = chartCenter + Vector2.Scale(ndcOutside, half);
            var canonical = MarkleyProjection.Fold(uv.x, uv.y);
            int first = Mathf.FloorToInt(chartCenter.y - half.y);
            int last = Mathf.FloorToInt(chartCenter.y + half.y);
            for (int row = first; row <= last; row++)
            {
                int pair = Mathf.FloorToInt(row * .5f);
                bool odd = row - pair * 2 == 1;
                float y = odd ? row + 1 - canonical.y : row + canonical.y;
                if (Mathf.Abs(y - chartCenter.y) > half.y) continue;
                float x = (odd ? .75f - canonical.x : canonical.x) - pair * .5f;
                if (System.Math.Abs(PlanetCoordinateSystem.NearestOffset(x - chartCenter.x, 1)) <= half.x) return true;
            }
            return false;
        }
    }
}
