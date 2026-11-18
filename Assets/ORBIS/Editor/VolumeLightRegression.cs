using System;
using System.Collections.Generic;
using System.IO;
using Orbis.Flat;
using Orbis.Planet;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
    // Real play-mode scene, seeded weather, all rays accumulated with the lens disabled.
    internal static class VolumeLightRegression
    {
        const int Width = 960, Height = 540;

        static float[] RegionLuma(Color32[] pixels, RectInt region)
        {
            var values = new float[region.width * region.height];
            int index = 0;
            for (int y = region.yMin; y < region.yMax; ++y)
            for (int x = region.xMin; x < region.xMax; ++x)
            {
                var pixel = pixels[y * Width + x];
                values[index++] = .2126f * pixel.r + .7152f * pixel.g + .0722f * pixel.b;
            }
            Array.Sort(values);
            return values;
        }

        internal static void Capture(Camera camera, FlatGlobePresenter globe, FlatScrollNavigator nav, FlatMapRenderer map,
            OrbisAtmosphereRenderer atmosphere, OrbisCelestialLens lens, string output)
        {
            var originalTarget = camera.targetTexture;
            var originalViewport = camera.pixelRect;
            float originalNear = camera.nearClipPlane;
            var previousActive = RenderTexture.active;
            var target = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var settings = globe.Snapshot.settings;
            bool initialRays = settings.godRays;
            bool initialFull = atmosphere.fullResolutionReference;
            float initialStrength = lens.strength;
            int initialQuality = settings.cloudQuality;
            var report = new List<string>();
            var failures = new List<string>();
            try
            {
                camera.targetTexture = target;
                nav.Home(0);
                nav.SetViewport(new Rect(0, 0, Width, Height));
                nav.Tick(3, 0);
                nav.Tick(3, 0);
                settings.cloudQuality = 1;
                atmosphere.fullResolutionReference = false;
                lens.strength = 0;
                int mipCount = map.Sky.ColumnTexture.mipmapCount;
                report.Add((mipCount >= 5 ? "PASS " : "FAIL ") + "published cloud opacity has five levels, count=" + mipCount);
                if (mipCount < 5) failures.Add("Cloud column neighbourhood");
                var inward = (globe.transform.position - camera.transform.position).normalized;
                var rim = (camera.transform.up * .69f + camera.transform.right * .72f).normalized;
                float distance = Vector3.Distance(globe.transform.position, camera.transform.position);
                float angle = Mathf.Asin(1.013f / distance);
                var grazing = inward * Mathf.Cos(angle) + rim * Mathf.Sin(angle);
                Pair("orbit-regional", grazing, true, true, true);
                Pair("orbit-clear", grazing, false, true, false);
                Pair("orbit-no-air", grazing, true, false, false);
                Pair("orbit-midnight", inward, true, true, false);
                var offset = .55f;
                Pair("orbit-sun-rotated", (inward + (camera.transform.up * .3f - camera.transform.right).normalized * offset).normalized, true, true, true);

                // The default world has a clear ocean gap beside a dense radial HP column here.
                const float gapU = 164.5f / 1024, gapV = 222.5f / 512;
                var location = PlanetCoordinateSystem.FromMapUV(gapU, gapV);
                var normal = PlanetCoordinateSystem.SurfaceNormal(location);
                var upLocal = new Vector3((float)normal.X, (float)normal.Y, (float)normal.Z);
                var eastLocal = Vector3.Cross(Vector3.up, upLocal).normalized;
                var up = globe.transform.TransformVector(upLocal).normalized;
                var east = globe.transform.TransformVector(eastLocal).normalized;
                var north = globe.transform.TransformVector(Vector3.Cross(upLocal, eastLocal)).normalized;

                // Recenter uses the minimap entry point. Only normal wheel notches position the camera.
                nav.Recenter(PlanetCoordinateSystem.FromMapUV(gapU - .03f, gapV), 0);
                nav.Wheel(-18, camera.pixelRect.center, 0);
                for (int frame = 0; frame < 60; ++frame) nav.Tick(1f / 30, 0);
                map.Configure(false, nav.PixelsPerTile);
                report.Add("Player scroll view: " + nav.ModeName + ", morph " + nav.Morph.ToString("F3"));
                var playerSun = (east + .27f * up + .5f * north).normalized;
                var playerSignal = Pair("wheel-coast", playerSun, true, true, true);
                var movedSignal = Pair("wheel-sun-moved", (east + .18f * up - north).normalized, true, true, true);
                CheckMovement(playerSignal, movedSignal);

                nav.Home(0);
                for (int frame = 0; frame < 90; ++frame) nav.Tick(1f / 30, 0);
                map.Configure(false, nav.PixelsPerTile);
                // Diagnostic cross-sections of the same region show the horizontal path through air.
                camera.transform.SetPositionAndRotation(globe.transform.position + up * 1.009f,
                    Quaternion.LookRotation(east - .6f * north - .10f * up, up));
                var lowSun = (east + .6f * north + .04f * up).normalized;
                var gapSignal = Pair("cloud-gap", lowSun, true, true, true);
                var movedGap = Pair("cloud-gap-sun-moved", (east - .32f * north + .14f * up).normalized, true, true, true);
                CheckMovement(gapSignal, movedGap);
                atmosphere.fullResolutionReference = true;
                Pair("cloud-gap-full", lowSun, true, true, true);
                atmosphere.fullResolutionReference = false;
                // At this ocean location two narrow transparent columns sit between three thick
                // banks. Look toward the gaps from 200 m above sea level, with the Sun to the side.
                var hole = PlanetCoordinateSystem.SurfaceNormal(PlanetCoordinateSystem.FromMapUV(.45557, .25879));
                upLocal = new Vector3((float)hole.X, (float)hole.Y, (float)hole.Z);
                eastLocal = Vector3.Cross(Vector3.up, upLocal).normalized;
                up = globe.transform.TransformVector(upLocal).normalized;
                east = globe.transform.TransformVector(eastLocal).normalized;
                north = globe.transform.TransformVector(Vector3.Cross(upLocal, eastLocal)).normalized;
                var horizontalLight = (.32f * east - .86f * north).normalized;
                var horizontalView = (-.10f * east - .99f * north).normalized;
                const float cloudHeight = .008f;
                var midPath = up * (1 + cloudHeight/2) - horizontalLight * (cloudHeight/(2*.45f));
                var observer = (midPath - horizontalView * (cloudHeight/(2*.27f))).normalized * 1.0003f;
                camera.nearClipPlane = .00005f;
                camera.transform.SetPositionAndRotation(globe.transform.position + observer,
                    Quaternion.LookRotation(horizontalView + .27f*up, up));
                var window = Pair("sea-level-cloud-window", (horizontalLight + .45f*up).normalized, true, true, true);
                int bright = 0, dark = 0;
                foreach (float difference in window)
                {
                    if (difference > 4) ++bright;
                    if (difference < -4) ++dark;
                }
                bool varied = bright > window.Length/100 && dark > window.Length/100;
                report.Add((varied ? "PASS " : "FAIL ") + $"both illuminated and shadowed air through the real opening: {bright}/{dark} pixels (4/255 threshold)");
                if (!varied) failures.Add("Cloud opening contrast");
                var cloudShader = globe.View.CloudMaterial.shader;
                foreach (var message in ShaderUtil.GetShaderMessages(cloudShader))
                    if (message.severity.ToString() == "Error")
                        throw new InvalidOperationException(cloudShader.name + " " + message.message);
                if (failures.Count > 0) throw new InvalidOperationException(string.Join(", ", failures));

                void CheckMovement(float[] first, float[] second)
                {
                    // Subtract the no-shafts frame for each sun before comparing. Ordinary terrain
                    // shading, sky brightness and cloud self-lighting then cancel out.
                    double cross = 0, a2 = 0, b2 = 0, a = 0, b = 0;
                    for (int i = 0; i < first.Length; ++i)
                    {
                        a += first[i]; b += second[i];
                        a2 += first[i] * first[i]; b2 += second[i] * second[i];
                        cross += first[i] * second[i];
                    }
                    double norm = Math.Sqrt(Math.Max(0, a2 - a*a/first.Length) * Math.Max(0, b2 - b*b/first.Length));
                    double correlation = norm > 1e-8 ? (cross - a*b/first.Length)/norm : 1;
                    bool changed = correlation < .95;
                    report.Add((changed ? "PASS " : "FAIL ") + "scattering pattern moves with Sun, correlation " + correlation.ToString("F3"));
                    if (!changed) failures.Add("Directional scattering");
                }

                float[] Pair(string label, Vector3 worldSun, bool clouds, bool air, bool expectSignal)
                {
                    var sun = globe.transform.InverseTransformVector(worldSun).normalized;
                    var sunUV = camera.WorldToViewportPoint(camera.transform.position + worldSun);
                    var off = Render(false);
                    var on = Render(true);
                    int changed = 0, max = 0, nightMax = 0;
                    var signal = new float[on.Length];
                    long absolute = 0;
                    for (int i = 0; i < off.Length; ++i)
                    {
                        int delta = Mathf.Max(Math.Abs(on[i].r - off[i].r), Math.Max(Math.Abs(on[i].g - off[i].g), Math.Abs(on[i].b - off[i].b)));
                        signal[i] = .2126f*(on[i].r-off[i].r) + .7152f*(on[i].g-off[i].g) + .0722f*(on[i].b-off[i].b);
                        if (label == "orbit-midnight" && Math.Abs(i % Width - Width/2) < Height / 12
                            && Math.Abs(i / Width - Height/2) < Height / 12) nightMax = Math.Max(nightMax, delta);
                        if (delta > 1) ++changed;
                        max = Math.Max(max, delta);
                        absolute += delta;
                    }
                    report.Add($"{label} solar viewport {sunUV.ToString("F3")}: max RGB difference {max}, pixels >1 DN {changed}, mean max-channel delta {absolute / (double)off.Length:F4}");
                    if (label == "orbit-midnight")
                    {
                        report.Add((nightMax <= 1 ? "PASS " : "FAIL ") + "deep night ground max change " + nightMax);
                        if (nightMax > 1) failures.Add(label);
                    }
                    else
                    {
                        bool passed = expectSignal ? changed > 64 : max <= 1;
                        report.Add((passed ? "PASS " : "FAIL ") + label);
                        if (!passed) failures.Add(label);
                    }
                    if (label == "cloud-gap-sun-moved")
                    {
                        // Same visible mountain face in both shots, clear of sky and sun. Catch a
                        // bright aerosol source which turns its slopes and valleys into one bright patch.
                        var region = new RectInt(315, 150, 225, 165);
                        float[] unlit = RegionLuma(off, region), illuminated = RegionLuma(on, region);
                        int p10 = unlit.Length/10, median = unlit.Length/2, p90 = unlit.Length*9/10;
                        float lift = illuminated[median] - unlit[median];
                        float spanRatio = (illuminated[p90] - illuminated[p10]) /
                            Mathf.Max(1, unlit[p90] - unlit[p10]);
                        bool readable = lift <= 25 && spanRatio >= .70f;
                        report.Add((readable ? "PASS " : "FAIL ") + $"mountain surface under grazing light: median lift {lift:F2}/255, retained contrast {spanRatio:F3}");
                        if (!readable) failures.Add("Backlit mountain whites out");
                    }
                    return signal;

                    Color32[] Render(bool rays)
                    {
                        settings.godRays = rays;
                        globe.TickScroll(0, 0, 0, nav.DisplaySpin, nav.Morph, clouds, air, true, true, sun, true);
                        map.Sky.Bind(map.Material, sun, clouds, clouds, air, true, false, false, true, 0);
                        map.Material.SetFloat("_WaterClock", 0);
                        nav.SyncMaterials();
                        camera.Render();
                        RenderTexture.active = target;
                        image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                        File.WriteAllBytes(Path.Combine(output, label + (rays ? "-on.png" : "-off.png")), image.EncodeToPNG());
                        return image.GetPixels32();
                    }
                }
            }
            finally
            {
                File.WriteAllLines(Path.Combine(output, "volume-light.txt"), report);
                lens.strength = initialStrength;
                settings.cloudQuality = initialQuality;
                settings.godRays = initialRays;
                atmosphere.fullResolutionReference = initialFull;
                camera.targetTexture = originalTarget;
                nav.SetViewport(originalViewport);
                camera.nearClipPlane = originalNear;
                nav.Home(0);
                nav.Tick(3, 0);
                nav.Tick(3, 0);
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(image);
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
