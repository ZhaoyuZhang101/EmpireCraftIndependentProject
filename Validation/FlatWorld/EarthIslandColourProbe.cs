using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Orbis.Flat;
using Orbis.Planet;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Run in a paused 512-wide Earth game, including an editor with an offscreen D3D11 device.
public static class EarthIslandColourProbe
{
    public static Task<string> Before() => Measure("before");
    public static Task<string> Run() => Measure("after");

    static async Task<string> Measure(string stage)
    {
        var app = UnityEngine.Object.FindFirstObjectByType<FlatWorldApp>();
        if (!app || app.Busy || app.World?.GeographicSurface == null || app.World.Width != 512)
            throw new InvalidOperationException("Generate and pause a 512-wide Earth first.");
        var nav = app.Navigation;
        var camera = (Camera)typeof(FlatWorldApp).GetField("viewCamera", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(app);
        var ui = app.GetComponentInChildren<UIDocument>().rootVisualElement;
        var clouds = ui.Q<Toggle>("clouds");
        var layer = ui.Q<DropdownField>("layer");
        var map = app.GetComponentInChildren<FlatMapRenderer>();
        double oldDay = app.World.Day;
        int oldLayer = layer.index;
        bool oldClouds = clouds.value;
        var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../ValidationOutput/IslandAppearance"));
        Directory.CreateDirectory(folder);
        var report = new StringBuilder();
        int failures = 0;
        try
        {
            layer.index = (int)MapLayer.Natural;
            clouds.value = false;
            foreach (var site in new[] {
                ("StHelena", -5.715, -15.963, -5.55, -15.963),
                ("Noronha", -32.430, -3.854, -32.31, -3.854),
                ("Gyaros", 24.71146, 37.61164, 24.63468, 37.61164) })
            {
                app.World.Day = Math.Floor(oldDay) + FlatScrollMath.Wrap(.5 - site.Item2 / 360, 1);
                double day = app.CalendarDay;
                // Let the normal game update publish the daylight for this longitude.
                await Task.Delay(100);
                foreach (bool land in new[] {true, false})
                {
                    double lon = land ? site.Item2 : site.Item4, lat = land ? site.Item3 : site.Item5;
                    var location = new PlanetLocation(lon * Math.PI / 180, lat * Math.PI / 180);
                    bool dry = FlatGeographicSurface.IsGroundAt(app.World, new FlatSeaPoint(lon, lat));
                    if (dry != land) throw new InvalidOperationException(site.Item1 + " shoreline control misplaced.");
                    var shot = Capture(nav, camera, location, day);
                    try
                    {
                        int x = shot.width / 2, y = shot.height / 2;
                        Color mean = Color.clear;
                        foreach (var p in shot.GetPixels(x - 1, y - 1, 3, 3)) mean += p / 9;
                        int tile = app.World.Index((int)((lon + 180) / 360 * app.World.Width),
                            (int)((lat + 90) / 180 * app.World.Height));
                        var underlay = map.TerrainTexture.GetPixelData<Color32>(0)[tile];
                        if (!underlay.Equals(FlatPalette.Terrain(app.World, tile)))
                            throw new InvalidOperationException(site.Item1 + " has different serial and job terrain colours.");
                        // The tropical forest palette can be dark and cool, with green
                        // above blue. Water at these daylight controls is blue-dominant.
                        bool blue = mean.b > mean.r && mean.b > mean.g;
                        bool ok = mean.maxColorComponent > .1f && (land ? !blue : blue);
                        if (!ok) failures++;
                        report.AppendLine($"{(ok ? "PASS" : "FAIL")} {site.Item1} {(land ? "island" : "ocean")} | RGB {ColorUtility.ToHtmlStringRGB(mean)} | tile {tile} {app.World.Water[tile]}, underlay {underlay.r}/{underlay.g}/{underlay.b}");
                        if (land) File.WriteAllBytes(Path.Combine(folder, site.Item1 + "-" + stage + ".png"), shot.EncodeToPNG());
                    }
                    finally { UnityEngine.Object.Destroy(shot); }
                }
            }
        }
        finally { app.World.Day = oldDay; clouds.value = oldClouds; layer.index = oldLayer; }
        File.WriteAllText(Path.Combine(folder, stage + ".txt"), report.ToString());
        if (failures > 0) throw new InvalidOperationException(report.ToString());
        return report.ToString();
    }

    // Use the actual game camera and its image effects. A fixed viewport reproduces the
    // reference Game view when the editor is minimized or has no screen in batch mode.
    static Texture2D Capture(FlatScrollNavigator nav, Camera camera, PlanetLocation location, double day)
    {
        const int width = 1420, height = 544;
        var viewport = camera.pixelRect;
        var previous = camera.targetTexture;
        var active = RenderTexture.active;
        var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        Texture2D image = null;
        try
        {
            camera.targetTexture = target;
            nav.SetViewport(new Rect(0, 0, width, height));
            nav.Focus(location, day);
            nav.Tick(3, day);
            nav.Recenter(location, day);
            for (int n = 0; n < 8; n++) { nav.Wheel(-3, camera.pixelRect.center, day); nav.Tick(.5f, day); }
            nav.SyncMaterials();
            var material = nav.SurfaceMaterial;
            int pass = material.FindPass("SCROLL_SURFACE");
            if (pass < 0) throw new InvalidOperationException("Missing terrain pass.");
            if (!ShaderUtil.IsPassCompiled(material, pass)) ShaderUtil.CompilePass(material, pass, true);
            nav.SyncMaterials();
            if (!ShaderUtil.IsPassCompiled(material, pass) || ShaderUtil.ShaderHasError(material.shader))
                throw new InvalidOperationException("Detailed terrain pass unavailable.");
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            return image;
        }
        catch { if (image) UnityEngine.Object.Destroy(image); throw; }
        finally
        {
            camera.targetTexture = previous;
            nav.SetViewport(viewport);
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(target);
        }
    }
}
