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
        public bool IsUsable() => !UnderConstruction;
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
        public static void VirtualVehicles(Realm realm) { }
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
}
