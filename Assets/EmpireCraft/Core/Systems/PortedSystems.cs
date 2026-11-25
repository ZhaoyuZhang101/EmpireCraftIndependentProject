using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{
    // Runs the ported systems as the world steps forward. In the mod these hung off the
    // game's frame loop, each person's birthday and each empire's update; here the month
    // step calls them, in one fixed order.
    //
    // Written by hand, and it names ported entry points directly: if a regeneration ever
    // suspends one of them the core stops compiling here, which is the place to notice.
    //
    // A system that throws is counted, reported once, and switched off after a few faults,
    // so one broken system cannot stop the world. Tests ask for zero faults.
    public static class PortedSystems
    {
        public sealed class Slot
        {
            public readonly string Name;
            public long Runs;
            public int Faults;
            public string LastFault;
            public bool Off;
            public double TotalMs, WorstMs;     // time spent in it, and its slowest single call
            public int WorstMonth = -1;         // the world month of that call
            internal Slot(string name) { Name = name; }
        }

        public const int FaultLimit = 5;
        public static bool Enabled = true;
        static readonly List<Slot> Order = new List<Slot>();
        static readonly Dictionary<string, Slot> ByName = new Dictionary<string, Slot>(StringComparer.Ordinal);
        public static IReadOnlyList<Slot> Slots => Order;
        public static int TotalFaults { get { int total = 0; foreach (var slot in Order) total += slot.Faults; return total; } }
        public static void Reset() { Order.Clear(); ByName.Clear(); }

        static void Run(string name, Action body)
        {
            if (!ByName.TryGetValue(name, out Slot slot)) { slot = new Slot(name); ByName[name] = slot; Order.Add(slot); }
            if (slot.Off) return;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                using (FrameProfiler.Measure("ported." + name)) body();
                slot.Runs++;
            }
            catch (Exception fault)
            {
                slot.Faults++;
                slot.LastFault = fault.GetType().Name + ": " + fault.Message;
                if (slot.Faults == 1) LogService.LogWarning("[EmpireCraft] ported system '" + name + "' failed: " + slot.LastFault);
                if (slot.Faults >= FaultLimit) slot.Off = true;
            }
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            slot.TotalMs += ms;
            if (ms > slot.WorstMs) { slot.WorstMs = ms; slot.WorstMonth = EmpireWorld.Current == null ? -1 : EmpireWorld.Current.Month; }
        }

        // Called by EmpireSimulation after the skeleton and the books, with the world current and the random stream seeded.
        public static void Month(EmpireWorld w)
        {
            if (!Enabled) return;

            // A person's year turns on the month of their birth: standing fades a little and is earned again.
            Run("renown", () =>
            {
                int month = w.Month % 12;
                var living = w.Living.ToArray();
                foreach (var person in living)
                    if (person.Alive && ((person.BirthMonth % 12) + 12) % 12 == month && w.Month > person.BirthMonth)
                        RenownSystem.Yearly(person);
            });

            // Monthly passes over the whole world. Each looks at the calendar itself.
            Run("privy purse", PrivyPurse.Tick);
            Run("treasury hoard", TreasuryHoardSystem.Tick);
            Run("offices", OfficeManager.Tick);
            Run("war objectives", WarObjective.Tick);
            Run("roads", RoadBuildingSystem.Tick);
            Run("tribal council", TribalCouncilSystem.Monthly);
            Run("abdication", ShanrangSystem.Monthly);
            Run("steppe order", SteppeOrderSystem.Monthly);
            Run("palace army", () => { foreach (var realm in w.LiveRealms.ToArray()) PalaceArmySystem.Monthly(realm); });

            if (w.Month % 12 != 0) return;
            Run("ambition", AmbitionSystem.Yearly);
            Run("hostages", HostageSystem.Yearly);
            Run("road treaties", RoadTreatySystem.Yearly);
        }
    }
}
