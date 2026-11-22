// Copy to Assets/ORBIS/Editor in a disposable Unity project. See README.md for the batch invocation.
using System;
using System.IO;
using Orbis.Flat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

[InitializeOnLoad]
public static class EarthClimateMenuRegression
{
    const string Key = "ORBIS.EarthClimateQA";
    const string Seed = "ORBIS-2D-2026";
    static bool requested;

    static EarthClimateMenuRegression()
    {
        if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick;
    }

    public static void Run()
    {
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "-earthClimateReference");
        if (!Application.isBatchMode || at < 0 || at + 1 >= args.Length)
            throw new ArgumentException("Supply a generated ECL1 file with -earthClimateReference in a batch Editor.");
        string path = Path.GetFullPath(args[at + 1]);
        using (var reader = new BinaryReader(File.OpenRead(path)))
        {
            if (reader.ReadUInt32() != 0x314C4345) throw new InvalidDataException("Expected ECL1.");
            int width = reader.ReadInt32(), height = reader.ReadInt32();
            if (width < 256 || width != 2 * height || reader.BaseStream.Length != 12 + 101L * width * height)
                throw new InvalidDataException("Unexpected Earth export dimensions or length.");
            SessionState.SetInt(Key + ".width", width);
        }
        SessionState.SetString(Key + ".file", path);
        SessionState.SetFloat(Key + ".start", (float)EditorApplication.timeSinceStartup);
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene("Assets/ORBIS/Scenes/Orbis2DWorld.unity");
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + ".start", 0) > 600)
                throw new TimeoutException("Waiting for the player Earth menu or generation.");
            if (!EditorApplication.isPlaying) return;
            var app = UnityEngine.Object.FindFirstObjectByType<FlatWorldApp>();
            if (app == null || app.Busy || app.World == null) return;
            int width = SessionState.GetInt(Key + ".width", 0);
            if (!requested)
            {
                var ui = app.GetComponentInChildren<UIDocument>().rootVisualElement;
                ui.Q<DropdownField>("preset").index = (int)TerrainPreset.Earth;
                var sizes = ui.Q<DropdownField>("size");
                int index = sizes.choices.IndexOf($"{width} × {width / 2}");
                if (index < 0) throw new ArgumentException("Earth size unavailable in player menu.");
                sizes.index = index;
                ui.Q<TextField>("seed").value = Seed;
                SessionState.SetFloat(Key + ".generation", (float)EditorApplication.timeSinceStartup);
                var button = ui.Q<Button>("generate");
                using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = button; button.SendEvent(submit); }
                if (!app.Busy) throw new InvalidOperationException("The menu ignored Generate.");
                requested = true;
                return;
            }
            var world = app.World;
            double seconds = EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + ".generation", 0);
            if (world.Preset != TerrainPreset.Earth || world.Width != width || world.Seed != Seed || world.Moisture == null)
                throw new InvalidDataException("The menu produced a different world.");
            Compare(world, SessionState.GetString(Key + ".file", ""), seconds);
            Finish(0);
        }
        catch (Exception e) { Debug.LogException(e); Finish(1); }
    }

    static void Compare(WorldData world, string path, double seconds)
    {
        long temperatureOffset = 12 + 5L * world.Count;
        long rainOffset = temperatureOffset + 48L * world.Count;
        double maxT = 0, maxP = 0, budget = 0;
        int stride = world.Width / 128;
        using (var temperatures = new BinaryReader(File.OpenRead(path)))
        using (var rain = new BinaryReader(File.OpenRead(path)))
        {
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 128; x++)
            {
                int i = world.Index(stride*x + (x+7*y+3)%stride, stride*y + (5*x+y+9)%stride);
                if (KoppenClassifier.Classify(world.Temperature, world.Precipitation, 12*i) != world.Climates[i])
                    throw new InvalidDataException("The player class differs from its monthly fields at tile " + i);
                temperatures.BaseStream.Position = temperatureOffset + 48L*i;
                rain.BaseStream.Position = rainOffset + 48L*i;
                for (int m = 0; m < 12; m++)
                {
                    maxT = Math.Max(maxT, Math.Abs(temperatures.ReadSingle() - world.Temperature[12*i+m]));
                    maxP = Math.Max(maxP, Math.Abs(rain.ReadSingle() - world.Precipitation[12*i+m]));
                }
            }
        }
        foreach (double error in world.Moisture.BudgetError) budget = Math.Max(budget, Math.Abs(error));
        if (!(maxT <= .001 && maxP <= .05 && budget <= 1e-5))
            throw new InvalidDataException($"Native/export mismatch: dT={maxT:G6}, dP={maxP:G6}, water budget={budget:G6}");
        Debug.Log($"PASS player Earth {world.Width}x{world.Height}, {world.Seed}, 8192 tiles, 12 months. " +
            $"Max dT {maxT:G7} C, dP {maxP:G7} mm/month, relative moisture residual {budget:G7}, menu generation {seconds:F2} s. " +
            $"{Application.unityVersion} / {SystemInfo.graphicsDeviceType} / {SystemInfo.graphicsDeviceName}");
    }

    static void Finish(int status)
    {
        SessionState.EraseBool(Key);
        EditorApplication.update -= Tick;
        EditorApplication.Exit(status);
    }
}
