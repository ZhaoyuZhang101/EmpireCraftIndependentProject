using System;
using System.Diagnostics;
using System.Linq;
using EmpireCraft.Core;
using EmpireCraft.Presentation;
using Orbis.Flat;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace EmpireCraft.EditorTools
{
    // The EmpireCraft core under Unity's own compiler and runtime, without a scene:
    //   Unity.exe -batchmode -nographics -projectPath <p> -logFile <log>
    //             -executeMethod EmpireCraft.EditorTools.EmpireCoreCheck.Run [-ecCoreWidth 256] [-ecCoreYears 40]
    // Loads the rule data, opens an EmpireWorld on a generated ORBIS map and steps it
    // through the runner frame by frame. Lines start with "[EmpireCoreCheck]"; the
    // editor exits 0 on PASS and 1 on FAIL.
    public static class EmpireCoreCheck
    {
        static string Arg(string name) { var a = Environment.GetCommandLineArgs(); for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1]; return null; }

        [MenuItem("EmpireCraft/Check Core")]
        public static void Run()
        {
            int code = 0;
            try
            {
                int width = int.Parse(Arg("-ecCoreWidth") ?? "256"), years = int.Parse(Arg("-ecCoreYears") ?? "40");
                int errorsBefore = LogService.Errors;
                EmpireCoreDriver.PrepareHost();
                int nodes = InstitutionDefinitionRegistry.All.Count();
                if (LogService.Errors != errorsBefore || nodes < 300 || OnomasticsRule.ALL_CULTURE_RULE.Count < 27 || LM.Count < 5000)
                    throw new Exception("rule data did not load: " + nodes + " institution nodes, " + OnomasticsRule.ALL_CULTURE_RULE.Count + " cultures, " + LM.Count + " texts");
                Debug.Log("[EmpireCoreCheck] data: " + InstitutionDefinitionRegistry.Lines.Count + " institution lines, " + nodes + " nodes, " +
                          OnomasticsRule.ALL_CULTURE_RULE.Count + " cultures, " + LM.Count + " texts from " + GameData.Folder);

                var clock = Stopwatch.StartNew();
                var map = FlatGenerator.Generate(width, width / 2, "EMPIRE-UNITY-CHECK", TerrainPreset.Continents, default);
                long generation = clock.ElapsedMilliseconds; clock.Restart();
                var world = EmpireRunner.Open(map, FMath.Seed(map.Seed));
                long opening = clock.ElapsedMilliseconds;
                Require(world, "opening");

                FrameClock.Reset(); FrameJobs.Reset(); SimulationFrameBudget.Reset();
                double day = map.Day; var runner = new EmpireRunner(world, day);
                int frames = 0, worstBehind = 0, worstAt = -1; double worstFrame = 0d, worstLater = 0d; var frame = new Stopwatch();
                PortedSystems.Reset();
                // 120 days a second at 60 frames a second, as the map runs at full speed.
                while (world.Month < years * 12)
                {
                    FrameClock.Advance(1f / 60f); day += 2d; frames++;
                    frame.Restart(); runner.Frame(day); double took = frame.Elapsed.TotalMilliseconds;
                    if (took > worstFrame) { worstFrame = took; worstAt = world.Month; }
                    if (world.Month > 12) worstLater = Math.Max(worstLater, took);   // past the first year: code is compiled, tables are loaded
                    worstBehind = Math.Max(worstBehind, runner.MonthsBehind(day));
                    if (world.Month % 12 == 0 && runner.LastFrameMonths > 0) Require(world, "year " + world.Year);
                }
                var bytes = EmpireSave.Encode(world); var loaded = EmpireSave.Decode(bytes, world.Map);
                if (!EmpireSave.Encode(loaded).SequenceEqual(bytes)) throw new Exception("save round trip differs");
                if (worstBehind > 1) throw new Exception("the runner fell " + worstBehind + " months behind the calendar");

                int realms = world.Realms.Count(r => r.Alive); long treasury = 0; double stability = 0; int cities = 0;
                foreach (var realm in world.Realms)
                {
                    if (!realm.Alive) continue;
                    treasury += world.Ledger.Realm(realm.Id).Treasury;
                    foreach (var city in realm.Cities) { stability += world.Ledger.City(city.Id).Stability; cities++; }
                }
                Debug.Log("[EmpireCoreCheck] world " + width + "x" + width / 2 + ": generation " + generation + " ms, opening " + opening + " ms; " + years + " years in " + frames +
                          " frames, worst frame " + worstFrame.ToString("F2") + " ms, never more than " + worstBehind + " month behind");
                Debug.Log("[EmpireCoreCheck] after " + years + " years: " + realms + "/" + world.Realms.Count + " realms alive, " + cities + " cities, " + world.Living.Count + "/" + world.People.Count +
                          " people living, mean stability " + (stability / Math.Max(1, cities)).ToString("F1") + ", treasuries " + treasury + ", save " + bytes.Length / 1024 + " KiB");
                var slots = PortedSystems.Slots.OrderByDescending(s => s.WorstMs).Take(4).Select(s => s.Name + " " + s.WorstMs.ToString("F2") + " ms at month " + s.WorstMonth);
                double portedTotal = PortedSystems.Slots.Sum(s => s.TotalMs);
                Debug.Log("[EmpireCoreCheck] slowest frame at month " + worstAt + "; after the first year the worst frame is " + worstLater.ToString("F2") + " ms. Ported systems: " + PortedSystems.Slots.Count +
                          " running, " + PortedSystems.TotalFaults + " faults, " + portedTotal.ToString("F0") + " ms in all; slowest calls: " + string.Join(", ", slots));
                if (PortedSystems.TotalFaults > 0) throw new Exception("ported systems failed: " + string.Join("; ", PortedSystems.Slots.Where(s => s.Faults > 0).Select(s => s.Name + " " + s.LastFault)));
                Debug.Log("[EmpireCoreCheck] PASS");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.Log("[EmpireCoreCheck] FAIL " + e.Message);
                code = 1;
            }
            finally { FrameClock.Reset(); FrameJobs.Reset(); SimulationFrameBudget.Reset(); }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static void Require(EmpireWorld world, string when)
        {
            var errors = EmpireInvariants.Check(world);
            if (errors.Count > 0) throw new Exception(when + ": " + string.Join("; ", errors));
        }
    }
}
