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
