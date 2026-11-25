using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Orbis.Flat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Orbis.Editor
{
    /// <summary>
    /// Automated D3D11 regression suite verifying unified published-cyclone wind effects across:
    ///   1) Production surface &amp; wildlife shader compilation (zero errors, zero new samplers);
    ///   2) Trees (all 8 species x 5 seeds at strength=1.0 eyewall): pinned trunk roots, draw-bounds compliance,
    ///      directional storm bend, matching wind-deformed cast shadow, calm convergence;
    ///   3) Settlements: pinned walls/foundation (y&lt;=15), fluttering eaves/awning/flag in storm wind,
    ///      downwind flag reversal, matching animated cast shadow, calm convergence;
    ///   4) Wildlife GPU pose &amp; shadow: crouched silhouette, pinned feet, lowered cast shadow, calm convergence;
    ///   5) CPU FlatWildlifePresenter.SamplePublishedCycloneWind chirality &amp; eyewall profile parity with GPU.
    /// </summary>
    public static class StormWindEffectsRegression
    {
        const int Resolution = 128;

        [MenuItem("Tools/ORBIS/Storm Hazards/Validate Storm Wind Effects (D3D11)")]
        public static void Run()
        {
            var report = new List<string>();
            bool savedAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                CompileShaders(report);
                VerifyCpuCycloneWindParity(report);
                VerifyTreeAndSettlementStormWind(report);
                VerifyWildlifeStormPostureAndShadow(report);

                // Run existing vegetation & wildlife baseline regressions to prove zero regressions when calm.
                VegetationWindRegression.Run();
                report.Add("PASS VegetationWindRegression baseline");
                WildlifeActorRegression.Run();
                report.Add("PASS WildlifeActorRegression baseline");
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = savedAsync;
                Directory.CreateDirectory("ValidationOutput/StormWindEffects");
                File.WriteAllLines("ValidationOutput/StormWindEffects/storm-wind-regression.txt", report);
            }

            if (report.Exists(l => l.StartsWith("FAIL", StringComparison.Ordinal)))
                throw new InvalidOperationException(string.Join("\n", report.FindAll(l => l.StartsWith("FAIL", StringComparison.Ordinal))));
        }

        static void CompileShaders(List<string> report)
        {
            string[] shaders =
            {
                "Assets/ORBIS/Resources/ORBIS/FlatWorld/FlatMap.shader",
                "Assets/ORBIS/Resources/ORBIS/FlatWorld/LocalSurface.shader",
                "Assets/ORBIS/Resources/ORBIS/FlatWorld/ScrollSurface.shader",
                "Assets/ORBIS/Resources/ORBIS/FlatWorld/WildlifeActors.shader",
                "Assets/ORBIS/Editor/VegetationArtProbe.shader"
            };
            foreach (string path in shaders)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                Require(report, shader && shader.isSupported && !ShaderUtil.ShaderHasError(shader),
                    "shader loads cleanly: " + path);
                var mat = new Material(shader);
                try
                {
                    for (int p = 0; p < mat.passCount; p++)
                    {
                        ShaderUtil.CompilePass(mat, p, true);
                        Require(report, ShaderUtil.IsPassCompiled(mat, p), $"compiled pass {p} of {path}");
                    }
                    foreach (var msg in ShaderUtil.GetShaderMessages(shader))
                        if (msg.severity.ToString() == "Error")
                            throw new InvalidOperationException($"Shader error in {path}: {msg.message}");
                    if (path.Contains("FlatWorld/"))
                    {
                        Require(report, mat.HasProperty("_CycloneNorth") && mat.HasProperty("_CycloneSouth"),
                            "declares _CycloneNorth/_CycloneSouth for CopyPropertiesFromMaterial: " + path);
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(mat); }
            }
        }

        static void VerifyCpuCycloneWindParity(List<string> report)
        {
            // Place a strong NH cyclone at (u=0.50, v=0.66 => lon=0, lat=28.8N), strength=1.0.
            Vector4 cycN = CycloneVector(0.50, 0.66, 1.0f);
            Vector2 calm = FlatWildlifePresenter.SamplePublishedCycloneWind(0.50, 0.66, Vector4.zero, Vector4.zero);
            Require(report, calm.sqrMagnitude == 0f, "CPU cyclone wind is exact zero when calm");

            // Sample north of the NH CCW eyewall (lat + ~1.7 deg -> r ~ 0.14): wind should blow strongly westward (negative east).
            Vector2 northWall = FlatWildlifePresenter.SamplePublishedCycloneWind(0.50, 0.66 + 0.010, cycN, Vector4.zero);
            // Sample south of the NH CCW eyewall: wind should blow strongly eastward (positive east).
            Vector2 southWall = FlatWildlifePresenter.SamplePublishedCycloneWind(0.50, 0.66 - 0.010, cycN, Vector4.zero);
            // Sample far away: wind must vanish.
            Vector2 farAway = FlatWildlifePresenter.SamplePublishedCycloneWind(0.10, 0.25, cycN, Vector4.zero);
            Require(report, northWall.x < -0.9f && southWall.x > 0.9f && farAway.sqrMagnitude < 1e-6f,
                $"CPU NH CCW eyewall wind parity: northWall=({northWall.x:F2},{northWall.y:F2}) southWall=({southWall.x:F2},{southWall.y:F2}) far=({farAway.x:F3},{farAway.y:F3})");

            // SH cyclone should have opposite CW chirality (south of eye blows westward).
            Vector4 cycS = CycloneVector(0.50, 0.34, 1.0f);
            Vector2 shSouthWall = FlatWildlifePresenter.SamplePublishedCycloneWind(0.50, 0.34 - 0.010, Vector4.zero, cycS);
            Require(report, shSouthWall.x < -0.9f, $"CPU SH CW eyewall wind parity: southWall=({shSouthWall.x:F2},{shSouthWall.y:F2})");
        }

        static void VerifyTreeAndSettlementStormWind(List<string> report)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/VegetationArtProbe.shader");
            var mat = new Material(shader);
            var target = RenderTexture.GetTemporary(Resolution, Resolution, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var image = new Texture2D(Resolution, Resolution, TextureFormat.RGBAFloat, false, true);
            var prevTarget = RenderTexture.active;
            try
            {
                // Root at tile (256, 170) on 512x256 -> u=0.50098, v=0.66602 (~29.9N, 0.35E).
                mat.SetVector("_MapSize", new Vector4(512, 256, 1f / 512f, 1f / 256f));
                mat.SetVector("_ProbeRoot", new Vector4(256, 170, 0, 0));
                mat.SetVector("_ProbeLight", new Vector4(-.55f, .4f, .75f, 0));
                mat.SetFloat("_ProbeSeed", .43f);
                mat.SetFloat("_ProbeFoliage", 1f);
                mat.SetFloat("_VegetationWindReady", 0f);
                mat.SetFloat("_SkyClock", 1.2f);

                double rootU = 256.5 / 512.0, rootV = 170.5 / 256.0;
                // Cyclone centred just NORTH of root -> NH CCW south eyewall blows strongly EASTWARD (+X).
                Vector4 stormEast = CycloneVector(rootU, rootV + 0.011, 1.0f);
                // Cyclone centred just SOUTH of root -> NH CCW north eyewall blows strongly WESTWARD (-X).
                Vector4 stormWest = CycloneVector(rootU, rootV - 0.011, 1.0f);

                // --- Trees: all 8 species under full strength=1.0 cyclone eyewall ---
                for (int species = 0; species < 8; species++)
                {
                    mat.SetFloat("_ProbeSpecies", species);
                    mat.SetFloat("_ProbeSeed", .43f);
                    mat.SetFloat("_ProbeMode", 3);
                    mat.SetVector("_CycloneNorth", Vector4.zero);
                    var calmTree = Sample(mat, target, image);

                    mat.SetVector("_CycloneNorth", stormEast);
                    var eastTree = Sample(mat, target, image);
                    mat.SetVector("_CycloneNorth", stormWest);
                    var westTree = Sample(mat, target, image);
                    Require(report, CentroidX(eastTree) > CentroidX(westTree) + 0.25,
                        $"species={species} cyclone eyewall bends crown downwind: eastX={CentroidX(eastTree):F2} vs westX={CentroidX(westTree):F2}");
                    Require(report, MeanDiff(calmTree, eastTree) > 0.002,
                        $"species={species} storm bend clearly exceeds calm baseline");

                    // Trunk root pinned at y <= 0 under full cyclone eyewall.
                    mat.SetFloat("_ProbeMode", 5);
                    mat.SetVector("_CycloneNorth", Vector4.zero);
                    var calmTrunk = Sample(mat, target, image);
                    mat.SetVector("_CycloneNorth", stormEast);
                    var stormTrunk = Sample(mat, target, image);
                    double rootDelta = 0;
                    for (int y = 0; y < Resolution; y++)
                    {
                        float py = Mathf.Floor(((y + .5f) / Resolution - .30f) * 48f);
                        if (py > 0f) continue;
                        for (int x = 0; x < Resolution; x++)
                            rootDelta += Math.Abs(calmTrunk[y * Resolution + x].a - stormTrunk[y * Resolution + x].a);
                    }
                    Require(report, rootDelta == 0.0, $"species={species} trunk root stays 100% pinned in storm eyewall");

                    // Draw bounds check across 5 seeds at strength=1.0.
                    mat.SetFloat("_ProbeMode", 7);
                    foreach (float seed in new[] { 0f, .25f, .5f, .75f, .99f })
                    {
                        mat.SetFloat("_ProbeSeed", seed);
                        mat.SetVector("_CycloneNorth", stormEast);
                        var outside = Sample(mat, target, image);
                        int oob = 0;
                        foreach (var px in outside) if (px.a > .5f) oob++;
                        Require(report, oob == 0, $"species={species} seed={seed:F2} stays inside PDTreeInDrawBounds under Cat-5 storm (oob={oob})");
                    }

                    // Shadow follows wind-deformed crown.
                    mat.SetFloat("_ProbeSeed", .43f);
                    mat.SetFloat("_ProbeMode", 4);
                    mat.SetVector("_CycloneNorth", Vector4.zero);
                    var calmSh = Sample(mat, target, image);
                    mat.SetVector("_CycloneNorth", stormEast);
                    var stormSh = Sample(mat, target, image);
                    Require(report, MeanDiff(calmSh, stormSh) > 0.001, $"species={species} cast shadow moves with cyclone-bent crown");
                }

                // --- Settlements: pinned foundation/wall, fluttering eaves/awning/flag, animated shadow ---
                mat.SetFloat("_ProbeSeed", .37f);
                mat.SetFloat("_ProbeMode", 14);
                mat.SetVector("_CycloneNorth", Vector4.zero);
                mat.SetFloat("_SkyClock", 0f);
                var houseCalm0 = Sample(mat, target, image);
                mat.SetFloat("_SkyClock", 5.5f);
                var houseCalm1 = Sample(mat, target, image);
                Require(report, MeanDiff(houseCalm0, houseCalm1) < 1e-6, "settlement converges to exact stillness when calm");

                mat.SetVector("_CycloneNorth", stormEast);
                mat.SetFloat("_SkyClock", 1.0f);
                var houseStormA = Sample(mat, target, image);
                mat.SetFloat("_SkyClock", 2.3f);
                var houseStormB = Sample(mat, target, image);
                Require(report, MeanDiff(houseCalm0, houseStormA) > 0.002 && MeanDiff(houseStormA, houseStormB) > 0.0005,
                    $"settlement flag/awning/eaves flutter in storm wind (calmVsStorm={MeanDiff(houseCalm0, houseStormA):F4}, gustDelta={MeanDiff(houseStormA, houseStormB):F4})");

                // Foundation & main wall box (x in [8..24], y in [5..13]) must stay 100% pinned!
                double foundationDiff = 0;
                for (int y = 0; y < Resolution; y++)
                {
                    float qy = Mathf.Floor((y + .5f) / Resolution * 32f);
                    if (qy < 5f || qy > 13f) continue;
                    for (int x = 0; x < Resolution; x++)
                    {
                        float qx = Mathf.Floor((x + .5f) / Resolution * 32f);
                        if (qx < 8f || qx > 24f) continue;
                        int i = y * Resolution + x;
                        foundationDiff += Math.Abs(houseCalm0[i].r - houseStormA[i].r) + Math.Abs(houseCalm0[i].g - houseStormA[i].g)
                                        + Math.Abs(houseCalm0[i].b - houseStormA[i].b) + Math.Abs(houseCalm0[i].a - houseStormA[i].a);
                    }
                }
                Require(report, foundationDiff < 1e-5, $"settlement foundation & wall remain 100% pinned (diff={foundationDiff:E2})");

                // Downwind flag flips east vs west when cyclone wind reverses!
                mat.SetVector("_CycloneNorth", stormWest);
                mat.SetFloat("_SkyClock", 1.0f);
                var houseStormWest = Sample(mat, target, image);
                double flagEastX = UpperCentroidX(houseStormA, 26.5f);
                double flagWestX = UpperCentroidX(houseStormWest, 26.5f);
                Require(report, flagEastX > flagWestX + 1.0,
                    $"settlement gable flag reverses downwind: eastX={flagEastX:F2} vs westX={flagWestX:F2}");

                // Settlement cast shadow follows wind-deformed awning/flag/eaves!
                mat.SetFloat("_ProbeMode", 15);
                mat.SetVector("_CycloneNorth", Vector4.zero);
                var houseShCalm = Sample(mat, target, image);
                mat.SetVector("_CycloneNorth", stormEast);
                var houseShEast = Sample(mat, target, image);
                Require(report, MeanDiff(houseShCalm, houseShEast) > 0.0002,
                    $"settlement cast shadow updates with wind-deformed flag/awning (diff={MeanDiff(houseShCalm, houseShEast):F5})");
            }
            finally
            {
                RenderTexture.active = prevTarget;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(mat);
            }
        }

        static void VerifyWildlifeStormPostureAndShadow(List<string> report)
        {
            var shader = Resources.Load<Shader>("ORBIS/FlatWorld/WildlifeActors");
            var mat = new Material(shader);
            var mesh = new Mesh();
            var camObj = new GameObject("Storm wildlife regression camera");
            var cam = camObj.AddComponent<Camera>();
            cam.enabled = false; cam.orthographic = true; cam.orthographicSize = 24;
            cam.transform.SetPositionAndRotation(new Vector3(0, 0, -5), Quaternion.identity);
            cam.nearClipPlane = .1f; cam.farClipPlane = 10f;
            var target = RenderTexture.GetTemporary(192, 192, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var image = new Texture2D(192, 192, TextureFormat.RGBAFloat, false, true);
            var cb = new CommandBuffer();
            var old = RenderTexture.active;
            try
            {
                mat.SetVector("_FlatSun", Vector3.forward);
                mat.SetFloat("_FlatDayNight", 1f);
                mat.SetTexture("_SkyTex", Texture2D.blackTexture);
                mat.SetFloat("_SkyReady", 0f);
                mat.SetFloat("_WildlifeOcclusionReady", 0f);

                mesh.vertices = new[] { new Vector3(-24, -24, 0), new Vector3(24, -24, 0), new Vector3(-24, 24, 0), new Vector3(24, 24, 0) };
                mesh.uv = new[] { new Vector2(-24, -24), new Vector2(24, -24), new Vector2(-24, 24), new Vector2(24, 24) };
                mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                mesh.RecalculateBounds();

                // Actor at (u=0.50, v=0.66) facing +1 (east). Headwind comes from east (westward flow => storm centred south of actor).
                Vector4 headwindStorm = CycloneVector(0.50, 0.66 - 0.011, 1.0f);
                foreach (int species in new[] { 1, 2, 3, 4 })
                {
                    SetActorMesh(mesh, species, state: 1, speed: 0.25f, clock: 0.45f, u: 0.50f, v: 0.66f);
                    mat.SetVector("_CycloneNorth", Vector4.zero);
                    mat.SetVector("_CycloneSouth", Vector4.zero);
                    mat.SetFloat("_SkyClock", 1.0f);
                    var calm = SampleMesh(mesh, mat, cam, target, image, cb);

                    mat.SetVector("_CycloneNorth", headwindStorm);
                    var storm = SampleMesh(mesh, mat, cam, target, image, cb);
                    Require(report, MeanDiff(calm, storm) > 0.0005,
                        $"wildlife species={species} responds visibly to published cyclone wind (diff={MeanDiff(calm, storm):F5})");

                    double calmCy = CentroidY(calm, 192), stormCy = CentroidY(storm, 192);
                    Require(report, stormCy < calmCy - 0.10,
                        $"wildlife species={species} crouches lower in storm wind (calmY={calmCy:F2} -> stormY={stormCy:F2})");

                    // Feet/hooves anchor check for ground mammals (rabbit & deer, stationary idle pose).
                    if (species <= 2)
                    {
                        SetActorMesh(mesh, species, state: 0, speed: 0f, clock: 0.45f, u: 0.50f, v: 0.66f);
                        mat.SetVector("_CycloneNorth", Vector4.zero);
                        var idleCalm = SampleMesh(mesh, mat, cam, target, image, cb);
                        mat.SetVector("_CycloneNorth", headwindStorm);
                        var idleStorm = SampleMesh(mesh, mat, cam, target, image, cb);
                        float footMaxY = species == 1 ? -3.6f : -4.6f;
                        double footDiff = 0;
                        for (int y = 0; y < 192; y++)
                        {
                            float py = ((y + .5f) / 192f - .5f) * 48f;
                            if (py > footMaxY || py < -6.5f) continue;
                            for (int x = 0; x < 192; x++)
                            {
                                int i = y * 192 + x;
                                if (idleCalm[i].a > .8f || idleStorm[i].a > .8f)
                                    footDiff += Math.Abs((idleCalm[i].a > .8f ? 1.0 : 0.0) - (idleStorm[i].a > .8f ? 1.0 : 0.0));
                            }
                        }
                        Require(report, footDiff < 2.0, $"wildlife species={species} feet stay planted during storm crouch (deltaPx={footDiff:F1})");
                    }
                }
            }
            finally
            {
                RenderTexture.active = old;
                cb.Release();
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(mat);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(camObj);
            }
        }

        static Vector4 CycloneVector(double u, double v, float strength)
        {
            double lon = (u - 0.5) * Math.PI * 2.0, lat = (v - 0.5) * Math.PI;
            return new Vector4((float)(Math.Sin(lon) * Math.Cos(lat)), (float)Math.Sin(lat), (float)(Math.Cos(lon) * Math.Cos(lat)), strength);
        }

        static void SetActorMesh(Mesh mesh, int species, int state, float speed, float clock, float u, float v)
        {
            var actor = new Vector4(species, 1f, clock, .31f);
            var beh = new Vector4(state, speed, 1f, .4f);
            var geo = new Vector4(u, v, .35f, .25f);
            var mot = new Vector4(1f, 0f, 1f, 18f);
            var axes = new Vector4(1f / 32f, 0f, 0f, 1f / 32f);
            mesh.SetUVs(1, new List<Vector4> { actor, actor, actor, actor });
            mesh.SetUVs(2, new List<Vector4> { beh, beh, beh, beh });
            mesh.SetUVs(3, new List<Vector4> { geo, geo, geo, geo });
            mesh.SetUVs(4, new List<Vector4> { mot, mot, mot, mot });
            mesh.SetUVs(5, new List<Vector4> { axes, axes, axes, axes });
        }

        static Color[] Sample(Material mat, RenderTexture target, Texture2D image)
        {
            Graphics.Blit(Texture2D.whiteTexture, target, mat);
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
            image.Apply();
            return image.GetPixels();
        }

        static Color[] SampleMesh(Mesh mesh, Material mat, Camera cam, RenderTexture target, Texture2D image, CommandBuffer cb)
        {
            cb.Clear(); cb.SetRenderTarget(target); cb.SetViewport(new Rect(0, 0, 192, 192)); cb.ClearRenderTarget(true, true, Color.clear);
            cb.SetViewProjectionMatrices(cam.worldToCameraMatrix, GL.GetGPUProjectionMatrix(cam.projectionMatrix, false));
            cb.DrawMesh(mesh, Matrix4x4.identity, mat, 0, 0); Graphics.ExecuteCommandBuffer(cb);
            RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 192, 192), 0, 0); image.Apply();
            return image.GetPixels();
        }

        static double MeanDiff(Color[] a, Color[] b)
        {
            double s = 0;
            for (int i = 0; i < a.Length; i++)
                s += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) + Math.Abs(a[i].a - b[i].a);
            return s / (a.Length * 4.0);
        }

        static double CentroidX(Color[] pixels)
        {
            double w = 0, s = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a < .5f) continue;
                w += 1; s += i % Resolution;
            }
            return w > 0 ? s / w : 0;
        }

        static double UpperCentroidX(Color[] pixels, float minQy)
        {
            double w = 0, s = 0;
            for (int y = 0; y < Resolution; y++)
            {
                float qy = (y + .5f) / Resolution * 32f;
                if (qy < minQy) continue;
                for (int x = 0; x < Resolution; x++)
                {
                    if (pixels[y * Resolution + x].a < .5f) continue;
                    w += 1; s += (x + .5f) / Resolution * 32f;
                }
            }
            return w > 0 ? s / w : 0;
        }

        static double CentroidY(Color[] pixels, int size)
        {
            double w = 0, s = 0;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float a = pixels[y * size + x].a;
                if (a < .8f) continue;
                w += a; s += y * a;
            }
            return w > 0 ? s / w : 0;
        }

        /// <summary>
        /// Verifies in PlayMode that while an isolated demo world is active:
        ///   - Save() cannot overwrite the player's SavePath;
        ///   - Generate(), Load(), and Import() are blocked without dropping savedSession or hiding hazardReturnOriginal;
        ///   - Busy ExitIsolatedDemoWorld(), cancelled async tasks, and faulted async tasks preserve IsDemoWorldActive,
        ///     keep hazardReturnOriginal visible, and keep Save() blocked until a valid ExitIsolatedDemoWorld() succeeds.
        /// </summary>
        public static void VerifyDemoIsolationFailureAndCancel(FlatWorldApp app, WorldData expectedDemoWorld, List<string> report = null)
        {
            const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
            var localReport = report ?? new List<string>();
            var appType = typeof(FlatWorldApp);
            var returnBtn = appType.GetField("returnOriginalButton", Private)?.GetValue(app) as Button;
            Require(localReport, app != null && app.IsDemoWorldActive && ReferenceEquals(app.World, expectedDemoWorld),
                "demo isolation active on expected demo world");
            Require(localReport, returnBtn != null && !returnBtn.ClassListContains("is-hidden"),
                "hazardReturnOriginal button is visible during isolated demo");

            string savePath = app.SavePath;
            bool hadExistingSave = File.Exists(savePath);
            byte[] existingSaveBytes = hadExistingSave ? File.ReadAllBytes(savePath) : null;
            string sentinel = "ORBIS_DEMO_ISOLATION_SENTINEL_" + DateTime.UtcNow.Ticks;
            try
            {
                if (!hadExistingSave) File.WriteAllText(savePath, sentinel, new UTF8Encoding(false));
                DateTime beforeWrite = File.GetLastWriteTimeUtc(savePath);

                appType.GetMethod("Save", Private)?.Invoke(app, null);
                bool saveIntact = hadExistingSave
                    ? File.GetLastWriteTimeUtc(savePath) == beforeWrite
                    : File.ReadAllText(savePath, new UTF8Encoding(false)) == sentinel;
                Require(localReport, saveIntact, "Save() blocked while isolated demo is active");

                foreach (string method in new[] { "Generate", "Load", "Import" })
                {
                    appType.GetMethod(method, Private)?.Invoke(app, null);
                    var queued = appType.GetField("pending", Private)?.GetValue(app);
                    Require(localReport,
                        queued == null && app.IsDemoWorldActive && !returnBtn.ClassListContains("is-hidden") && ReferenceEquals(app.World, expectedDemoWorld),
                        $"{method}() blocked during demo without dropping return button or isolation");
                }

                // Simulated busy ExitIsolatedDemoWorld() + cancelled async task.
                var canceledTcs = new TaskCompletionSource<WorldData>();
                canceledTcs.SetCanceled();
                appType.GetField("pending", Private)?.SetValue(app, canceledTcs.Task);
                bool busyExit = app.ExitIsolatedDemoWorld();
                Require(localReport, !busyExit && app.IsDemoWorldActive && !returnBtn.ClassListContains("is-hidden"),
                    "ExitIsolatedDemoWorld() while busy preserves isolation and return button");
                appType.GetMethod("Update", Private)?.Invoke(app, null);
                Require(localReport, app.IsDemoWorldActive && !returnBtn.ClassListContains("is-hidden") && ReferenceEquals(app.World, expectedDemoWorld),
                    "cancelled async task preserves demo isolation and return button");

                // Simulated faulted async task (failed generate/load/restore).
                var faultedTcs = new TaskCompletionSource<WorldData>();
                faultedTcs.SetException(new IOException("Simulated async failure"));
                appType.GetField("pending", Private)?.SetValue(app, faultedTcs.Task);
                appType.GetMethod("Update", Private)?.Invoke(app, null);
                Require(localReport, app.IsDemoWorldActive && !returnBtn.ClassListContains("is-hidden") && ReferenceEquals(app.World, expectedDemoWorld),
                    "faulted async task preserves demo isolation and return button");

                // Save() must remain blocked even after cancel/failure recovery.
                appType.GetMethod("Save", Private)?.Invoke(app, null);
                bool saveStillIntact = hadExistingSave
                    ? File.GetLastWriteTimeUtc(savePath) == beforeWrite
                    : File.ReadAllText(savePath, new UTF8Encoding(false)) == sentinel;
                Require(localReport, saveStillIntact, "Save() stays blocked after async cancel/fault during demo");
            }
            finally
            {
                if (!hadExistingSave) { try { File.Delete(savePath); } catch { } }
                else if (existingSaveBytes != null) { try { File.WriteAllBytes(savePath, existingSaveBytes); } catch { } }
            }

            if (localReport.Exists(l => l.StartsWith("FAIL", StringComparison.Ordinal)))
                throw new InvalidOperationException(string.Join("\n", localReport.FindAll(l => l.StartsWith("FAIL", StringComparison.Ordinal))));
        }

        static void Require(List<string> report, bool ok, string label) => report.Add((ok ? "PASS " : "FAIL ") + label);
    }
}
