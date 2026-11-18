using System;
using Orbis.Planet;
using UnityEngine;

namespace Orbis.Flat
{
    public sealed partial class FlatScrollNavigator
    {
        // Scales here are pixels per equatorial tile at the local chart centre. The
        // gap between thresholds absorbs a reversed wheel notch without switching twice.
        const float AtlasEnterScale = 1.42f, AtlasExitScale = 1.08f;
        bool detailFromAtlas, atlasEnterPending, atlasExitPending, atlasAligning;
        public bool HasStrategicReturn => atlasMode || detailFromAtlas;
        int atlasFocus = -1;
        float atlasExitZoom, atlasWheelPixels;
        Vector2 atlasScreen;
        struct AtlasBookmark
        {
            internal PlanetTangentFrame frame;
            internal Quaternion orbit;
            internal float zoom, targetZoom, zoomVelocity, spin;
            internal double day;
            internal bool locked, zoomAnchor;
            internal Vector2 zoomScreen;
            internal PlanetVector groundAnchor;
        }
        AtlasBookmark atlasBookmark;

        public void ToggleStrategic(double day)
        {
            if (!HasStrategicReturn) { OpenStrategic(day); return; }
            SetStrategicSurface(false);
            detailFromAtlas = atlasAligning = atlasEnterPending = atlasExitPending = false;
            projection = new PlanetLocalProjection(PlanetCoordinateSystem.FromWorld(atlasBookmark.frame.Up, default, 1));
            projection.AlignEast(atlasBookmark.frame.East);
            lockDay = atlasBookmark.day;
            locked = atlasBookmark.locked;
            spin = atlasBookmark.spin;
            orbitRotation = atlasBookmark.orbit;
            if (!locked)
            {
                float elapsed = (float)(FlatSolarClock.Phase(day) * Tau) - spin;
                orbitRotation = Quaternion.AngleAxis(elapsed * Mathf.Rad2Deg, Vector3.up) * orbitRotation;
            }
            zoom = atlasBookmark.zoom;
            targetZoom = atlasBookmark.targetZoom;
            zoomVelocity = atlasBookmark.zoomVelocity;
            zoomAnchor = atlasBookmark.zoomAnchor;
            zoomScreen = atlasBookmark.zoomScreen;
            zoomGroundAnchor = atlasBookmark.groundAnchor;
            Tick(0, day);
            SyncMaterials();
        }

        void OpenStrategic(double day)
        {
            if (!TryGetPlanetLocation(viewCamera.pixelRect.center, out var center)) center = projection.Center;
            if (!HasStrategicReturn)
            {
                atlasBookmark = new AtlasBookmark { frame = projection.Frame, orbit = orbitRotation,
                    zoom = zoom, targetZoom = targetZoom, zoomVelocity = zoomVelocity,
                    zoomScreen = zoomScreen, groundAnchor = zoomGroundAnchor, zoomAnchor = zoomAnchor,
                    spin = spin, day = lockDay, locked = locked };
            }
            if (!atlas)
            {
                var go = new GameObject("Global strategic atlas");
                go.transform.SetParent(transform, false);
                atlas = go.AddComponent<MarkleyAtlasNavigator>();
                atlas.Initialize(world, viewCamera, maps);
                atlas.SetRoute(route);
            }
            atlasEnterPending = atlasExitPending = atlasAligning = false;
            SetStrategicSurface(true);
            atlas.ShowStrategic(center, 0, day);
            SyncMaterials();
        }

        void SetStrategicSurface(bool strategic)
        {
            atlasMode = strategic;
            atlas.gameObject.SetActive(strategic);
            terrain.enabled = !strategic;
            if (routeRenderer) routeRenderer.enabled = !strategic;
        }

        PlanetLocation TileLocation(int tile) => PlanetCoordinateSystem.FromMapUV(
            (tile % world.Width + .5) / world.Width, (tile / world.Width + .5) / world.Height);

        void FocusStrategic(int tile, double day)
        {
            atlasFocus = tile;
            atlasScreen = viewCamera.pixelRect.center;
            atlasEnterPending = true;
            var point = atlas.NearestChartPoint(TileLocation(tile));
            atlas.DetailMetric(point, out _, out _, out float magnification);
            float localPixels = Mathf.Max(48, EntryPixels * (AtlasEnterScale + .12f));
            atlas.FocusStrategic(tile, localPixels / magnification);
        }

        bool RouteAtlasWheel(float amount, Vector2 screen, double day)
        {
            float wheel = Mathf.Clamp(amount, -24, 24);
            if (atlasMode)
            {
                if (Mathf.Abs(wheel) < .00001f) return true;
                if (wheel > 0 && atlas.AtOverviewLimit) { GoGlobe(day); return true; }
                float requested = atlasEnterPending && atlasFocus < 0 ? atlasWheelPixels : atlas.RequestedPixels;
                float displayed = atlas.PixelsPerTile;
                bool reversing = wheel < 0 ? requested < displayed : requested > displayed;
                atlasScreen = screen;
                atlasFocus = -1;
                atlasEnterPending = false;
                // Allow small worlds to reach the same angular field of view as large
                // ones. Bound the pending target even if many wheel events share a frame.
                float limit = EntryPixels * 32;
                atlasWheelPixels = Mathf.Min(limit, (reversing ? displayed : requested) * Mathf.Exp(-wheel * ZoomRate));
                float chartTarget = atlasWheelPixels;
                if (wheel < 0 && atlas.TryReliefPoint(screen, out var point))
                {
                    atlas.DetailMetric(point, out _, out _, out float magnification);
                    float entry = ChartEntryPixels(magnification);
                    atlasEnterPending = atlasWheelPixels >= entry;
                    // Spend the rest of a large wheel burst on the local chart. Advancing
                    // the atlas all the way first can jump past the local camera's max zoom.
                    if (atlasEnterPending) chartTarget = Mathf.Max(displayed, Mathf.Min(chartTarget, entry * 1.002f));
                }
                atlas.ZoomStrategic(chartTarget, screen);
                return true;
            }
            if (!detailFromAtlas || Mathf.Abs(wheel) < .00001f) return false;
            atlasScreen = screen;
            if (wheel < 0)
            {
                if (atlasExitPending) atlasAligning = true;
                atlasExitPending = false;
                return false;
            }
            float from = atlasExitPending ? atlasExitZoom : Mathf.Max(zoom, targetZoom);
            float desired = Mathf.Min(MaxZoom, from + wheel);
            float boundary = ZoomForPixels(EntryPixels * AtlasExitScale);
            if (desired < boundary) return false;
            atlasExitPending = true;
            atlasExitZoom = desired;
            if ((!zoomAnchor || screen != zoomScreen) && TryGetPlanetLocation(screen, out var anchor))
                SetLocalZoomAnchor(screen, PlanetCoordinateSystem.SurfaceNormal(anchor));
            targetZoom = boundary;
            return true;
        }

        float ChartEntryPixels(float magnification)
        {
            float center = EntryPixels * AtlasEnterScale;
            float offset = (atlasScreen - viewCamera.pixelRect.center).magnitude * TileScale;
            return (center + offset * offset / (4 * center)) / magnification;
        }

        void TickStrategic(float dt, double day)
        {
            atlas.Tick(dt, day);
            if (!atlasEnterPending || atlas.IsFocusing) return;
            Vector2 point;
            if (atlasFocus >= 0)
            {
                atlasScreen = viewCamera.pixelRect.center;
                point = atlas.NearestChartPoint(TileLocation(atlasFocus));
            }
            else if (!atlas.TryReliefPoint(atlasScreen, out point)) return;
            atlas.DetailMetric(point, out var location, out var right, out float magnification);
            // Stereographic scale at offset r is 1 + r*r/4 on the unit sphere.
            // Solve for the centre scale that gives the same ground scale at the cursor.
            float ground = atlas.PixelsPerTile * magnification;
            float offset = (atlasScreen - viewCamera.pixelRect.center).magnitude * TileScale;
            float localPixels = .5f * (ground + Mathf.Sqrt(Mathf.Max(0, ground * ground - offset * offset)));
            if (atlasFocus < 0 && localPixels < EntryPixels * AtlasEnterScale * .9999f)
            {
                if (!atlas.IsZooming)
                {
                    float boundary = ChartEntryPixels(magnification);
                    atlasEnterPending = atlasWheelPixels >= boundary;
                    atlas.ZoomStrategic(Mathf.Min(atlasWheelPixels, boundary * 1.002f), atlasScreen);
                }
                return;
            }
            if (atlasFocus >= 0) location = TileLocation(atlasFocus);
            float remaining = atlasFocus >= 0 ? 1 : atlasWheelPixels / atlas.PixelsPerTile;
            var anchor = PlanetCoordinateSystem.SurfaceNormal(location);
            SetStrategicSurface(false);
            atlasEnterPending = atlasExitPending = false;
            detailFromAtlas = atlasAligning = true;
            projection = new PlanetLocalProjection(location);
            projection.AlignEast(right);
            locked = true;
            lockDay = day;
            spin = (float)(FlatSolarClock.Phase(day) * Tau);
            morph = 1;
            distance = End;
            zoom = Mathf.Clamp(ZoomForPixels(localPixels), MinZoom, 0);
            zoomVelocity = 0;
            Frame();
            float altitude = Altitude(zoom);
            PlaceFlatCamera(altitude);
            AnchorLocalPoint(anchor, atlasScreen, altitude, true);
            orbitRotation = Quaternion.LookRotation(-normal, north);
            SetLocalZoomAnchor(atlasScreen, anchor);
            targetZoom = Mathf.Clamp(zoom - Mathf.Log(remaining) / ZoomRate, MinZoom, 0);
            Tick(0, day);
            SyncMaterials();
        }

        bool TickAtlasDetail(float dt, double day, float altitude)
        {
            if (!detailFromAtlas || (!atlasAligning && !atlasExitPending) || dt <= 0) return false;
            PlanetVector anchor;
            PlanetLocation location;
            if (zoomAnchor && zoomScreen == atlasScreen)
            {
                anchor = zoomGroundAnchor;
                location = PlanetCoordinateSystem.FromWorld(anchor, default, 1);
            }
            else
            {
                if (!TryGetPlanetLocation(atlasScreen, out location)) return false;
                anchor = PlanetCoordinateSystem.SurfaceNormal(location);
            }
            var frame = projection.Frame;
            var chartPoint = Vector2.zero;
            float magnification = 1;
            double turnWeight = 1;
            PlanetVector desiredRight;
            if (atlasExitPending)
            {
                chartPoint = atlas.NearestChartPoint(location);
                atlas.DetailMetric(chartPoint, out _, out var atAnchor, out magnification);
                // For a stereographic chart the screen basis at the cursor is the
                // parallel transport of the basis at the centre along the short arc.
                var rotated = Quaternion.FromToRotation(V(anchor), V(frame.Up)) * V(atAnchor);
                desiredRight = new PlanetVector(rotated.x, rotated.y, rotated.z);
            }
            else
            {
                var center = projection.Center;
                turnWeight = projection.NorthAlignmentWeight;
                if (turnWeight < .0001) { atlasAligning = false; return false; }
                desiredRight = PlanetCoordinateSystem.LocalFrame(center, default, 1).East;
            }
            double angle = Math.Atan2(PlanetVector.Dot(desiredRight, frame.North), PlanetVector.Dot(desiredRight, frame.East));
            if (Math.Abs(angle) > .0005)
            {
                double turn = angle * (1 - Math.Exp(-9 * turnWeight * dt));
                projection.AlignEast(frame.East * Math.Cos(turn) + frame.North * Math.Sin(turn));
                Frame();
                PlaceFlatCamera(altitude);
                AnchorLocalPoint(anchor, atlasScreen, altitude, true);
                // Rotating about an off-centre cursor moves the chart centre. Its
                // updated meridian determines how much turn is left on the next frame.
                return false;
            }
            if (!atlasExitPending) { atlasAligning = false; return false; }
            if (Mathf.Abs(zoom - targetZoom) > .002f) return false;
            var xy = projection.StereographicLog(anchor);
            float ground = pixels * (float)(1 + (xy.X * xy.X + xy.Y * xy.Y) * .25);
            float chartPixels = ground / magnification;
            float remaining = Mathf.Exp((zoom - atlasExitZoom) * ZoomRate);
            detailFromAtlas = atlasExitPending = atlasAligning = atlasEnterPending = false;
            SetStrategicSurface(true);
            atlas.ShowStrategic(location, chartPixels, day);
            atlas.AnchorSheet(atlas.NearestChartPoint(location), atlasScreen);
            atlas.Tick(0, day);
            atlas.ZoomStrategic(chartPixels * remaining, atlasScreen);
            SyncMaterials();
            return true;
        }
    }
}
