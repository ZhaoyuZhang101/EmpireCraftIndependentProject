using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{
    // Things the ported systems ask about that this game keeps as numbers on a ledger, or
    // does not have at all. The classes exist so the questions can be asked; the answers
    // are "none": there are no building objects on the map, no citizens with jobs, no
    // tables of building kinds. A system that needs more than "none" brings its own books.

    // A kind of building, as a table of kinds would describe it. No table is loaded.
    public enum FloraType : byte { None, Tree, Plant, Fungi }

    public sealed class BuildingKind
    {
        public string Id, Type, UpgradeTo;
        public int UpgradeLevel, HousingSlots, ConstructionProgressNeeded, MaxHouses;
        public bool Wheat, Flora;
        public FloraType FloraType;
        public bool HasHousingSlots() => HousingSlots > 0;
    }

    // A building standing on the map. Nothing creates one: what a city has built is counted, not placed.
    public sealed class Building : Entity
    {
        public long Id = -1L;
        public bool Removed;
        public override long Key => Id;
        public override bool Alive => !Removed;
        public Building Record => this;
        public bool IsOnRemove() => Removed;
        public BuildingKind Asset;
        public BookShelf Books;     // the books kept here, if it keeps any
        public City City;
        public Cell Cell;
        public bool UnderConstruction;
        public bool IsUnderConstruction() => UnderConstruction;
        public BuildingDocks Docks => null;   // docks would be a built object; none is built here
        public bool IsUsable() => !UnderConstruction;
    }

    // A harbour's docks: the sea cells boats leave from.
    public sealed class BuildingDocks
    {
        public readonly HashSet<Cell> TilesOcean = new HashSet<Cell>();
    }

    // The books a building keeps, by key.
    public sealed class BookShelf
    {
        public readonly List<long> Ids = new List<long>();
    }

    // The (empty) table of building kinds.
    public sealed class BuildingKinds
    {
        public static readonly BuildingKinds Table = new BuildingKinds();
        public readonly Dictionary<string, BuildingKind> dict = new Dictionary<string, BuildingKind>(StringComparer.Ordinal);
        public readonly List<BuildingKind> list = new List<BuildingKind>();
        public BuildingKind Get(string id) => id != null && dict.TryGetValue(id, out BuildingKind kind) ? kind : null;
    }

    // The work a citizen does. Persons on record are not labourers; they have none.
    public sealed class JobKind
    {
        public string Id;
    }

    // What a biome's ground is called in its taller form, for the rules that tell woodland from open land by it.
    public sealed class BiomeInfo
    {
        public readonly string Id, TileHigh, TileLow;
        BiomeInfo(string id) { Id = id; TileHigh = id + "_high"; TileLow = id + "_low"; }
        static readonly Dictionary<string, BiomeInfo> Known = new Dictionary<string, BiomeInfo>(StringComparer.Ordinal);
        public static BiomeInfo Of(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            lock (Known)
            {
                if (!Known.TryGetValue(id, out BiomeInfo info)) Known[id] = info = new BiomeInfo(id);
                return info;
            }
        }
    }

    // A setting of the game the ported code reads by name ("language" ...).
    public sealed class GameOption
    {
        public readonly string Id;
        string _text; public bool BoolVal; public int IntVal;
        public GameOption(string id) { Id = id; }
        // The language option always answers with the language the texts are loaded in.
        public string StringVal { get { return Id == "language" ? LM.Language : _text; } set { if (Id == "language") LM.Language = value; else _text = value; } }
    }

    public static class GameOptions
    {
        public static readonly Dictionary<string, GameOption> All = new Dictionary<string, GameOption>(StringComparer.Ordinal) { ["language"] = new GameOption("language") };
        public static GameOption Get(string id) => id != null && All.TryGetValue(id, out GameOption option) ? option : null;
    }

    // What the client is showing, as far as the ported systems care: which empire is selected, and their requests to redraw.
    public static class GameView
    {
        public static object SelectedEmpire;
        public static event Action<object> RedrawRequested;
        public static void Redraw(object what = null) { RedrawRequested?.Invoke(what); }
    }

    // A list borrowed for the length of a `using`. Nothing is pooled here; it is a list that can be disposed.
    public sealed class ListPool<T> : List<T>, IDisposable
    {
        public ListPool() { }
        public ListPool(int capacity) : base(capacity) { }
        public ListPool(IEnumerable<T> items) : base(items ?? Array.Empty<T>()) { }
        public void Dispose() { }
    }

    // Choosing among persons: seniority, and a roll that favours the able.
    public static class PersonSorts
    {
        public static int OldestFirst(Person a, Person b) => a.BirthMonth != b.BirthMonth ? a.BirthMonth.CompareTo(b.BirthMonth) : a.Id.CompareTo(b.Id);
        // The senior of the candidates. (What a culture prizes besides age is for its own rules to add.)
        public static Person SeniorOf(IEnumerable<Person> candidates, Culture culture = null)
        {
            Person best = null;
            if (candidates != null) foreach (var person in candidates) if (person != null && (best == null || OldestFirst(person, best) < 0)) best = person;
            return best;
        }
        // The best of a number of rolls; wits and stewardship raise the ceiling of each.
        public static int AttributeDice(Person person, int rolls)
        {
            if (person == null) return 0;
            int ceiling = 10 + Math.Max(0, person.Intelligence) + Math.Max(0, person.Stewardship), best = 0;
            for (int i = 0; i < Math.Max(1, rolls); i++) best = Math.Max(best, EcRandom.Range(0, ceiling + 1));
            return best;
        }
    }

    // The mod's own trace of why an empire was dissolved. Diagnostics are not ported.
    public static class EmpireLossDiagnostics
    {
        public static void OnLoaded() { }
        public static void OnDissolving(params object[] what) { }
        public static void OnDissolveSkipped(params object[] what) { }
        public static void OnCoreMoved(params object[] what) { }
        public static void Tick(params object[] what) { }
    }

    // A fresh key for a record, from the current world's counter (see EmpireWorld.NextRecordId).
    public static class RecordIds
    {
        static long _loose = 1;   // only when no world is current, as in a tool run outside one
        public static long Next() { var world = EmpireWorld.Current; return world != null ? world.TakeRecordId() : _loose++; }
    }

    // A random pick from a list, on the world's random stream.
    public static class ListPicks
    {
        public static void Swap<T>(this IList<T> list, int a, int b) { if (list == null || a == b || a < 0 || b < 0 || a >= list.Count || b >= list.Count) return; T kept = list[a]; list[a] = list[b]; list[b] = kept; }
        public static bool HasValue(this long key) => key >= 0;
        public static T GetRandom<T>(this IList<T> list) => list == null || list.Count == 0 ? default(T) : list[EcRandom.Range(0, list.Count)];
    }

    // Another mod the original coexists with (vehicles, protests, finance). It is not here, so nothing of it applies.
    public static class WarBoxCompatibility
    {
        public static bool Loaded => false;
        public static bool TryGetFinance(Realm realm, out bool crisis, out float unemployment) { crisis = false; unemployment = 0f; return false; }
        public static void CountProtests(object empire, out int demonstrating, out int striking) { demonstrating = 0; striking = 0; }
        public static void SweepVehicles() { }
        public static int VirtualVehicles(Realm realm) => 0;
        public static void LoseVehicles(object side, double loss) { }
    }

    // Named values a system may hang on an object. Nothing in this game's own records uses them.
    public sealed class CustomDataContainer<T>
    {
        public readonly Dictionary<string, T> dict = new Dictionary<string, T>(StringComparer.Ordinal);
        public Dictionary<string, T>.KeyCollection Keys => dict.Keys;
        public T this[string key] { get { return dict.TryGetValue(key, out T value) ? value : default(T); } set { dict[key] = value; } }
    }

    // A city's headline numbers, as the ported systems read them. All of them follow from the city's counted
    // population, the land it holds and the guards on its books; only the troop quota is set from outside.
    public sealed class CityStatus
    {
        readonly City _city;
        internal CityStatus(City city) { _city = city; }
        public const float AdultShare = 0.65f;
        public int Population => _city.PopulationCount();
        public int PopulationAdults => (int)(Population * AdultShare);
        public int PopulationChildren => Population - PopulationAdults;
        public int HousingTotal => _city.GetPopulationMaximum();
        public int HousingOccupied => Math.Min(Population, HousingTotal);
        public int HousingFree => Math.Max(0, HousingTotal - Population);
        public int HousesMax => HousingTotal / 5;
        public int WarriorSlots;                       // how many troops the city is asked to keep
        public int WarriorsCurrent => _city.Warriors();
    }

    // A tongue. None is on record: speech follows culture here.
    public sealed class Language : Entity
    {
        public long Id = -1L;
        public string Name;
        public override long Key => Id;
        public override bool Alive => false;
        public Language Record => this;
    }

    // What decision routine a realm is running, for the ported code that assigns one.
    public sealed class RealmAi
    {
        public string Task;
        public void SetTask(string task, bool clean = true, bool force = false) { Task = task; }
    }

    // Cells by their number, the way the ported code looks one up in a table.
    public readonly struct ZoneLookup
    {
        readonly EmpireWorld _world;
        internal ZoneLookup(EmpireWorld world) { _world = world; }
        public bool TryGetValue(long id, out Cell cell) { cell = _world == null || id < 0 || id >= _world.TileCity.Length ? null : _world.CellAt((int)id); return cell != null; }
        public bool ContainsKey(long id) => _world != null && id >= 0 && id < _world.TileCity.Length;
    }

    // Kinds of goods. The table is empty for now: the systems that trade, store and grow goods
    // ask it and find nothing, until this game brings its own list of goods.
    public enum ResType { Currency, Strategic, Ingredient, Ingredient_Food, Food }

    public sealed class ResourceKind
    {
        public string id;
        public ResType type;
        public bool food;
        public int supply_give;
        public string getTranslatedName() => LM.Get(id);
    }

    public sealed class ResourceKinds
    {
        public static readonly ResourceKinds Table = new ResourceKinds();
        public readonly List<ResourceKind> list = new List<ResourceKind>();
        public readonly Dictionary<string, ResourceKind> dict = new Dictionary<string, ResourceKind>();
        public ResourceKind get(string id) => id != null && dict.TryGetValue(id, out ResourceKind kind) ? kind : null;
        // A copy of a kind under a new id (none is made while the table is empty).
        public ResourceKind clone(string id, string from)
        {
            ResourceKind source = get(from);
            if (source == null || id == null) return null;
            var made = new ResourceKind { id = id, type = source.type, food = source.food, supply_give = source.supply_give };
            dict[id] = made; list.Add(made);
            return made;
        }
    }

    // Tuning numbers of the world the ported diplomacy reads. This game's own values.
    public sealed class WorldRules
    {
        public static readonly WorldRules m = new WorldRules();
        public int minimum_years_between_wars = 5;        // a truce after a war has ended
        public int diplomacy_years_war_timeout = 3;       // a realm waits this long after its last war before starting another
        public int diplomacy_years_war_min_warriors = 50; // troops a realm needs before it goes to war
        public int minimum_kingdom_age_for_attack = 5;    // a new realm is left alone this many years
        public int alliance_timeout = 5;                  // years between joining and leaving alliances
        public int rebellions_min_age = 10;               // a city rises only after this many years under its realm
        public int rebellions_min_warriors = 50;
        public float rebellions_unhappy_multiplier = 0.1f;
    }

    // Arms and armour: nobody carries any (soldiers are counted, persons on record do not fight as units).
    public sealed class Equipment { }
    public static class ItemCrafting
    {
        public static bool tryToCraftRandomWeapon(Person person, City city) => false;
        public static bool tryToCraftRandomArmor(Person person, City city) => false;
    }

    // Tips flashed on the screen are the client's; the world's news (WorldNews) is what it reads.
    public static class WorldTip
    {
        public static void showNow(string text, bool translate = true, string position = "top", float seconds = 3f) { }
    }

    // The mod switched some of its game patches on lazily; this game has no patches to switch.
    public static class TechnologyPatch
    {
        public static void EnsureModPatches() { }
    }

    // Whether a realm is minded to go to war at all (before it picks a target): it has a ruler and a capital,
    // is not already at war, its last war began long enough ago, and it has the troops. This game's own rule.
    public static class DiplomacyHelpers
    {
        public static bool isWarNeeded(Realm realm)
        {
            if (realm == null || !realm.Alive || realm.Ruler == null || realm.Capital == null || realm.Wars.Count > 0) return false;
            if (realm.LastWarAt >= 0 && GameClock.YearsSince(realm.LastWarAt) <= WorldRules.m.diplomacy_years_war_timeout) return false;
            return realm.Warriors() > WorldRules.m.diplomacy_years_war_min_warriors;
        }
    }

    // Counts of what has happened in a world (cities conquered, cities that rebelled ...). Not saved.
    public sealed class WorldStatsData
    {
        public string name;
        public long citiesConquered, citiesRebelled, citiesCreated, citiesDestroyed, kingdomsCreated, kingdomsDestroyed;
        public void initNewWorld() { citiesConquered = citiesRebelled = citiesCreated = citiesDestroyed = kingdomsCreated = kingdomsDestroyed = 0; }
    }
    public sealed class WorldStats { public readonly WorldStatsData data = new WorldStatsData(); }

    // The mod's game patches derive from these; there is no game to patch here.
    public interface GamePatch { }

    // The mod's slow-step warnings (a step that took long is logged). Here they only measure.
    public static class PerfTimer
    {
        public static long Begin() => System.Diagnostics.Stopwatch.GetTimestamp();
        public static bool Slow(long started, out double milliseconds)
        {
            milliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d / System.Diagnostics.Stopwatch.Frequency;
            return false;
        }
        public static void Warn(string label, double milliseconds) { }
    }

    // The mod's patch on commoners (its index of who lives where on the map): no map units here, nothing to index.
    public static class NoCommonersPatch
    {
        public static void ResetPopulationIndex() { }
    }

    // The phone terminal (a player seat steering a ruler) is not part of this game yet: nobody campaigns in person.
    public static class TerminalHost
    {
        public static float CampaignFactor(Realm realm) => 1f;
    }

    // The mod's city plans (built-up cells of a town) belong to its map drawing; a city here has no plan,
    // so only its seat counts as built up.
    public static class PureMapPatch
    {
        public static HashSet<int> BuiltZones(City city) => null;
    }

    // How corps are drawn on the map is the client's business.
    public static class CorpsMapRenderer
    {
        public static void EnsureHost() { }
        public static void ClearLocate() { }
        public static void ResetWorldState() { }
    }

    // Things laid over the ground (walls, roads as tiles): none here.
    public sealed class TerrainLayers
    {
        public static readonly TerrainLayers Top = new TerrainLayers();
        public readonly List<TerrainKind> list = new List<TerrainKind>();
    }

    // The landmass (or body of water) a cell lies on, as one object per landmass (compared by reference).
    public sealed class CellRegion
    {
        public readonly IslandInfo island;
        public CellRegion(IslandInfo island) { this.island = island; }
    }
    public sealed class IslandInfo
    {
        readonly int _size;
        public IslandInfo(int size) { _size = size; }
        public int getTileCount() => _size;
    }

    // A colour as four bytes (the mod's wilderness tints).
    public readonly struct EcColor32
    {
        public readonly byte r, g, b, a;
        public EcColor32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    // The map's wilderness overlay and the town fabric drawn by hand are the client's; nothing is drawn or hand-laid here.
    public static class WildernessOverlay
    {
        public static void MarkAround(Cell zone) { }
        public static void MarkZone(Cell zone) { }
    }
    public static class UrbanFabric
    {
        public static bool ManualGround(Cell tile) => false;
    }

    // Which free cell a city takes next as it grows: the most fertile one next to its land, within seven cells of its
    // seat (a little less for each cell farther off). This game's rule, the same the skeleton's growth used; the mod
    // then checks it its own way (CityExpansionPatch.GrowVirtualCity).
    public sealed class CityZoneGrowth
    {
        public static readonly CityZoneGrowth Instance = new CityZoneGrowth();
        public Cell getZoneToClaim(Person claimer, City city)
        {
            var w = city?.World; if (w == null || !city.Alive) return null;
            int best = -1; float bestScore = -1f;
            foreach (int t in city.Tiles)
                for (int d = 0; d < 4; d++)
                {
                    int n = w.Map.Neighbor(t, d); if (n < 0 || w.TileCity[n] >= 0 || !w.Claimable(n)) continue;
                    float distance = w.Distance(city.Tile, n); if (distance > 7) continue;
                    float s = w.Map.Fertility[n] - distance * .04f; if (s > bestScore || (s == bestScore && n < best)) { bestScore = s; best = n; }
                }
            return best < 0 ? null : w.CellAt(best);
        }
    }
    public sealed class ModDeclare { public string FolderPath => GameData.Folder; }

    // A row of cards on a screen of the mod; screens are the client's business here.
    public sealed class AutoHoriLayoutGroup { }

    // Name plates on the map belong to the client. The ported code asks them for a realm's
    // display name and tells them to refresh; the name is the realm's own and nothing is drawn.
    public static class EmpireCraftNamePlateLibrary
    {
        public static string GetDisplayName(Realm realm) => realm?.Name ?? "";
        public static void RequestRefresh() { }
        public static void ResetWorldState() { }
    }
}
