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

    // A random pick from a list, on the world's random stream.
    public static class ListPicks
    {
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

    // Cells by their number, the way the ported code looks one up in a table.
    public readonly struct ZoneLookup
    {
        readonly EmpireWorld _world;
        internal ZoneLookup(EmpireWorld world) { _world = world; }
        public bool TryGetValue(long id, out Cell cell) { cell = _world == null || id < 0 || id >= _world.TileCity.Length ? null : _world.CellAt((int)id); return cell != null; }
        public bool ContainsKey(long id) => _world != null && id >= 0 && id < _world.TileCity.Length;
    }
}
