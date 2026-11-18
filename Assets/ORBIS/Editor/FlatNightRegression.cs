using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
    public static class FlatNightRegression
    {
        [MenuItem("Tools/ORBIS/Local Planet/Validate flat moonlight")]
        public static void Run()
        {
            var probe = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/FlatNightProbe.shader"));
            var display = new Material(Resources.Load<Shader>("ORBIS/Display"));
            var previous = RenderTexture.active;
            bool previousWrite = GL.sRGBWrite;
            var scene = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var output = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var image = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true);
            var lines = new List<string>();
            var failures = new List<string>();
            var night = new OrbisNightLightingSettings();
            var settings = new PlanetSettings { nightLighting = night };
            try
            {
                GL.sRGBWrite = false;
                display.SetFloat("_Exposure", 1);
                probe.SetFloat("_FlatDayNight", 1);
                probe.SetFloat("_FlatCloudShadows", 1);
                probe.SetFloat("_SkyReady", 1);

                float Sample(string label, Vector3 swatch, float unfold, float phase = 0, bool day = false,
                    bool cloudy = false, bool moonBelow = false)
                {
                    var sun = day ? Vector3.forward : Vector3.back;
                    night.phaseAtDay100 = phase;
                    // At full phase, reversing the ephemeris sun puts the Moon below the test horizon.
                    // The surface sun is set independently so the patch remains on the night side.
                    OrbisNightLighting.Bind(probe, settings, moonBelow ? -sun : sun, 100);
                    probe.SetVector("_ProbeAlbedo", swatch);
                    probe.SetVector("_FlatSun", sun);
                    probe.SetFloat("_FlatNightAdaptation", unfold);
                    probe.SetTexture("_SkyTex", cloudy ? Texture2D.whiteTexture : Texture2D.blackTexture);
                    Graphics.Blit(Texture2D.blackTexture, scene, probe);
                    Graphics.Blit(scene, output, display);
                    RenderTexture.active = output;
                    image.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
                    var pixel = image.GetPixel(4, 4);
                    if (QualitySettings.activeColorSpace == ColorSpace.Linear) pixel = pixel.gamma;
                    float luma = 255 * (.2126f * pixel.r + .7152f * pixel.g + .0722f * pixel.b);
                    lines.Add(label + " = " + luma.ToString("F2", CultureInfo.InvariantCulture));
                    return luma;
                }

                void Check(string label, bool passed)
                {
                    lines.Add((passed ? "PASS " : "FAIL ") + label);
                    if (!passed) failures.Add(label);
                }

                lines.Add("Display luma (0-255), " + QualitySettings.activeColorSpace + ", " + SystemInfo.graphicsDeviceType);
                var grass = new Vector3(.43f, .48f, .32f);
                var forest = new Vector3(.19f, .34f, .22f);
                var deepWater = new Vector3(.10f, .22f, .31f);
                float land = Sample("Orbital grass, new moon", grass, 0);
                float trees = Sample("Orbital forest, new moon", forest, 0);
                float ocean = Sample("Orbital deep-water swatch, new moon", deepWater, 0);
                float full = Sample("Orbital grass, full moon", grass, 0, .5f);
                float halfway = Sample("Grass halfway through unfolding, new moon", grass, .5f);
                float local = Sample("Local grass, new moon", grass, 1);
                float localFull = Sample("Local grass, full moon", grass, 1, .5f);
                float below = Sample("Local grass, full moon below horizon", grass, 1, .5f, moonBelow: true);
                float overcast = Sample("Local grass, overcast full moon", grass, 1, .5f, cloudy: true);
                float daylight = Sample("Orbital grass, noon", grass, 0, day: true);
                float localDay = Sample("Local grass, noon", grass, 1, day: true);
                night.fullMoonStrength = night.skyStrength = night.airglowStrength = 0;
                float unlit = Sample("Night with all illumination off", grass, 0, .5f);

                Check("Moonless grass, forest and sea retain distinct tones", land >= 30 && trees >= 14
                    && ocean > 4 && land - ocean >= 16 && trees - ocean >= 5);
                Check("Moonlight follows phase and local horizon", full > land * 1.2f
                    && localFull > local * 1.2f && Mathf.Abs(below - local) < .8f);
                Check("Overcast dims full moon while preserving local readout", overcast < localFull && overcast > 20);
                Check("Night adaptation is gradual through unfolding", land < halfway && halfway < local && local < land * 1.6f);
                Check("Noon stays consistent and distinct from night", land < daylight * .48f && Mathf.Abs(localDay - daylight) < .15f);
                Check("Zero incident light stays black", unlit < .5f);
                Directory.CreateDirectory("Documentation/LocalPlanetUpgrade");
                File.WriteAllLines("Documentation/LocalPlanetUpgrade/night-regression.txt", lines);
                if (failures.Count > 0) throw new InvalidOperationException(string.Join(", ", failures));
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousWrite;
                RenderTexture.ReleaseTemporary(scene);
                RenderTexture.ReleaseTemporary(output);
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(probe);
                UnityEngine.Object.DestroyImmediate(display);
            }
        }
    }
}
