using System;
using System.Collections;
using System.Collections.Generic;
using Orbis.Flat;

namespace EmpireCraft.Core
{
    // What kind of ground a cell is, as the ported systems ask about it. One shared
    // instance per (kind, biome); the facts come from the ORBIS map, nothing is stored.
    public sealed class TerrainKind
    {
        public readonly string Id;          // "deep_ocean", "shallow_waters", "lake", "river", "summit", "mountains", "hills", "sand", "snow", "soil"
        public readonly string BiomeId;     // the biome under the name the ported rules know it by (see Land.BiomeIdOf)
        public readonly Biome Biome;        // the ORBIS biome
        public int Index { get; internal set; }   // a small number of its own, for tables indexed by kind
        public BiomeInfo BiomeAsset => BiomeInfo.Of(BiomeId);
        public readonly bool Liquid, Ocean, Ground, Mountains, Summit, Rocks, Sand, Block, CanBeFarm;
        // Things this world's ground never is. Roads, walls and fields are not terrain here.
        public bool Lava => false;
        public bool Wall => false;
        // The cells of this kind laid over the ground: none, nothing is laid over the ground here.
        public IEnumerable<Cell> GetCurrentTiles() => Array.Empty<Cell>();
        public bool Road => false;
        public bool FarmField => false;
        public int HeightMin => Summit ? Land.SummitHeight : Mountains ? Land.MountainHeight : Rocks ? Land.HillHeight : 0;

        internal TerrainKind(string id, Biome biome, bool liquid, bool ocean, bool mountains, bool summit, bool rocks, bool sand)
        {
            Id = id; Biome = biome; BiomeId = liquid ? "" : Land.BiomeIdOf(biome); Liquid = liquid; Ocean = ocean; Ground = !liquid;
            Mountains = mountains; Summit = summit; Rocks = rocks; Sand = sand; Block = summit;
            CanBeFarm = !liquid && !mountains && !sand && biome != Biome.Snow && biome != Biome.Alpine;
        }
    }

    public static class Land
    {
        // Elevation bands in metres. The summit line is where ORBIS stops calling land habitable.
        public const int HillHeight = 1000, MountainHeight = 2000, SummitHeight = 3520;
        // The mod's land rules read a biome by the id the base game gave it. This is the nearest of those for each ORBIS biome.
        public static string BiomeIdOf(Biome biome)
        {
            switch (biome)
            {
                case Biome.Beach: return "biome_sand";
                case Biome.Grassland: return "biome_grass";
                case Biome.Woodland: return "biome_birch";
                case Biome.Rainforest: return "biome_jungle";
                case Biome.Desert: return "biome_desert";
                case Biome.Steppe: return "biome_savanna";
                case Biome.Taiga: return "biome_maple";
                case Biome.Tundra: return "biome_permafrost";
                case Biome.Alpine: return "biome_rocklands";
                case Biome.Snow: return "biome_permafrost";
                case Biome.Wetland: return "biome_swamp";
                default: return "";
            }
        }
        // How many small tiles the mod's zone held. Its rules count ground and water inside a zone in these.
        public const int TilesPerZone = 64;
        // Cells along the side of a zone. The mod's zones were blocks of small tiles; here a zone is one cell.
        public const int ZoneSize = 1;
        static readonly Dictionary<int, TerrainKind> Kinds = new Dictionary<int, TerrainKind>();

        // Distance between two cells in cells, the short way round: the map wraps east-west.
        public static int SquaredDist(Cell a, Cell b)
        {
            if (a == null || b == null) return int.MaxValue;
            int dx = Math.Abs(a.X - b.X), dy = a.Y - b.Y, width = a.World.Width;
            if (dx * 2 > width) dx = width - dx;
            return dx * dx + dy * dy;
        }
        public static float Dist(Cell a, Cell b) => a == null || b == null ? float.MaxValue : (float)Math.Sqrt(SquaredDist(a, b));

        public static TerrainKind KindOf(WorldData map, int index)
        {
            int category; WaterKind water = map.Water[index]; int height = map.Elevation[index]; Biome biome = map.Biomes[index];
            if (water == WaterKind.DeepOcean || water == WaterKind.MiddleOcean) category = 0;
            else if (water == WaterKind.ShallowOcean) category = 1;
            else if (water == WaterKind.Lake) category = 2;
            else if (water == WaterKind.River) category = 3;
            else if (height >= SummitHeight) category = 4;
            else if (height >= MountainHeight) category = 5;
            else if (height >= HillHeight) category = 6;
            else if (biome == Biome.Desert) category = 7;
            else if (biome == Biome.Snow) category = 8;
            else category = 9;
            int key = category * 64 + (int)biome;
            lock (Kinds)
            {
                if (Kinds.TryGetValue(key, out TerrainKind kind)) return kind;
                switch (category)
                {
                    case 0: kind = new TerrainKind("deep_ocean", biome, true, true, false, false, false, false); break;
                    case 1: kind = new TerrainKind("shallow_waters", biome, true, true, false, false, false, false); break;
                    case 2: kind = new TerrainKind("lake", biome, true, false, false, false, false, false); break;
                    case 3: kind = new TerrainKind("river", biome, false, false, false, false, false, false); break;
                    case 4: kind = new TerrainKind("summit", biome, false, false, true, true, true, false); break;
                    case 5: kind = new TerrainKind("mountains", biome, false, false, true, false, true, false); break;
                    case 6: kind = new TerrainKind("hills", biome, false, false, false, false, true, false); break;
                    case 7: kind = new TerrainKind("sand", biome, false, false, false, false, false, true); break;
                    case 8: kind = new TerrainKind("snow", biome, false, false, false, false, false, false); break;
                    default: kind = new TerrainKind("soil", biome, false, false, false, false, false, false); break;
                }
                kind.Index = key; Kinds[key] = kind;
                return kind;
            }
        }
    }

    // Kinds of things that stand on a cell, as the ported code asks for them (see Cell.GetHashset).
    public enum BuildingList { Trees, Minerals, Ruins }

    // One cell of the ORBIS grid as the polity systems see it. The mod worked with
    // two sizes, a "zone" of 8x8 small tiles and the tile itself; at this map's scale
    // (a cell is tens of kilometres) one cell is both, so a cell is its own centre
    // tile and its own only tile. Cells are created on first use and never stored.
    public sealed class Cell
    {
        public readonly EmpireWorld World;
        public readonly int Index, X, Y;
        Cell[] _self, _near, _around;

        internal Cell(EmpireWorld world, int index) { World = world; Index = index; X = index % world.Width; Y = index / world.Width; }

        public long Key => Index;
        public Cell Record => this;
        // Drawing helpers belong to the client; there is nothing to reset here.
        public void ResetRenderHelpers() { }
        // Free land a city may take: ground no city holds yet.
        public bool CanBeClaimedByCity(City city) => World.TileCity[Index] < 0 && World.Claimable(Index);
        // Ground here is never frozen for a season.
        public bool IsTemporaryFrozen() => false;
        public CellRegion Region => new CellRegion(World.IslandObject(Index));
        // Trees, deposits, ruins standing on the cell: none, nothing is built or grows here as an object.
        static readonly HashSet<Building> NoBuildings = new HashSet<Building>();
        public HashSet<Building> GetHashset(BuildingList list) => NoBuildings;
        public City City => World.CityAt(Index);
        public bool HasCity() => World.TileCity[Index] >= 0;
        public Realm Realm => World.RealmAt(Index);
        public Cell Zone => this;
        public Cell Center => this;
        public IReadOnlyList<Cell> Tiles => _self ?? (_self = new[] { this });
        public TerrainKind Type => Land.KindOf(World.Map, Index);
        public TerrainKind MainType => Type;
        public TerrainKind TopType => null;      // nothing is laid over the ground here
        public Building Building => null;        // and nothing is built on it as an object
        public int Height => World.Map.Elevation[Index];
        public bool IsWater => Type.Liquid;
        public bool IsLand => World.Claimable(Index);
        public IReadOnlyList<Cell> GroundTiles => IsLand ? Tiles : Array.Empty<Cell>();
        public IReadOnlyList<Cell> LiquidTiles => IsWater ? Tiles : Array.Empty<Cell>();
        // Ground and water inside the cell, in the mod's small tiles: all ground, all water, or a river running through.
        public int GroundCount => IsWater ? 0 : World.Map.Water[Index] == WaterKind.River ? Land.TilesPerZone * 3 / 4 : Land.TilesPerZone;
        public int LiquidCount => Land.TilesPerZone - GroundCount;
        public bool OnEdge => Y == 0 || Y == World.Height - 1;         // the map wraps east-west; only the polar rows are an edge
        public bool CanSettle => World.Settleable(Index) && World.TileCity[Index] < 0;
        public EcVector2Int Pos => new EcVector2Int(X, Y);
        public EcVector2 PosV => new EcVector2(X, Y);

        public Cell Left => World.CellAt(World.Map.Neighbor(Index, 0));
        public Cell Right => World.CellAt(World.Map.Neighbor(Index, 1));
        public Cell Down => World.CellAt(World.Map.Neighbor(Index, 2));
        public Cell Up => World.CellAt(World.Map.Neighbor(Index, 3));

        // The four cells sharing an edge, fewer on a polar row.
        public Cell[] Neighbours
        {
            get
            {
                if (_near != null) return _near;
                var found = new List<Cell>(4);
                for (int d = 0; d < 4; d++) { var cell = World.CellAt(World.Map.Neighbor(Index, d)); if (cell != null) found.Add(cell); }
                return _near = found.ToArray();
            }
        }

        // The up to eight cells around.
        public Cell[] NeighboursAll
        {
            get
            {
                if (_around != null) return _around;
                var found = new List<Cell>(8);
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var cell = World.CellAt(X + dx, Y + dy);
                        if (cell != null) found.Add(cell);
                    }
                return _around = found.ToArray();
            }
        }

        // Reachable over land (and rivers) without crossing sea.
        public bool IsSameIsland(Cell other)
        {
            if (other == null) return false;
            int mine = World.LandmassOf(Index);
            return mine >= 0 && mine == World.LandmassOf(other.Index);
        }

        public override string ToString() => "(" + X + "," + Y + ")";
    }

    // The grid as a whole: cells by coordinate or index. Redraw requests from the
    // ported code end here; drawing belongs to the client.
    public sealed class ZoneGrid : IReadOnlyList<Cell>
    {
        readonly EmpireWorld _world;
        internal ZoneGrid(EmpireWorld world) { _world = world; }
        public int Width => _world.Width;
        public int Height => _world.Height;
        public int Count => _world.TileCity.Length;
        public Cell this[int index] => _world.CellAt(index);
        public Cell Get(int x, int y) => _world.CellAt(x, y);
        public Cell ById(long id) => id < 0 || id >= Count ? null : _world.CellAt((int)id);
        // Every cell as a list, made on first use; and cells by number.
        List<Cell> _all;
        public List<Cell> All { get { if (_all == null) { var cells = new List<Cell>(Count); for (int i = 0; i < Count; i++) cells.Add(_world.CellAt(i)); _all = cells; } return _all; } }
        public ZoneLookup Lookup => new ZoneLookup(_world);
        public void MarkDirty() { }
        public IEnumerator<Cell> GetEnumerator() { for (int i = 0; i < Count; i++) yield return _world.CellAt(i); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
