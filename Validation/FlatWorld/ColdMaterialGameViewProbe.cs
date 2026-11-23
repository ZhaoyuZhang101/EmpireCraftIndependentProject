// Run via Unity Pipeline eval_file in a playing, simulation-paused Earth scene. Use the
// sunlit Asian globe or a local view with a visible animal. Keep the camera fixed for the run.
// This reads the Game view itself. Camera.Render would change the target and compilation context.
var app = UnityEngine.Object.FindFirstObjectByType<Orbis.Flat.FlatWorldApp>();
if (!EditorApplication.isPlaying || EditorApplication.isPaused || !app || app.Busy || !app.Navigation)
    throw new InvalidOperationException("An active world and a running Game view are required.");
var nav = app.Navigation;
var camera = app.GetComponentInChildren<Camera>();
bool globe = nav.IsGlobe;
if (!globe && (!nav.IsFlat || nav.IsAtlas)) throw new InvalidOperationException("Choose the globe or a local animal.");
var actor = nav.GetComponent<Orbis.Flat.FlatWildlifePresenter>();
string objectName = globe ? "Local spherical terrain" : "Wildlife actor artwork";
var renderer = nav.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == objectName);
bool alreadyPending = renderer.sharedMaterial.IsKeywordEnabled("ORBIS_PREVIEW");
var material = globe ? nav.SurfaceMaterial : (Material)typeof(Orbis.Flat.FlatWildlifePresenter)
    .GetField("material", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(actor);
int pass = globe ? material.FindPass("SCROLL_SURFACE") : 0;
if (!EditorSettings.asyncShaderCompilation || (!alreadyPending && !ShaderUtil.IsPassCompiled(material, pass)) ||
    (!globe && actor.VisibleActors < 1)) throw new InvalidOperationException("Use a visible globe or animal with asynchronous compilation enabled.");

var viewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
var gameView = (EditorWindow)Resources.FindObjectsOfTypeAll(viewType).First();
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly;
System.Reflection.FieldInfo targetField = null;
for (var type = viewType; type != null && targetField == null; type = type.BaseType)
    targetField = type.GetField("m_TargetTexture", flags);
if (targetField == null) throw new InvalidOperationException("GameView render target is unavailable.");

var centre = camera.pixelRect.center;
if (!globe)
{
    var vertices = renderer.GetComponent<MeshFilter>().sharedMesh.vertices;
    centre = camera.WorldToScreenPoint(renderer.transform.TransformPoint((vertices[0] + vertices[3]) * .5f));
}
var output = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ValidationOutput/GlobeSurface");
System.IO.Directory.CreateDirectory(output);
string name = globe ? "globe" : "animal";
string path = System.IO.Path.Combine(output, name + "-cold-result.txt");
var pixels = new Texture2D(250, 160, TextureFormat.RGBA32, false);
void Sample(out int cyan, out int land, string save = null)
{
    var target = (RenderTexture)targetField.GetValue(gameView);
    int x = Mathf.Clamp(Mathf.RoundToInt(centre.x) - pixels.width / 2, 0, target.width - pixels.width);
    int y = Mathf.Clamp(Mathf.RoundToInt(target.height - centre.y) - pixels.height / 2, 0, target.height - pixels.height);
    var old = RenderTexture.active;
    try { RenderTexture.active = target; pixels.ReadPixels(new Rect(x, y, pixels.width, pixels.height), 0, 0); }
    finally { RenderTexture.active = old; }
    cyan = land = 0;
    foreach (var c in pixels.GetRawTextureData<Color32>())
    {
        if (c.r < 4 && c.g > 208 && Mathf.Abs(c.b - c.g) < 3) cyan++;
        if (c.r > .69f * c.b && c.g > 70) land++;
    }
    if (save != null) System.IO.File.WriteAllBytes(System.IO.Path.Combine(output, name + "-" + save + ".png"), pixels.EncodeToPNG());
}
Sample(out int initialCyan, out int initialLand, "reference");
if (initialCyan > 8 || (globe && initialLand < 2000)) throw new InvalidOperationException("Reference region is already empty or outside daylit land.");

// A fresh native program hash forces a cold draw without clearing shared shader caches.
// The extra uniform is zero, so it cannot change the warm reference image.
var original = material.shader;
Shader cold = null;
if (!alreadyPending)
{
    string asset = AssetDatabase.GetAssetPath(original), id = "OrbisCold" + Guid.NewGuid().ToString("N");
    string directory = System.IO.Path.GetDirectoryName(asset).Replace('\\', '/');
    string source = System.IO.File.ReadAllText(asset).Replace(original.name, "Hidden/" + id);
    source = System.Text.RegularExpressions.Regex.Replace(source, "#include \\\"([^\\\"]+)\\\"", m =>
        m.Groups[1].Value == "UnityCG.cginc" ? m.Value : "#include \"" + directory + "/" + m.Groups[1].Value + "\"");
    source = globe ? source.Replace("v2f vert(appdata v)", "float " + id + "; v2f vert(appdata v)")
                           .Replace("o.uv=geo;", "o.pos.x+=" + id + ";o.uv=geo;") :
                     source.Replace("float4 frag(v2f i)", "float " + id + "; float4 frag(v2f i)")
                           .Replace("max(0,colour),opacity", "max(0,colour)+" + id + ",opacity");
    cold = ShaderUtil.CreateShaderAsset(source, false);
    var errors = ShaderUtil.GetShaderMessages(cold).Where(m => m.severity.ToString() == "Error").ToArray();
    if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors.Select(m => m.message)));
    material.shader = cold;
}
bool oldBackground = Application.runInBackground;
Application.runInBackground = true;
int frame = -1, steps = 0, pending = 0, warm = 0, worstCyan = 0, minimumLand = int.MaxValue;
double start = EditorApplication.timeSinceStartup;
EditorApplication.CallbackFunction poll = null;
void Finish(string result)
{
    EditorApplication.update -= poll;
    if (cold && material) material.shader = original;
    Application.runInBackground = oldBackground;
    UnityEngine.Object.DestroyImmediate(pixels);
    if (cold) UnityEngine.Object.DestroyImmediate(cold);
    System.IO.File.WriteAllText(path, $"{result}\n{Application.unityVersion} {SystemInfo.graphicsDeviceName} {SystemInfo.graphicsDeviceType}\n" +
        $"referenceLand={initialLand} pendingFrames={pending} warmFrames={warm} maxCyan={worstCyan} minLand={minimumLand}\n");
}
poll = () =>
{
    try
    {
        if (!material || !EditorApplication.isPlaying || (EditorApplication.timeSinceStartup - start) > 900)
        { Finish("FAIL world changed or test timed out"); return; }
        gameView.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
        if (Time.frameCount == frame) return;
        frame = Time.frameCount;
        if (++steps < 3) return;
        bool detailed = renderer.sharedMaterial == material && ShaderUtil.IsPassCompiled(material, pass);
        bool preview = renderer.sharedMaterial.IsKeywordEnabled("ORBIS_PREVIEW");
        Sample(out int cyan, out int land, steps == 4 ? "first-draw" : detailed && warm == 3 ? "detail" : null);
        worstCyan = Math.Max(cyan, worstCyan); minimumLand = Math.Min(land, minimumLand);
        if (preview && !detailed) pending++;
        warm = detailed ? warm + 1 : 0;
        if (cyan > 8 || (globe && land < initialLand * .10)) { Finish("FAIL placeholder or missing surface"); return; }
        if (warm >= 5) Finish(pending > 0 ? "PASS" : "INCONCLUSIVE no cold preview frame sampled");
    }
    catch (Exception error) { Finish("FAIL " + error.Message); }
};
System.IO.File.WriteAllText(path, "RUNNING");
EditorApplication.update += poll;
gameView.Focus(); gameView.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
return new { name, path, frame = Time.frameCount };
