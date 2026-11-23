// Unity Pipeline eval_file. Run with a loaded 4096 Earth. The probe shares the published
// terrain and drives its own disabled camera, leaving the player's view and clock alone.
var app = UnityEngine.Object.FindFirstObjectByType<Orbis.Flat.FlatWorldApp>();
if (app?.World?.Width != 4096 || app.World.Preset != Orbis.Flat.TerrainPreset.Earth || app.Busy)
    throw new InvalidOperationException("Load the 4096 Earth preset before running the fold drag regression.");
var map = app.GetComponentInChildren<Orbis.Flat.FlatMapRenderer>();
if (map.PreparationPending) throw new InvalidOperationException("Wait for terrain publication.");
var root = new GameObject("Fold drag regression");
try
{
    var cameraObject = new GameObject("Fold drag regression camera");
    cameraObject.transform.SetParent(root.transform, false);
    var camera = cameraObject.AddComponent<Camera>();
    camera.enabled = false;
    camera.pixelRect = new Rect(0, 0, 1419, 543);
    var nav = root.AddComponent<Orbis.Flat.FlatScrollNavigator>();
    nav.Initialize(app.World, camera, map);
    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    var navType = nav.GetType();
    var chartPoint = navType.GetMethod("LocalPoint", flags);
    var rendered = navType.GetMethod("TryRenderedSurface", flags);
    var sample = navType.GetMethod("ProjectedVertex", flags);
    var relief = navType.GetProperty("ReliefParameters", flags);
    const double degrees = Math.PI / 180, day = .18;
    var center = new Orbis.Planet.PlanetLocation(61 * degrees, 41 * degrees);
    nav.Recenter(center, day);
    nav.Wheel(Mathf.Log(Mathf.Lerp(.6f, .22f, .30f) / 2.1f) / .075f, camera.pixelRect.center, day);
    nav.Tick(3, day);
    nav.Recenter(center, day);
    float morph = nav.Morph, pixels = nav.PixelsPerTile;
    if (Math.Abs(morph - .30f) > .001f) throw new Exception("The test did not reach 30% unfold.");

    var sites = new List<Orbis.Planet.PlanetVector>();
    for (int y = 0; y <= 22; y++)
        for (int x = 0; x <= 39; x++)
            sites.Add(Orbis.Planet.PlanetCoordinateSystem.SurfaceNormal(new Orbis.Planet.PlanetLocation(
                (40 + x * 1.125) * degrees, (29 + y * .925) * degrees)));
    var lastError = new Vector2[sites.Count];
    var previousDelta = new Vector2[sites.Count];
    var errors = new List<float>();
    float maximumParallax = 0;
    for (int frame = 0; frame <= 32; frame++)
    {
        if (frame > 0) nav.Drag(new Vector2(frame <= 16 ? 4 : -4, 0));
        nav.Tick(1f / 60, day);
        if (nav.Morph != morph || nav.PixelsPerTile != pixels) throw new Exception("A drag changed zoom.");
        var view = (Vector4)relief.GetValue(nav);
        for (int i = 0; i < sites.Count; i++)
        {
            var chart = (Vector2)chartPoint.Invoke(nav, new object[]{ sites[i] });
            var args = new object[]{ chart, view, false, Vector3.zero, Vector3.zero, Vector3.zero };
            if (!(bool)rendered.Invoke(nav, args)) throw new Exception("Lost an in-view landmark");
            Vector2 screen = camera.WorldToScreenPoint((Vector3)args[3]);
            Vector2 reference = camera.WorldToScreenPoint((Vector3)(Vector4)sample.Invoke(nav, new object[]{ chart, view }));
            var error = screen - reference;
            var delta = error - lastError[i];
            if (frame >= 2 && frame != 17) errors.Add((delta - previousDelta[i]).magnitude);
            previousDelta[i] = delta;
            lastError[i] = error;
            if (frame != 0) continue;
            Vector3 radial = new Vector3((float)sites[i].X, (float)sites[i].Y, -(float)sites[i].Z);
            radial = Quaternion.AngleAxis(nav.DisplaySpin * Mathf.Rad2Deg, Vector3.up) * radial;
            var material = nav.SurfaceMaterial;
            Vector3 sea = (Vector3)material.GetVector("_ScrollNormal") + (Vector3)material.GetVector("_ScrollRight") * chart.x
                + (Vector3)material.GetVector("_ScrollUp") * chart.y;
            Vector2 atSeaLevel = camera.WorldToScreenPoint(Vector3.Lerp(radial, sea, morph));
            maximumParallax = Mathf.Max(maximumParallax, (reference - atSeaLevel).magnitude);
        }
    }
    // Read geographic directions from the production vertex projection. Forward-project
    // each GPU hit through the CPU triangles used by routes and picking.
    const int width = 512, height = 196;
    var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
    var image = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
    var probe = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/PolarDetailProbe.shader"));
    var commands = new UnityEngine.Rendering.CommandBuffer();
    var previousTarget = RenderTexture.active;
    float gpuError = 0;
    try
    {
        probe.CopyPropertiesFromMaterial(nav.SurfaceMaterial);
        ShaderUtil.CompilePass(probe, 1, true);
        var mesh = nav.GetComponentsInChildren<MeshFilter>().Single(f => f.name == "Local spherical terrain");
        commands.SetRenderTarget(target);
        commands.SetViewport(new Rect(0, 0, width, height));
        commands.ClearRenderTarget(true, true, Color.clear);
        commands.SetViewProjectionMatrices(camera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(camera.projectionMatrix, false));
        commands.DrawMesh(mesh.sharedMesh, mesh.transform.localToWorldMatrix, probe, 0, 1);
        Graphics.ExecuteCommandBuffer(commands);
        RenderTexture.active = target;
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        var view = (Vector4)relief.GetValue(nav);
        for (int y = 48; y < height - 25; y += 20)
            for (int x = 132; x < width - 100; x += 18)
            {
                var hit = image.GetPixel(x, y);
                if (hit.a < .5f) throw new Exception("Missing GPU terrain sample");
                var point = (Vector2)chartPoint.Invoke(nav, new object[]{new Orbis.Planet.PlanetVector(hit.r, hit.g, hit.b)});
                var args = new object[]{point, view, false, Vector3.zero, Vector3.zero, Vector3.zero};
                if (!(bool)rendered.Invoke(nav, args)) throw new Exception("Missing CPU terrain sample");
                Vector2 screen = camera.WorldToScreenPoint((Vector3)args[3]);
                var pixel = Vector2.Scale(new Vector2((x + .5f) / width, (y + .5f) / height), camera.pixelRect.size);
                gpuError = Mathf.Max(gpuError, Vector2.Distance(screen, pixel));
            }
    }
    finally
    {
        RenderTexture.active = previousTarget;
        commands.Release();
        RenderTexture.ReleaseTemporary(target);
        UnityEngine.Object.DestroyImmediate(probe);
        UnityEngine.Object.DestroyImmediate(image);
    }
    nav.GoGlobe(day);
    nav.Tick(3, day);
    var rotation = camera.transform.rotation;
    nav.Drag(new Vector2(4, 0));
    nav.Drag(new Vector2(-4, 0));
    if (!nav.IsGlobe || Quaternion.Angle(rotation, camera.transform.rotation) > .05f)
        throw new Exception("Globe drag did not return to its starting orientation.");
    int tile = app.World.Index((int)((61 + 180) / 360.0 * app.World.Width), (int)((41 + 90) / 180.0 * app.World.Height));
    nav.Focus(tile, day);
    nav.Tick(3, day);
    float localPixels = nav.PixelsPerTile;
    nav.Drag(new Vector2(-4, 0));
    if (!nav.IsFlat || nav.PixelsPerTile != localPixels || !nav.TryGetPlanetLocation(camera.pixelRect.center, out _))
        throw new Exception("Local drag lost zoom or its terrain hit.");
    errors.Sort();
    float worst = errors[errors.Count - 1], p95 = errors[(int)(errors.Count * .95)];
    string details = $"landmarks={sites.Count}, p95={p95:F3} px, worst={worst:F3} px, mountain parallax={maximumParallax:F2} px, GPU mismatch={gpuError:F3} px";
    if (p95 > .18f || worst > .8f || maximumParallax < 2 || gpuError > .20f) throw new Exception("Fold drag regression: " + details);
    return details;
}
finally { UnityEngine.Object.DestroyImmediate(root); }
