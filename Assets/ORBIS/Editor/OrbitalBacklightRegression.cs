using System;
using System.Collections.Generic;
using System.IO;
using Orbis.Flat;
using UnityEngine;

namespace Orbis.Editor
{
    // Optional orbital and local night checks for the seeded SolarLensRegression Play Mode scene.
    // Images and measurements use final display values at fixed exposure, time, weather and ocean phase.
    internal static class OrbitalBacklightRegression
    {
        const int Width = 960, Height = 600;
        const float ObserverDistance = 4.55f; // Planet radii
        static readonly Vector3 Luminance = new Vector3(.2126f, .7152f, .0722f);

        internal static void Capture(Camera camera, FlatScrollNavigator nav, FlatGlobePresenter globe, FlatMapRenderer map,
            OrbisAtmosphereRenderer atmosphere, OrbisCelestialLens lens, string output)
        {
            var target = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var previousTarget = camera.targetTexture;
            var previousViewport = camera.pixelRect;
            var previousActive = RenderTexture.active;
            float previousStrength = lens.strength;
            int previousQuality = globe.Snapshot.settings.cloudQuality;
            bool previousFull = atmosphere.fullResolutionReference;
            var report = new List<string>();
            var failures = new List<string>();
            var land = new List<int>();
            var ocean = new List<int>();
            try
            {
                if (previousStrength <= 0) throw new InvalidOperationException("The lens must be enabled for the occlusion checks.");
                camera.targetTexture = target;
                nav.Home(0);
                nav.SetViewport(new Rect(0, 0, Width, Height));
                nav.Tick(3, 0);
                nav.Tick(3, 0);
                Vector3 inward = (globe.transform.position - camera.transform.position).normalized;
                camera.transform.position = globe.transform.position - inward * ObserverDistance;
                Vector3 upper = (camera.transform.up + .18f * camera.transform.right).normalized;
                globe.Snapshot.settings.cloudQuality = 1;
                SampleGeography(9, 5, true);

                foreach (bool fullResolution in new[] {false, true})
                {
                    atmosphere.fullResolutionReference = fullResolution;
                    string mode = fullResolution ? "full" : "balanced";
                    Color32[] Orbit(string label, float impact, bool air = true, bool clouds = false, bool flare = false, bool save = false)
                    {
                        float angle = Mathf.Asin(Mathf.Clamp01(impact / ObserverDistance));
                        Vector3 direction = impact < 0 ? -inward : inward * Mathf.Cos(angle) + upper * Mathf.Sin(angle);
                        Bind(globe.transform.InverseTransformVector(direction).normalized, air, clouds, flare);
                        return Render(label + "-" + mode, save);
                    }

                    var noon = Orbit("noon", -1, save: !fullResolution);
                    var midnight = Orbit("midnight", 0, save: !fullResolution);
                    CheckTerrain(mode, noon, midnight);

                    foreach (bool offscreen in new[] {false, true})
                    {
                        float impact = offscreen ? 1.82f : .972f;
                        string label = offscreen ? "sun-outside" : "rim";
                        var bare = Orbit(label + "-no-air", impact, air: false);
                        var rim = Orbit(label, impact, save: !fullResolution || !offscreen);
                        var withLens = Orbit(label + "-lens", impact, flare: true);
                        string scene = mode + " " + label;
                        int changes = 0;
                        for (int i = 0; i < rim.Length; ++i)
                            if (!rim[i].Equals(withLens[i])) ++changes;
                        Check(changes == 0, scene + " solar lens changed pixels=" + changes);

                        // Subtract an otherwise identical frame without air to remove the skybox and stars.
                        // Sample both sides of the limb at the same tangent heights and angular widths.
                        float coreLuma = 0;
                        foreach (float radius in new[] {1.008f, 1.020f, 1.050f, 1.10f, 1.25f})
                        {
                            Vector3 lit = ArcSignal(camera, inward, upper, radius, bare, rim);
                            Vector3 dark = ArcSignal(camera, inward, -upper, radius, bare, rim);
                            float luma = Vector3.Dot(lit, Luminance);
                            report.Add($"{scene} at {radius:F3}R sunward RGB={lit:F2} opposite RGB={dark:F2}");
                            if (radius == 1.008f)
                            {
                                coreLuma = luma;
                                Check(lit.x > lit.z + 12 && lit.x > dark.x + 40 && luma > 80, scene + " warm sunward arc");
                            }
                            if (radius == 1.050f)
                                Check(luma < coreLuma * .60f, scene + " scattering tapers above the surface");
                            if (radius == 1.25f)
                                Check(lit.sqrMagnitude <= 4 && dark.sqrMagnitude <= 4, scene + " exterior sky remains clear");
                        }
                    }

                    if (!fullResolution)
                    {
                        Orbit("clouds-rim", .972f, clouds: true, flare: true, save: true);
                        Orbit("grazing-lens", 1.014f, flare: true, save: true);
                        Orbit("high-sun", 1.45f, clouds: true, flare: true, save: true);
                    }
                }

                // A mixed estuary on the regression world's 512x256 grid.
                const int estuaryX = 132, estuaryY = 187;
                nav.Focus(map.World.Index(estuaryX, estuaryY), 0);
                for (int tick = 0; tick < 180; ++tick) nav.Tick(1f / 30, 0);
                if (!nav.IsFlat) throw new InvalidOperationException("Local surface did not finish unfolding.");
                var location = FlatWeatherSimulation.Direction((estuaryX + .5) / map.World.Width, (estuaryY + .5) / map.World.Height);
                var surfaceNormal = new Vector3((float)location.X, (float)location.Y, (float)location.Z);
                map.Configure(false, nav.PixelsPerTile);
                atmosphere.fullResolutionReference = false;
                Bind(surfaceNormal, true, false, false);
                var localDay = Render("local-day", true);
                Bind(-surfaceNormal, true, false, false);
                var localNight = Render("local-night", true);
                SampleGeography(12, 24, false);
                CheckTerrain("local", localDay, localNight);
                if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));

                void Bind(Vector3 sun, bool air, bool clouds, bool flare)
                {
                    globe.TickScroll(0, 0, 0, nav.DisplaySpin, nav.Morph, clouds, air, true, true, sun, true);
                    map.Sky.Bind(map.Material, sun, clouds, clouds, air, true, false, false, true, 0);
                    map.Material.SetFloat("_WaterClock", 0);
                    nav.SyncMaterials();
                    lens.strength = flare ? previousStrength : 0;
                }
                Color32[] Render(string label, bool save)
                {
                    camera.Render();
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    if (save) File.WriteAllBytes(Path.Combine(output, label + ".png"), image.EncodeToPNG());
                    return image.GetPixels32();
                }
                void SampleGeography(int margin, int stride, bool orbital)
                {
                    land.Clear();
                    ocean.Clear();
                    for (int y = margin; y < Height - margin; y += stride)
                    for (int x = margin; x < Width - margin; x += stride)
                    {
                        var ray = camera.ViewportPointToRay(new Vector3((x + .5f) / Width, (y + .5f) / Height));
                        if (orbital && ObserverDistance * Vector3.Cross(inward, ray.direction).magnitude > .76f) continue;
                        int tile = nav.Pick(new Vector2(x + .5f, y + .5f));
                        if (tile < 0) continue;
                        var kind = map.World.Water[tile];
                        if (kind == WaterKind.Land) land.Add(y * Width + x);
                        if (FlatTerrainAnalysis.Ocean(kind)) ocean.Add(y * Width + x);
                    }
                }
                void CheckTerrain(string mode, Color32[] day, Color32[] night)
                {
                    float landMedian = CheckArea(land, "land", 20, 10);
                    float seaMedian = CheckArea(ocean, "ocean", 9, 2);
                    Check(landMedian > seaMedian + 6, mode + " night coast remains distinct");

                    float CheckArea(List<int> indices, string name, float minimum, float minimumContrast)
                    {
                        if (indices.Count < 10) throw new InvalidOperationException("Insufficient " + name + " samples.");
                        var noon = Brightness(day, indices);
                        var midnight = Brightness(night, indices);
                        float dayMedian = noon[noon.Length / 2], nightMedian = midnight[midnight.Length / 2];
                        float span = midnight[Mathf.RoundToInt((midnight.Length - 1) * .9f)] -
                            midnight[Mathf.RoundToInt((midnight.Length - 1) * .1f)];
                        report.Add($"{mode} {name} samples={indices.Count} noon median={dayMedian:F2} midnight median={nightMedian:F2} night P90-P10={span:F2}");
                        Check(nightMedian >= minimum && nightMedian < dayMedian * .55f && span > minimumContrast,
                            mode + " " + name + " dark-side brightness and contrast");
                        return nightMedian;
                    }
                }
                void Check(bool passed, string description)
                {
                    report.Add((passed ? "PASS " : "FAIL ") + description);
                    if (!passed) failures.Add(description);
                }
            }
            finally
            {
                lens.strength = previousStrength;
                globe.Snapshot.settings.cloudQuality = previousQuality;
                atmosphere.fullResolutionReference = previousFull;
                camera.targetTexture = previousTarget;
                nav.SetViewport(previousViewport);
                nav.Home(0);
                nav.Tick(3, 0);
                nav.Tick(3, 0);
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(image);
                RenderTexture.ReleaseTemporary(target);
                File.WriteAllLines(Path.Combine(output, "backlight-measurements.txt"), report);
            }
        }

        static float[] Brightness(Color32[] pixels, List<int> indices)
        {
            var values = new float[indices.Count];
            for (int i = 0; i < values.Length; ++i)
            {
                var p = pixels[indices[i]];
                values[i] = p.r * Luminance.x + p.g * Luminance.y + p.b * Luminance.z;
            }
            Array.Sort(values);
            return values;
        }

        static Vector3 ArcSignal(Camera camera, Vector3 inward, Vector3 upper, float radius, Color32[] bare, Color32[] rim)
        {
            Vector3 across = Vector3.Cross(inward, upper).normalized;
            float angle = Mathf.Asin(radius / ObserverDistance);
            var sum = Vector3.zero;
            const int halfArcDegrees = 14;
            for (int degrees = -halfArcDegrees; degrees <= halfArcDegrees; ++degrees)
            {
                float azimuth = degrees * Mathf.Deg2Rad;
                Vector3 radial = upper * Mathf.Cos(azimuth) + across * Mathf.Sin(azimuth);
                Vector3 ray = inward * Mathf.Cos(angle) + radial * Mathf.Sin(angle);
                Vector3 uv = camera.WorldToViewportPoint(camera.transform.position + ray);
                int index = Mathf.FloorToInt(uv.y * Height) * Width + Mathf.FloorToInt(uv.x * Width);
                var a = rim[index];
                var b = bare[index];
                sum += new Vector3(a.r - b.r, a.g - b.g, a.b - b.b);
            }
            return sum / (halfArcDegrees * 2 + 1);
        }
    }
}
