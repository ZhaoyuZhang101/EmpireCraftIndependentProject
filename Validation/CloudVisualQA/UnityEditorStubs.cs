// Compile-check-only Unity & ORBIS presentation API stubs for EditorCompileCheck.csproj.
// Not compiled by Unity (outside Assets/). Mirrors exact public signatures verified in Assets/ORBIS/.
using System;
using Orbis.Planet;

namespace UnityEngine
{
    public enum RenderTextureFormat { ARGB32, ARGBFloat }
    public enum RenderTextureReadWrite { Default, Linear }
    public enum TextureFormat { RGBA32, RGBAFloat, RGB24 }
    public enum GraphicsDeviceType { Direct3D11 }

    public class Object
    {
        public static T FindFirstObjectByType<T>() where T : Object => default;
        public static void DestroyImmediate(Object obj) { }
        public static implicit operator bool(Object exists) => exists != null;
    }

    public class GameObject : Object
    {
        public T AddComponent<T>() where T : Component => default;
    }

    public class Component : Object
    {
        public GameObject gameObject => null;
        public Transform transform => null;
        public T GetComponent<T>() => default;
        public T[] GetComponents<T>() => Array.Empty<T>();
        public T GetComponentInChildren<T>() => default;
    }

    public class Behaviour : Component { public bool enabled; }
    public class MonoBehaviour : Behaviour { }
    public class Transform : Component { public void SetPositionAndRotation(Vector3 pos, Quaternion rot) { } }
    public class TextAsset : Object { public byte[] bytes => Array.Empty<byte>(); }
    public class Texture : Object { public int width; public int height; }
    public struct RenderBuffer { }
    public class RenderTexture : Texture
    {
        public static RenderTexture active;
        public RenderBuffer colorBuffer => default;
        public RenderBuffer depthBuffer => default;
        public static RenderTexture GetTemporary(int w, int h, int d, RenderTextureFormat f, RenderTextureReadWrite rw = RenderTextureReadWrite.Default) => null;
        public static void ReleaseTemporary(RenderTexture rt) { }
    }
    public class Texture2D : Texture
    {
        public Texture2D(int w, int h, TextureFormat f, bool mip, bool linear = false) { }
        public void ReadPixels(Rect r, int dx, int dy) { }
        public void Apply(bool updateMipmaps = true, bool makeNoLongerReadable = false) { }
        public Color[] GetPixels() => Array.Empty<Color>();
        public Color32[] GetPixels32() => Array.Empty<Color32>();
    }
    public class Material : Object
    {
        public Texture GetTexture(string name) => null;
        public float GetFloat(string name) => 0f;
        public void SetFloat(string name, float v) { }
        public void SetTexture(string name, Texture t) { }
        public void SetVector(string name, Vector4 v) { }
        public bool SetPass(int pass) => true;
    }
    public class Camera : Behaviour
    {
        public RenderTexture targetTexture;
        public Rect pixelRect;
        public bool orthographic;
        public float fieldOfView, nearClipPlane, farClipPlane;
        public void ResetProjectionMatrix() { }
        public void Render() { }
    }
    public struct Vector4 { public float x, y, z, w; public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; } }
    public struct Rect { public Rect(float x, float y, float w, float h) { } }
    public struct Vector2 { public float x, y; }
    public struct Color { public float r, g, b, a; }
    public struct Color32 { public byte r, g, b, a; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up => new Vector3(0, 1, 0);
        public float sqrMagnitude => x * x + y * y + z * z;
        public Vector3 normalized => this;
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator *(Vector3 a, float s) => new Vector3(a.x * s, a.y * s, a.z * s);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => default;
        public static Vector3 ProjectOnPlane(Vector3 v, Vector3 planeNormal) => v;
    }
    public struct Quaternion
    {
        public static Quaternion AngleAxis(float angle, Vector3 axis) => default;
        public static Quaternion LookRotation(Vector3 forward, Vector3 up) => default;
        public static Vector3 operator *(Quaternion q, Vector3 v) => v;
    }
    public static class Mathf
    {
        public const float Deg2Rad = 0.017453292f;
        public const float Rad2Deg = 57.29578f;
        public static float Tan(float f) => (float)Math.Tan(f);
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Asin(float f) => (float)Math.Asin(f);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Clamp(float v, float min, float max) => Math.Max(min, Math.Min(max, v));
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp(t, 0f, 1f);
    }
    public static class GL
    {
        public const int QUADS = 7;
        public static bool sRGBWrite;
        public static void PushMatrix() { }
        public static void PopMatrix() { }
        public static void LoadOrtho() { }
        public static void Begin(int mode) { }
        public static void End() { }
        public static void TexCoord2(float x, float y) { }
        public static void Vertex3(float x, float y, float z) { }
    }
    public static class Graphics
    {
        public static void Blit(Texture src, RenderTexture dst) { }
        public static void SetRenderTarget(RenderBuffer[] colorBuffers, RenderBuffer depthBuffer) { }
    }
    public static class Resources { public static T Load<T>(string path) where T : Object => default; }
    public static class Application
    {
        public static string unityVersion => "6000.0.55f1";
        public static string dataPath => "";
        public static bool isBatchMode => false;
    }
    public static class SystemInfo
    {
        public static GraphicsDeviceType graphicsDeviceType => GraphicsDeviceType.Direct3D11;
        public static string graphicsDeviceName => "StubGPU";
    }
    public static class Debug
    {
        public static void Log(object msg) { }
        public static void LogException(Exception ex) { }
    }
}

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class InitializeOnLoadAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class MenuItemAttribute : Attribute { public MenuItemAttribute(string itemName) { } }
    public static class EditorApplication
    {
        public static event Action update;
        public static bool isPlaying;
        public static double timeSinceStartup;
        public static void EnterPlaymode() { }
        public static void ExitPlaymode() { }
        public static void Exit(int code) { }
        internal static void SuppressWarning() { update?.Invoke(); }
    }
    public static class SessionState
    {
        public static bool GetBool(string k, bool d) => d;
        public static void SetBool(string k, bool v) { }
        public static string GetString(string k, string d) => d;
        public static void SetString(string k, string v) { }
        public static int GetInt(string k, int d) => d;
        public static void SetInt(string k, int v) { }
        public static float GetFloat(string k, float d) => d;
        public static void SetFloat(string k, float v) { }
    }
    public static class ShaderUtil { public static bool allowAsyncCompilation; }
}

namespace UnityEditor.SceneManagement
{
    public static class EditorSceneManager { public static void OpenScene(string path) { } }
}

namespace Orbis
{
    public sealed class OrbisSettings
    {
        public float exposure = 1f, cloudCover = 1f, cloudDensity = 1f;
        public int cloudQuality = 1;
        public bool clouds, cloudShadows, godRays, atmosphere, night, multipleScattering;
    }
    public static class OrbisWeatherField { public const float HeightScale = 1f / 6371000f; }
    public sealed class OrbisWorld { public OrbisSettings settings = new OrbisSettings(); }
    public sealed class OrbisPlanetView : UnityEngine.MonoBehaviour { public UnityEngine.Material CloudMaterial => null; }
    public sealed class OrbisAtmosphereRenderer : UnityEngine.MonoBehaviour
    {
        public OrbisPlanetView view;
        public bool fullResolutionReference, referenceCloudIntegration;
        public int cloudDebug;
    }
    public sealed class OrbisCelestialLens : UnityEngine.MonoBehaviour { public float strength; }
}

namespace Orbis.Flat
{
    public sealed class FlatSkyRenderer : UnityEngine.MonoBehaviour
    {
        public UnityEngine.RenderTexture Texture { get; private set; }
        public UnityEngine.RenderTexture ColumnTexture { get; private set; }
        public double Clock { get; private set; }
        public double RenderClock => 0;
        public bool Ready => true;
        public long? RegionalClimateStep { get; private set; }
        public bool RegionalPrecipitationReady => false;
        public UnityEngine.Texture2D RegionalPrecipitationTexture => null;
        public UnityEngine.Vector4 RegionalPrecipitationPoles => default;
        public bool TrySampleRegionalPrecipitation(double u, double v, out RegionalPrecipitationDiagnostic diagnostic) { diagnostic = default; return false; }
        public void Tick(float delta, bool needed, bool animate, bool autoSun, int width, double calendarDay, UnityEngine.Vector2 focus = default, bool local = false) { }
        public UnityEngine.Vector3 Sun(float manualAngle, double calendarDay = double.NaN, bool automatic = false) => default;
        public void Bind(UnityEngine.Material target, UnityEngine.Vector3 sun, bool clouds, bool shadows, bool atmosphere, bool dayNight, bool rain, bool lightning, bool nightLights, float opacity) { }
    }
    public sealed class FlatMapRenderer : UnityEngine.MonoBehaviour
    {
        public FlatSkyRenderer Sky { get; private set; }
        public UnityEngine.Material Material { get; private set; }
        public bool PreparationPending => false;
        public void SetLayer(MapLayer layer) { }
        public void Configure(bool grid, float pixelsPerTile, float flowClock = 0f) { }
        public void SetVegetationDay(double day, UnityEngine.Material target = null) { }
        public void Flush(bool force = false) { }
    }
    public sealed class FlatScrollNavigator : UnityEngine.MonoBehaviour
    {
        public float DisplaySpin => 0;
        public float PixelsPerTile => 1;
        public void Home(double day) { }
        public void Recenter(PlanetLocation loc, double day) { }
        public void SetViewport(UnityEngine.Rect r) { }
        public void Tick(float dt, double day) { }
        public void SyncMaterials() { }
    }
    public sealed class FlatGlobePresenter : UnityEngine.MonoBehaviour
    {
        public OrbisPlanetView View { get; private set; }
        public OrbisWorld Snapshot { get; private set; }
        public void TickScroll(float dt, double visualTime, double calendarDay, float spin, float unfold,
            bool clouds, bool atmosphere, bool night, bool shadows, UnityEngine.Vector3 sun, bool natural) { }
    }
    public sealed class FlatWorldApp : UnityEngine.MonoBehaviour
    {
        public WorldData World { get; private set; }
        public bool Busy => false;
        public FlatScrollNavigator Navigation => null;
    }
}

namespace Orbis.Editor
{
    public static class ClimateCloudRegression { public static void Run() { } }
    public static class HPCloudHeightRegression { public static void Run() { } }
    public static class WaterCloudRegression { public static void Run() { } }
    public static class LightningRegression { public static void Run() { } }
}
