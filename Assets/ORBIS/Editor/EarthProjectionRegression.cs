using System;
using System.Collections.Generic;
using System.Reflection;
using Orbis.Flat;
using Orbis.Planet;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
    public static class EarthProjectionRegression
    {
        [MenuItem("Tools/ORBIS/Local Planet/Validate Earth projection")]
        public static void Run()
        {
            const int width = 512;
            var dem = Resources.Load<TextAsset>("ORBIS/FlatWorld/Earth512");
            var water = Resources.Load<TextAsset>("ORBIS/FlatWorld/EarthWater512");
            var world = FlatGenerator.Generate(width, width / 2, "Earth projection", TerrainPreset.Earth, default, dem.bytes, water.bytes);
            var physical = new PlanetSurfaceData(world, 6371008.8, default);
            var root = new GameObject("Earth projection regression");
            var probe = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/PolarDetailProbe.shader"));
            var rt = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var read = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var previous = RenderTexture.active;
            bool async = ShaderUtil.allowAsyncCompilation;
            var failures = new List<string>();
            Color Sample(int mode, float longitude, float latitude)
            {
                probe.SetFloat("_ProjectionProbe", mode);
                probe.SetVector("_ProbeOrigin", new Vector4(longitude/360 + .5f, latitude/180 + .5f, 0, 0));
                Graphics.Blit(Texture2D.whiteTexture, rt, probe, 0);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                return read.GetPixel(0, 0);
            }
            try
            {
                var renderer = root.AddComponent<FlatMapRenderer>();
                renderer.Initialize(world);
                probe.CopyPropertiesFromMaterial(renderer.Material);
                probe.SetFloat("_ProjectionProbe", 5);
                ShaderUtil.allowAsyncCompilation = false;
                VegetationArtRegression.CheckSurveyedTreeRoots(renderer.Material);
                ShaderUtil.CompilePass(probe, 0, true);
                var poles = FlatGlobeGeometry.PolarHeights(world);
                double poleError = 0, centreError = 0, shapeError = 0, depthError = 0;
                foreach (int row in new[] { 0, world.Height - 1 })
                {
                    double poleBed = 0, poleDepth = 0;
                    for (int x = 0; x < width; x++)
                    {
                        poleBed += world.Elevation[row * width + x];
                        poleDepth += world.Facts.Depth[row * width + x];
                    }
                    poleBed /= width;
                    poleDepth /= width;
                    for (int x = 0; x < width; x += 23)
                    foreach (float ring in new[] { 0f, .25f, .5f })
                    {
                        int tile = row * width + x;
                        float u = (x + .5f) / width;
                        float v = row == 0 ? ring / world.Height : 1 - ring / world.Height;
                        probe.SetVector("_ProbeOrigin", new Vector4(u, v, 0, 0));
                        Graphics.Blit(Texture2D.whiteTexture, rt, probe, 0);
                        RenderTexture.active = rt;
                        read.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                        var pixel = read.GetPixel(0, 0);
                        double expected = poleBed + (world.Elevation[tile] - poleBed) * (2 * ring);
                        double expectedDepth = poleDepth + (world.Facts.Depth[tile] - poleDepth) * (2 * ring);
                        double sample = physical.Sample(PlanetCoordinateSystem.FromMapUV(u, v)).GroundHeight;
                        double discrepancy = Math.Max(Math.Abs(pixel.r - expected), Math.Abs(sample - expected));
                        if (ring == 0) poleError = Math.Max(poleError, discrepancy);
                        else centreError = Math.Max(centreError, discrepancy);
                        depthError = Math.Max(depthError, Math.Abs(pixel.g - expectedDepth));
                        var uv = new Vector2(u, v);
                        float radius = FlatGlobeGeometry.Radius(FlatGlobeGeometry.SurfaceHeight(world, uv), v, world.Height, poles);
                        double stage = row == 0 ? expected : 0; // Antarctic ice surface and Arctic Ocean.
                        shapeError = Math.Max(shapeError, Math.Abs((radius - 1) / FlatGlobeGeometry.HeightScale - stage));
                    }
                }
                string result = $"Earth512: polar bed {poleError:F2} m, cap/centre {centreError:F2} m, depth {depthError:F2} m, mesh {shapeError:F2} m";
                Debug.Log(result);
                // Map fields can be stored in half floats, including depths above 4096 m.
                if (Math.Max(Math.Max(poleError, centreError), depthError) > 4.1 || shapeError > .05) failures.Add(result);

                var type = typeof(FlatScrollNavigator).Assembly.GetType("Orbis.Flat.FlatWorldMinimap", true);
                var pixelPosition = (Func<Vector2, Rect, Vector2>)type.GetMethod("Pixel", BindingFlags.Static | BindingFlags.NonPublic)
                    .CreateDelegate(typeof(Func<Vector2, Rect, Vector2>));
                var rect = new Rect(0, 0, 240, 120);
                double Area(float u, float v)
                {
                    const float step = .001f;
                    Vector2 a = pixelPosition(new Vector2(u + step, v), rect) - pixelPosition(new Vector2(u - step, v), rect);
                    Vector2 b = pixelPosition(new Vector2(u, v + step), rect) - pixelPosition(new Vector2(u, v - step), rect);
                    return Math.Abs(a.x * b.y - a.y * b.x) / Math.Cos((v - .5) * Math.PI);
                }
                double equator = Area(.5f, .5f), worst = 0;
                foreach (float lat in new[] { -80f, -60f, 0f, 60f, 80f })
                foreach (float u in new[] { .002f, .25f, .5f, .91f, .998f })
                    worst = Math.Max(worst, Math.Abs(Area(u, lat / 180 + .5f) / equator - 1));
                Debug.Log($"Minimap worst relative ground area error {worst:P3}");
                if (worst > .005) failures.Add($"Minimap area error {worst:P2}");
                // Bilinear samples from the independent 4320 x 2160 ETOPO overview,
                // in longitude, latitude, metres. Includes Arctic and Antarctic coasts.
                var survey = new[] { new Vector3(24.33f,79.20f,-100), new Vector3(16.94f,78.28f,133),
                    new Vector3(13,76.7f,-984), new Vector3(-62.77f,-64.48f,-242),
                    new Vector3(-64.06f,-65.2f,130), new Vector3(165.2f,-74.87f,-782),
                    new Vector3(-178.45f,67.68f,228), new Vector3(179.72f,73.44f,-65) };
                double squared = 0, coarseSquared = 0;
                foreach (var site in survey)
                {
                    double error = Sample(6, site.x, site.y).r - site.z;
                    double coarseError = Sample(5, site.x, site.y).r - site.z;
                    squared += error * error;
                    coarseSquared += coarseError * coarseError;
                }
                double rms = Math.Sqrt(squared/survey.Length);
                Debug.Log($"Polar display bed vs ETOPO at {survey.Length} off-grid sites: grid RMSE {Math.Sqrt(coarseSquared/survey.Length):F1} m, survey RMSE {rms:F1} m");
                if (!(rms <= 65 && squared < coarseSquared * .5)) failures.Add($"Off-grid polar relief RMSE {rms:F1} m");
                var waterProbe = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/WaterCoastProbe.shader"));
                try
                {
                    waterProbe.CopyPropertiesFromMaterial(renderer.Material);
                    waterProbe.SetFloat("_ProbeMode",12);
                    waterProbe.SetVector("_BedSun",new Vector4(.5f,.5f,1,0));
                    foreach (var site in new[] { survey[2], survey[5], survey[7] })
                    {
                        float bed = Sample(6,site.x,site.y).r;
                        waterProbe.SetVector("_CoastOrigin",new Vector4((site.x/360+.5f)*width,(site.y/180+.5f)*world.Height,0,0));
                        Graphics.Blit(Texture2D.whiteTexture,rt,waterProbe,0);
                        RenderTexture.active=rt;
                        read.ReadPixels(new Rect(0,0,1,1),0,0);
                        var value = read.GetPixel(0,0);
                        if (!(Mathf.Abs(value.r-Mathf.Max(0,value.g-bed))<3 && value.b>.5f && value.b<1.5f))
                            failures.Add($"Submerged survey at {site.x},{site.y}: optical depth {value.r:F1}, stage minus bed {value.g-bed:F1}");
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(waterProbe); }
                void CheckGrid(float lon, float lat, string label)
                {
                    float delta = Math.Abs(Sample(6, lon, lat).r - Sample(5, lon, lat).r);
                    if (!(delta <= .01f)) failures.Add($"{label}: survey overrode grid by {delta} m");
                }
                CheckGrid(19, 0, "Equator");
                CheckGrid(-140, -44, "Outside survey coverage");
                // NOAA native pixel centres. The first two heights share one
                // Earth512 simulation cell on the southern flank of the Dabie range.
                const float landLon=115.525f, landLat=31.075f;
                if (SystemInfo.maxTextureSize >= 8192)
                {
                    var sites = new[] {new Vector3(landLon,landLat,304.90f), new Vector3(115.508333f,31.058333f,213.44f),
                        new Vector3(118.358333f,32.158333f,22.87f), new Vector3(7.858333f,46.491667f,2668.90f),
                        new Vector3(-71.875f,-13.358333f,3967.79f), new Vector3(35.441667f,31.041667f,-391.20f)};
                    foreach (var site in sites)
                    {
                        var terrain=Sample(8,site.x,site.y);
                        if (terrain.g < .999f || Mathf.Abs(terrain.r-site.z)>18)
                            failures.Add($"Native land at {site.x},{site.y}: {terrain.r:F1} m, expected {site.z:F1} m, weight {terrain.g:F3}");
                    }
                    var slope=Sample(8,landLon,landLat);
                    // True centred source gradients, in metres per 5 km on the surface.
                    if (Mathf.Abs(slope.b-223.8f)>20 || Mathf.Abs(slope.a-276.9f)>20)
                        failures.Add($"Measured east/north slope at Dabie: {slope.b:F1}, {slope.a:F1}");
                }
                if (Sample(9,landLon,landLat).g!=0 || Sample(8,15,79).g!=0)
                    failures.Add("Land survey covered a wet receiver or the existing polar chart");
                // Interior points on opposite sides of Natural Earth 1:10m ocean polygons.
                // In Earth512 the old regional charts either missed or rounded these banks.
                if (world.UsesGlobalStraits && SystemInfo.maxTextureSize >= 4096)
                {
                    var shores = new[] {
                        ("Messenia", 21.73f, 36.82f, false), ("Mani", 22.44f, 36.53f, false),
                        ("Laconia", 22.96f, 36.86f, false), ("Argolic Gulf", 23.03f, 37.19f, true),
                        ("Haiyang bay", 121.45f, 36.70f, true), ("Shandong peninsula", 122.55f, 37.26f, false),
                        ("Brest roadstead", -4.47f, 48.32f, true), ("Northern Madagascar", 49.52f, -12.85f, false)
                    };
                    foreach (var (place, lon, lat, sea) in shores)
                    {
                        int cell = world.Index((int)((lon/360+.5f)*width), (int)((lat/180+.5f)*world.Height));
                        probe.SetFloat("_ProbeWaterKind", (int)world.Water[cell]);
                        var coast = Sample(7, lon, lat);
                        if (coast.b < .99f || (sea ? coast.r <= 0 : coast.r >= 0))
                            failures.Add($"{place}: expected {(sea ? "sea" : "land")}, signed shore {coast.r:F4}, survey weight {coast.b:F3}");
                    }
                }
                // Natural Earth polygon membership at interior points misrepresented
                // by the 512-grid coastline in Svalbard and the Antarctic Peninsula.
                foreach (var site in new[] { new Vector3(18.35312f,80.15885f,-1), new Vector3(11.27188f,78.51615f,-1),
                    new Vector3(21.79062f,77.30885f,1), new Vector3(20.55313f,79.26823f,1),
                    new Vector3(-56.22344f,-63.2625f,-1), new Vector3(-58.25156f,-62.0125f,-1),
                    new Vector3(-61.99844f,-67.6875f,1), new Vector3(-65.95156f,-66.4125f,1) })
                {
                    int tile = world.Index((int)((site.x/360+.5f)*width),(int)((site.y/180+.5f)*world.Height));
                    probe.SetFloat("_ProbeWaterKind",(int)world.Water[tile]);
                    var shore = Sample(7,site.x,site.y);
                    if (!(shore.r*site.z>0 && shore.g*site.z<0))
                        failures.Add($"Marine shoreline at {site.x},{site.y}: grid {shore.g:F3}, survey {shore.r:F3}");
                    probe.SetFloat("_ProbeWaterKind",(int)WaterKind.Lake);
                    var lake = Sample(7,site.x,site.y);
                    if (lake.r != lake.g || lake.b != 0) failures.Add("A coastal lake inherited a marine shoreline");
                }

                // A known plane on both charts isolates the gradient transform from
                // the frequency content of the real terrain and from camera derivatives.
                const int face = 129;
                var plane = new Texture2D(face*2, face, TextureFormat.R16, false, true);
                try
                {
                    var pixels = plane.GetPixelData<ushort>(0);
                    for (int y=0; y<face; y++)
                    for (int x=0; x<2*face; x++)
                        pixels[y*2*face+x] = (ushort)(32768 + 64*(x%face-face/2) + 80*(y-face/2));
                    plane.Apply(false, true);
                    probe.SetTexture("_EarthPolarBed", plane);
                    float maxSlopeError = 0;
                    foreach (float lat in new[] { -89f, -73f, 73f, 89f })
                    foreach (float lon in new[] { -179f, -90f, 0f, 52f, 179f })
                    {
                        var value = Sample(6, lon, lat);
                        float angle = lon * Mathf.Deg2Rad;
                        float factor = (float)(face/(2*Math.Tan(Math.PI/8))*2*Math.PI/width) / (1+Mathf.Abs(Mathf.Sin(lat*Mathf.Deg2Rad)));
                        float east = (64*Mathf.Cos(angle)-80*Mathf.Sin(angle))*factor;
                        float north = -Mathf.Sign(lat)*(64*Mathf.Sin(angle)+80*Mathf.Cos(angle))*factor;
                        maxSlopeError = Mathf.Max(maxSlopeError, Mathf.Abs(value.b-east), Mathf.Abs(value.a-north));
                    }
                    Debug.Log($"Survey chart east/north gradient maximum error {maxSlopeError:F3} m per equatorial cell");
                    if (!(maxSlopeError < .35f)) failures.Add($"Survey gradient transform error {maxSlopeError:F3}");
                }
                finally
                {
                    probe.CopyPropertiesFromMaterial(renderer.Material);
                    UnityEngine.Object.DestroyImmediate(plane);
                }

                float intact = Sample(6, survey[1].x, survey[1].y).r;
                int editRow = (int)((73.44/180+.5)*world.Height);
                world.Elevation[world.Index(width-1,editRow)] = 2800;
                world.Elevation[world.Index(0,world.Height-1)] = 1600;
                world.Elevation[world.Index((int)((landLon/360+.5f)*width),(int)((landLat/180+.5f)*world.Height))] = 2200;
                FlatGenerator.Rebuild(world, default);
                renderer.Flush(true);
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (renderer.VisualTexture.GetPixel(width-1,editRow).r != 2800 && wait.Elapsed.TotalSeconds < 20)
                {
                    renderer.Flush();
                    System.Threading.Thread.Sleep(1);
                }
                if (renderer.VisualTexture.GetPixel(width-1,editRow).r != 2800)
                    failures.Add("Edited survey mask was not published");
                probe.CopyPropertiesFromMaterial(renderer.Material);
                var editedLand=Sample(8,landLon,landLat);
                if (editedLand.g!=0 || Mathf.Abs(editedLand.r-Sample(5,landLon,landLat).r)>.01f)
                    failures.Add("Measured land covered a player terrain edit");
                CheckGrid(179.72f,73.44f,"Edited date-line cell");
                CheckGrid(-179.91f,73.44f,"Across date line from edit");
                probe.SetFloat("_ProbeWaterKind",(int)WaterKind.Land);
                var editedShore = Sample(7,179.72f,73.44f);
                if (editedShore.r != editedShore.g || editedShore.b != 0) failures.Add("Survey coastline covered a terrain edit");
                foreach (float lon in new[] { -180f, -90f, 0f, 90f })
                    CheckGrid(lon,90,"Edited polar cap");
                if (!(Math.Abs(Sample(6,survey[1].x,survey[1].y).r-intact) < .01f)) failures.Add("Distant edit changed an intact survey");

                var generated = new GameObject("Procedural projection control");
                generated.transform.SetParent(root.transform);
                var procedural = generated.AddComponent<FlatMapRenderer>();
                procedural.Initialize(FlatGenerator.Generate(64,32,"Projection control",TerrainPreset.Continents,default));
                probe.CopyPropertiesFromMaterial(procedural.Material);
                if (procedural.Material.GetFloat("_EarthPolarReady") != 0) failures.Add("Survey enabled on a procedural world");
                CheckGrid(45,83,"Procedural Arctic");
                if (Sample(8,landLon,landLat).g!=0) failures.Add("Earth land relief enabled on a procedural world");
                if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
                Debug.Log("Earth projection PASS: polar ground, survey slopes, map edits, date line, procedural fallback and equal-area HUD.");
            }
            finally
            {
                // Plain MonoBehaviours created outside Play Mode do not receive the
                // normal gameplay destruction callback. Finish their native jobs first.
                if (!Application.isPlaying)
                {
                    var jobs = typeof(FlatMapRenderer).GetField("renderJobs", BindingFlags.Instance | BindingFlags.NonPublic);
                    foreach (var map in root.GetComponentsInChildren<FlatMapRenderer>())
                        (jobs.GetValue(map) as FlatMapRenderJobs)?.Dispose();
                }
                ShaderUtil.allowAsyncCompilation = async;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(read);
                UnityEngine.Object.DestroyImmediate(probe);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
