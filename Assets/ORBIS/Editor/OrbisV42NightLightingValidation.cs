using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.Editor
{
    /// <summary>
    /// Explicit, current-API compilation check. No automatic import/play-mode hooks.
    /// Writes a checkpoint BEFORE each import/pass so a native compiler crash cannot
    /// leave a success report from an earlier run in place. Does not change project settings.
    /// </summary>
    public static class OrbisV42NightLightingValidation
    {
        [Serializable] sealed class PassResult
        {
            public int index;
            public string name, exception;
            public bool requested, compiled;
        }
        [Serializable] sealed class ShaderResult
        {
            public string path, shaderName, exception;
            public bool loaded, supported, hasErrors, passed;
            public int expectedPassCount, actualPassCount;
            public PassResult[] passes = new PassResult[0];
            public string[] messages = new string[0];
        }
        [Serializable] sealed class SourceResult
        {
            public string path, sha256;
        }
        [Serializable] sealed class Report
        {
            public string patch = "V4.2 Repair IPCFix1 + NightLighting";
            public string timeUtc, updatedUtc, unityVersion, operatingSystem;
            public string graphicsAPI, graphicsDevice, colorSpace, editorLogPath;
            public string state = "STARTED", currentOperation, exception;
            public bool builtIn, graphicsAvailable, shaderOptimizationDisabled;
            public bool success, compilerErrorObserved;
            public bool nightSettingsTestsPerformed, nightSettingsTestsPassed;
            public bool playModeTestPerformed = false, visualAcceptancePerformed = false;
            public bool crossPlatformBuildPerformed = false;
            public string scope = "Default-material passes on CURRENT Editor graphics API/color space only. " +
                "Uniform values do not define variants. This is not an FPS, visual or cross-platform acceptance test.";
            public ShaderResult[] shaders = new ShaderResult[0];
            public SourceResult[] sources = new SourceResult[0];
            public string[] compilerLog = new string[0];
        }

        static readonly ConcurrentQueue<string> Captured = new ConcurrentQueue<string>();
        static int compilerError;

        [MenuItem("Tools/ORBIS/V4.2 Repair/编译夜侧光照并保存诊断")]
        public static void Validate() { Run(); }

        // Example: Unity -batchmode -projectPath <project> -executeMethod
        // Orbis.Editor.OrbisV42NightLightingValidation.ValidateBatch -logFile <log>
        // Do not use -nographics: a graphics backend is required for this check.
        public static void ValidateBatch()
        {
            bool ok = Run();
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static void CaptureLog(string condition, string stackTrace, LogType type)
        {
            if (string.IsNullOrEmpty(condition)) return;
            string lower = condition.ToLowerInvariant();
            if (!lower.Contains("shader") && !lower.Contains("compiler") &&
                !lower.Contains("ipc") && !lower.Contains("0x80000004")) return;
            if (Captured.Count < 256) Captured.Enqueue(type + ": " + condition);
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                Interlocked.Exchange(ref compilerError, 1);
        }

        static string HashFile(string path)
        {
            if (!File.Exists(path)) return "MISSING";
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        static void Checkpoint(Report report, string path)
        {
            report.updatedUtc = DateTime.UtcNow.ToString("O");
            report.compilerLog = Captured.ToArray();
            report.compilerErrorObserved = Interlocked.CompareExchange(ref compilerError, 0, 0) != 0;
            // Flush and close before calling native shader compilation.
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
        }

        static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException("Night settings test: " + reason);
        }
        static void TestNightSettings()
        {
            // Real Unity JsonUtility tests when this menu is run, NOT a claim that
            // the offline deliverable has executed Unity/C#.
            var old = JsonUtility.FromJson<PlanetSettings>("{}");
            old.nightLighting = null; // explicit representation of a pre-extension save
            var restored = OrbisNightLighting.Ensure(old);
            Require(restored.enabled && restored.skyStrength == 1f && restored.airglowStrength == 1f,
                "old save must get default night illumination");
            restored.skyStrength = 0; restored.airglowStrength = 0; restored.fullMoonStrength = 0;
            var roundtrip = JsonUtility.FromJson<PlanetSettings>(JsonUtility.ToJson(old));
            var zeros = OrbisNightLighting.Ensure(roundtrip);
            Require(zeros.skyStrength == 0 && zeros.airglowStrength == 0 && zeros.fullMoonStrength == 0,
                "intentional zeros must survive save roundtrip");
            var settings = new OrbisNightLightingSettings();
            float fraction, brightness;
            Vector3 sun = new Vector3(.4f, .2f, .8f).normalized;
            Vector3 moon = OrbisNightLighting.EvaluateMoon(sun, 100, settings, out fraction, out brightness);
            Require(Vector3.Dot(sun, moon) < -.9999f && fraction > .9999f && brightness > .9999f,
                "default day100 is full moon, opposite the sun");
            settings.phaseAtDay100 = 0;
            moon = OrbisNightLighting.EvaluateMoon(sun, 100, settings, out fraction, out brightness);
            Require(Vector3.Dot(sun, moon) > .9999f && fraction < .0001f && brightness < .0001f,
                "new moon has no direct lunar illumination");
            settings.phaseAtDay100 = .25f;
            OrbisNightLighting.EvaluateMoon(sun, 100, settings, out fraction, out brightness);
            Require(Mathf.Abs(fraction - .5f) < .0001f && Mathf.Abs(brightness - 1f/Mathf.PI) < .0001f,
                "quarter moon follows the documented Lambert approximation");
            moon = OrbisNightLighting.EvaluateMoon(Vector3.up, 1e8, settings, out fraction, out brightness);
            Require(!float.IsNaN(moon.sqrMagnitude) && Mathf.Abs(moon.magnitude-1) < .0001f,
                "polar sun and long clock must stay finite");
            settings.skyStrength = float.NaN; settings.lunarCycleDays = 0;
            settings.Sanitize();
            Require(settings.skyStrength == 1 && settings.lunarCycleDays >= 2, "invalid settings bounded");
        }

        static bool Run()
        {
            string ignored;
            while (Captured.TryDequeue(out ignored)) { }
            Interlocked.Exchange(ref compilerError, 0);
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(root, "Documentation/V4_2_Repair_IPCFix_NightLighting");
            string output = Path.Combine(directory, "Unity-compile-report.json");
            var report = new Report
            {
                timeUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                operatingSystem = SystemInfo.operatingSystem,
                graphicsAPI = SystemInfo.graphicsDeviceType.ToString(),
                graphicsDevice = SystemInfo.graphicsDeviceName,
                colorSpace = QualitySettings.activeColorSpace.ToString(),
                editorLogPath = Application.consoleLogPath,
                builtIn = GraphicsSettings.currentRenderPipeline == null,
                graphicsAvailable = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
                shaderOptimizationDisabled = ShaderUtil.disableShaderOptimization
            };
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            bool subscribed = false;
            try
            {
                Directory.CreateDirectory(directory);
                var sources = new List<SourceResult>();
                foreach (string name in new[] { "Clouds.shader", "OrbisAtmosphereCommon.cginc",
                    "OrbisCloudCommon.cginc", "OrbisAtmosphereGLtracy.cginc",
                    "OrbisNightLighting.cginc", "OrbisOptics.cginc", "OrbisWater.cginc", "Surface.shader" })
                {
                    string relative = "Assets/ORBIS/Resources/ORBIS/" + name;
                    sources.Add(new SourceResult { path = relative, sha256 = HashFile(Path.Combine(root, relative)) });
                }
                foreach (string name in new[] { "OrbisNightLighting.cs", "OrbisWorld.cs", "OrbisPlanetView.cs", "OrbisDemo.cs" })
                {
                    string relative = "Assets/ORBIS/Runtime/" + name;
                    sources.Add(new SourceResult { path = relative, sha256 = HashFile(Path.Combine(root, relative)) });
                }
                report.sources = sources.ToArray();
                report.currentOperation = "Testing live C# night settings, phases and JsonUtility migration";
                Checkpoint(report, output);
                report.nightSettingsTestsPerformed = true;
                TestNightSettings();
                report.nightSettingsTestsPassed = true;
                Checkpoint(report, output);
                if (!report.graphicsAvailable || !report.builtIn)
                {
                    report.state = "NOT_RUN";
                    report.exception = "A real graphics backend and the original Built-in pipeline are required.";
                    Checkpoint(report, output);
                    Debug.LogError("ORBIS IPC 检查未执行；未将跳过当作通过。报告：" + output);
                    return false;
                }
                Application.logMessageReceivedThreaded += CaptureLog;
                subscribed = true;
                ShaderUtil.allowAsyncCompilation = false;
                string[] names = { "Clouds", "CloudShadowCache", "Surface", "RiverWater",
                    "AtmosphereComposite", "Display", "WeatherParticles" };
                int[] passCounts = { 4, 1, 2, 1, 2, 1, 1 };
                string[] cloudPasses = { "MRT_VOLUME", "FULL_RES_VOLUME", "MAP_VOLUME", "MRT_MAP_VOLUME" };
                var items = new List<ShaderResult>();
                bool allPassed = !report.shaderOptimizationDisabled && report.nightSettingsTestsPassed;
                for (int n = 0; n < names.Length; n++)
                {
                    var item = new ShaderResult
                    {
                        shaderName = "ORBIS/" + names[n], expectedPassCount = passCounts[n],
                        path = "Assets/ORBIS/Resources/ORBIS/" + names[n] + ".shader"
                    };
                    items.Add(item); report.shaders = items.ToArray();
                    Material material = null;
                    try
                    {
                        report.state = "IMPORTING"; report.currentOperation = item.path;
                        Checkpoint(report, output);
                        Shader old = AssetDatabase.LoadAssetAtPath<Shader>(item.path);
                        if (old != null) ShaderUtil.ClearCachedData(old);
                        AssetDatabase.ImportAsset(item.path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(item.path);
                        item.loaded = shader != null;
                        if (!item.loaded) throw new InvalidOperationException("Shader not found: " + item.path);
                        material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                        item.actualPassCount = material.passCount;
                        var passes = new List<PassResult>();
                        bool passesOK = item.actualPassCount == item.expectedPassCount;
                        for (int i = 0; i < material.passCount; i++)
                        {
                            var pass = new PassResult { index = i, name = material.GetPassName(i), requested = true };
                            passes.Add(pass); item.passes = passes.ToArray();
                            report.state = "COMPILING_PASS";
                            report.currentOperation = item.shaderName + " / " + i + " / " + pass.name;
                            Checkpoint(report, output);
                            try
                            {
                                ShaderUtil.CompilePass(material, i, true);
                                pass.compiled = ShaderUtil.IsPassCompiled(material, i);
                            }
                            catch (Exception e) { pass.exception = e.ToString(); }
                            passesOK &= pass.compiled && string.IsNullOrEmpty(pass.exception);
                            if (n == 0) passesOK &= i < cloudPasses.Length && pass.name == cloudPasses[i];
                            Checkpoint(report, output);
                        }
                        item.supported = shader.isSupported;
                        item.hasErrors = ShaderUtil.ShaderHasError(shader);
                        var messages = new List<string>();
                        foreach (var message in ShaderUtil.GetShaderMessages(shader))
                            messages.Add(message.severity + ": " + message.file + ":" + message.line + " " + message.message);
                        item.messages = messages.ToArray();
                        item.passed = passesOK && item.supported && !item.hasErrors;
                    }
                    catch (Exception e) { item.exception = e.ToString(); item.passed = false; }
                    finally
                    {
                        if (material != null) UnityEngine.Object.DestroyImmediate(material);
                        allPassed &= item.passed;
                        Checkpoint(report, output);
                    }
                }
                report.success = allPassed && Interlocked.CompareExchange(ref compilerError, 0, 0) == 0;
                report.state = report.success ? "COMPLETED" : "FAILED";
                report.currentOperation = "Finished current-API pass checks";
                if (report.shaderOptimizationDisabled)
                    report.exception = "Editor shader optimization is disabled; optimized production compilation is NOT verified.";
            }
            catch (Exception e)
            {
                report.success = false; report.state = "FAILED"; report.exception = e.ToString();
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = previousAsync;
                if (subscribed) Application.logMessageReceivedThreaded -= CaptureLog;
                try { Checkpoint(report, output); }
                catch (Exception e) { report.success = false; Debug.LogError("ORBIS 无法写入编译报告：" + e.Message); }
            }
            if (report.success) Debug.Log("ORBIS 当前 API 的 10 个默认 Pass 编译检查通过；不代表跨平台或画面验收。报告：" + output);
            else Debug.LogError("ORBIS 编译检查未通过。报告：" + output + "；Editor.log：" + report.editorLogPath);
            return report.success;
        }
    }
}
