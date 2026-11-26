// Generated from the EmpireCraft mod: Scripts/GeneralSystems/CityLedger.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using Time = EmpireCraft.Core.FrameClock;
using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// 建筑台账(用户 2026-11-11 "那全改成虚拟的呢""目前的计划是先做模组最后一点一点的剥离出来""成为独立游戏"，定"先做建筑")：
// 纯净地图下城里的建筑不再落成原版的建筑实体，只在每座城的台账(CityExtraData.works，见 CityWorks)里记"哪种、几座"。
//   · 记台账的(Virtual)：民居、仓库、风车、市集、寺庙、兵营、瞭望塔、水井、篝火——原版里它们只提供住房位、许盖的民居数、
//     仓储和"有没有这类建筑"的判断，都能按数算；
//   · 仍是实体的：码头(船要停靠、从它下水)、带藏书位的(公所、图书馆、文化城池：书存在建筑里)、别的模组的工厂之类——下一步再剥；
//   · 原版与模组里按座数问的地方(City.countBuildingsType / hasBuildingType / countBuildingsOfID / countBuildings、住房位、
//     许盖的民居数)由 CityLedgerPatch 把台账的数加进去，实体与台账并存时两边相加；
//   · 营建(见 CityConstructionSystem)：开工只记"在建哪一种"，施工点够了记一座；升级是把一座换成高一级的；
//   · 仓储：没有仓库实体时货本来就记在 settlement_supplies(见 StateSettlementPatch)，台账下每种货的上限 = 原版单库上限 × 仓库座数；
//     原版城间转运也改从账面库存出货(见 CityLedgerSupply)；
//   · 旧建筑并入(Fold)：读档后、以及别处又落成的可记账建筑，分帧记进台账、货挪进账面库存、实体拆掉。
// 台账里已有的数任何模式下都算数(中途关掉纯净地图，记过账的民居不会凭空没了)；Active 只决定新盖的记账还是落成实体、旧实体并不并入。
public static class CityLedger
{
    public static bool Active => PureMapMode.Enabled;

    public static bool Uses(City city) =>
        Active && city?.Record != null && !city.IsGone() && !AncientWarfareCompatibility.OwnsObject(city);

    public const string HouseType = "type_house", StockpileType = "type_stockpile", BonfireType = "type_bonfire";

    private static readonly HashSet<string> VirtualTypes = new()
    {
        HouseType, StockpileType, BonfireType, "type_windmill", "type_market", "type_temple", "type_barracks",
        "type_watch_tower", "type_well"
    };

    // 这种建筑记不记台账
#if EC_PENDING
    public static bool Virtual(BuildingKind asset) =>
        asset?.Type != null && asset.city_building && asset.building_type == BuildingType.Building_Civ && !asset.docks &&
        asset.book_slots <= 0 && VirtualTypes.Contains(asset.Type) && !PureMapMode.IsDecor(asset) &&
        !LivelihoodSystem.Replaces(asset) && !TechnologySystem.IsFactory(asset);
#endif

    public static BuildingKind Asset(string id) =>
        !string.IsNullOrEmpty(id) && BuildingKinds.Table.dict.TryGetValue(id, out BuildingKind asset) ? asset : null;

    #region 读

    public static CityWorks Works(City city) => city?.Record == null ? null : city.GetOrCreate().works;

    private static CityWorks Ensure(City city)
    {
        _any = true;
        return city.GetOrCreate().works ??= new CityWorks();
    }

    // 有没有哪座城记了账(没有时补丁直接放行，不逐城查)。记过一笔就一直为真(换了存档多查几下而已，不会错)；
    // 还没有时每隔 AnyScanSeconds 秒(真实时间)扫一遍各城——读进一个记过账的存档、又没开纯净地图时靠它认出来
    private const float AnyScanSeconds = 2f;
    private static bool _any;
    private static float _anyScanAt = float.NegativeInfinity;

    public static bool Any
    {
        get
        {
            if (_any) return true;
            float now = Time.realtimeSinceStartup;
            if (now - _anyScanAt < AnyScanSeconds && now >= _anyScanAt) return false;
            _anyScanAt = now;
            EmpireWorld world = EmpireWorld.Current;
            if (world?.Cities == null) return false;
            foreach (City city in world.Cities)
                if (Works(city) is CityWorks works && (works.lots.Count > 0 || works.building != null))
                {
                    _any = true;
                    break;
                }
            return _any;
        }
    }

    public static WorksSummary SummaryOf(City city)
    {
        CityWorks works = Works(city);
        if (works == null) return null;
        if (works.summary != null) return works.summary;
        var summary = new WorksSummary();
        foreach (WorkLot lot in works.lots)
        {
            BuildingKind asset = lot == null || lot.n <= 0 ? null : Asset(lot.id);
            if (asset == null) continue;
            summary.total += lot.n;
            summary.by_id[asset.Id] = (summary.by_id.TryGetValue(asset.Id, out int byId) ? byId : 0) + lot.n;
            if (asset.Type != null)
                summary.by_type[asset.Type] = (summary.by_type.TryGetValue(asset.Type, out int byType) ? byType : 0) + lot.n;
            if (asset.HasHousingSlots()) summary.housing += asset.HousingSlots * lot.n;
            summary.max_houses += asset.MaxHouses * lot.n;
        }
        return works.summary = summary;
    }

    // 台账里这一类、这一种建成几座；一共几座；住房位；许盖的民居数
    public static int CountType(City city, string type) =>
        type != null && SummaryOf(city) is WorksSummary summary && summary.by_type.TryGetValue(type, out int n) ? n : 0;

    public static int CountId(City city, string id) =>
        id != null && SummaryOf(city) is WorksSummary summary && summary.by_id.TryGetValue(id, out int n) ? n : 0;

    public static int Total(City city) => SummaryOf(city)?.total ?? 0;
    public static int HousingSlots(City city) => SummaryOf(city)?.housing ?? 0;
    public static int MaxHouses(City city) => SummaryOf(city)?.max_houses ?? 0;

    // 在建的那一种(没有为 null)
    public static BuildingKind Pending(City city) => Asset(Works(city)?.building);

    // 城里的建筑一共几座(实体加台账)
    public static int Buildings(City city) => (city?.Buildings?.Count ?? 0) + Total(city);

    // 建成的城市建筑一座一项：实体的(real 不为空，zone 是它脚下的地块)连同台账里的(real 为空，zone 是记的地块或 -1)
    public struct Item
    {
        public BuildingKind asset;
        public int zone;
        public Building real;
    }

    public static void Collect(City city, List<Item> into)
    {
        if (city == null || into == null) return;
        if (city.Buildings != null)
            foreach (Building building in city.Buildings)
            {
                if (building?.Asset == null || building.IsGone() || building.IsOnRemove() || building.IsUnderConstruction()) continue;
                into.Add(new Item { asset = building.Asset, zone = building.Cell?.Zone?.Index ?? -1, real = building });
            }
        CityWorks works = Works(city);
        if (works == null) return;
        foreach (WorkLot lot in works.lots)
        {
            BuildingKind asset = lot == null || lot.n <= 0 ? null : Asset(lot.id);
            if (asset == null) continue;
            for (int i = 0; i < lot.n; i++) into.Add(new Item { asset = asset, zone = lot.zone });
        }
    }

    // 各种建成的建筑各几座(实体加台账)，不展开成一座一项
    public static void CollectCounts(City city, Dictionary<BuildingKind, int> into)
    {
        if (city == null || into == null) return;
        if (city.Buildings != null)
            foreach (Building building in city.Buildings)
            {
                if (building?.Asset == null || building.IsGone() || building.IsOnRemove() || building.IsUnderConstruction()) continue;
                into[building.Asset] = (into.TryGetValue(building.Asset, out int n) ? n : 0) + 1;
            }
        CityWorks works = Works(city);
        if (works == null) return;
        foreach (WorkLot lot in works.lots)
        {
            BuildingKind asset = lot == null || lot.n <= 0 ? null : Asset(lot.id);
            if (asset != null) into[asset] = (into.TryGetValue(asset, out int n) ? n : 0) + lot.n;
        }
    }

    #endregion

    #region 改

#if EC_PENDING
    private static void Touch(City city, CityWorks works)
    {
        works.summary = null;
        try
        {
            city.setStatusDirty();
            CityPopulationSystem.InvalidateHouseholdCaches();
        }
        catch (Exception)
        {
            // 城正在销毁：不用管
        }
    }
#endif

    private static WorkLot Find(CityWorks works, string id, int zone)
    {
        foreach (WorkLot lot in works.lots)
            if (lot != null && lot.zone == zone && lot.id == id) return lot;
        return null;
    }

    // 记 n 座(篝火一城只记一处)
#if EC_PENDING
    public static void Add(City city, BuildingKind asset, int zone = -1, int n = 1)
    {
        if (city?.Record == null || asset?.Id == null || n <= 0) return;
        if (asset.Type == BonfireType)
        {
            if (CountType(city, BonfireType) > 0) return;
            n = 1;
        }
        CityWorks works = Ensure(city);
        WorkLot lot = Find(works, asset.Id, zone);
        if (lot == null) works.lots.Add(new WorkLot { id = asset.Id, zone = zone, n = n });
        else lot.n += n;
        Touch(city, works);
    }
#endif

    // 销 n 座这一种的：zone 不为 null 时只销那块地上的；返回销了几座
#if EC_PENDING
    public static int Remove(City city, string id, int? zone = null, int n = 1)
    {
        CityWorks works = Works(city);
        if (works == null || id == null || n <= 0) return 0;
        int removed = 0;
        for (int i = works.lots.Count - 1; i >= 0 && removed < n; i--)
        {
            WorkLot lot = works.lots[i];
            if (lot == null || lot.id != id || zone.HasValue && lot.zone != zone.Value) continue;
            int take = Mathf.Min(lot.n, n - removed);
            lot.n -= take;
            removed += take;
            if (lot.n <= 0) works.lots.RemoveAt(i);
        }
        if (removed > 0) Touch(city, works);
        return removed;
    }
#endif

    // 挪 n 座到另一块地(手动城区对账用)；返回挪了几座
#if EC_PENDING
    public static int Move(City city, string id, int from, int to, int n = 1)
    {
        if (from == to) return 0;
        BuildingKind asset = Asset(id);
        if (asset == null) return 0;
        int moved = Remove(city, id, from, n);
        if (moved > 0) Add(city, asset, to, moved);
        return moved;
    }
#endif

    // 一座 from 换成 to(升级、降级)，留在原来那块地；zone 为 null 时随便挑一座
#if EC_PENDING
    public static bool Swap(City city, string from, BuildingKind to, int? zone = null)
    {
        CityWorks works = Works(city);
        if (works == null || from == null || to?.Id == null || from == to.Id) return false;
        foreach (WorkLot lot in works.lots)
        {
            if (lot == null || lot.n <= 0 || lot.id != from || zone.HasValue && lot.zone != zone.Value) continue;
            int at = lot.zone;
            Remove(city, from, at, 1);
            Add(city, to, at, 1);
            return true;
        }
        return false;
    }
#endif

    // 台账里的各笔(只读着用；要改走 Add / Remove / Swap)
    public static IReadOnlyList<WorkLot> Lots(City city) => Works(city)?.lots ?? (IReadOnlyList<WorkLot>)Array.Empty<WorkLot>();

    #endregion

    #region 营建

    // 能不能把一座这种建筑升一级(资产许升、有高一级、科技许可)；返回高一级的资产
#if EC_PENDING
    public static BuildingKind UpgradeTarget(City city, BuildingKind from)
    {
        if (from == null || !from.can_be_upgraded || string.IsNullOrEmpty(from.UpgradeTo) || from.UpgradeTo == from.Id) return null;
        BuildingKind to = Asset(from.UpgradeTo);
        return to != null && TechnologySystem.CanBuild(city, to.Id) ? to : null;
    }
#endif

    // 开工：只记在建哪一种(一城一处工地)
    public static bool Start(City city, BuildingKind asset, int zone = -1)
    {
        if (city?.Record == null || asset?.Id == null) return false;
        CityWorks works = Ensure(city);
        if (works.building != null) return false;
        works.building = asset.Id;
        works.building_zone = zone;
        works.progress = 0;
        return true;
    }

    // 推进在建的那一座，返回用掉的施工点；够了就记成一座
#if EC_PENDING
    public static int Advance(City city, int points)
    {
        CityWorks works = Works(city);
        if (works?.building == null || points <= 0) return 0;
        BuildingKind asset = Asset(works.building);
        if (asset == null)
        {
            works.building = null;
            return 0;
        }
        int needed = Mathf.Max(1, asset.ConstructionProgressNeeded + 1 - works.progress);
        int share = Mathf.Min(needed, points);
        works.progress += share;
        if (share < needed) return share;
        int zone = works.building_zone;
        works.building = null;
        works.building_zone = -1;
        works.progress = 0;
        Add(city, asset, zone);
        return share;
    }
#endif

    // 照原版城市规划开下一处工地(同 CityBehBuild.buildTick，不修路)：挑一张可建的订单；升级订单把一座升一级(照原版扣造价)；
    // 记台账的建筑照原版扣造价后只记在建，别的(码头、公所之类)照旧落成实体。手动城区的城要城区里还有空的建筑位。
    // 返回 true 表示开了工或升了级
#if EC_PENDING
    public static bool BuildTick(City city)
    {
        if (!Uses(city)) return CityBehBuild.buildTick(city);
        if (Pending(city) != null || RealConstruction(city)) return false;
        if (AssetManager.city_build_orders.get(city.getActorAsset()?.build_order_template_id ?? "") == null) return false;
        CityBehBuild._possible_buildings.Clear();
        CityBehBuild._possible_buildings_no_resources.Clear();
        CityBehBuild.calcPossibleBuildings(city);
        BuildOrder order = CityBehBuild._possible_buildings.Count == 0 ? null : CityBehBuild._possible_buildings.GetRandom();
        CityBehBuild._possible_buildings.Clear();
        CityBehBuild._possible_buildings_no_resources.Clear();
        BuildingKind asset = order?.getBuildingAsset(city);
        if (asset == null) return false;
        if (order.upgrade) return UpgradeOne(city, asset.Id, true);
        if (!Virtual(asset)) return CityBehBuild.tryToBuild(city, asset) != null;
        if (!TechnologySystem.CanBuild(city, asset.Id) || !city.hasEnoughResourcesFor(asset.cost)) return false;
        int zone = -1;
        if (CityZoningSystem.Manual(city) && asset.Type != StockpileType)
        {
            zone = CityZoningSystem.FreeZone(city);
            if (zone < 0) return false;
        }
        if (!Start(city, asset, zone)) return false;
        city.spendResourcesForBuildingAsset(asset.cost);
        return true;
    }
#endif

    private static bool RealConstruction(City city)
    {
        if (city.Buildings == null) return false;
        foreach (Building building in city.Buildings)
            if (building?.Asset != null && building.IsUnderConstruction()) return true;
        return false;
    }

    // 把一座这种建筑升一级：实体与台账里的按座数随机挑；spend 为真时照原版扣高一级的造价(不够不升)
#if EC_PENDING
    public static bool UpgradeOne(City city, string id, bool spend)
    {
        List<Building> real = city.getBuildingListOfID(id);
        int realCount = real?.Count ?? 0, ledger = CountId(city, id);
        if (realCount + ledger <= 0) return false;
        if (EcRandom.Range(0, realCount + ledger) < realCount)
        {
            Building building = real.GetRandom();
            if (building == null) return false;
            return spend ? CityBehBuild.upgradeBuilding(building, city) : building.upgradeBuilding();
        }
        BuildingKind to = UpgradeTarget(city, Asset(id));
        if (to == null || spend && !city.hasEnoughResourcesFor(to.cost)) return false;
        if (!Swap(city, id, to)) return false;
        if (spend) city.spendResourcesForBuildingAsset(to.cost);
        return true;
    }
#endif

    #endregion

    #region 仓储

    private static Dictionary<string, int> Stock(City city)
    {
        CityPopulationData population = city.GetOrCreate().population;
        if (population == null) return null;
        return population.settlement_supplies ??= new Dictionary<string, int>();
    }

    // 账面库存里这种货的上限：原版一座仓库装到资源自己的上限，这里 × 仓库座数(至少算一座)
#if EC_PENDING
    public static int StockCap(City city, string resource)
    {
        ResourceKind asset = ResourceKinds.Table.dict.TryGetValue(resource ?? "", out ResourceKind found) ? found : null;
        int single = asset == null || asset.maximum <= 0 ? 999 : asset.maximum;
        long cap = (long)single * Mathf.Max(1, CountType(city, StockpileType));
        return (int)Math.Min(int.MaxValue, cap);
    }
#endif

    #endregion

    #region 旧建筑并入

#if EC_PENDING
    private static bool Foldable(City city, Building building) =>
        building?.Asset != null && !building.IsGone() && !building.IsOnRemove() && !building.isRuin() &&
        building.Cell?.Zone?.City == city && Virtual(building.Asset);
#endif

    // 把本城可记账的建筑实体记进台账：货挪进账面库存，在建的接着建(台账里已有工地时先留着，等它完工)，实体拆掉。返回并了几座
#if EC_PENDING
    public static int Fold(City city)
    {
        if (!Uses(city) || city.Buildings == null || city.Buildings.Count == 0) return 0;
        List<Building> take = null;
        foreach (Building building in city.Buildings)
            if (Foldable(city, building)) (take ??= new List<Building>()).Add(building);
        if (take == null) return 0;
        HashSet<int> urban = CityZoningSystem.UrbanSet(city);
        int folded = 0;
        foreach (Building building in take)
        {
            try
            {
                BuildingKind asset = building.Asset;
                int at = building.Cell.Zone.Index;
                int zone = urban != null && asset.Type != StockpileType && urban.Contains(at) ? at : -1;
                if (building.IsUnderConstruction())
                {
                    if (Pending(city) != null || !Start(city, asset, zone)) continue;
                    Works(city).progress = Mathf.Max(0, building.getConstructionProgress());
                }
                else
                {
                    MoveStock(city, building);
                    Add(city, asset, zone);
                }
                if (city.under_construction_building == building) city.under_construction_building = null;
                building.startRemove();
                folded++;
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][建筑台账] 并入失败({city.Name}): {exception.Message}");
            }
        }
        return folded;
    }

    private static void MoveStock(City city, Building building)
    {
        CityResources resources = building.resources;
        if (resources == null) return;
        Dictionary<string, int> stock = null;
        foreach (CityStorageSlot slot in resources.getSlots())
        {
            if (slot == null || slot.amount <= 0 || string.IsNullOrEmpty(slot.id)) continue;
            stock ??= Stock(city);
            if (stock == null) return;
            stock[slot.id] = (int)Math.Min(int.MaxValue, (long)(stock.TryGetValue(slot.id, out int have) ? have : 0) + slot.amount);
        }
        if (stock != null) city.StorageVersion++;
    }
#endif

    private const int CitiesPerFrame = 4;
    private static int _cursor, _foldedTotal;
    private static object _tickWorld;

    // 每帧(纯净地图，见 PureMapMode.Tick)：轮着查几座城，有可记账的实体就并进台账
#if EC_PENDING
    public static void Tick()
    {
        EmpireWorld world = EmpireWorld.Current;
        if (!Active || world?.Cities == null) return;
        if (!ReferenceEquals(_tickWorld, world))
        {
            _tickWorld = world;
            _cursor = 0;
            _foldedTotal = 0;
        }
        List<City> cities = world.Cities;
        int count = cities?.Count ?? 0;
        if (count == 0) return;
        for (int i = 0; i < CitiesPerFrame && i < count; i++)
        {
            if (_cursor >= count)
            {
                _cursor = 0;
                if (_foldedTotal > 0) LogService.LogInfo($"[EmpireCraft][建筑台账] 这一轮并入建筑 {_foldedTotal} 座");
                _foldedTotal = 0;
            }
            City city = cities[_cursor++];
            if (Uses(city)) _foldedTotal += Fold(city);
        }
    }
#endif

    #endregion
}
}
