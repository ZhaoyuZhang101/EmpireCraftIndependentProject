using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Core;
using Orbis.Flat;

// Port phase 1: the rule sets brought over from the mod, their data files, the
// host layer they run on, and the ledger that runs them on a world.
public static class RulesChecks
{
    static int checks;
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static bool Near(double a, double b, double tolerance = 1e-6) => Math.Abs(a - b) <= tolerance;

    public static string DataFolder()
    {
        string folder = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "EmpireCraft", "Data");
        if (!Directory.Exists(folder)) throw new Exception("Run from the repository root: " + folder + " not found");
        return folder;
    }

    public static void Rules()
    {
        checks = 0;
        // City stability.
        float calm = CityStabilityRules.Natural(30, 70, 0.05f, 0f, 0f, false, 0f);
        float hungry = CityStabilityRules.Natural(30, 70, 0.05f, 0f, 0f, true, 0f);
        float rotten = CityStabilityRules.Natural(30, 70, 0.9f, 0f, 0f, false, 0f);
        Check(calm > hungry && calm > rotten && calm <= 100f && hungry >= 0f, "famine and corruption must lower natural stability");
        Check(CityStabilityRules.RequiredGarrison(10000, 30f, 1f) > CityStabilityRules.RequiredGarrison(10000, 80f, 1f), "restless cities need more guards");
        Check(CityStabilityRules.RequiredGarrison(10000, 30f, 0f) == 0, "no garrison policy, no guards");
        double carry = 0d; int billed = 0;
        for (int month = 0; month < 120; month++) billed += CityStabilityRules.Invoice(7.3d, ref carry);
        Check(billed == 73 && carry < 1d, "ten years of a 7.3 annual bill must invoice 73, got " + billed);
        Check(Near(CityStabilityRules.Advance(50f, 90f, 0f), 52f) && Near(CityStabilityRules.Advance(50f, 10f, 0f), 48f), "stability walks at most two points a month");
        Check(Near(CityStabilityRules.Suppression(100, 100, 1f), 20f) && CityStabilityRules.Suppression(0, 100, 1f) == 0f, "a full paid garrison holds 20 points");
        var inputs = new[]
        {
            new CityStabilityInput(8000, 1600, 20, 60, 0.2f, 0f, 0f, 10f, false, true, -1, -1),
            new CityStabilityInput(300, 60, -10, 30, 0.6f, 0f, 0.4f, 40f, true, true, -1, -1)
        };
        var batch = CityStabilityMath.Compute(inputs);
        for (int i = 0; i < inputs.Length; i++)
        {
            var single = CityStabilityMath.Compute(inputs[i]);
            Check(single.Natural == batch[i].Natural && single.Guards == batch[i].Guards && single.GovernanceAnnual == batch[i].GovernanceAnnual, "batch and single stability budgets must agree");
        }
        Check(batch[1].Natural < batch[0].Natural && batch[1].Governance > 1f, "a troubled city is governed harder");

        // Treasury books.
        var books = new TreasuryData(); double now = 0d; Func<double, int> since = stamp => (int)Math.Floor(now - stamp);
        long balance = 0;
        for (int month = 0; month < 30; month++)
        {
            now = month;
            TreasuryRules.Record(books, now, since, TreasuryRules.ChangeBalance(ref balance, 100), TreasuryCategory.ResidentTax);
            TreasuryRules.Record(books, now, since, TreasuryRules.ChangeBalance(ref balance, -40), TreasuryCategory.Garrison);
        }
        var report = TreasuryRules.Report(books, now, since);
        Check(balance == 1800 && books.periods.Count == 12, "books keep twelve monthly periods, got " + books.periods.Count);
        Check(report.income == 1200 && report.expense == 480 && report.cash_balance == 720, "twelve-month report " + report.income + "/" + report.expense);
        Check(TreasuryRules.EssentialExpense(books, now, since) == 480, "garrison pay is an essential expense");
        int cityCarry = 0, centralCarry = 0; long toCity = 0, toLocal = 0, toCentral = 0;
        for (int month = 0; month < 100; month++)
        {
            var (city, local, central) = TreasuryRules.SplitTax(37, 30, 45, ref cityCarry, ref centralCarry);
            Check(city + local + central == 37 && city >= 0 && local >= 0 && central >= 0, "a tax split must conserve the tax");
            toCity += city; toLocal += local; toCentral += central;
        }
        Check(toCity == 1110 && toCentral == 1665 && toLocal == 925, "split carries must reach the exact shares over time: " + toCity + "/" + toLocal + "/" + toCentral);
        int[] shares = TreasuryRules.Allocate(100, new[] { 30, 500, 20 });
        Check(shares.Sum() == 100 && shares[0] <= 30 && shares[2] <= 20, "an allocated bill is paid once and within each budget");

        // Corruption and the dynastic cycle.
        Check(Near(CorruptionRules.Leak(1d), 0.5d) && CorruptionRules.Leak(0d) == 0d, "full corruption leaks half the tax");
        Check(CorruptionRules.AnnualOfficeChange(0.8d, true) > 0d && CorruptionRules.AnnualOfficeChange(0d, true, 1d) < CorruptionRules.AnnualOfficeChange(0d, true, 0d), "honest offices clean up, faster with integrity");
        float early = DynasticCycleRules.AnnualGrowth(20, 10f, false, false, 0.1d, false, false, false, 0f);
        float late = DynasticCycleRules.AnnualGrowth(320, 10f, false, false, 0.1d, false, false, false, 0f);
        float crisis = DynasticCycleRules.AnnualGrowth(320, 70f, true, true, 0.7d, true, false, false, 0f);
        Check(early >= DynasticCycleRules.MinGrowth && late > early && crisis > late, "strain grows faster with age and misrule: " + early + "/" + late + "/" + crisis);
        Check(DynasticCycleRules.Carry(90f, DynasticCycleSystem.Transition.Clear) <= 20f && DynasticCycleRules.Carry(90f, DynasticCycleSystem.Transition.Keep) == 90f, "a true change of dynasty sheds most strain");
        Check(DynasticCycleRules.LegitimacyPenalty(100f) == -40 && DynasticCycleRules.LegitimacyPenalty(0f) == 0, "strain costs up to 40 legitimacy");
        Check(MaladySystem.PaceAt(10) < MaladySystem.PaceAt(100) && MaladySystem.PaceAt(100) < MaladySystem.PaceAt(200), "founding decades fester slowly");
        Check(DynasticCycleRules.CollapseUprisingChance(25) == 0f && DynasticCycleRules.CollapseUprisingChance(0) > DynasticCycleRules.CollapseUprisingChance(15), "uprisings follow a legitimacy collapse");

        // Institutions, court, parties, peerage.
        Check(InstitutionRules.CalculateAnnualProgress(10f, 80f, 10f) > InstitutionRules.CalculateAnnualProgress(10f, 20f, 70f), "support speeds a reform");
        Check(InstitutionRules.ResolveStage(0f) != InstitutionRules.ResolveStage(100f), "a finished reform is in another stage");
        Check(InstitutionRules.IsTierAbsorbable(4, 3, 1) && !InstitutionRules.IsTierAbsorbable(9, 2, 1) && !InstitutionRules.IsTierAbsorbable(3, 3, 1), "only institutions a step ahead can be absorbed");
        Check(CourtCrisisRules.GrievanceChange(10, true, 0f) > CourtCrisisRules.GrievanceChange(90, false, 50f), "a weak throne and a boycott breed grievance");
        var crowd = new Dictionary<PartyIdeology, int> { [PartyIdeology.Conservatism] = 400, [PartyIdeology.SocialDemocracy] = 900, [PartyIdeology.Confucianism] = 5000 };
        var pressure = PartyOpeningRules.Measure(crowd, PartyIdeology.Conservatism);
        Check(pressure.HasChallenge && pressure.Challenger == PartyIdeology.SocialDemocracy, "ancient schools do not count as a party challenge");
        Check(IdeologyFamilies.IsAncient(PartyIdeology.Legalism) && !IdeologyFamilies.IsAncient(PartyIdeology.Communism) && IdeologyFamilies.IsLeft(PartyIdeology.Communism), "ideology families");
        var tree = new Dictionary<string, string[]> { ["father"] = new[] { "elder", "younger" }, ["elder"] = new[] { "grandson" }, ["younger"] = new string[0], ["grandson"] = new string[0] };
        var ids = new Dictionary<string, long> { ["father"] = 1, ["elder"] = 2, ["younger"] = 3, ["grandson"] = 4 };
        string heir = PeerageSuccessionRules.FindDescendant("father", p => tree[p], p => true, p => p != "elder", p => ids[p]);
        Check(heir == "grandson", "a dead eldest son's line comes before his brother, got " + heir);
        Check(OfficialRankRules.Ratchet(5, 3) == 3 && OfficialRankRules.Ratchet(2, 6) == 2 && OfficialRankRules.Clamp(-4) == 0, "a rank only ratchets upward");
        Check(Enum.IsDefined(typeof(RegimeType), RegimeType.LvLing) && Enum.GetValues(typeof(TemporaryFactionType)).Length > 50, "regime and claim catalogues are present");

        // Host layer.
        Check(EcColorUtility.TryParseHtmlString("#FF8000", out EcColor orange) && orange.Rgb == 0xFF8000u && !EcColorUtility.TryParseHtmlString("nope", out _), "html colours");
        Check(EcColor.HSVToRGB(0f, 1f, 1f).Rgb == 0xFF0000u && EcColor.HSVToRGB(1f / 3f, 1f, 1f).Rgb == 0x00FF00u && EcColor.HSVToRGB(0.5f, 0f, 0.5f).Rgb == 0x808080u, "hsv colours");
        Check(EcMath.Clamp01(1.5f) == 1f && EcMath.RoundToInt(2.6f) == 3 && EcMath.Lerp(0f, 10f, 0.25f) == 2.5f, "math helpers");
        LM.Clear(); LM.LoadJson("{\"class_peasant\":\"农民\",\"n\":3}");
        Check(SocialClass.Peasant.ToTranslate() == "农民" && LM.Get("missing_key") == "missing_key" && LM.Count == 1, "text table");
        Console.WriteLine("PASS ported rules (" + checks + " checks)");
    }

    // A sample of the second batch (everything the closure run found portable).
    public static void MoreRules()
    {
        checks = 0;
        var heap = new MinHeap<int>(); var order = new List<float>();
        for (int i = 0; i < 200; i++) heap.Push((i * 7919) % 211, i);
        while (heap.Count > 0) order.Add(heap.Pop().key);
        Check(order.Count == 200 && order.SequenceEqual(order.OrderBy(k => k)), "the heap pops in key order");
        var buckets = new HashSet<int>();
        for (int a = 0; a < 64; a++) for (int b = 0; b < 64; b++) buckets.Add(PackedKeyComparer.Instance.GetHashCode(((long)a << 32) | (uint)b) & 0xFFFF);
        Check(buckets.Count > 3900, "packed pair keys spread over the hash space, " + buckets.Count + " of 4096 distinct");

        Check(StateDebtRules.AnnualRate(true, 100, false) < StateDebtRules.AnnualRate(false, 20, true) && StateDebtRules.AnnualRate(false, 0, true) <= 0.12f, "weak, unbanked states borrow dearer, capped at 12%");
        double carry = 0d; long interest = 0;
        for (int month = 0; month < 12; month++) interest += StateDebtRules.MonthlyInterest(1000, 0.07f, ref carry);
        Check(interest == 70 && carry < 1e-6, "a year of monthly interest adds up to the annual rate, got " + interest);
        Check(StateDebtRules.Annualize(new long[] { 1200, 0 }, 2) == 0 && StateDebtRules.Annualize(Enumerable.Repeat(10L, 12).ToList(), 12) == 120, "one windfall month is not a yearly income");

        double taxCarry = 0d; long taxed = 0;
        for (int month = 0; month < 40; month++) taxed += SectorTaxRules.Assess(33.3d, 0.1d, TreasuryCategory.LandTax, ref taxCarry);
        Check(Math.Abs(taxed + taxCarry - 133.2d) < 1e-6, "sector tax keeps its fractions, " + taxed + " + " + taxCarry);
        Check(SectorTaxRules.Rate(0.1d, TreasuryCategory.CommercialTax) > SectorTaxRules.Rate(0.1d, TreasuryCategory.LandTax) && SectorTaxRules.Rate(0d, TreasuryCategory.CommercialTax) == 0d, "trade is taxed above land; a zero rate stays zero");

        // Five electors, three candidates: a clear majority wins outright, a split goes to a run-off.
        var clear = ImperialElectionRules.Tally(new float[,] { { 9, 1, 1 }, { 8, 2, 1 }, { 7, 1, 3 }, { 1, 9, 1 }, { 1, 1, 9 } });
        Check(clear.winner == 0 && !clear.runoff && clear.winnerVotes == 3, "three of five votes elect outright");
        var split = ImperialElectionRules.Tally(new float[,] { { 9, 1, 2 }, { 8, 2, 1 }, { 1, 9, 2 }, { 1, 8, 2 }, { 1, 5, 9 } });
        Check(split.runoff && split.winner == 1 && split.winnerVotes == 3, "a split college goes to a run-off, winner " + split.winner);

        var touched = new int[1000];
        ParallelWork.For(touched.Length, i => System.Threading.Interlocked.Increment(ref touched[i]));
        Check(touched.All(n => n == 1), "parallel work covers every index exactly once");
        Console.WriteLine("PASS second batch sample (" + checks + " checks)");
    }

    // The mod code brought over member by member: the state it hangs on entities works on ours.
    public static void Generated()
    {
        checks = 0;
        var map = FlatGenerator.Generate(256, 128, "EMPIRE-GENERATED", TerrainPreset.Continents, default);
        var w = Start(map); EmpireWorld.Current = w;
        Realm realm = w.LiveRealms[0], other = w.LiveRealms[1]; City city = realm.Capital;
        var state = realm.GetOrCreate();
        Check(state != null && ReferenceEquals(state, realm.GetOrCreate()) && !ReferenceEquals(state, other.GetOrCreate()), "a realm keeps one state record of its own");
        Check(other.Capital.GetOrCreate(true) == null, "asking for saved state does not create it");
        city.GetOrCreate().Money = 1234;
        Check(city.GetTreasuryBalance() == 1234 && city.GetMoney() == 1234, "city treasury reads back through the ported accessors");
        Check(ReferenceEquals(city.GetOrCreate(true), city.GetOrCreate()), "created state is found again");
        // A second world shares nothing with the first.
        var w2 = Start(map);
        Check(w2.LiveRealms[0].Capital.GetTreasuryBalance() == 0 && !ReferenceEquals(w2.LiveRealms[0].GetOrCreate(), state), "state belongs to the entity, not to the id");
        Check(realm.Record == realm && realm.Key == realm.Id && !realm.IsGone() && realm.Ruler.IsRuler() && realm.Ruler.Realm == realm, "entity members the ported code asks for");
        var store = new RecordStore<KingdomTitle, KingdomTitleData>();
        var title = store.NewObject(); var second = store.NewObject();
        Check(title.Key == 1 && second.Key == 2 && store.Get(2) == second && title.Alive && store.Count == 2, "a record store hands out keys and finds by key");
        store.RemoveObject(title);
        Check(title.IsGone() && store.Get(1) == null && store.Count == 1 && store.NewObject().Key == 3, "a removed record is gone and its key is not reused");
        EcRandom.Seed(w.Seed, 7); float first = EcRandom.value; int pick = EcRandom.Range(0, 100);
        EcRandom.Seed(w.Seed, 7);
        Check(EcRandom.value == first && EcRandom.Range(0, 100) == pick && first >= 0f && first < 1f && pick >= 0 && pick < 100, "the random stream replays from its seed");
        EmpireWorld.Current = null;
        Console.WriteLine("PASS generated code sample (" + checks + " checks)");
    }

    // Wars: sides, leaders, leaving, ending, a fallen realm, and the save.
    public static void Wars()
    {
        checks = 0;
        var map = FlatGenerator.Generate(256, 128, "EMPIRE-WARS", TerrainPreset.Continents, default);
        var w = Start(map); w.Activate();
        Realm a = w.LiveRealms[0], b = w.LiveRealms[1], c = w.LiveRealms[2], d = w.LiveRealms[3];
        var war = w.StartWar(a, b, WarKinds.Normal);
        Check(a.IsAtWarWith(b) && b.IsEnemy(a) && a.HasEnemies() && !c.HasEnemies() && w.HasWars() && ReferenceEquals(w.StartWar(a, b), war), "a declared war is known to both sides and not declared twice");
        war.JoinAttackers(c); war.JoinDefenders(d); war.JoinDefenders(d);
        Check(c.IsAtWarWith(b) && c.IsAtWarWith(d) && a.IsOnSameSide(c) && !a.IsAtWarWith(c) && a.GetEnemies().Count == 2 && war.IsAttacker(c) && war.HasRealm(d) && war.Defenders.Count == 2, "allies join a side once");
        Consistent(w, "at war");
        var bytes = EmpireSave.Encode(w); var loaded = EmpireSave.Decode(bytes, w.Map);
        Check(EmpireSave.Encode(loaded).SequenceEqual(bytes) && loaded.LiveWars.Count == 1 && loaded.LiveRealms[0].IsAtWarWith(loaded.LiveRealms[1]) && loaded.LiveRealms[2].GetEnemies().Count == 2, "a running war survives the save");
        Check(EmpireSave.Decode(EmpireSave.Encode(w, 2), w.Map).Wars.Count == 0, "a version-2 section loads without wars");
        war.Leave(a);
        Check(war.MainAttacker == c && !a.HasEnemies() && war.Alive && war.PastAttackers.Contains(a), "the lead passes on when the leader leaves");
        war.Leave(c);
        Check(war.HasEnded() && war.Outcome == WarOutcome.Defenders && !b.HasEnemies() && !w.HasWars() && w.WarByKey(war.Key) == null, "a side left empty has lost");
        Consistent(w, "after the war");
        var second = w.StartWar(a, b, WarKinds.Rebellion);
        Check(second != war && second.IsTotalWar() && second.Id == 1, "old enemies can fight a new war");
        w.EndWar(second, WarOutcome.Peace);
        Check(!a.IsAtWarWith(b) && second.Outcome == WarOutcome.Peace && second.DurationMonths() == 0, "peace ends the war for everyone");
        var third = w.StartWar(d, c);
        foreach (var city in d.Cities.ToArray()) WorldOps.TransferCity(w, city, a);
        Check(!d.Alive && third.HasEnded() && third.Outcome == WarOutcome.Defenders && !c.HasEnemies(), "a fallen realm leaves its wars");
        Consistent(w, "after a realm fell");
        Run(w, 5, x => Consistent(x, "year " + x.Year));
        Check(w.Annals.Entries.Count(e => e.Kind == AnnalKind.WarStarted) == 3 && w.Annals.Entries.Count(e => e.Kind == AnnalKind.WarEnded) == 3, "the annals record every war");
        EmpireWorld.Current = null;
        Console.WriteLine("PASS wars (" + checks + " checks)");
    }

    // What a person carries beyond the family record, and that it is saved.
    public static void People()
    {
        checks = 0;
        var map = FlatGenerator.Generate(256, 128, "EMPIRE-PEOPLE", TerrainPreset.Continents, default);
        var w = Start(map); w.Activate();
        Person ruler = w.LiveRealms[0].Ruler, other = w.LiveRealms[1].Ruler;
        Check(ruler.Renown == 0 && ruler.TraitCount == 0 && !ruler.HasTrait("wise") && ruler.Stats["warfare"] == 0f, "people start without standing, traits or talents");
        ruler.AddRenown(40); ruler.AddRenown(-100); ruler.AddRenown(25); ruler.AddMoney(300); ruler.AddMoney(-50);
        ruler.Stewardship = 7; ruler.Warfare = 3; ruler.Intelligence = 9; ruler.Diplomacy = 5;
        Check(ruler.Renown == 25 && ruler.Money == 250 && ruler.Stats["intelligence"] == 9f && ruler.Stats["nonsense"] == 0f, "renown does not go below zero; talents read by name");
        Check(ruler.AddTrait("wise") && !ruler.AddTrait("wise") && ruler.AddTrait("frugal") && ruler.HasTrait("wise") && ruler.RemoveTrait("frugal") && !ruler.HasTrait("frugal") && ruler.TraitCount == 1 && !other.HasTrait("wise"), "traits are a set of ids on the person");
        Check(ruler.IsMale() && !ruler.IsFemale() && ruler.IsFitToRule() && ruler.HasRealm() && !ruler.IsWarrior() && ruler.GetParents().Count() == 0 && ruler.GetChildren().All(c => c.GetParents().Contains(ruler)), "what the ported code asks about a person");
        var bytes = EmpireSave.Encode(w); var loaded = EmpireSave.Decode(bytes, w.Map);
        var again = loaded.People[ruler.Id];
        Check(EmpireSave.Encode(loaded).SequenceEqual(bytes) && again.Renown == 25 && again.Money == 250 && again.Intelligence == 9 && again.HasTrait("wise") && again.TraitCount == 1, "person attributes survive the save");
        Check(EmpireSave.Decode(EmpireSave.Encode(w, 3), w.Map).People[ruler.Id].Renown == 0, "a version-3 section loads people without attributes");
        var child = ruler.Children.FirstOrDefault();
        if (child != null) { child.JoinCity(other.City); Check(child.City == other.City && child.Realm == other.Realm, "a person who moves belongs to the new city's realm"); }
        EmpireWorld.Current = null;
        Console.WriteLine("PASS people (" + checks + " checks)");
    }

    // The population is a number; only persons on record are entities. Faiths and cultures are records held by realms, cities and persons.
    public static void Beliefs()
    {
        checks = 0;
        var map = FlatGenerator.Generate(256, 128, "EMPIRE-BELIEFS", TerrainPreset.Continents, default);
        var w = Start(map); w.Activate();
        Run(w, 2);
        var realm = w.LiveRealms[0]; var other = w.LiveRealms[1]; var capital = realm.Capital; var ruler = realm.Ruler;
        Check(capital.People.Contains(ruler) && capital.People.All(p => p.Alive && p.City == capital) && capital.PopulationCount() > capital.People.Count, "a city's people on record are a handful; its population is a number");
        Check(realm.People.Contains(ruler) && realm.People.All(p => p.Realm == realm) && !realm.People.Intersect(other.People).Any() && realm.PopulationCount() == (int)realm.Cities.Sum(c => c.Population), "a realm's headcount is the sum of its cities");
        Check(realm.Warriors() == realm.Cities.Sum(c => w.Ledger.City(c.Id).Garrison) && w.LiveRealms.Sum(r => (long)r.Warriors()) > 0 && capital.Warriors() == w.Ledger.City(capital.Id).Garrison, "troops are the guards on the books");
        Check(ruler.Cell == capital.SeatCell() && new Person().Cell == null, "a person is at the seat of the home city");
        var heir = capital.People.FirstOrDefault(p => p != ruler);
        if (heir != null) { WorldOps.Death(w, heir); Check(!capital.People.Contains(heir) && !realm.People.Contains(heir), "the dead leave the record of the living"); }

        Check(w.Religions.Count == 0 && !realm.HasReligion() && !capital.HasReligion() && !ruler.HasReligion() && !realm.HasCulture(), "nobody holds a faith or a culture until a system gives one");
        var faith = w.Religions.NewReligion(ruler); faith.Name = "Sun";
        Check(faith.Key == 1 && faith.Alive && faith.data.creator_id == ruler.Key && faith.data.creator_kingdom_id == realm.Key && faith.Age() == 0 && !ruler.HasReligion() && w.Religions.Get(1) == faith, "a new faith is on record and followed by nobody");
        realm.SetReligion(faith); capital.SetReligion(faith); ruler.SetReligion(faith);
        Check(realm.HasReligion() && faith.Realms.SequenceEqual(new[] { realm }) && faith.Cities.SequenceEqual(new[] { capital }) && faith.Followers() == (long)capital.Population && !other.HasReligion(), "a faith's following is the headcount of the cities that hold it");
        var sect = w.Religions.NewReligion(other.Ruler); sect.Name = "Moon"; other.Capital.SetReligion(sect);
        w.Religions.RemoveObject(sect);
        Check(sect.Key == 2 && !sect.Alive && sect.IsGone() && !other.Capital.HasReligion() && w.Religions.Get(2) == null && w.Religions.Count == 1, "a faith struck from the record is held by nobody");
        var folk = w.Cultures.NewCulture(ruler); folk.Name = "River"; realm.SetCulture(folk); capital.SetCulture(folk);
        Check(folk.AddTrait("farming") && !folk.AddTrait("farming") && folk.AddTrait("bronze") && folk.HasTrait("farming") && folk.RemoveTrait("bronze") && folk.TraitCount == 1 && folk.Cities.Count == 1 && realm.HasCulture() && capital.Culture == folk, "a culture carries traits and is held like a faith");
        Run(w, 3);
        Check(faith.Age() == 3 && EmpireInvariants.Check(w).Count == 0, "a faith ages with the world");
        Check(GameClock.MonthOf(0) == 1 && GameClock.MonthOf(23.5) == 12 && GameClock.YearOf(24) == w.StartYear + 2 && GameClock.DateText(13) == (w.StartYear + 1) + "年2月" && GameClock.Now + GameClock.MonthLength == w.Month + 1, "a timestamp is a world month");

        var bytes = EmpireSave.Encode(w); var loaded = EmpireSave.Decode(bytes, w.Map);
        var again = loaded.Realms[realm.Id];
        Check(EmpireSave.Encode(loaded).SequenceEqual(bytes) && again.Religion != null && again.Religion.Name == "Sun" && again.Religion.Key == 1 && again.Capital.Religion == again.Religion && loaded.People[ruler.Id].Religion == again.Religion &&
              again.Culture.Name == "River" && again.Culture.HasTrait("farming") && again.Culture.TraitCount == 1 && again.World == loaded && again.Religion.World == loaded, "faiths, cultures and their holders survive the save");
        Check(loaded.Cities[other.Capital.Id].Religion == null && loaded.Religions.Count == 1 && loaded.Religions.NewReligion(null).Key == 3, "a struck faith is not saved and its key is not handed out again");
        var old = EmpireSave.Decode(EmpireSave.Encode(w, 4), w.Map);
        Check(old.Religions.Count == 0 && old.Cultures.Count == 0 && !old.Realms[realm.Id].HasReligion() && old.Realms[realm.Id].World == old, "a version-4 section loads without faiths");
        Cultures.SetFamily(folk, "Huaxia");
        var families = EmpireSave.Decode(EmpireSave.Encode(w), w.Map);
        Check(Cultures.FamilyOf(folk) == "Huaxia" && Cultures.FamilyOf(null) == "" && Cultures.FamilyOf(families.Realms[realm.Id].Culture) == "Huaxia" &&
              Cultures.FamilyOf(EmpireSave.Decode(EmpireSave.Encode(w, 5), w.Map).Realms[realm.Id].Culture) == "" && folk.GetOnomasticData(EntityKind.Realm) == null, "a culture's family of rules is saved from version 6");
        EmpireWorld.Current = null;
        Console.WriteLine("PASS population and beliefs (" + checks + " checks)");
    }

    // Small things the entry points of the ported systems ask the host: world laws, the loading flag, chance.
    public static void HostSwitches()
    {
        checks = 0;
        WorldLaws.Reset();
        Check(WorldLaws.All.Count >= 30 && WorldLaws.Get("ban_empire") == WorldLaws.BanEmpire && WorldLaws.Get("nonsense") == null && WorldLaws.All.Select(l => l.Id).Distinct().Count() == WorldLaws.All.Count, "world laws are found by id, each once");
        Check(WorldLaws.Rebellions.IsEnabled() && WorldLaws.Diplomacy.IsEnabled() && WorldLaws.CivArmy.IsEnabled() && !WorldLaws.BanEmpire.IsEnabled() && !WorldLaws.BanAiWar.IsEnabled(), "the defaults: the world plays, nothing is banned");
        WorldLaws.BanEmpire.Toggle(true); bool on = WorldLaws.BanEmpire.IsEnabled(); WorldLaws.Reset();
        Check(on && !WorldLaws.BanEmpire.IsEnabled() && WorldLaws.All.All(l => l.Enabled == l.Default), "a law is switched and reset");
        Check(!GameClock.Loading, "no world is loading");
        EcRandom.Seed(42, 1); int hits = 0; for (int i = 0; i < 4000; i++) if (EcRandom.Chance(0.25f)) hits++;
        Check(hits > 850 && hits < 1150 && !EcRandom.Chance(0f) && EcRandom.Chance(1f), "chance follows its probability");
        Check(Near(EcMath.Sin(EcMath.PI / 2f), 1.0, 1e-5) && Near(EcMath.Cos(0f), 1.0) && Near(EcMath.Log10(1000f), 3.0, 1e-5) && Near(EcMath.Atan2(1f, 1f) * EcMath.Rad2Deg, 45.0, 1e-4), "the maths the ported code asks for");
        var map = FlatGenerator.Generate(256, 128, "EMPIRE-HOST", TerrainPreset.Continents, default);
        var w = Start(map); w.Activate();
        var realm = w.LiveRealms[0]; var capital = realm.Capital;
        Check(realm.CountCities() == realm.Cities.Count && realm.HasCities() && realm.CountZones() == realm.Cities.Sum(c => c.Tiles.Count) && capital.CountZones() == capital.Tiles.Count && Land.ZoneSize == 1, "a realm counts its cities and cells");
        capital.SetLeader(realm.Ruler); bool led = capital.HasLeader() && realm.Ruler.IsCityLeader(); capital.RemoveLeader();
        Check(led && !capital.HasLeader() && !realm.Ruler.IsCityLeader(), "a city's leader is set and removed");
        Check(realm.KindOfEntity == EntityKind.Realm && capital.KindOfEntity == EntityKind.City && realm.Ruler.KindOfEntity == EntityKind.Person && realm.RulingClan.KindOfEntity == EntityKind.Clan &&
              realm.GetSpecies() == "human" && capital.GetSpecies() == "human" && capital.HasRealm() && realm.Ruler.Asset.Id == "human" && !realm.Ruler.Asset.IsBoat && realm.Ruler.Asset.Civ &&
              !realm.Ruler.HasArmy() && !realm.Ruler.IsArmyCaptain && realm.Ruler.Job == null && realm.GetRulingClan() == realm.RulingClan, "what kind of thing an entity is; one people; no armies of persons");
        var host = new Army { Id = 1, City = capital, Captain = realm.Ruler, Soldiers = 300 }; realm.Ruler.Army = host;
        var seatCell = capital.SeatCell();
        Check(host.Alive && host.GetRealm() == realm && host.People.Count == 1 && realm.Ruler.IsArmyCaptain && host.KindOfEntity == EntityKind.Army, "an army is a number of soldiers and a captain");
        realm.Ruler.Army = null;
        TraitKinds.Clear(); TraitKinds.Set("wise", TraitType.Positive); TraitKinds.Set("cruel", TraitType.Negative);
        Check("wise".TraitKindOf() == TraitType.Positive && "cruel".TraitKindOf() == TraitType.Negative && "tall".TraitKindOf() == TraitType.Other, "a trait counts for, against or neither way");
        TraitKinds.Clear();
        Check(w.Books.Count == 0 && w.Plots.Count == 0 && w.Books.GenerateNewBook(realm.Ruler) == null && !w.Plots.NewObject().IsActive() == false && w.Plots.Count == 1 &&
              seatCell.Building == null && new Building { Id = 7 }.Alive && new Building { Removed = true }.IsGone(), "books and plots are stores that start empty; nothing is built as an object");
        Check(capital.GetPopulationMaximum() > 150 && capital.GetPopulationMaximum() == (int)SkeletonRules.Capacity(w, capital) && new[] { 3, 5, 7 }.GetRandom() >= 3 && new List<int>().GetRandom() == 0 &&
              new Building().Books == null && !WarBoxCompatibility.Loaded, "a city's capacity; a pick from a list; no bookshelves; no other mod");
        Check(capital.Buildings.Count == 0 && !capital.HasBuildingType("type_docks") && capital.GetBuildingWithBookSlot() == null && BuildingKinds.Table.Get("house") == null &&
              GameOptions.All["language"].StringVal == LM.Language && GameOptions.Get("nonsense") == null, "no building objects, no table of kinds; the language option follows the loaded texts");
        var other = w.LiveRealms[1]; var subject = capital.People.FirstOrDefault(p => p != realm.Ruler);
        if (subject != null)
        {
            subject.SetRealm(other); realm.Ruler.SetRealm(other);
            Check(subject.Realm == other && subject.City == other.Capital && realm.Ruler.Realm == realm, "a subject goes over to another realm; a reigning ruler does not");
            int realms = w.LiveRealms.Count; var seat = other.Capital; int annals = w.Annals.Entries.Count;
            Check(seat.MakeOwnRealm(other.Ruler) == null && w.LiveRealms.Count == realms, "a reigning ruler founds no second realm");
            var risen = seat.MakeOwnRealm(subject, rebellion: true);
            Check(risen != null && risen.Ruler == subject && risen.Capital == seat && seat.Realm == risen && risen.Name == seat.Name && subject.Throne == risen && risen.World == w &&
                  w.Annals.Entries.Skip(annals).Any(e => e.Kind == AnnalKind.Secession) && EmpireInvariants.Check(w).Count == 0, "a city stands up as its own realm");
            subject.Die(true, DeathCause.Weapon, false, false); subject.Die();
            Check(!subject.Alive && risen.Ruler == null && risen.PreviousRuler == subject && subject.DeathMonth == w.Month, "a person dies by a system's hand, once");
            EmpireSimulation.Month(w);
            Check(EmpireInvariants.Check(w).Count == 0 && (risen.Ruler != null || !risen.Alive), "the vacant throne is settled within the month");
        }
        EmpireWorld.Current = null;
        Console.WriteLine("PASS host switches (" + checks + " checks)");
    }

    // The first yearly entry point of a ported system that compiles whole: a dissolute ruler's palace
    // spends part of the realm's yearly income. Run here by hand on the mod's own books; the
    // simulation does not call it yet (nothing forms empires or grades rulers).
    public static void FirstEntry()
    {
        checks = 0;
        var map = FlatGenerator.Generate(256, 128, "EMPIRE-ENTRY", TerrainPreset.Continents, default);
        var w = Start(map); w.Activate();
        var realm = w.LiveRealms[0]; var ruler = realm.Ruler;
        var empire = w.Empires.NewObject(); empire.CoreKingdom = realm;
        Check(empire.Emperor == ruler && w.Empires.Get(empire.Key) == empire, "an empire on record with its core realm and emperor");
        for (int month = 0; month < 12; month++) { EmpireSimulation.Month(w); realm.AddMoney(1000, TreasuryCategory.LandTax); }
        long income = StateDebtSystem.AnnualStableIncome(realm); int before = realm.GetMoney();
        Check(before == 12000 && income > 0, "a year of land tax on the realm's own books");
        Check(RulerQualitySystem.Of(ruler) == RulerQualitySystem.Grade.Average, "an ungraded ruler is average");
        RulerQualitySystem.Yearly(empire);
        Check(realm.GetMoney() == before, "an average ruler's palace costs nothing extra");
        ruler.AddTrait("dissoluteRuler");
        Check(RulerQualitySystem.Of(ruler) == RulerQualitySystem.Grade.Dissolute && RulerQualitySystem.IsBad(ruler) && RulerQualitySystem.Indulgent(ruler), "a trait makes the grade");
        RulerQualitySystem.Yearly(empire);
        int cost = before - realm.GetMoney();
        Check(cost > 0 && cost == (int)Math.Min(before, (long)(income * 0.15f)), "a dissolute ruler's palace spends 15% of the yearly income");
        var report = TreasurySystem.Report(realm);
        Check(report != null && report.months > 0, "the spending is on the books");
        ReignRecordSystem.OnYear(empire);
        EmpireWorld.Current = null;
        Console.WriteLine("PASS first live entry (" + checks + " checks): income " + income + ", palace " + cost);
    }

    // World news: a kind's template filled with the message's texts, how loud it is, and the world's log.
    public static void NewsChecks()
    {
        checks = 0;
        LM.Clear(); LM.LoadFile(Path.Combine(DataFolder(), "Locales", "ch.json")); LM.Language = "ch";
        Check(NewsKinds.All.Count >= 90 && NewsKinds.Get(WorldNews.GenericKind) == NewsKinds.empirecraft_event_log && NewsKinds.Get("nonsense") == null &&
              NewsKinds.All.Select(k => k.Id).Distinct().Count() == NewsKinds.All.Count, "news kinds are found by id, each once");
        int withText = NewsKinds.All.Count(k => LM.Has(k.Id));
        Check(withText >= NewsKinds.All.Count * 9 / 10, "nearly every kind has a template in the language file (" + withText + "/" + NewsKinds.All.Count + ")");
        Check(NewsKinds.become_new_empire_log.Major && NewsKinds.restore_historcial_empire.Major && NewsKinds.empire_crashed == null && !NewsKinds.change_city_name_log.Major && !NewsKinds.empirecraft_event_log.Major, "the big kinds are marked");
        var map = FlatGenerator.Generate(256, 128, "EMPIRE-NEWS", TerrainPreset.Continents, default);
        var w = Start(map); w.Activate();
        var realm = w.LiveRealms[0];
        var crowned = new News(NewsKinds.become_new_empire_log, "甲", "乙") { Realm = realm, Color1 = realm.GetColor().ColorText };
        string line = crowned.Text();
        Check(line.Contains("甲") && line.Contains("乙") && !line.Contains("$") && crowned.KindId == "become_new_empire_log" && crowned.GetKind().LocaleId() == "become_new_empire_log", "a template is filled with the message's texts: " + line);
        crowned.Add();
        var top = w.News.Items[w.News.Items.Count - 1];
        Check(w.News.Items.Count == 1 && top.Text == line && top.Level == NewsLevel.Major && top.Public && top.OnScreen && top.RealmId == realm.Id && top.Month == w.Month && top.Group == "emperors", "a major kind is heard everywhere");
        new News(NewsKinds.change_city_name_log, "甲", "旧", "新").Add();
        top = w.News.Items[w.News.Items.Count - 1];
        Check(top.Level == NewsLevel.History && !top.Public && !top.OnScreen && top.RealmId == -1, "an everyday kind goes on the log quietly");
        WorldNews.Pending = NewsLevel.Minor; WorldNews.PendingRealm = realm;
        new News(NewsKinds.empirecraft_event_log, "市面缺粮").Add();
        top = w.News.Items[w.News.Items.Count - 1];
        Check(top.Text == "市面缺粮" && top.Level == NewsLevel.Minor && !top.Public && !top.OnScreen && top.RealmId == realm.Id, "free text is as loud as the poster says");
        WorldNews.Pending = NewsLevel.History;
        bool small = WorldNews.OnScreen(new News(NewsKinds.empirecraft_event_log, "某事"));
        var keep = WorldNews.MajorRealm; WorldNews.MajorRealm = r => r == realm;
        bool big = WorldNews.OnScreen(new News(NewsKinds.empirecraft_event_log, "某事"));
        WorldNews.MajorRealm = keep;
        WorldNews.Pending = NewsLevel.Major; bool heard = WorldNews.IsPublic(new News(NewsKinds.empirecraft_event_log, "大事"));
        WorldNews.Pending = NewsLevel.None; WorldNews.PendingRealm = null;
        Check(!small && big && heard, "a realm's history flashes on screen only for a great realm; major free text is public");
        new News(NewsKinds.empirecraft_event_log, " ").Add(); new News(null, "x").Add();
        Check(w.News.Items.Count == 3, "empty lines are not posted");
        int heardBy = 0; Action<News, NewsItem> listener = (message, item) => heardBy++; WorldNews.Posted += listener;
        WorldNews.Pending = NewsLevel.Minor;
        for (int i = 0; i < 600; i++) new News(NewsKinds.empirecraft_event_log, "第" + i + "件").Add();
        WorldNews.Pending = NewsLevel.None; WorldNews.Posted -= listener;
        Check(w.News.Items.Count == NewsLog.Cap && w.News.Posted == 603 && heardBy == 600 && w.News.Items[NewsLog.Cap - 1].Text == "第599件" && w.News.Items[0].Text == "第88件", "the log keeps the last lines only");
        long posted = w.News.Posted;
        TranslateHelper.LogEventOnly("转运使到任", realm); TranslateHelper.LogEventOnly("转运使到任", realm); TranslateHelper.LogMajor("天下大定", realm);
        var last = w.News.Items[w.News.Items.Count - 1]; var prior = w.News.Items[w.News.Items.Count - 2];
        Check(w.News.Posted == posted + 2 && prior.Text == "转运使到任" && prior.Level == NewsLevel.Minor && prior.RealmId == realm.Id && !prior.OnScreen &&
              last.Text == "天下大定" && last.Level == NewsLevel.Major && last.Public && last.OnScreen && WorldNews.Pending == NewsLevel.None && WorldNews.PendingRealm == null,
              "the ported helpers post through the same door; a line repeated within three years is posted once");
        TranslateHelper.LogChangeCityName(realm.Ruler, realm.Capital, "旧名", "新名");
        last = w.News.Items[w.News.Items.Count - 1];
        Check(w.News.Posted == posted + 3 && last.KindId == "change_city_name_log" && last.Text.Contains("旧名") && last.Text.Contains("新名") && !last.Text.Contains("$") && last.Level == NewsLevel.History, "a ported announcement fills its own template: " + last.Text);
        var tint = realm.GetColor();
        Check(tint.Id == "#" + realm.Color.ToString("X6") && tint.Main.Rgb == realm.Color && realm.Capital.GetColor() == tint && realm.Ruler.GetColor() == tint && ReferenceEquals(realm.GetColor(), tint) &&
              Tint.FromHex("#8a8a80").Id == "#8A8A80" && Tint.FromHex("not a colour") == null, "a realm's colours come from its map colour");
        EmpireWorld.Current = null;
        Console.WriteLine("PASS world news (" + checks + " checks): " + NewsKinds.All.Count + " kinds, " + NewsKinds.All.Count(k => k.Major) + " major");
    }

    // Making a person: the population is a number, so "one particular person out of a population group"
    // is a person on record (the hand-written half of CityPopulationSystem), and what the ported code then does to them.
    public static void MakingPeople()
    {
        checks = 0;
        var map = FlatGenerator.Generate(256, 128, "EMPIRE-MAKING", TerrainPreset.Continents, default);
        var w = Start(map); w.Activate();
        var realm = w.LiveRealms[0]; var city = realm.Capital;
        var folk = w.Cultures.NewCulture(realm.Ruler); Cultures.SetFamily(folk, "Huaxia");
        var faith = w.Religions.NewReligion(realm.Ruler); city.SetReligion(faith);
        var group = new PopGroup { culture = "Huaxia", species = "human", social_class = SocialClass.Peasant };
        double headcount = city.Population; int onRecord = w.People.Count, houses = w.Clans.Count;
        Check(CityPopulationSystem.SpawnFromGroup(city, group, soldier: true) == null && w.People.Count == onRecord, "a soldier is a number, not a person");
        var made = CityPopulationSystem.SpawnFromGroup(city, group, soldier: false);
        Check(made != null && made.Alive && made.City == city && made.Realm == realm && made.IsAdult() && made.Age() >= 25 && made.Age() <= 55 && made.HasClan() && made.Clan.Founder == made &&
              w.Clans.Count == houses + 1 && w.People.Count == onRecord + 1 && city.People.Contains(made) && city.Population == headcount &&
              made.Religion == faith && made.Culture == folk && made.Name.StartsWith(made.Clan.Surname) && EmpireInvariants.Check(w).Count == 0,
              "a person on record: adult, of a house of their own, of the city's faith and the group's culture");
        Check(CityPopulationSystem.SpawnFromGroup(null, group, false) == null && CityPopulationSystem.SpawnFromGroup(city, null, false) == null, "nobody without a city or a group");
        int age = made.Age(); made.AgeOvergrowth += 40 - made.Age();
        Check(made.Age() == 40 && made.AgeOvergrowth == 0 && made.BirthStamp == made.BirthMonth && age != 40 || age == 40, "years added to an age move the birth back");
        made.BirthStamp = w.Month - 30 * 12 + 0.5;
        Check(made.Age() == 30, "a birth stamp is written in months");
        string house = made.Clan.Surname; made.Name = house + "安"; string kept = made.Given; made.Name = "独"; made.Clan.Name = "新";
        Check(kept == "安" && made.Given == "独" && made.FullName() == "新独" && made.Clan.Surname == "新", "a full name keeps the house name; a house is renamed");
        var parent = realm.Ruler; made.ParentKey1 = parent.Key;
        Check(made.Father == parent && parent.Children.Contains(made) && parent.IsParentOf(made) && made.ParentKey1 == parent.Key && made.ParentKey2 == -1L, "a parent set by key is linked both ways");
        made.ParentKey1 = -1L;
        Check(made.Father == null && !parent.Children.Contains(made) && !parent.IsParentOf(made), "and unlinked");
        var own = w.NewClan(made); made.SetClan(parent.Clan); bool joined = made.Clan == parent.Clan; made.SetClan(own);
        Check(own != null && own.Founder == made && joined && made.Clan == own && !made.HasFamily() && made.Family == null && city.SeatCell().HasCity() && EmpireInvariants.Check(w).Count == 0, "a new house, a change of house, no household records");
        Check(made.GetHealth() == made.GetMaxHealth() && made.Loot == 0 && !made.IsFavorite() && made.Position.x == city.SeatCell().X && realm.Location.x == city.Center.x && realm.GetCities() == realm.Cities &&
              own.People.Contains(made) && own.SpeciesId == "human" && Near(EcMath.Log(8f, 2f), 3.0, 1e-5) && EcColorUtility.ToHtmlStringRGBA(new EcColor(1f, 0f, 0f, 1f)) == "FF0000FF", "a few more questions the ported code asks");
        // Through the ported code: its "a person of this people and culture" and "a civilian" both end in the hand-written half.
        int before = w.People.Count;
        var named = CityPopulationSystem.SpawnPerson(city, "human", "Huaxia"); var civilian = CityPopulationSystem.SpawnCivilian(city);
        VirtualGenealogySystem.SetAge(named, 44);
        Check(named != null && named.City == city && named.Culture == folk && named.Age() == 44 && w.People.Count >= before + 1 && (civilian == null || civilian.City == city) &&
              CityPopulationSystem.SpawnPerson(city, "", "Huaxia") == null && EmpireInvariants.Check(w).Count == 0, "the ported callers make persons on record through it" + (civilian == null ? " (no civilian made)" : ""));
        var bytes = EmpireSave.Encode(w); var loaded = EmpireSave.Decode(bytes, w.Map);
        Check(EmpireSave.Encode(loaded).SequenceEqual(bytes) && loaded.People[made.Id].FullName() == made.FullName() && loaded.People[made.Id].Culture.Family == "Huaxia", "a made person is saved like any other");
        EmpireWorld.Current = null;
        Console.WriteLine("PASS making people (" + checks + " checks): " + made.FullName() + ", " + made.Age());
    }

    // The grid as the ported systems see it: cells, terrain, a city's territory and neighbours.
    public static void Grid()
    {
        checks = 0;
        var map = FlatGenerator.Generate(256, 128, "EMPIRE-GRID", TerrainPreset.Continents, default);
        var w = Start(map); w.Activate();
        Check(w.Width == 256 && w.Height == 128 && w.Zones.Count == 256 * 128 && w.Zones.Get(3, 5).Index == 5 * 256 + 3 && w.Zones.ById(7).X == 7 && w.CellAt(-1) == null && w.CellAt(0, 128) == null, "cells by coordinate and index");
        var west = w.CellAt(0, 40); var east = w.CellAt(255, 40);
        Check(Land.SquaredDist(west, east) == 1 && Land.SquaredDist(w.CellAt(10, 10), w.CellAt(13, 14)) == 25 && Land.Dist(w.CellAt(10, 10), w.CellAt(13, 14)) == 5f &&
              Land.SquaredDist(w.CellAt(0, 0), w.CellAt(128, 0)) == 128 * 128 && Land.SquaredDist(west, null) == int.MaxValue, "cell distances go the short way round");
        Check(ReferenceEquals(w.CellAt(256, 40), west) && west.Left == east && east.Right == west && west.Neighbours.Count == 4 && west.NeighboursAll.Count == 8 && w.CellAt(10, 0).Neighbours.Count == 3 && w.CellAt(10, 0).OnEdge && !west.OnEdge, "the grid wraps east-west and ends at the polar rows");
        Check(ReferenceEquals(west.Zone, west) && ReferenceEquals(west.Center, west) && west.Tiles.Count == 1 && west.Pos.x == 0 && west.PosV.y == 40f, "a cell is its own zone, centre and tile");
        Check(w.Zones.All.Count == w.Zones.Count && ReferenceEquals(w.Zones.All[41], w.CellAt(41)) && w.AllCells.Length == w.Zones.Count && w.Zones.Lookup.TryGetValue(41, out Cell found) && found == w.CellAt(41) &&
              !w.Zones.Lookup.TryGetValue(-1, out found) && found == null, "all cells as a list, an array, and by number");
        Check(w.Zones.All.All(c => c.GroundCount + c.LiquidCount == Land.TilesPerZone && (c.IsWater ? c.GroundCount == 0 : c.GroundCount > 0)) &&
              w.Zones.All.Where(c => !c.Type.Liquid).All(c => c.Type.BiomeId.StartsWith("biome_") && c.Type.BiomeAsset != null && c.Type.BiomeAsset.TileHigh != c.Type.Id) &&
              w.Zones.All.Select(c => c.Type).Distinct().Select(t => t.Index).Distinct().Count() == w.Zones.All.Select(c => c.Type).Distinct().Count(), "ground and water are counted in the mod's small tiles; every kind of land has a biome id and a number of its own");
        int sea = Enumerable.Range(0, map.Count).First(i => map.Water[i] == WaterKind.DeepOcean);
        var city = w.LiveRealms[0].Capital; var seat = city.SeatCell();
        Check(w.CellAt(sea).Type.Liquid && w.CellAt(sea).Type.Ocean && !w.CellAt(sea).IsLand && w.CellAt(sea).City == null && !w.CellAt(sea).IsSameIsland(seat), "the sea is water, belongs to no city and to no landmass");
        Check(seat.Type.Ground && !seat.Type.Liquid && seat.IsLand && seat.City == city && seat.Realm == city.Realm && seat.IsSameIsland(seat) && !seat.CanSettle && seat.Type.HeightMin <= seat.Height, "a city's seat is its own land");
        Check(city.Zones.Count == city.Tiles.Count && city.Zones[0] == seat && city.Zones.All(c => c.City == city) && city.Center.x == seat.X, "a city's zones are the cells of its tiles");
        foreach (var other in w.Cities)
            foreach (var near in other.NeighbourCities)
                Check(near != other && near.NeighbourCities.Contains(other), "neighbouring is mutual");
        Check(w.Cities.All(c => c.NeighbourRealms.All(r => r != c.Realm && r.Alive)), "neighbouring realms are foreign and alive");
        var free = city.Zones.SelectMany(c => c.Neighbours).FirstOrDefault(c => c.City == null && c.IsLand);
        if (free != null)
        {
            int before = city.Tiles.Count;
            city.AddZone(free); city.AddZone(free);
            Check(city.Tiles.Count == before + 1 && free.City == city && city.Zones.Contains(free), "a city takes a free cell next to its land once");
            city.RemoveZone(free); city.RemoveZone(seat);
            Check(city.Tiles.Count == before && free.City == null && seat.City == city && !city.Zones.Contains(free), "a city gives a cell back but never its seat");
        }
        Consistent(w, "after territory changes");
        var loaded = EmpireSave.Decode(EmpireSave.Encode(w), w.Map);
        Check(loaded.Cities[city.Id].SeatCell().City == loaded.Cities[city.Id] && loaded.Cities[city.Id].Zones.Count == city.Tiles.Count, "a loaded city knows its world");
        EmpireWorld.Current = null;
        Console.WriteLine("PASS grid (" + checks + " checks)");
    }

    // The shared frame budget: jobs run once a frame while there is budget, are put
    // off when there is none, and are never starved.
    public static void Scheduling()
    {
        checks = 0;
        FrameClock.Reset(); FrameJobs.Reset(); SimulationFrameBudget.Reset();
        int ran = 0;
        for (int frame = 0; frame < 60; frame++)
        {
            FrameClock.Advance(1f / 60f);
            Check(FrameJobs.Run("check.light", FrameJobs.Priority.Normal, () => ran++), "an affordable job runs every frame");
            Check(!FrameJobs.Run("check.light", FrameJobs.Priority.Normal, () => ran++), "a job runs once a frame");
        }
        Check(ran == 60 && SimulationFrameBudget.BudgetMilliseconds >= 1d && SimulationFrameBudget.BudgetMilliseconds <= 8d, "budget " + SimulationFrameBudget.BudgetMilliseconds);
        int low = 0, longestGap = 0, gap = 0;
        for (int frame = 0; frame < 300; frame++)
        {
            FrameClock.Advance(1f / 60f);
            // A greedy high-priority job spends the whole frame budget first.
            FrameJobs.Run("check.greedy", FrameJobs.Priority.High, () => { var clock = System.Diagnostics.Stopwatch.StartNew(); while (clock.Elapsed.TotalMilliseconds < SimulationFrameBudget.BudgetMilliseconds + 0.5d) { } });
            if (FrameJobs.Run("check.low", FrameJobs.Priority.Low, () => low++)) { longestGap = Math.Max(longestGap, gap); gap = 0; } else gap++;
        }
        Check(low >= 3 && low < 300 && longestGap <= 90, "a starved low job is forced through within 90 frames: ran " + low + ", longest wait " + longestGap);
        Check(FrameJobs.BuildReport() != null, "deferrals are reported");
        FrameClock.Reset(); FrameJobs.Reset(); SimulationFrameBudget.Reset();
        Console.WriteLine("PASS frame budget (" + checks + " checks; low job ran " + low + "/300 frames under a greedy job, longest wait " + longestGap + " frames)");
    }

    public static void Data()
    {
        checks = 0;
        GameData.Folder = DataFolder();
        var lines = new List<string>(); int warnings = LogService.Warnings, errors = LogService.Errors;
        LogService.Sink = (level, message) => { if (level != LogLevel.Info) lines.Add(level + ": " + message); };
        InstitutionDefinitionRegistry.Load();
        int nodes = InstitutionDefinitionRegistry.All.Count();
        var trees = InstitutionDefinitionRegistry.Lines;
        Check(LogService.Errors == errors, "institution data must load without errors: " + string.Join(" | ", lines.Take(3)));
        Check(trees.Count >= 7 && nodes > 300, "institution trees: " + trees.Count + " lines, " + nodes + " nodes");
        foreach (string line in trees)
        {
            Check(InstitutionDefinitionRegistry.GetRootNodes(line).Count > 0, line + " has no root node");
            Check(InstitutionDefinitionRegistry.TryGetRootRegime(line, out _), line + " root declares no regime");
            foreach (var node in InstitutionDefinitionRegistry.GetForLine(line))
                Check(InstitutionDefinitionRegistry.ArePrerequisitesMet(node, id => true), node.id + " has unreachable prerequisites");
        }
        Check(InstitutionDefinitionRegistry.GetNodesWithFeature(PartySystem.FeaturePartyPolitics).Any(), "some line opens party politics");
        int institutionWarnings = LogService.Warnings - warnings;
        OnomasticsRule.ReadSetting();
        Check(OnomasticsRule.ALL_CULTURE_RULE.Count >= 27, "culture rules: " + OnomasticsRule.ALL_CULTURE_RULE.Count);
        Check(OnomasticsRule.ALL_CULTURE_RULE.ContainsKey("Huaxia") && "Youmu".IsNomadCulture() && !"Huaxia".IsNomadCulture(), "culture traits");
        Check("Huaxia".GetCultureColor() != "Roma".GetCultureColor(), "cultures have their own colours");
        LM.Clear(); int texts = LM.LoadFile(Path.Combine(GameData.Folder, "Locales", "ch.json")); LM.Language = "ch";
        Check(texts > 5000 && SocialClass.Peasant.ToTranslate() != "class_peasant", "text table: " + texts + " entries");
        Check(!string.IsNullOrEmpty("Huaxia".GetCultureTranslate()), "culture names translate");
        LogService.Sink = null;
        Console.WriteLine("PASS rule data (" + checks + " checks): " + trees.Count + " institution lines, " + nodes + " nodes, " +
                          OnomasticsRule.ALL_CULTURE_RULE.Count + " cultures, " + texts + " texts; " + institutionWarnings + " institution warnings, " +
                          (LogService.Warnings - warnings - institutionWarnings) + " culture warnings (the mod's own data notes)");
    }

    static EmpireWorld Start(WorldData map) => EmpireRunner.Open(map.Clone(), FMath.Seed(map.Seed));
    static void Run(EmpireWorld w, int years, Action<EmpireWorld> yearly = null)
    {
        for (int m = 0; m < years * 12; m++) { EmpireSimulation.Month(w); if (w.Month % 12 == 0) yearly?.Invoke(w); }
    }
    static void Consistent(EmpireWorld w, string when)
    {
        var errors = EmpireInvariants.Check(w); checks++;
        if (errors.Count > 0) throw new Exception(when + ": " + string.Join("; ", errors));
        foreach (var realm in w.Realms)
        {
            if (!realm.Alive) continue;
            var account = w.Ledger.Realm(realm.Id);
            if (!(account.Pressure >= 0f && account.Pressure <= 100f) || account.Legitimacy < 0 || account.Legitimacy > 100 || account.Treasury < 0)
                throw new Exception(when + ": realm " + realm.Id + " books out of range (strain " + account.Pressure + ", legitimacy " + account.Legitimacy + ", treasury " + account.Treasury + ")");
            foreach (var city in realm.Cities)
            {
                int id = city.Id; var book = w.Ledger.City(id);
                if (!(book.Stability >= 0f && book.Stability <= 100f) || !(book.Corruption >= 0f && book.Corruption <= 1f))
                    throw new Exception(when + ": city " + id + " books out of range");
            }
        }
    }

    // The ledger on a small world: in range, deterministic, save-stable, and the
    // treasury moves by exactly what the books record.
    public static void Accounts()
    {
        checks = 0;
        var map = FlatGenerator.Generate(256, 128, "EMPIRE-ACCOUNTS", TerrainPreset.Continents, default);
        var a = Start(map); Consistent(a, "opening");
        var treasuryBefore = new Dictionary<int, long>();
        Run(a, 60, w =>
        {
            Consistent(w, "year " + w.Year);
            foreach (var realm in w.Realms)
            {
                if (!realm.Alive) continue;
                var account = w.Ledger.Realm(realm.Id);
                // Twelve months of books explain the year's change in the treasury (once a realm is a year old).
                if (treasuryBefore.TryGetValue(realm.Id, out long before) && w.Month - realm.FoundedMonth >= 24)
                    Check(RealmAccounts.Report(w, realm.Id).cash_balance == account.Treasury - before, "realm " + realm.Id + " treasury does not match its books in year " + w.Year);
                treasuryBefore[realm.Id] = account.Treasury;
            }
        });
        var b = Start(map); Run(b, 60);
        Check(EmpireSave.Encode(a).SequenceEqual(EmpireSave.Encode(b)), "same seed must give the same 60 years of books");
        var bytes = EmpireSave.Encode(a); var loaded = EmpireSave.Decode(bytes, a.Map);
        Check(EmpireSave.Encode(loaded).SequenceEqual(bytes), "save round trip with books");
        Run(a, 15); Run(loaded, 15);
        Check(EmpireSave.Encode(a).SequenceEqual(EmpireSave.Encode(loaded)), "a loaded world keeps the same books");
        // A section written before the books existed still loads, and its world goes on with fresh books.
        var old = EmpireSave.Encode(a, 1); var fromOld = EmpireSave.Decode(old, a.Map);
        Check(old.Length < EmpireSave.Encode(a).Length && EmpireSave.Encode(fromOld, 1).SequenceEqual(old), "a version-1 section loads");
        Check(fromOld.Ledger.Realms.All(r => r.Treasury == 0 && r.Books.periods.Count == 0), "a version-1 world starts with empty books");
        Run(fromOld, 5); Consistent(fromOld, "after continuing a version-1 world");
        // The ledger only keeps books: the polity history must equal the skeleton's own.
        var plain = Start(map); var same = Start(map);
        for (int m = 0; m < 240; m++) { SkeletonRules.Month(plain); EmpireSimulation.Month(same); }
        Check(plain.Annals.Entries.Count == same.Annals.Entries.Count && plain.Cities.Count == same.Cities.Count && plain.People.Count == same.People.Count, "books must not change the course of the world yet");
        Console.WriteLine("PASS realm accounts 60+15 years (" + checks + " checks): " + Summary(a));
    }

    // The runner follows the ORBIS calendar through the frame budget.
    public static void Runner()
    {
        checks = 0;
        var map = FlatGenerator.Generate(256, 128, "EMPIRE-RUNNER", TerrainPreset.Continents, default);
        FrameClock.Reset(); FrameJobs.Reset(); SimulationFrameBudget.Reset();
        var world = Start(map); double day = 1234.5d; var runner = new EmpireRunner(world, day);
        int worstBehind = 0, frames = 0;
        // 120 days a second at 60 frames a second, for 40 game years.
        while (world.Month < 480)
        {
            FrameClock.Advance(1f / 60f); day += 2d; frames++;
            runner.Frame(day);
            worstBehind = Math.Max(worstBehind, runner.MonthsBehind(day));
        }
        Check(worstBehind <= 1, "the runner keeps up with the calendar, fell " + worstBehind + " months behind");
        // A jump in the calendar (a loaded save, a paused host) is caught up over a few frames.
        day += 30d * 240; int catchUp = 0;
        while (runner.MonthsBehind(day) > 0 && catchUp < 2000) { FrameClock.Advance(1f / 60f); runner.Frame(day); catchUp++; }
        Check(runner.MonthsBehind(day) == 0 && world.Month == 720, "a 20-year jump is caught up, month " + world.Month);
        Consistent(world, "after the runner");
        FrameClock.Reset(); FrameJobs.Reset(); SimulationFrameBudget.Reset();
        Console.WriteLine("PASS runner (" + checks + " checks): 40 years in " + frames + " frames, never more than " + worstBehind + " month behind; 20-year jump caught up in " + catchUp + " frames");
    }

    public static string Summary(EmpireWorld w)
    {
        int realms = 0, cities = 0, restless = 0, arrears = 0; double stability = 0, corruption = 0, strain = 0, legitimacy = 0; long treasury = 0;
        foreach (var realm in w.Realms)
        {
            if (!realm.Alive) continue;
            var account = w.Ledger.Realm(realm.Id); realms++;
            strain += account.Pressure; legitimacy += account.Legitimacy; treasury += account.Treasury; if (account.InArrears) arrears++;
            foreach (var city in realm.Cities)
            {
                var book = w.Ledger.City(city.Id); cities++; stability += book.Stability; corruption += book.Corruption;
                if (book.Stability < CityStabilityRules.RebellionThreshold) restless++;
            }
        }
        return $"{realms} realms, {cities} cities; stability {stability / Math.Max(1, cities):F1} ({restless} cities under {CityStabilityRules.RebellionThreshold:F0}), " +
               $"corruption {corruption / Math.Max(1, cities):P0}, dynastic strain {strain / Math.Max(1, realms):F1}, legitimacy {legitimacy / Math.Max(1, realms):F0}, " +
               $"treasuries {treasury:N0} in all, {arrears} realms in arrears";
    }
}
