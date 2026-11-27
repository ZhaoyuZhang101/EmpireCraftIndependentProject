using UnityEngine;

namespace Orbis
{
    /// <summary>Space artwork is camera background only; the ORBIS optical pass still owns daylight.</summary>
    public static class OrbisCelestialBackdrop
    {
        static Material skybox;
        static bool loaded;

        public static void Apply(Camera camera, bool globe)
        {
            if (!camera) return;
            if (!loaded)
            {
                loaded = true;
                Material source = Resources.Load<Material>("ORBIS/Celestial/Skybox_Space");
                Shader wideShader = Resources.Load<Shader>("ORBIS/Celestial/OrbisSpaceSkybox");
                if (!source) Debug.LogWarning("ORBIS celestial skybox is missing; using the existing clear color.");
                else if (wideShader && wideShader.isSupported)
                {
                    skybox = new Material(wideShader) { name = "ORBIS Telephoto Space Skybox", hideFlags = HideFlags.HideAndDontSave };
                    skybox.CopyPropertiesFromMaterial(source);
                }
                else skybox = source;
            }
            if (globe && skybox)
            {
                RenderSettings.skybox = skybox;
                camera.clearFlags = CameraClearFlags.Skybox;
            }
            else camera.clearFlags = CameraClearFlags.SolidColor;
        }
    }
}
