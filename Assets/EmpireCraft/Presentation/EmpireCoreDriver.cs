using System;
using System.IO;
using EmpireCraft.Core;
using Orbis.Flat;
using UnityEngine;

namespace EmpireCraft.Presentation
{
    // Runs the EmpireCraft core beside the ORBIS world: whenever FlatWorldApp shows a
    // world, an EmpireWorld is opened on it and stepped with the ORBIS calendar, one
    // frame at a time, inside the shared frame budget. Nothing is drawn here; windows
    // and map layers read EmpireCoreDriver.Instance.World.
    //
    // Not done yet: the EmpireCraft state is not written with the ORBIS save (a world
    // opens afresh each time), and terrain edits do not re-check city sites.
    public sealed class EmpireCoreDriver : MonoBehaviour
    {
        public static EmpireCoreDriver Instance { get; private set; }
        // Set false before the scene loads to keep the core out of a session.
        public static bool Enabled = true;
        public EmpireWorld World { get { return runner != null ? runner.World : null; } }
        public EmpireRunner Runner { get { return runner; } }

        FlatWorldApp app; WorldData map; EmpireRunner runner;
        static bool hostReady;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Enabled || Instance) return;
            var host = new GameObject("EmpireCraft Core");
            DontDestroyOnLoad(host);
            Instance = host.AddComponent<EmpireCoreDriver>();
        }

        // Rule data sits in Assets/EmpireCraft/Data in the editor. A player build has
        // to ship a copy (StreamingAssets/EmpireCraft/Data) before it can load any.
        public static string DataFolder
        {
            get
            {
                string shipped = Path.Combine(Application.streamingAssetsPath, "EmpireCraft", "Data");
                return Directory.Exists(shipped) ? shipped : Path.Combine(Application.dataPath, "EmpireCraft", "Data");
            }
        }

        // Log sink, data folder and rule data. Safe to call again.
        public static void PrepareHost()
        {
            if (hostReady) return;
            hostReady = true;
            LogService.Sink = (level, message) =>
            {
                if (level == LogLevel.Error) Debug.LogError(message);
                else if (level == LogLevel.Warning) Debug.LogWarning(message);
            };
            GameData.Folder = DataFolder;
            // The game's text is Simplified Chinese: the mod's "cz" tables (its "ch" ones are
            // Traditional, and most peoples carry only a "cz" name).
            LM.Language = "cz";
            if (!Directory.Exists(GameData.Folder)) { Debug.LogWarning("[EmpireCraft] rule data folder missing: " + GameData.Folder); return; }
            try
            {
                InstitutionDefinitionRegistry.Load();
                OnomasticsRule.ReadSetting();
                string texts = Path.Combine(GameData.Folder, "Locales", LM.Language + ".json");
                if (File.Exists(texts)) LM.LoadFile(texts);
                // Regime templates and their titles; realms take theirs from their people when a world opens.
                RegimeManager.init();
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        void Update()
        {
            FrameClock.Advance(Time.unscaledDeltaTime);
            if (!app) app = FindFirstObjectByType<FlatWorldApp>();
            if (!app || app.World == null || app.Busy) return;
            if (!ReferenceEquals(app.World, map)) Open(app.World);
            if (runner == null) return;
            try { runner.Frame(app.World.Day); }
            catch (Exception e)
            {
                // A broken step must not take the map down with it; stop the core and say so.
                Debug.LogException(e);
                Debug.LogError("[EmpireCraft] core stopped at " + runner.World.Year + "年" + runner.World.MonthOfYear + "月 after the error above.");
                runner = null;
            }
        }

        void Open(WorldData world)
        {
            map = world; runner = null;
            PrepareHost();
            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var empire = EmpireRunner.Open(world, FMath.Seed(world.Seed));
                runner = new EmpireRunner(empire, world.Day);
                int realms = 0; foreach (var realm in empire.Realms) if (realm.Alive) realms++;
                Debug.Log("[EmpireCraft] core opened on " + world.Width + "x" + world.Height + " world \"" + world.Seed + "\": " +
                          realms + " realms, " + empire.Cities.Count + " cities, " + empire.Living.Count + " people (" + clock.ElapsedMilliseconds + " ms).");
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
