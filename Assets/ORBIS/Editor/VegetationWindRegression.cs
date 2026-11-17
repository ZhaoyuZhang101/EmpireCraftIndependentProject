using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
    public static class VegetationWindRegression
    {
        const int Resolution = 128;

        [MenuItem("Tools/ORBIS/Local Planet/Validate overhead vegetation wind")]
        public static void Run()
        {
            var lines = new List<string>();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/VegetationArtProbe.shader");
            var material = new Material(shader);
            var wind = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var image = new Texture2D(Resolution, Resolution, TextureFormat.RGBAFloat, false, true);
            var target = RenderTexture.GetTemporary(Resolution, Resolution, 0,
                RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var previousTarget = RenderTexture.active;
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                ShaderUtil.CompilePass(material, 0, true);
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    if (message.severity.ToString() == "Error") throw new InvalidOperationException(message.message);
                material.SetVector("_MapSize", new Vector4(16, 8, 1f / 16, 1f / 8));
                material.SetVector("_ProbeRoot", new Vector4(7, 3, 0, 0));
                material.SetVector("_ProbeLight", new Vector4(-.55f, .4f, .75f, 0));
                material.SetFloat("_ProbeSeed", .43f);
                material.SetFloat("_ProbeFoliage", 1);
                wind.SetPixel(0, 0, new Color(1, .35f, -.8f, .5f));
                wind.Apply();
                material.SetTexture("_VegetationWindTex", wind);
                material.SetFloat("_VegetationWindReady", 1);
                material.SetFloat("_VegetationWindBlend", .2f);
                material.SetFloat("_ProbeMode", 6);
                var basis = Sample(material, target, image)[0];
                Check(lines, basis.b > .94f && basis.a > .94f,
                    "all species use an overhead view of at least 70 degrees");

                for (int species = 0; species < 8; species++)
                {
                    string label = "species=" + species;
                    material.SetFloat("_ProbeSpecies", species);
                    material.SetFloat("_ProbeMode", 3);
                    material.SetFloat("_SkyClock", 0);
                    var first = Sample(material, target, image);
                    material.SetFloat("_SkyClock", 1.7f);
                    var second = Sample(material, target, image);
                    Check(lines, Difference(first, second) > .0001,
                        label + " crown sways over time");
                    Check(lines, Difference(second, Sample(material, target, image)) == 0,
                        label + " paused visual clock freezes the sprite");
                    material.SetVector("_ProbeRoot", new Vector4(23, 3, 0, 0));
                    Check(lines, Difference(second, Sample(material, target, image)) < .00001,
                        label + " wind phase agrees across the date line");
                    material.SetVector("_ProbeRoot", new Vector4(7, 3, 0, 0));
                    Check(lines, ClearBorder(second), label + " moving crown stays inside its draw bounds");

                    material.SetFloat("_ProbeMode", 4);
                    material.SetFloat("_SkyClock", 0);
                    var shadow = Sample(material, target, image);
                    material.SetFloat("_SkyClock", 1.7f);
                    Check(lines, Difference(shadow, Sample(material, target, image)) > .0001,
                        label + " shader shadow follows the moving crown");

                    material.SetFloat("_ProbeMode", 8);
                    material.SetVector("_ProbeLight", new Vector4(0, -.7f * Mathf.Tan(18 * Mathf.Deg2Rad) / .4f, .7f, 0));
                    int mismatches = 0, crownPixels = 0;
                    foreach (var pixel in Sample(material, target, image))
                    {
                        if (pixel.r > .5f) crownPixels++;
                        if ((pixel.r > .5f) != (pixel.g > (species == 3 ? .5f : .762f))) mismatches++;
                    }
                    Check(lines, crownPixels > 100 && mismatches < 12,
                        label + " wind-deformed shadow and sprite share the same geometry; mismatch=" + mismatches);
                    material.SetVector("_ProbeLight", new Vector4(-.55f, .4f, .75f, 0));

                    material.SetFloat("_ProbeMode", 5);
                    material.SetFloat("_SkyClock", 0);
                    var trunk = Sample(material, target, image);
                    material.SetFloat("_SkyClock", 1.7f);
                    var movingTrunk = Sample(material, target, image);
                    double rootChange = 0;
                    for (int y = 0; y < Resolution; y++)
                    {
                        float pixelY = Mathf.Floor(((y + .5f) / Resolution - .20f) * 48);
                        if (pixelY > 0) continue;
                        for (int x = 0; x < Resolution; x++)
                            rootChange += Math.Abs(trunk[y * Resolution + x].a - movingTrunk[y * Resolution + x].a);
                    }
                    Check(lines, rootChange == 0, label + " trunk base remains pinned to the ground");

                    material.SetFloat("_ProbeMode", 3);
                    material.SetFloat("_VegetationWindReady", 0);
                    material.SetFloat("_SkyClock", 0);
                    var calm = Sample(material, target, image);
                    material.SetFloat("_SkyClock", 8);
                    Check(lines, Difference(calm, Sample(material, target, image)) == 0,
                        label + " legacy materials without a wind field remain stable");
                    material.SetFloat("_VegetationWindReady", 1);
                }
                material.SetFloat("_ProbeSpecies", 0);
                material.SetFloat("_SkyClock", 2.4f);
                material.SetFloat("_VegetationWindBlend", 0);
                var eastward = Sample(material, target, image);
                material.SetFloat("_VegetationWindBlend", 1);
                var westward = Sample(material, target, image);
                Check(lines, CentroidX(eastward) > CentroidX(westward) + .5,
                    "prevailing monthly wind controls crown bend direction");
                wind.SetPixel(0, 0, new Color(4, -4, 4, -4));
                wind.Apply();
                material.SetFloat("_ProbeMode", 7);
                for (int species = 0; species < 8; species++)
                {
                    material.SetFloat("_ProbeSpecies", species);
                    foreach (float seed in new[] { 0f, .249f, .5f, .749f, .999f })
                    {
                        material.SetFloat("_ProbeSeed", seed);
                        foreach (var pixel in Sample(material, target, image))
                            if (pixel.a > .5f) throw new InvalidOperationException("Wind-deformed species=" + species + " exceeds production draw bounds");
                    }
                    Check(lines, true, "species=" + species + " retains complete sprites in strong gusts across five shapes");
                }
                ValidateRuntimeWind(lines);
                if (lines.Exists(line => line.StartsWith("FAIL")))
                    throw new InvalidOperationException(string.Join("\n", lines.FindAll(line => line.StartsWith("FAIL"))));
            }
            finally
            {
                Directory.CreateDirectory("ValidationOutput/overhead-vegetation");
                File.WriteAllLines("ValidationOutput/overhead-vegetation/wind-regression.txt", lines);
                RenderTexture.active = previousTarget;
                ShaderUtil.allowAsyncCompilation = previousAsync;
                RenderTexture.ReleaseTemporary(target);
                foreach (var resource in new UnityEngine.Object[] { material, wind, image })
                    UnityEngine.Object.DestroyImmediate(resource);
            }
        }

        static void ValidateRuntimeWind(List<string> lines)
        {
            if (!Application.isPlaying) return;
            var map = UnityEngine.Object.FindFirstObjectByType<Flat.FlatMapRenderer>();
            if (!map || map.World == null) return;
            var world = map.World;
            var sky = map.Sky;
            double previousDay = world.Day;
            var probe = new Material(Resources.Load<Shader>("ORBIS/FlatWorld/FlatMap"));
            var pendingStrip = typeof(Flat.FlatSkyRenderer).GetField("pendingStrip",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            try
            {
                double clock = sky.Clock;
                sky.Tick(.2f, false, false, false, 512, world.Day);
                Check(lines, sky.Clock == clock, "clouds disabled and paused keeps vegetation time frozen");
                sky.Tick(.2f, false, true, false, 512, world.Day);
                sky.Bind(probe, Vector3.forward, false, false, false, false, false, false, false, 0);
                Check(lines, probe.GetFloat("_VegetationWindReady") > .5f && Math.Abs(sky.Clock - clock - .2) < .0001,
                    "clouds disabled still supplies vegetation wind and advancing visual time");
                for (int i = 0; i < 4 && (int)pendingStrip.GetValue(sky) >= 0; i++)
                    sky.Tick(0, true, false, false, 512, world.Day);
                sky.Tick(.6f, true, true, false, 512, world.Day);
                Check(lines, (int)pendingStrip.GetValue(sky) == 1, "cloud cache starts a four-strip weather snapshot");
                var cloudWind = sky.DynamicWind.GetPixels();
                world.Day = previousDay + 30;
                sky.Tick(0, true, false, false, 512, world.Day);
                sky.Bind(probe, Vector3.forward, false, false, false, false, false, false, false, 0);
                var texture = (Texture2D)probe.GetTexture("_VegetationWindTex");
                float east = world.Moisture == null ? .4f : world.Moisture.Sample(world.Moisture.East, 64.5f / 128, 32.5f / 64, world.Month);
                Check(lines, Math.Abs(texture.GetPixel(64, 32).r - east) < .001,
                    "vegetation receives the new monthly wind during a pending cloud snapshot");
                Check(lines, Difference(cloudWind, sky.DynamicWind.GetPixels()) == 0,
                    "monthly vegetation wind updates preserve cloud drift across pending render strips");
            }
            finally
            {
                world.Day = previousDay;
                for (int i = 0; i < 5; i++) sky.Tick(0, true, false, false, 512, world.Day);
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        static Color[] Sample(Material material, RenderTexture target, Texture2D image)
        {
            Graphics.Blit(Texture2D.whiteTexture, target, material);
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
            image.Apply();
            return image.GetPixels();
        }

        static double Difference(Color[] first, Color[] second)
        {
            double difference = 0;
            for (int i = 0; i < first.Length; i++)
                difference += Math.Abs(first[i].r - second[i].r) + Math.Abs(first[i].g - second[i].g)
                    + Math.Abs(first[i].b - second[i].b) + Math.Abs(first[i].a - second[i].a);
            return difference / (first.Length * 4);
        }

        static bool ClearBorder(Color[] pixels)
        {
            for (int i = 0; i < Resolution; i++)
                if (pixels[i].a > 0 || pixels[(Resolution - 1) * Resolution + i].a > 0
                    || pixels[i * Resolution].a > 0 || pixels[i * Resolution + Resolution - 1].a > 0)
                    return false;
            return true;
        }

        static double CentroidX(Color[] pixels)
        {
            double weight = 0, position = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                weight += pixels[i].a;
                position += (i % Resolution) * pixels[i].a;
            }
            return position / Math.Max(1, weight);
        }

        static void Check(List<string> lines, bool passed, string description)
        {
            lines.Add((passed ? "PASS " : "FAIL ") + description);
        }
    }
}
