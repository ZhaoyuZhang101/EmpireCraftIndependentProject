using UnityEngine;

namespace Orbis
{
    // The atmosphere owns composition. This component supplies the optics for its current globe.
    [RequireComponent(typeof(Camera))]
    public sealed class OrbisCelestialLens : MonoBehaviour
    {
        public OrbisPlanetView view;
        public Orbis.Flat.FlatScrollNavigator navigation;
        [Range(0, 2)] public float strength = 1.15f;

        const int ApertureSize = 8;
        static readonly int Sun = Shader.PropertyToID("_LensSun");
        static readonly int DiskAxes = Shader.PropertyToID("_LensDiskAxes");
        static readonly int Transmission = Shader.PropertyToID("_LensTransmission");
        RenderTexture aperture;

        internal Vector4 Sample(Camera camera, RenderTexture source, Material volume)
        {
            float unfold = navigation ? navigation.Morph : 0;
            float light = strength * Mathf.Max(0, view.world.settings.heat)
                * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.06f, .55f, unfold)));
            if (light <= 0 || camera.orthographic) return Vector4.zero;

            // TransformVector includes the scroll globe's reflected Z axis.
            Vector3 sun = view.VisualSunLocal.normalized;
            Vector3 worldSun = view.transform.TransformVector(sun).normalized;
            float radius = Mathf.Clamp(view.atmosphereProfile.sunAngularRadiusDegrees, .1f, 1.2f) * Mathf.Deg2Rad;
            if (Vector3.Dot(camera.transform.forward, worldSun) <= Mathf.Sin(radius)) return Vector4.zero;
            Vector3 eye = view.transform.InverseTransformPoint(camera.transform.position);
            if (Vector3.Dot(eye, sun) < 0 && Vector3.Cross(eye, sun).magnitude + eye.magnitude * Mathf.Sin(radius) < 1)
                return Vector4.zero;

            Vector3 centre = camera.transform.position + worldSun;
            Vector2 uv = camera.WorldToViewportPoint(centre);
            Vector3 right = Vector3.Cross(camera.transform.up, worldSun).normalized;
            Vector3 up = Vector3.Cross(worldSun, right);
            float tangent = Mathf.Tan(radius);
            Vector2 dx = (Vector2)camera.WorldToViewportPoint(centre + right * tangent) - uv;
            Vector2 dy = (Vector2)camera.WorldToViewportPoint(centre + up * tangent) - uv;
            float rx = Mathf.Abs(dx.x) + Mathf.Abs(dy.x), ry = Mathf.Abs(dx.y) + Mathf.Abs(dy.y);
            if (uv.x < -rx || uv.x > 1 + rx || uv.y < -ry || uv.y > 1 + ry) return Vector4.zero;
            int pass = volume.FindPass("SOLAR_APERTURE");
            if (pass < 0) return Vector4.zero;

            if (!aperture)
            {
                aperture = new RenderTexture(ApertureSize, ApertureSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    name = "ORBIS solar disc transmission", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
                };
            }
            if (!aperture.IsCreated() && !aperture.Create()) return Vector4.zero;
            Vector4 projected = new Vector4(uv.x, uv.y, light, source.width / (float)source.height);
            volume.SetVector(Sun, projected);
            volume.SetVector(DiskAxes, new Vector4(dx.x, dx.y, dy.x, dy.y));
            // This tiny pass uses the same depth and HP integration in both atmosphere quality paths.
            bool previousSRGB = GL.sRGBWrite;
            var previousTarget = RenderTexture.active;
            try
            {
                GL.sRGBWrite = false;
                Graphics.Blit(source, aperture, volume, pass);
            }
            finally { RenderTexture.active = previousTarget; GL.sRGBWrite = previousSRGB; }
            return projected;
        }

        internal static void Bind(Material material, OrbisCelestialLens lens, Vector4 projected)
        {
            material.SetVector(Sun, projected);
            material.SetTexture(Transmission, lens && projected.z > 0 ? (Texture)lens.aperture : Texture2D.blackTexture);
        }

        void OnDisable()
        {
            if (aperture) Destroy(aperture);
            aperture = null;
        }
    }
}
