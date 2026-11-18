using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Orbis.Flat;
using Orbis.Planet;
using UnityEngine;

namespace Orbis.Editor
{
    // Paired game-camera renders of a frozen, generated weather field using ordinary wheel input.
    internal static class CloudCrossingRegression
    {
        const int Width = 512, Height = 288, FramesPerLeg = 72;

        internal static void Capture(Camera camera, FlatGlobePresenter globe, FlatScrollNavigator nav, FlatMapRenderer map,
            OrbisAtmosphereRenderer atmosphere, OrbisCelestialLens lens, string output)
        {
            var oldTarget = camera.targetTexture;
            var oldViewport = camera.pixelRect;
            var oldActive = RenderTexture.active;
            var settings = globe.Snapshot.settings;
            int oldQuality = settings.cloudQuality;
            bool oldReference = atmosphere.fullResolutionReference;
            float oldLens = lens.strength;
            var target = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var checks = new List<string>();
            var failures = new List<string>();
            try
            {
                camera.targetTexture = target;
                nav.SetViewport(new Rect(0, 0, Width, Height));
                lens.strength = 0;
                settings.cloudQuality = 1;
                atmosphere.fullResolutionReference = false;
                Trace("coast", 154.5f/1024, 222.5f/512);
                Trace("ocean", 913.5f/1024, 234.5f/512);
                // In this seeded weather snapshot a broad natural clearing surrounds this column.
                Trace("clearing", 298.5f/1024, 180.5f/512);
                atmosphere.fullResolutionReference = true;
                Trace("coast-full", 154.5f/1024, 222.5f/512);
                if (failures.Count != 0) throw new InvalidOperationException(string.Join(", ", failures));

                void Trace(string route, float u, float v)
                {
                    string path = Path.Combine(output, route);
                    Directory.CreateDirectory(path);
                    nav.Home(0);
                    nav.Tick(3, 0);
                    nav.Tick(3, 0);
                    nav.Recenter(PlanetCoordinateSystem.FromMapUV(u, v), 0);
                    nav.Wheel(-16, camera.pixelRect.center, 0);
                    nav.Tick(3, 0);
                    Vector3 up = globe.transform.InverseTransformVector(camera.transform.position-globe.transform.position).normalized;
                    Vector3 east = Vector3.Cross(Vector3.up, up).normalized;
                    Vector3 sun = (.65f * up + east + .18f * Vector3.Cross(up, east)).normalized;
                    using var report = new StreamWriter(Path.Combine(path, "frames.csv"));
                    report.WriteLine("frame,morph,distance,centerU,centerV,luma,clearLuma,cloudAbs,cloudMean,deltaCloud,deltaFrame,deltaClear,centerCloud");
                    Color32[] lastOn = null, lastOff = null;
                    var midSignal = new[] {double.PositiveInfinity, double.PositiveInfinity};
                    var approachSignal = new double[2];
                    var largestJump = new double[2];
                    var baseJump = new double[2];
                    var clearingSignal = new double[2];
                    double previousLuma = 0;
                    double groundSignal = -1, groundShadow = -1;
                    for (int frame = 0; frame < 2*FramesPerLeg; ++frame)
                    {
                        int legFrame = frame % FramesPerLeg;
                        if (legFrame < 64 && legFrame % 4 == 0)
                            nav.Wheel(frame < FramesPerLeg ? -1 : 1, camera.pixelRect.center, 0);
                        nav.Tick(1f/30, 0);
                        map.Configure(false, nav.PixelsPerTile);
                        map.Sky.Bind(map.Material, sun, true, true, true, true, true, true, true, 0);
                        map.Material.SetFloat("_WaterClock", 0);
                        nav.SyncMaterials();
                        globe.TickScroll(0, 0, 0, nav.DisplaySpin, nav.Morph, true, true, true, true, sun, true);
                        // This control drops only the ray-marched clouds. Terrain and cloud shadows are identical.
                        var volume = globe.View.CloudMaterial;
                        float clouds = volume.GetFloat("_Clouds"), shafts = volume.GetFloat("_GodRays");
                        volume.SetFloat("_Clouds", 0);
                        volume.SetFloat("_GodRays", 0);
                        var off = Read();
                        volume.SetFloat("_Clouds", clouds);
                        volume.SetFloat("_GodRays", shafts);
                        var on = Read();
                        if (route == "coast" || route == "ocean")
                            File.WriteAllBytes(Path.Combine(path, frame.ToString("D3") + ".png"), image.EncodeToPNG());
                        if (route == "coast" && (frame == 30 || frame == 38))
                            checks.Add(CloudProjectionRegression.Check(camera, atmosphere));
                        double luma = 0, clear = 0, absolute = 0, mean = 0, cloudStep = 0, frameStep = 0, clearStep = 0;
                        int count = 0, centerCount = 0;
                        double centerCloud = 0;
                        for (int y = Height/8; y < Height-Height/8; ++y)
                        for (int x = Width/8; x < Width-Width/8; ++x)
                        {
                            int i = y*Width+x;
                            double a = Luma(on[i]), b = Luma(off[i]);
                            luma += a; clear += b; absolute += Math.Abs(a-b); mean += a-b;
                            if (Math.Abs(x-Width/2)<Width/16 && Math.Abs(y-Height/2)<Height/16)
                            {
                                centerCloud += Math.Abs(a-b);
                                ++centerCount;
                            }
                            if (lastOn != null)
                            {
                                double da = a-Luma(lastOn[i]), db = b-Luma(lastOff[i]);
                                cloudStep += Math.Abs(da-db); frameStep += Math.Abs(da); clearStep += Math.Abs(db);
                            }
                            ++count;
                        }
                        var center = nav.CenterTile;
                        int leg = frame / FramesPerLeg;
                        if (nav.Morph >= .63f && nav.Morph <= .69f)
                            midSignal[leg] = Math.Min(midSignal[leg], absolute/count);
                        if (nav.Morph >= .58f && nav.Morph <= .61f)
                            approachSignal[leg] = Math.Max(approachSignal[leg], absolute/count);
                        if (nav.Morph >= .59f && nav.Morph < .75f)
                            largestJump[leg] = Math.Max(largestJump[leg], Math.Abs(luma/count-previousLuma));
                        if (nav.Morph >= .75f && nav.Morph <= .98f)
                            baseJump[leg] = Math.Max(baseJump[leg], Math.Abs(luma/count-previousLuma));
                        if (nav.Morph >= .80f && nav.Morph <= .92f)
                            clearingSignal[leg] = Math.Max(clearingSignal[leg], centerCloud/centerCount);
                        previousLuma = luma/count;
                        if (frame == FramesPerLeg-1 && nav.IsFlat)
                        {
                            groundSignal = absolute/count;
                            map.Material.SetFloat("_FlatCloudShadows", 0);
                            nav.SyncMaterials();
                            var unshadowed = Read();
                            groundShadow = 0;
                            for (int i = 0; i < on.Length; ++i)
                                groundShadow += Math.Abs(Luma(unshadowed[i])-Luma(on[i]));
                            groundShadow /= on.Length;
                        }
                        report.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "{0},{1:F6},{2:F6},{3:F6},{4:F6},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11:F4},{12:F4}",
                            frame, nav.Morph, nav.Distance, center.x/map.World.Width, center.y/map.World.Height,
                            luma/count, clear/count, absolute/count, mean/count, cloudStep/count, frameStep/count, clearStep/count,
                            centerCloud/centerCount));
                        lastOn = on;
                        lastOff = off;
                    }
                    for (int leg = 0; leg < 2; ++leg)
                    {
                        string direction = route + (leg == 0 ? " inward" : " outward");
                        // Both routes cross a populated HP column. Entering the blend must not
                        // erase it for several consecutive frames, or flash the whole frame dark.
                        if (route == "clearing")
                            Check(direction + " center of real cloud gap stays visible", clearingSignal[leg] < 5,
                                $"maximum center cloud contribution {clearingSignal[leg]:F2} /255");
                        else
                            Check(direction + " retains a cloudy mid-fold", approachSignal[leg] > 3 &&
                                !double.IsInfinity(midSignal[leg]) && midSignal[leg] >= .25*approachSignal[leg],
                                $"mid {midSignal[leg]:F2}, approach {approachSignal[leg]:F2} /255");
                        Check(direction + " continuous exposure", largestJump[leg] < 20,
                            $"largest frame mean change {largestJump[leg]:F2} /255");
                        Check(direction + " exits cloud base gradually", baseJump[leg] < 20,
                            $"largest cloud-base frame mean change {baseJump[leg]:F2} /255");
                    }
                    Check(route + " exits below cloud", groundSignal >= 0 && groundSignal < 1 &&
                        (route == "clearing" || groundShadow > .2),
                        $"overhead cloud difference {groundSignal:F2}, ground shadow difference {groundShadow:F2} /255");
                }

                void Check(string name, bool passed, string metric)
                {
                    checks.Add((passed ? "PASS " : "FAIL ") + name + " — " + metric);
                    if (!passed) failures.Add(name);
                }

                Color32[] Read()
                {
                    camera.Render();
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    return image.GetPixels32();
                }
            }
            finally
            {
                File.WriteAllLines(Path.Combine(output, "cloud-crossing.txt"), checks);
                camera.targetTexture = oldTarget;
                nav.SetViewport(oldViewport);
                settings.cloudQuality = oldQuality;
                atmosphere.fullResolutionReference = oldReference;
                lens.strength = oldLens;
                nav.Home(0);
                nav.Tick(3, 0);
                RenderTexture.active = oldActive;
                UnityEngine.Object.DestroyImmediate(image);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        static double Luma(Color32 c) => .2126*c.r + .7152*c.g + .0722*c.b;
    }
}
