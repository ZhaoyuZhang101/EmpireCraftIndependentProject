using System;
using System.Collections.Generic;
using System.IO;
using Orbis.Flat;
using Orbis.Planet;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.Editor
{
    public static class AtlasDetailRegression
    {
        [MenuItem("Tools/ORBIS/Local Planet/Validate strategic atlas handoff")]
        public static void Run()
        {
            var report = new List<string>();
            var root = new GameObject("Strategic atlas handoff regression");
            // A 464-pixel viewport exposes the centre-fan roundoff at 81.5625°N.
            // A fixed render target makes the case independent of the editor window.
            var target = RenderTexture.GetTemporary(960, 480, 0);
            const int width = 64;
            try
            {
                var cameraObject = new GameObject("Strategic handoff camera");
                cameraObject.transform.SetParent(root.transform, false);
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.targetTexture = target;
                camera.pixelRect = new Rect(32, 16, 928, 464);
                var world = FlatGenerator.Generate(width, width / 2, "ATLAS-HANDOFF", TerrainPreset.Continents, default);
                var map = root.AddComponent<FlatMapRenderer>();
                map.Initialize(world);
                var nav = root.AddComponent<FlatScrollNavigator>();
                nav.Initialize(world, camera, map);
                nav.Recenter(PlanetCoordinateSystem.FromMapUV(.998, .93), 0);
                Bookmark(report, nav, camera, width, true);
                GlobeExits(report, nav, camera, width);
                foreach (int row in new[] { 1, 16, 30 })
                {
                    int tile = row * width + width - 1;
                    var location = PlanetCoordinateSystem.FromMapUV((width - .5) / width, (row + .5) / world.Height);
                    nav.OpenAtlas(0);
                    Check(report, nav.IsAtlas && nav.SurfaceMaterial.shader.name.Contains("ScrollSurface"), "overview uses the Markley renderer");
                    nav.Focus(tile, 0);
                    nav.Tick(3, 0);
                    nav.Tick(3, 0);
                    bool found = nav.TryGetPlanetLocation(camera.pixelRect.center, out var focused);
                    double error = found ? PixelError(location, focused, nav.PixelsPerTile, width) : double.NaN;
                    var north = PlanetCoordinateSystem.LocalFrame(location, default, 1).North;
                    Check(report, nav.IsFlat && !nav.IsAtlas && nav.Pick(camera.pixelRect.center) == tile &&
                        found && error <= .20 && PlanetVector.Dot(north, nav.LocalFrame.North) > .999,
                        $"F focuses the real tile in north-up local detail; row={row}, hit={found}, error={error:F4} px");
                }
                nav.OpenAtlas(0);
                nav.Recenter(PlanetCoordinateSystem.FromMapUV(.998, .93), 0);
                var cursor = camera.pixelRect.center + new Vector2(83, 37);
                bool hit = nav.TryGetPlanetLocation(cursor, out var anchor);
                for (int i = 0; nav.IsAtlas && i < 48; i++)
                {
                    nav.Wheel(-2, cursor, 0);
                    nav.Tick(.12f, 0);
                }
                for (int i = 0; i < 40; i++) nav.Tick(.04f, 0);
                bool localHit = nav.TryGetPlanetLocation(cursor, out var zoomed);
                double drift = PixelError(anchor, zoomed, nav.PixelsPerTile, width);
                Check(report, hit && localHit && nav.IsFlat && !nav.IsAtlas && drift <= 1,
                    $"off-centre atlas wheel enters local detail and holds the ground under the cursor; drift={drift:F4} px");
                nav.Wheel(1, cursor, 0);
                nav.Tick(1, 0);
                Check(report, !nav.IsAtlas && nav.IsFlat, "one reverse notch stays on the local side of the handoff");
                nav.TryGetPlanetLocation(cursor, out var beforeReturn);
                for (int i = 0; !nav.IsAtlas && i < 30; i++)
                {
                    nav.Wheel(2, cursor, 0);
                    nav.Tick(.15f, 0);
                }
                nav.Tick(2, 0);
                bool atlasHit = nav.TryGetPlanetLocation(cursor, out var afterReturn);
                drift = PixelError(beforeReturn, afterReturn, nav.PixelsPerTile, width);
                Check(report, nav.IsAtlas && atlasHit && drift <= 1, $"outward detail wheel returns to Markley at the cursor; drift={drift:F4} px");
                nav.GoGlobe(0);
                nav.Tick(3, 0);
                nav.Focus(30 * width + width - 1, 0);
                nav.Tick(3, 0);
                nav.Tick(3, 0);
                Bookmark(report, nav, camera, width, false);
                // Remove relief from this disposable fixture to isolate the chart's
                // angular scale and orientation on the frame the renderer switches.
                Array.Clear(world.Elevation, 0, world.Count);
                Array.Clear(world.Facts.Depth, 0, world.Count);
                world.TerrainRevision++;
                FlatGroundHandoff(report, nav, camera);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                RenderTexture.ReleaseTemporary(target);
                Directory.CreateDirectory("ValidationOutput/LocalPlanet");
                File.WriteAllLines("ValidationOutput/LocalPlanet/strategic-atlas-handoff.txt", report);
            }
            if (report.Exists(line => line.StartsWith("FAIL", StringComparison.Ordinal)))
                throw new InvalidOperationException(string.Join("\n", report));
            Debug.Log(string.Join("\n", report));
        }

        static double PixelError(PlanetLocation a, PlanetLocation b, float pixels, int width)
        {
            return PlanetLocalProjection.Distance(PlanetCoordinateSystem.SurfaceNormal(a), PlanetCoordinateSystem.SurfaceNormal(b), 1)
                * pixels * width / PlanetCoordinateSystem.Tau;
        }

        static void GlobeExits(List<string> report, FlatScrollNavigator nav, Camera camera, int width)
        {
            var center = camera.pixelRect.center;
            float groundScale = camera.pixelHeight * (2 * Mathf.PI / width) /
                (2 * (nav.Distance - 1) * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f));
            Check(report, Mathf.Abs(groundScale / nav.PixelsPerTile - 1) < .001f &&
                nav.SurfaceMaterial.GetFloat("_LocalPhysicalArtwork") == 1,
                $"globe surface detail follows actual ground pixel scale and latitude; expected={groundScale:F2} actual={nav.PixelsPerTile:F2}");
            foreach (bool wheel in new[] { false, true })
            {
                nav.GoGlobe(0);
                nav.Tick(3, 0);
                nav.Recenter(PlanetCoordinateSystem.FromMapUV(.97, .86), 0);
                bool from = nav.TryGetPlanetLocation(center, out var original);
                float scale = nav.PixelsPerTile;
                nav.ToggleStrategic(0);
                if (wheel)
                {
                    for (int i = 0; i < 24 && nav.IsAtlas; i++) { nav.Wheel(-3, center, 0); nav.Tick(.2f, 0); }
                }
                else nav.Focus(3 * width + 13, 0);
                nav.Tick(3, 0);
                nav.Tick(3, 0);
                bool reachedDetail = nav.IsDetailed && !nav.IsAtlas;
                nav.PanBy(new Vector2(4, 3));
                nav.ToggleStrategic(.3);
                nav.Tick(3, .3);
                bool at = nav.TryGetPlanetLocation(center, out var restored);
                double error = PixelError(original, restored, scale, width);
                Check(report, from && reachedDetail && nav.IsGlobe && at && error < .10 &&
                    Mathf.Abs(scale - nav.PixelsPerTile) < .001f,
                    $"M from atlas detail returns to the bookmarked globe; wheel={wheel} detail={reachedDetail} end={nav.ModeName} error={error:F3}px");
            }
            nav.GoGlobe(0); nav.Tick(3, 0);
            nav.OpenAtlas(0); nav.Wheel(1, center, 0); nav.Tick(3, 0);
            Check(report, nav.IsGlobe, "outward wheel from the fitted strategic map returns to the globe");
            nav.OpenAtlas(0); nav.Home(0); nav.Tick(3, 0);
            Check(report, nav.IsGlobe && !camera.orthographic, "Home leaves the strategic map for the globe");
            nav.GoGlobe(0); nav.Tick(3, 0);
            nav.Focus(18 * width + 5, 0); nav.Tick(3, 0);
            nav.GoGlobe(0); nav.Tick(.08f, 0);
            bool wasReturning = nav.IsTransitioning;
            nav.ToggleStrategic(0); nav.ToggleStrategic(0); nav.Tick(3, 0);
            Check(report, wasReturning && nav.IsGlobe, "M during a globe return resumes the interrupted zoom to its endpoint");
        }

        public static void KeyboardInPlay()
        {
            var app = UnityEngine.Object.FindFirstObjectByType<FlatWorldApp>();
            var panel = app.GetComponentInChildren<UIDocument>().rootVisualElement;
            var nav = app.Navigation;
            nav.GoGlobe(app.CalendarDay); nav.Tick(3, app.CalendarDay);
            var sidebar = panel.Q<Button>("tabWorld");
            void Key(VisualElement target, bool down)
            {
                var input = new Event { type = down ? EventType.KeyDown : EventType.KeyUp, keyCode = KeyCode.M };
                using (EventBase evt = down ? (EventBase)KeyDownEvent.GetPooled(input) : KeyUpEvent.GetPooled(input))
                { evt.target = target; target.SendEvent(evt); }
            }
            Key(sidebar, true);
            bool opened = nav.IsAtlas;
            Key(panel.Q("viewport"), true);
            bool held = nav.IsAtlas;
            Key(sidebar, false);
            Key(sidebar, true); Key(sidebar, false);
            bool exited = nav.IsGlobe;
            var seed = panel.Q<TextField>("seed");
            Key(seed, true); Key(seed, false);
            if (!opened || !held || !exited || !nav.IsGlobe)
                throw new InvalidOperationException($"M HUD shortcut: sidebar open={opened}, key repeat stayed open={held}, returned={exited}, text entry ignored={nav.IsGlobe}");
        }

        static void Bookmark(List<string> report, FlatScrollNavigator nav, Camera camera, int width, bool globe)
        {
            var screen = camera.pixelRect.center;
            bool source = nav.TryGetPlanetLocation(screen, out var before);
            float scale = nav.PixelsPerTile;
            nav.ToggleStrategic(0);
            nav.Recenter(PlanetCoordinateSystem.FromMapUV(.3, .22), .3);
            nav.ToggleStrategic(.3);
            bool returned = nav.TryGetPlanetLocation(screen, out var after);
            double error = source && returned ? PixelError(before, after, scale, width) : double.NaN;
            Check(report, source && returned && !nav.IsAtlas && globe == nav.IsGlobe &&
                Mathf.Abs(scale - nav.PixelsPerTile) < .001f && error < .05,
                $"M restores the original position and scale across a clock change; globe={globe}, hits={source}/{returned}, scale={scale:F4}/{nav.PixelsPerTile:F4}, error={error:F4} px");
        }

        static void FlatGroundHandoff(List<string> report, FlatScrollNavigator nav, Camera camera)
        {
            nav.OpenAtlas(0);
            nav.Recenter(PlanetCoordinateSystem.FromMapUV(.993, .944), 0);
            var atlas = nav.GetComponentInChildren<MarkleyAtlasNavigator>();
            var cursor = camera.pixelRect.center + new Vector2(73, -45);
            double scaleRatio = double.NaN, headingDot = double.NaN;
            for (int step = 0; nav.IsAtlas && step < 64; step++)
            {
                float priorPixels = atlas.PixelsPerTile;
                var prior = ScreenTangent(nav, cursor);
                nav.Wheel(-1, cursor, 0);
                nav.Tick(.12f, 0);
                if (nav.IsAtlas) continue;
                var after = ScreenTangent(nav, cursor);
                double priorLength = Math.Sqrt(PlanetVector.Dot(prior, prior));
                double length = Math.Sqrt(PlanetVector.Dot(after, after));
                scaleRatio = (length * atlas.PixelsPerTile) / (priorLength * priorPixels);
                headingDot = PlanetVector.Dot(prior, after) / (priorLength * length);
            }
            Check(report, !nav.IsAtlas && Math.Abs(scaleRatio - 1) < .03 && headingDot > .998,
                $"first local frame matches the Markley ground scale and heading at the cursor; ratio={scaleRatio:F6}, dot={headingDot:F6}");
            for (int i = 0; i < 60; i++) nav.Tick(.05f, 0);
            var geographic = PlanetCoordinateSystem.LocalFrame(PlanetCoordinateSystem.FromWorld(nav.LocalFrame.Up, default, 1), default, 1);
            Check(report, !nav.IsAtlas && PlanetVector.Dot(geographic.North, nav.LocalFrame.North) > .999,
                "local detail settles north-up after the scale-matched handoff");
        }

        static PlanetVector ScreenTangent(FlatScrollNavigator nav, Vector2 point)
        {
            if (!nav.TryGetPlanetLocation(point - 2 * Vector2.right, out var left) ||
                !nav.TryGetPlanetLocation(point + 2 * Vector2.right, out var right)) return default;
            return PlanetCoordinateSystem.SurfaceNormal(right) - PlanetCoordinateSystem.SurfaceNormal(left);
        }

        static void Check(List<string> report, bool ok, string description)
        {
            report.Add((ok ? "PASS " : "FAIL ") + description);
        }
    }
}
