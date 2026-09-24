using System;
using UnityEngine;

namespace Orbis
{
    /// <summary>
    /// V4.2 Repair IPCFix1 extension. Values are scene-linear artistic units in the
    /// existing radius-1 / atmosphere-1.20 model, NOT calibrated lux or an ephemeris.
    /// Stored in PlanetSettings; old saves receive defaults, explicit zero stays zero.
    /// </summary>
    [Serializable]
    public sealed class OrbisNightLightingSettings
    {
        public bool enabled = true;
        [Tooltip("微弱夜间天光。默认保留地貌轮廓；不是给最终像素强行加灰。")]
        [Range(0f, 3f)] public float skyStrength = 1f;
        [Tooltip("高空气辉及其很弱的向下照明；不依赖太阳直射可见性。")]
        [Range(0f, 3f)] public float airglowStrength = 1f;
        [Tooltip("满月照明，场景线性艺术单位，不是 lux。新月/地平线下自动衰减。")]
        [Range(0f, .4f)] public float fullMoonStrength = .12f;
        [Tooltip("月光与天光的天气柱遮挡近似，不增加云体积追踪。")]
        [Range(0f, 2f)] public float cloudAttenuation = 1f;
        [Tooltip("游戏历日中的月相周期；不是现实日期天文历表。")]
        [Range(2f, 120f)] public float lunarCycleDays = 29.53f;
        [Tooltip("游戏第100日的月相：0新月，0.5满月。")]
        [Range(0f, 1f)] public float phaseAtDay100 = .5f;
        [Range(0f, 30f)] public float orbitalInclinationDegrees = 5.1f;

        internal static float Safe(float x, float fallback, float min, float max)
        {
            return float.IsNaN(x) || float.IsInfinity(x) ? fallback : Mathf.Clamp(x, min, max);
        }
        public void Sanitize()
        {
            skyStrength = Safe(skyStrength, 1, 0, 3);
            airglowStrength = Safe(airglowStrength, 1, 0, 3);
            fullMoonStrength = Safe(fullMoonStrength, .12f, 0, .4f);
            cloudAttenuation = Safe(cloudAttenuation, 1, 0, 2);
            lunarCycleDays = Safe(lunarCycleDays, 29.53f, 2, 120);
            phaseAtDay100 = Safe(phaseAtDay100, .5f, 0, 1);
            orbitalInclinationDegrees = Safe(orbitalInclinationDegrees, 5.1f, 0, 30);
        }
    }

    public static class OrbisNightLighting
    {
        static readonly int ParamsID = Shader.PropertyToID("_OrbisNightParams");
        static readonly int MoonID = Shader.PropertyToID("_OrbisMoon");
        static readonly int MoonColorID = Shader.PropertyToID("_OrbisMoonColor");
        static readonly int SkyID = Shader.PropertyToID("_OrbisNightSkyColor");
        static readonly int GlowID = Shader.PropertyToID("_OrbisAirglowColor");
        static readonly int ShellID = Shader.PropertyToID("_OrbisAirglowShell");

        public static OrbisNightLightingSettings Ensure(PlanetSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (settings.nightLighting == null) settings.nightLighting = new OrbisNightLightingSettings();
            settings.nightLighting.Sanitize();
            return settings.nightLighting;
        }

        // Both directions are sample -> light in PLANET-LOCAL coordinates.
        // Stable double phase and the same interpolated calendar as VisualSun avoid
        // quantized moon movement; no Time.time, no extra Update and no worker access.
        public static Vector3 EvaluateMoon(Vector3 sun, double calendarDay,
            OrbisNightLightingSettings settings, out float illuminatedFraction, out float brightness)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.Sanitize();
            if (float.IsNaN(sun.sqrMagnitude) || float.IsInfinity(sun.sqrMagnitude) || sun.sqrMagnitude < 1e-10f)
                sun = Vector3.forward;
            sun.Normalize();
            if (double.IsNaN(calendarDay) || double.IsInfinity(calendarDay)) calendarDay = 100;
            double turns = (calendarDay - 100.0) / settings.lunarCycleDays + settings.phaseAtDay100;
            turns -= Math.Floor(turns);
            double theta = turns * Math.PI * 2.0;
            Vector3 east = Vector3.Cross(Vector3.up, sun);
            if (east.sqrMagnitude < 1e-8f) east = Vector3.Cross(Vector3.right, sun);
            east.Normalize();
            Vector3 north = Vector3.Cross(sun, east).normalized;
            float inc = settings.orbitalInclinationDegrees * Mathf.Deg2Rad;
            Vector3 tangent = east * Mathf.Cos(inc) + north * Mathf.Sin(inc);
            Vector3 moon = (sun * (float)Math.Cos(theta) + tangent * (float)Math.Sin(theta)).normalized;
            float phaseCos = Mathf.Clamp(-Vector3.Dot(sun, moon), -1, 1);
            illuminatedFraction = Mathf.Clamp01(.5f * (1 + phaseCos));
            // Lambert sphere phase law; an illustrative phase response, not the
            // lunar opposition surge or a photometrically fitted Moon model.
            float alpha = Mathf.Acos(phaseCos);
            brightness = Mathf.Clamp01((Mathf.Sin(alpha) + (Mathf.PI - alpha) * phaseCos) / Mathf.PI);
            return moon;
        }

        public static void Bind(Material material, PlanetSettings planet, Vector3 sun, double day)
        {
            if (!material) return;
            OrbisNightLightingSettings settings = Ensure(planet);
            float fraction, brightness;
            Vector3 moon = EvaluateMoon(sun, day, settings, out fraction, out brightness);
            material.SetVector(ParamsID, new Vector4(settings.enabled ? 1 : 0,
                settings.skyStrength, settings.airglowStrength, settings.cloudAttenuation));
            material.SetVector(MoonID, new Vector4(moon.x, moon.y, moon.z, settings.fullMoonStrength * brightness));
            // Explicit LINEAR values via SetVector: never double-convert SetColor.
            material.SetVector(MoonColorID, new Vector4(.90f, .94f, 1f, fraction));
            material.SetVector(SkyID, new Vector4(.040f, .055f, .080f, 0));
            // Airglow must remain a SUBTLE night-side floor. Real 557.7 nm airglow can be green,
            // but at this scene scale a saturated green source limb-brightens into a neon ring.
            // Use a dim desaturated blue-grey integrated source; night sky/moonlight carry most visibility.
            material.SetVector(GlowID, new Vector4(.014f, .020f, .028f, 0));
            // Broad enough for existing low-quality midpoint budgets. This is an
            // art-scaled high-altitude layer, NOT a claim of real 90-km geometry.
            material.SetVector(ShellID, new Vector4(1.165f, .016f, 0, 0));
        }
    }

    public sealed partial class OrbisPlanetView
    {
        [Header("V4.2 Repair IPC — 夜侧光照（随世界存档）")]
        public OrbisNightLightingSettings nightLighting;
        Vector3 nightVisualSun = Vector3.forward;
        double nightVisualDay = 100;
        void SyncNightLighting(Vector3 sun, double day)
        {
            nightVisualSun = sun;
            nightVisualDay = day >= 0 ? day : world.day;
            nightLighting = OrbisNightLighting.Ensure(world.settings);
        }
        void BindNightLighting(Material material)
        {
            nightLighting = OrbisNightLighting.Ensure(world.settings);
            OrbisNightLighting.Bind(material, world.settings, nightVisualSun, nightVisualDay);
        }
    }
}
