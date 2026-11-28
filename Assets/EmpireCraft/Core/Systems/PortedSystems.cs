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

        // How often a realm looks at its standing as an empire (proclaiming one, its yearly upkeep): every
        // RealmCheckMonths months, realms spread over them by id.
        public const int RealmCheckMonths = 3;
        // The decision system works in slices with a time budget (in the mod it ran a little every frame);
        // a month gives it this many slices.
        const int DecisionSlices = 4;
        // Time a month step may take in the systems' own budgets: in effect, all it needs.
        const double HeadlessStepMilliseconds = 1e12;
        // At most this many calls to settle a month of corps (each works through a slice of the realms).
        const int CorpsMonthSlices = 256;
        const int PopulationMonthSlices = 1024;
        const int SettlementMonthSlices = 1024;

        // The war manager the mod's war end is handed (it tells the windows that wars changed).
        static readonly WarManager Wars = new WarManager();

        // The kinds of plot, and the decision system told of every plot started. Code, not data: done once.
        static void Prepare()
        {
            if (PlotKinds.Library.get("become_empire") == null) EmpireCraftPlotsAddition.init();
            PlotStore.Started = DecisionSystem.OnPlotCreated;
            // A city taken in war and a city changing hands go through the mod's city rules (CityPatch).
            WorldActions.CaptureHandler = CityPatch.FinishedCapture;
            WorldActions.JoinHandler = CityPatch.joinAnotherKingdom;
            WorldActions.BeforeSetKingdom = CityPatch.setKingdom;
            WorldActions.AfterSetKingdom = CityPatch.setKingdom_Postfix;
            // A new city: its culture set up, then whether it stays in the founder's realm (CityPatch).
            WorldActions.NewCityHandler = CityPatch.newCity;
            WorldActions.CityBuiltHandler = CityPatch.CityBuilt;
            // A war ends through the mod's war end (WarPatch.end_war): the peace settled by the war's aim, the occupation the
            // two sides left in each other's cities cleared, the endings of rebellions and claims (see WorldOps.EndWar).
            WorldOps.EndWarHandler = (war, outcome) => WarPatch.end_war(Wars, war, outcome);
        }

        // Called by EmpireSimulation after the skeleton and the books, with the world current and the random stream seeded.
        public static void Month(EmpireWorld w)
        {
            if (!Enabled) return;
            Prepare();
            // A month is one step here, with no frame to keep smooth inside it: the mod's own headless-core mode, where
            // the background gets as much time as the step needs. Each system then finishes its month in the step, so the
            // world does not depend on how fast the machine is (the same seed gives the same world).
            double? budget = SimulationFrameBudget.ServerMilliseconds;
            SimulationFrameBudget.ServerMilliseconds = HeadlessStepMilliseconds;
            SimWatch.Frozen = true;
            try { MonthBody(w); }
            finally { SimWatch.Frozen = false; SimulationFrameBudget.ServerMilliseconds = budget; }
        }

        static void MonthBody(EmpireWorld w)
        {

            // The skeleton's changes to head counts go into the population groups first (see PopulationBridge).
            Run("population in", () => PopulationBridge.Before(w));
            // Wilderness: barren land (mountains, deserts, tundra, rainforest, marsh) gathered into regions that belong to the
            // nearest city's title; it keeps cities from spreading into it and lets realms meet across it.
            Run("wilderness", WildernessSystem.Tick);

            // Each realm's own routine: is it an empire, should it proclaim one (which starts the plot that prepares it).
            Run("realm empire check", () =>
            {
                var check = new EmpireCraftKingdomBehCheckEmpire();
                foreach (var realm in w.LiveRealms.ToArray())
                    if (realm.Alive && (realm.Id + w.Month) % RealmCheckMonths == 0) check.Execute(realm);
            });
            // Each empire still stands (the mod's monthly liveness pass, EmpireManager.CheckLiveness): what can be repaired is,
            // an empire whose core realm holds no land is dissolved once the round is through.
            Run("empire liveness", () => w.Empires.CheckLiveness());
            // Plots under way move on, those due are weighed and started, finished ones take effect.
            Run("decisions", () => { for (int i = 0; i < DecisionSlices; i++) DecisionSystem.Tick(); });
            Run("plots sweep", () => w.Plots.Sweep());
            // Corps and fronts: levies, mustering, marching, the fronts' push and the fighting, a month's worth. The mod
            // settled the month a few realms a frame; here the month is settled before the world steps on.
            Run("corps and fronts", () =>
            {
                CorpsSystem.Update();
                for (int slice = 0; slice < CorpsMonthSlices && CorpsSystem.MonthPending; slice++) CorpsSystem.Update();
            });
            // The cities' month: levies, marching off, migration, plague, reading, growth (see the CityPopulationSystem seam).
            Run("population", () =>
            {
                CityPopulationSystem.Tick();
                for (int slice = 0; slice < PopulationMonthSlices && CityPopulationSystem.MonthPending; slice++) CityPopulationSystem.Tick();
            });
            // State settlement: a realm whose cities have taken all the land they may sends settlers to found a new city
            // on free land it can reach (paid from its treasury, people moved from its cities), and people flee famine or
            // crowding to found one of their own. The mod checked each realm once a month and searched a realm a frame.
            Run("state settlement", () =>
            {
                foreach (var realm in w.LiveRealms.ToArray()) StateSettlementSystem.Check(realm);
                StateSettlementSystem.Tick();
                for (int slice = 0; slice < SettlementMonthSlices && StateSettlementSystem.SearchesPending; slice++) StateSettlementSystem.Tick();
            });
            Run("small states submit", SmallStateSubmissionSystem.Tick);
            Run("national unification", NationalUnificationSystem.Tick);

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
            // Peace settlements hand over what was won (and check each war's objective).
            Run("war goals", WarGoalSystem.Tick);
            // Each running war's own month (the mod's War.update patch, WarPatch.update): a war missing its main attacker or
            // defender ends (DropBrokenWar); then the war's aim is set when it starts and reviewed yearly, the two sides talk
            // peace once a year, taking the aimed-at city (a rebel's, the capital) wins it, claims and wars over an emperor's
            // title end when won, and a war past the longest a war runs is brought to a stop.
            Run("wars", () => { foreach (War war in new System.Collections.Generic.List<War>(w.LiveWars)) if (war.Alive && WarPatch.DropBrokenWar(war) && war.Alive) WarPatch.update(war); });
            Run("roads", RoadBuildingSystem.Tick);
            // The mod's monthly royal-house pass (RoyalHouseSystem.Tick); its links that compile, in its order. City-states
            // and tribes keep one city each; a tribe also follows its people's regime out of the tribal stage, holds its
            // council and abdications, and members leave, are let go or are enfeoffed (TribalFissionService).
            Run("city-states one city", CityStateService.EnforceOneCity);
            Run("tribes", TribalFissionService.EnforceOneCity);
            Run("tribal assembly", TribalAssemblySystem.Monthly);
            Run("royal marriage", RoyalMarriageSystem.Monthly);
            Run("steppe order", SteppeOrderSystem.Monthly);
            Run("palace army", () => { foreach (var realm in w.LiveRealms.ToArray()) PalaceArmySystem.Monthly(realm); });

            // The world's yearly scans, in the mod's order (it ran one of them a frame; each keeps its own calendar and
            // returns at once until it is due). Those not listed still wait on parts not brought over.
            Run("scan: clan branches", ClanBranchSystem.TryYearlyScan);
            Run("scan: ideology spread", IdeologySpreadSystem.TryYearlyScan);
            Run("scan: technology", TechnologySystem.TryYearlyScan);
            Run("scan: institution drift", InstitutionSystem.TryYearlyCultureDrift);
            Run("scan: capital loss naming", CapitalLossNamingSystem.TryYearlyScan);
            Run("scan: wartime custody", WartimeCustodySystem.TryMonthlyScan);
            Run("scan: jimi frontier", JimiSystem.TryMonthlyScan);
            Run("scan: internal boundaries", InternalBoundarySystem.TryYearlyScan);
            Run("scan: state debt", StateDebtSystem.TryMonthlyScan);
            Run("scan: old capital restored", CoreCapitalRestorationSystem.TryMonthlyScan);
            Run("scan: feudal tribute", FeudalVassalService.TryYearlyTribute);
            Run("scan: great powers", GreatPowerDiplomacy.TryYearlyScan);
            Run("scan: institution contact", InstitutionSystem.TryYearlyWorldPass);
            Run("scan: institution realms", InstitutionSystem.TryYearlyKingdomScan);
            Run("scan: papacy", PapacySystem.TryYearlyDiagnose);
            Run("scan: legacy institutions", LegacyInstitutionSystem.TryYearlyScan);

            if (w.Month % 12 != 0) { Run("population out", () => PopulationBridge.After(w)); return; }
            // Realms, cities and people that came about during the year take the culture of where they are.
            Run("culture upkeep", () => CultureGeography.Upkeep(w));
            Run("ambition", AmbitionSystem.Yearly);
            Run("hostages", HostageSystem.Yearly);
            Run("road treaties", RoadTreatySystem.Yearly);
            foreach (var empire in w.Empires.list.ToArray())
                if (empire.Alive && empire.CoreKingdom != null && empire.CoreKingdom.Alive) EmpireYear(empire);
            Run("population out", () => PopulationBridge.After(w));
        }

        // An empire's year. In the mod one chain ran it (the empire's update, then its constitutional and economic
        // year); that chain still waits on parts not brought over, so its links that compile are called here in
        // the mod's order. Links still missing are simply not there yet; nothing stands in for them.
        static void EmpireYear(Empire empire)
        {
            var state = empire.data.constitutional_economy ??= new ConstitutionalEconomyState();
            state.recent_trade ??= new List<CompletedTradeVoyage>();
            state.stable_culture ??= "";
            // Reforms the empire's court pushes along its people's institution line (in the tribal stage only a
            // confederation pushes them), then the technology drive beside them.
            Run("empire: institutions", () => InstitutionSystem.Update(empire));
            Run("empire: tech drive", () => TechDriveSystem.Update(empire));
            Run("empire: constitution", () => ConstitutionSystem.Update(empire, state));
            Run("empire: parliament", () => ParliamentSystem.Update(empire));
            Run("empire: provincial politics", () => ProvincialPoliticsSystem.Update(empire));
            Run("empire: name contest", () => RealmNamingSystem.Monthly(empire));
            Run("empire: economic cycle", () => EconomicCycleSystem.Update(empire, state));
            Run("empire: labour", () => LabourSystem.Update(empire, state));
            Run("empire: corruption", () => CorruptionSystem.Update(empire));
            Run("empire: ideology dynamics", () => IdeologyDynamicsSystem.Update(empire, state));
            Run("empire: ideology education", () => IdeologyEducationSystem.Update(empire));
            Run("empire: military coup", () => MilitaryCoupSystem.Yearly(empire));
            Run("empire: keju", () => KejuSystem.Yearly(empire));
            Run("empire: court maladies", () => MaladySystem.Yearly(empire));
            Run("empire: ruler quality", () => RulerQualitySystem.Yearly(empire));
            Run("empire: consort kin", () => ConsortKinSystem.Yearly(empire));
            Run("empire: palace army", () => PalaceArmySystem.Yearly(empire));
            Run("empire: title centralisation", () => ModernTitleCentralization.Update(empire));
            Run("empire: conglomerates", () => ConglomerateSystem.Update(empire, state));
            Run("empire: ancient schools", () => AncientSchoolsSystem.Yearly(empire));
            Run("empire: ethnic rank", () => EthnicRankSystem.Yearly(empire));
            Run("empire: guoye", () => GuoyeSystem.Yearly(empire));
            Run("empire: late imperial", () => LateImperialSystem.Yearly(empire));
            Run("empire: party finance", () => PartyFinanceSystem.Yearly(empire));
            Run("empire: party inner", () => PartyInnerSystem.Yearly(empire));
            Run("empire: party relations", () => PartyRelations.Yearly(empire));
            Run("empire: rites", () => RitesSystem.Yearly(empire));
            Run("empire: reign record", () => ReignRecordSystem.OnYear(empire));
        }
    }
}
