using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{
    // Per-city and per-realm books kept by RealmAccounts. Indexed by entity id and
    // grown on demand; an account outlives its city or realm so ids stay aligned.
    public sealed class CityAccount
    {
        public float Stability = 60f;       // 0..100, walks toward the natural level
        public float Corruption = 0.1f;     // 0..1 share of the local office
        public double TaxCarry, GovernanceCarry, GarrisonCarry;
        public int Garrison;                // guards actually paid for this month
        public int UnrestMonths;            // consecutive months below the rebellion threshold
        // Scratch for the month in progress; not saved.
        internal float Natural; internal int Required;
    }

    public sealed class RealmAccount
    {
        public long Treasury;
        public int Dynasty = -1;            // clan id the dynastic clock runs for
        public int DynastyStart;            // month the reigning house took the throne
        public float Pressure;              // dynastic strain, 0..100
        public float SaturatedYears;
        public float Corruption;            // realm-wide rate from the last yearly close
        public int Legitimacy = 70;
        public bool InArrears;              // could not pay last month's bills in full
        public long Unpaid;                 // bills left unpaid since the last yearly close
        public readonly TreasuryData Books = new TreasuryData();
    }

    public sealed class RealmLedger
    {
        public readonly List<CityAccount> Cities = new List<CityAccount>();
        public readonly List<RealmAccount> Realms = new List<RealmAccount>();
        public CityAccount City(int id) { while (Cities.Count <= id) Cities.Add(new CityAccount()); return Cities[id]; }
        public RealmAccount Realm(int id) { while (Realms.Count <= id) Realms.Add(new RealmAccount()); return Realms[id]; }
        public void Ensure(int cities, int realms) { if (cities > 0) City(cities - 1); if (realms > 0) Realm(realms - 1); }

        // The treasury rules count whole months between two timestamps; here a timestamp is a world month.
        EmpireWorld _world; Func<double, int> _monthsSince;
        internal Func<double, int> MonthsSince(EmpireWorld w)
        {
            if (_monthsSince == null || !ReferenceEquals(_world, w)) { _world = w; _monthsSince = stamp => (int)Math.Floor(w.Month - stamp); }
            return _monthsSince;
        }
    }

    // Port phase 1 wiring: runs the ported rule sets (city stability, treasury books,
    // corruption, dynastic cycle) on the skeleton world every month and year.
    //
    // It only keeps books. Nothing here changes who owns what, so the skeleton world
    // evolves exactly as before; the feedback (unrest, uprisings, collapse) arrives
    // with the ported systems of phases 2-5, which also replace the stand-in inputs
    // marked below (loyalty, tax per head, office holders).
    public static class RealmAccounts
    {
        const uint Stream = 0x4C454447u;
        // Stand-in fiscal scale until LandEconomySystem and CityPopulationSystem are ported.
        public const double TaxPerHeadMonthly = 0.004d;
        public const int PeoplePerHousehold = 5;
        public const int BaseLegitimacy = 70;

        // One month of books for every living realm. Call after the world month advanced.
        public static void Month(EmpireWorld w)
        {
            var ledger = w.Ledger;
            ledger.Ensure(w.Cities.Count, w.Realms.Count);
            double now = w.Month;
            Func<double, int> monthsSince = ledger.MonthsSince(w);
            foreach (var realm in w.Realms)
            {
                if (!realm.Alive) continue;
                var account = ledger.Realms[realm.Id];
                if (account.Dynasty < 0) { account.Dynasty = realm.RulingClan.IdOf(); account.DynastyStart = realm.FoundedMonth; }
                float grievance = DynasticCycleRules.HarshBurden(account.Pressure);
                long tax = 0, leaked = 0, governanceDue = 0, garrisonDue = 0;
                foreach (var city in realm.Cities)
                {
                    var book = ledger.Cities[city.Id];
                    int population = (int)Math.Min(int.MaxValue, city.Population);
                    var input = new CityStabilityInput(population, Math.Max(1, population / PeoplePerHousehold),
                        Loyalty(w, realm, city), account.Legitimacy, book.Corruption, 0f, 0f, grievance, false, true, -1, -1);
                    CityStabilityBudget budget = CityStabilityMath.Compute(input);
                    book.Natural = budget.Natural; book.Required = budget.Guards;
                    double due = population * TaxPerHeadMonthly + book.TaxCarry;
                    long collected = (long)Math.Floor(due); book.TaxCarry = due - collected;
                    tax += collected;
                    leaked += (long)Math.Floor(collected * CorruptionRules.Leak(book.Corruption));
                    governanceDue += CityStabilityRules.Invoice(budget.GovernanceAnnual, ref book.GovernanceCarry);
                    garrisonDue += CityStabilityRules.Invoice(budget.GuardMonthly * 12d, ref book.GarrisonCarry);
                }
                Post(account, now, monthsSince, tax, TreasuryCategory.ResidentTax);
                Post(account, now, monthsSince, -leaked, TreasuryCategory.Corruption);
                long bills = governanceDue + garrisonDue, cash = Math.Max(0L, account.Treasury);
                double funding = bills <= 0 ? 1d : Math.Min(1d, cash / (double)bills);
                long governancePaid = (long)Math.Floor(governanceDue * funding), garrisonPaid = (long)Math.Floor(garrisonDue * funding);
                Post(account, now, monthsSince, -governancePaid, TreasuryCategory.Governance);
                Post(account, now, monthsSince, -garrisonPaid, TreasuryCategory.Garrison);
                account.InArrears = funding < 1d;
                account.Unpaid += bills - governancePaid - garrisonPaid;
                foreach (var city in realm.Cities)
                {
                    var book = ledger.Cities[city.Id];
                    book.Garrison = (int)(book.Required * funding);
                    float held = CityStabilityRules.Suppression(book.Garrison, book.Required, (float)funding);
                    book.Stability = CityStabilityRules.Advance(book.Stability, CityStabilityRules.Clamp(book.Natural + held), (float)funding * 100f);
                    book.UnrestMonths = book.Stability < CityStabilityRules.RebellionThreshold ? book.UnrestMonths + 1 : 0;
                }
            }
        }

        // Yearly close: office corruption drifts, the dynastic clock advances.
        public static void Year(EmpireWorld w)
        {
            var ledger = w.Ledger;
            ledger.Ensure(w.Cities.Count, w.Realms.Count);
            foreach (var realm in w.Realms)
            {
                if (!realm.Alive) continue;
                var account = ledger.Realms[realm.Id];
                if (realm.RulingClan.IdOf() != account.Dynasty)
                {
                    // A new house on the throne: the strain of the old dynasty is mostly shed.
                    if (account.Dynasty >= 0)
                    {
                        account.Pressure = DynasticCycleRules.Carry(account.Pressure, DynasticCycleSystem.Transition.Clear);
                        account.SaturatedYears = 0f;
                    }
                    account.Dynasty = realm.RulingClan.IdOf(); account.DynastyStart = w.Month;
                }
                double sum = 0d, capital = 0d;
                foreach (var city in realm.Cities)
                {
                    int id = city.Id; var book = ledger.Cities[id];
                    // Stand-in for the office holder's character until officials are ported.
                    double corruptibility = w.Chance(Stream, id, 1) < 0.35d ? w.Chance(Stream, id, 2) : 0d;
                    double integrity = w.Chance(Stream, id, 3);
                    book.Corruption = (float)CorruptionRules.Bound(book.Corruption + CorruptionRules.AnnualOfficeChange(corruptibility, true, integrity));
                    sum += book.Corruption;
                    if (city == realm.Capital) capital = book.Corruption;
                }
                double mean = realm.Cities.Count > 0 ? sum / realm.Cities.Count : 0d;
                double corruption = CorruptionRules.Realm(capital, mean, mean);
                int age = Math.Max(0, (w.Month - account.DynastyStart) / 12);
                float burden = DynasticCycleRules.HarshBurden(account.Pressure) + (float)corruption * 40f;
                bool bankrupt = account.Unpaid > 0;
                float growth = DynasticCycleRules.AnnualGrowth(age, burden, bankrupt, false, corruption, realm.Ruler == null, false, false, 0f)
                               * MaladySystem.PaceAt(age);
                account.Pressure = DynasticCycleRules.Advance(account.Pressure, growth);
                account.SaturatedYears = account.Pressure >= DynasticCycleRules.SaturatedPressure ? account.SaturatedYears + 1f : 0f;
                account.Corruption = (float)corruption;
                account.Legitimacy = Math.Max(0, Math.Min(100, BaseLegitimacy + DynasticCycleRules.LegitimacyPenalty(account.Pressure)));
                account.Unpaid = 0;
            }
        }

        // Stand-in loyalty: firm at the capital, thinning with distance from it.
        static int Loyalty(EmpireWorld w, Realm realm, City city)
        {
            if (realm.Capital == null || realm.Capital == city) return 30;
            float distance = w.Distance(realm.Capital.Tile, city.Tile);
            return (int)Math.Max(-20f, 30f - distance * 1.2f);
        }

        static void Post(RealmAccount account, double now, Func<double, int> monthsSince, long change, TreasuryCategory category)
        {
            if (change == 0) return;
            TreasuryRules.ChangeBalance(ref account.Treasury, change);
            TreasuryRules.Record(account.Books, now, monthsSince, change, category);
        }

        public static TreasuryReport Report(EmpireWorld w, int realm) =>
            TreasuryRules.Report(w.Ledger.Realm(realm).Books, w.Month, w.Ledger.MonthsSince(w));
    }

    // The whole simulation step, in the order the parts depend on each other.
    public static class EmpireSimulation
    {
        public static void Month(EmpireWorld w)
        {
            w.Activate();
            EcRandom.Seed(w.Seed, w.Month + 1);
            using (FrameProfiler.Measure("empire.skeleton")) SkeletonRules.Month(w);
            using (FrameProfiler.Measure("empire.accounts.month")) RealmAccounts.Month(w);
            if (w.Month % 12 == 0)
                using (FrameProfiler.Measure("empire.accounts.year")) RealmAccounts.Year(w);
            // The systems brought over from the mod, in their fixed order (see PortedSystems).
            PortedSystems.Month(w);
        }
    }

    // Keeps an EmpireWorld in step with the ORBIS calendar (30 days a month), one
    // frame at a time, inside the shared frame budget. The host calls Frame once
    // per rendered frame after advancing FrameClock.
    public sealed class EmpireRunner
    {
        public const double DaysPerMonth = 30d;
        public readonly EmpireWorld World;
        readonly double _originDay;
        readonly Action _step;
        int _target;
        public int MaxMonthsPerFrame = 12;
        public int LastFrameMonths { get; private set; }

        // `day` is the ORBIS day that corresponds to the world's current month.
        public EmpireRunner(EmpireWorld world, double day)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            _originDay = day - world.Month * DaysPerMonth;
            _step = Step;
        }

        public int MonthsBehind(double day) => Math.Max(0, (int)Math.Floor((day - _originDay) / DaysPerMonth) - World.Month);

        public int Frame(double day)
        {
            LastFrameMonths = 0;
            _target = (int)Math.Floor((day - _originDay) / DaysPerMonth);
            if (World.Month < _target) FrameJobs.Run("empire.month", FrameJobs.Priority.Normal, _step);
            return LastFrameMonths;
        }

        void Step()
        {
            do { EmpireSimulation.Month(World); LastFrameMonths++; }
            while (World.Month < _target && LastFrameMonths < MaxMonthsPerFrame && SimulationFrameBudget.HasTime);
        }

        // Opening sizes for a map: about 300 cities and 40 realms on a 1024-wide world.
        public static EmpireWorld Open(Orbis.Flat.WorldData map, ulong seed)
        {
            var world = new EmpireWorld(map, seed);
            int cities = Math.Max(30, (int)(300L * map.Width * map.Width / (1024L * 1024L)));
            int realms = Math.Max(6, 40 * map.Width / 1024);
            EmpireBootstrap.Seed(world, cities, realms);
            // Current from the start: switching worlds resets the ported systems (WorldSwitch), which is loading work,
            // not something for the first frame of play.
            world.Activate();
            return world;
        }
    }
}
