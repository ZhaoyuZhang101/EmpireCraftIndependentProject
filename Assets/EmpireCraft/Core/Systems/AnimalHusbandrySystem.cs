// Generated from the EmpireCraft mod: Scripts/GeneralSystems/AnimalHusbandrySystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace EmpireCraft.Core
{

// 畜牧与屠宰：城市的肉、皮革、骨头按地形和生产力自动产出，不靠地图上的动物
// (无小人模式下全图野生动物每城只留两只点缀，见 NoCommonersPatch.WildCap)。
//   - 牧场(大型建筑，建在农耕区或空地)：牧群按本城草场(空着的可耕地)多少决定容量，每月自行繁殖补栏，
//     每年按头数产肉和皮革；领地里碰到的家畜也顺手圈进牧群；
//   - 屠宰场(建在空地，制革科技解锁)：按本城山林(树木)多少每月猎得野味，折成肉、皮革、骨头；
//     领地里的野兽一并猎取；牧群超过容量八成时宰掉多余的部分；
//   - 产出乘农业生产力(古代 1，现代 3)；
//   - 游牧制度(制度线 Youmu)的国家：牧场容量、圈养数量翻倍，牧群繁殖快一半，每城可多建两座牧场。
//   纯净地图下不建牧场、屠宰场：放牧是城外牧村的生计，牧村折成几座牧场，研究了制革牧村、林场村再折出屠宰场
//   (见 LivelihoodSystem.Pastures/Slaughterhouses；游牧城照三座牧场)。
// 每月一次：先把全图动物按所在城市领地分组，再逐城结算(分帧)。
public static class AnimalHusbandrySystem
{
    public const string PastureType = "type_pasture";
    public const string SlaughterhouseType = "type_slaughterhouse";

    public const float HerdPerPasture = 60f;
    private const int CapturePerPasture = 15;
    private const int HuntPerSlaughterhouse = 25;
    private const float HerdGrowthPerMonth = 0.015f;
    // 牧群每头每年：肉、皮革
    private const float HerdMeatPerYear = 0.6f;
    private const float HerdLeatherPerYear = 0.25f;
    // 牧群超过容量这么多时，屠宰场宰到 SlaughterDownTo
    private const float SlaughterAbove = 0.8f;
    private const float SlaughterDownTo = 0.6f;

    // 可以圈养的家畜
    private static readonly HashSet<string> Livestock = new()
        { "sheep", "cow", "chicken", "goat", "alpaca", "buffalo", "ostrich", "rabbit", "capybara" };

    // 按野生阵营区分体型：(肉, 皮革, 骨头, 折合牧群头数)
    private static readonly HashSet<string> Large = new() { "cow", "buffalo", "rhino", "bear", "crocodile", "seal" };
    private static readonly HashSet<string> Medium = new()
    {
        "sheep", "goat", "alpaca", "ostrich", "wolf", "hyena", "dog", "fox", "monkey", "penguin", "capybara", "cat",
        "raccoon", "armadillo", "turtle", "snake"
    };
    private static readonly HashSet<string> Small = new()
        { "chicken", "rabbit", "rat", "frog", "crab", "lemon_snail", "piranha", "scorpion" };

    // 农田害虫：城市领地内农田上的每月清除(除虫)，每城最多 PestsPerCity 只
    private static readonly HashSet<string> Pests = new() { "fly", "grasshopper", "beetle", "insect" };
    private const int PestsPerCity = 60;

#if EC_PENDING
    private static bool IsPest(ActorAsset asset) =>
        asset != null && !asset.civ && (Pests.Contains(asset.id) || Pests.Contains(asset.kingdom_id_wild ?? ""));

    public static bool IsGame(ActorAsset asset)
    {
        if (asset == null || asset.civ || asset.is_boat) return false;
        string wild = asset.kingdom_id_wild ?? "";
        return Large.Contains(wild) || Medium.Contains(wild) || Small.Contains(wild);
    }

    private static (float meat, float leather, float bones, float head) Yield(ActorAsset asset)
    {
        string wild = asset.kingdom_id_wild ?? "";
        if (Large.Contains(wild)) return (6f, 3f, 2f, 2f);
        if (Medium.Contains(wild)) return (3f, 1.5f, 1f, 1f);
        return (1f, 0.4f, 0.3f, 0.3f);
    }

    public static bool IsNomadic(Realm kingdom)
    {
        if (kingdom == null || kingdom.IsWild || kingdom.IsGone()) return false;
        try
        {
            // 按文化特性不按制度线：游牧一系也含阿兹特克、林地部族，他们不是游牧(见 OnomasticsRule.NomadTrait)
            return EmpireCraft.Core.OnomasticsRule.IsNomadCulture(CultureService.GetRealmCulture(kingdom));
        }
        catch
        {
            return false;
        }
    }

    public static int CountBuildings(City city, string type)
    {
        int count = 0;
        if (city?.buildings == null) return 0;
        foreach (Building building in city.buildings)
            if (building?.asset?.type == type && !building.isUnderConstruction()) count++;
        return count;
    }
#endif

    // 牧场、屠宰场几座(纯净地图：牧村、林场村折算，见 LivelihoodSystem)
#if EC_PENDING
    public static float Pastures(City city) =>
        LivelihoodSystem.Active ? LivelihoodSystem.Pastures(city) : CountBuildings(city, PastureType);

    public static float Slaughterhouses(City city) =>
        LivelihoodSystem.Active ? LivelihoodSystem.Slaughterhouses(city) : CountBuildings(city, SlaughterhouseType);

    public static float HerdCapacity(City city) =>
        Pastures(city) * HerdPerPasture * (IsNomadic(city?.Realm) ? 2f : 1f) * (0.4f + 1.2f * Rangeland(city));
#endif

    // 地形：草场比例(城市区块里空着的可耕地占比)、山林程度(每块区块平均树木数 / 8，最多 1)。每月算一次缓存
    private static readonly Dictionary<City, (double at, float range, float wild)> Terrain = new();

#if EC_PENDING
    public static float Rangeland(City city) => TerrainOf(city).range;
    public static float Wildness(City city) => TerrainOf(city).wild;

    private static (double at, float range, float wild) TerrainOf(City city)
    {
        if (city?.Zones == null || city.Zones.Count == 0) return (0d, 0f, 0f);
        double now = GameClock.Now;
        if (Terrain.TryGetValue(city, out (double at, float range, float wild) cached) && now >= cached.at &&
            GameClock.MonthsSince(cached.at) < 1) return cached;
        (float range, float wild) = MeasureTerrain(city);
        (double, float, float) result = (now, range, wild);
        Terrain[city] = result;
        return result;
    }
#endif

    // 每月开一轮时把各城的草场、山林程度分给几个核一起数(只读地块，各城互不相干)，主线程收回来记进缓存，
    // 这个月里谁来问都是现成的。以前轮到哪座城现数：六百城一个月要过上百万格，流速三千时占主线程 3.5%
    // 只在独立核心里这样做(2026-11-24 群友"11/5 那版更流畅，之后越来越卡")：游戏里主线程要在这一帧干等各核把全图数完，
    // 每月卡一下(核少的机器更明显)；游戏里照旧轮到哪座城现数、记一个月，散在各帧里
#if EC_PENDING
    private static void PrewarmTerrain(List<City> cities, double now)
    {
        if (!SimulationFrameBudget.ServerMilliseconds.HasValue) return;
        if (ParallelWork.MaxWorkers <= 1 || cities.Count < 8) return;
        var measured = new (bool ok, float range, float wild)[cities.Count];
        ParallelWork.For(cities.Count, index =>
        {
            City city = cities[index];
            if (city?.Record == null || city.Zones == null || city.Zones.Count == 0) return;
            try
            {
                (float range, float wild) = MeasureTerrain(city);
                measured[index] = (true, range, wild);
            }
            catch (Exception)
            {
                // 这座城留给 TerrainOf 现数
            }
        });
        for (int i = 0; i < cities.Count; i++)
            if (measured[i].ok) Terrain[cities[i]] = (now, measured[i].range, measured[i].wild);
    }
#endif

    // 工作线程也调：只读城的地块，不碰别的
#if EC_PENDING
    private static (float range, float wild) MeasureTerrain(City city)
    {
        int tiles = 0, trees = 0;
        float open = 0f;
        foreach (Cell zone in city.Zones)
        {
            if (zone?.Tiles == null) continue;
            trees += zone.getHashset(BuildingList.Trees)?.Count ?? 0;
            foreach (Cell tile in zone.Tiles)
            {
                if (tile?.Type == null || tile.Type.Liquid) continue;
                tiles++;
                // 可放牧：能耕(草地、荒漠、稀树草原……)又没开成田、没盖房的空地；山石(连同岩地、冰封丘陵)不算，同 LandKinds。
                // 按水草算：草地一整格，稀树草原、林地八成，沼泽四成，荒原两成五，沙漠一成五，沙地一成
                // 高寒高原不能耕，是草甸牧场(牦牛)，照样算可放牧
                bool alpine = PlateauTerrain.IsAlpine(tile) && !tile.Type.FarmField;
                if ((tile.Type.CanBeFarm || alpine) && !tile.Type.FarmField && !tile.hasBuilding() && !LandKinds.Is(tile.Type, LandKinds.Rough))
                    open += Grazing(alpine ? tile.MainType : tile.Type);
            }
        }
        return (tiles == 0 ? 0f : open / tiles, Mathf.Clamp01(trees / (city.Zones.Count * 8f)));
    }

    private static float Grazing(TerrainKind type)
    {
        // 高寒草甸六成，黄土高原八成，草原高原一整格
        if (PlateauTerrain.IsPlateau(type))
            return ReferenceEquals(type, PlateauTerrain.Alpine) ? 0.6f : ReferenceEquals(type, PlateauTerrain.Steppe) ? 1f : 0.8f;
        switch (type.BiomeId)
        {
            case "biome_desert": return 0.15f;
            case "biome_wasteland": return 0.25f;
            case "biome_savanna": return 0.8f;
            case "biome_sand": return 0.1f;
        }
        int kinds = LandKinds.Of(type);
        if ((kinds & LandKinds.Sand) != 0) return 0.1f;
        if ((kinds & LandKinds.Wet) != 0) return 0.4f;
        if ((kinds & (LandKinds.Forest | LandKinds.Woodland)) != 0) return 0.8f;
        return 1f;
    }
#endif

    // 狩猎：每座屠宰场每月按山林程度猎得的野味(折合中型野兽只数)
    private const float HuntPerSlaughterhouseMonth = 6f;
    // 牧群每月自行补栏(占容量的比例)
    private const float RestockPerMonth = 0.04f;

    // ---- 每月一轮 ----
    private static readonly Queue<City> Pending = new();
    private static readonly Dictionary<City, List<Person>> AnimalsByCity = new();
    private static readonly Dictionary<City, List<Person>> PestsByCity = new();
    private static double _lastPass = -1d;
    private const double SliceBudgetMs = 1.5d;
    private static object _world;
    private static readonly HashSet<City> Wanted = new();
    private static List<City> _groupCities;
    private static List<Person> _groupActors;
    private static int _groupCityIndex, _groupActorIndex, _groupActorLimit;
    private static bool _pestControl;

    public static void ResetWorldState()
    {
        _world = EmpireWorld.Current;
        _lastPass = -1d;
        Pending.Clear();
        Wanted.Clear();
        AnimalsByCity.Clear();
        PestsByCity.Clear();
        Terrain.Clear();
        _groupCities = null;
        _groupActors = null;
    }

#if EC_PENDING
    public static void Tick()
    {
        EmpireWorld world = EmpireWorld.Current;
        if (world?.Cities == null || GameClock.Paused || !GameClock.Running || GameClock.Loading) return;
        if (!ReferenceEquals(_world, world)) ResetWorldState();
        double now = world.Now();
        if (Pending.Count == 0 && _groupCities == null)
        {
            if (_lastPass >= 0d && now >= _lastPass && GameClock.MonthsSince(_lastPass) < 1) return;
            _lastPass = now;
            AnimalsByCity.Clear();
            PestsByCity.Clear();
            Wanted.Clear();
            _groupCities = new List<City>(world.Cities);
            PrewarmTerrain(_groupCities, now);
            _groupActors = world.Living.getSimpleList();
            _groupActorLimit = _groupActors.Count;
            _groupCityIndex = _groupActorIndex = 0;
            _pestControl = CityPopulationSystem.AbstractPopulationEnabled;
        }
        Stopwatch watch = Stopwatch.StartNew();
        if (_groupCities != null)
        {
            while (_groupCityIndex < _groupCities.Count && (!_pestControl ||
                   watch.Elapsed.TotalMilliseconds < SliceBudgetMs && SimulationFrameBudget.HasTime))
            {
                City city = _groupCities[_groupCityIndex++];
                if (city?.Record == null || city.IsGone() || city.Realm == null || city.Realm.IsWild) continue;
                Pending.Enqueue(city);
                if (CountBuildings(city, PastureType) > 0 || CountBuildings(city, SlaughterhouseType) > 0) Wanted.Add(city);
            }
            if (_groupCityIndex < _groupCities.Count) return;
            if (Wanted.Count == 0 && !_pestControl) _groupActorIndex = _groupActorLimit;
            while (_groupActorIndex < Math.Min(_groupActorLimit, _groupActors.Count) &&
                   (!_pestControl || watch.Elapsed.TotalMilliseconds < SliceBudgetMs && SimulationFrameBudget.HasTime))
                GroupAnimal(_groupActors[_groupActorIndex++]);
            if (_groupActorIndex < Math.Min(_groupActorLimit, _groupActors.Count)) return;
            _groupCities = null;
            _groupActors = null;
            if (!_pestControl) watch.Restart(); // 普通模式维持一次分组后开始计畜牧结算预算。
        }
        while (Pending.Count > 0 && watch.Elapsed.TotalMilliseconds < SliceBudgetMs && SimulationFrameBudget.HasTime)
        {
            City city = Pending.Dequeue();
            if (city?.Record == null || city.IsGone()) continue;
            try
            {
                Settle(city);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][畜牧] 结算失败({city.Name}): {exception.Message}");
            }
        }
        if (Pending.Count == 0)
        {
            if (Terrain.Count > world.Cities.Count * 2) Terrain.Clear();
            Wanted.Clear();
            AnimalsByCity.Clear();
            PestsByCity.Clear();
        }
    }
#endif

    // 只看有牧场或屠宰场的城的领地，省得每月给所有动物分组
#if EC_PENDING
    private static void GroupAnimal(Person actor)
    {
            if (actor?.Record == null || actor.IsGone() || !actor.Alive || actor.asset == null) return;
            if (actor.isFavorite() || actor.isCameraFollowingUnit()) return;
            Cell tile = actor.Cell;
            City owner = tile?.Zone?.City;
            if (owner == null) return;
            // 无小人模式没有农民赶虫：农田(田地、规划农耕区)上的害虫由城市每月清除
            if (_pestControl && IsPest(actor.asset) &&
                (tile.Type != null && tile.Type.FarmField || FarmlandSystem.IsPlanned(owner, tile.Zone)))
            {
                if (!PestsByCity.TryGetValue(owner, out List<Person> pests)) PestsByCity[owner] = pests = new List<Person>();
                if (pests.Count < PestsPerCity) pests.Add(actor);
                return;
            }
            if (!IsGame(actor.asset) || !Wanted.Contains(owner)) return;
            if (!AnimalsByCity.TryGetValue(owner, out List<Person> list)) AnimalsByCity[owner] = list = new List<Person>();
            list.Add(actor);
    }

    private static void Settle(City city)
    {
        if (PestsByCity.TryGetValue(city, out List<Person> pestList))
            foreach (Person pest in pestList)
                if (pest != null && !pest.IsGone() && pest.Alive && pest.Cell?.Zone?.City == city) Remove(pest);
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data == null) return;
        float pastures = Pastures(city);
        float slaughterhouses = Slaughterhouses(city);
        bool nomadic = IsNomadic(city.Realm);
        float capacity = HerdCapacity(city);
        if (data.herd > capacity) data.herd = capacity;
        AnimalsByCity.TryGetValue(city, out List<Person> animals);
        animals?.RemoveAll(animal => animal?.asset == null || animal.IsGone() || !animal.Alive ||
            animal.Cell?.Zone?.City != city);
        float meat = 0f, leather = 0f, bones = 0f;

        // 牧场：圈进家畜
        if (pastures > 0 && animals != null)
        {
            int limit = Mathf.RoundToInt(pastures * CapturePerPasture * (nomadic ? 2 : 1));
            for (int i = animals.Count - 1; i >= 0 && limit > 0 && data.herd < capacity; i--)
            {
                Person animal = animals[i];
                if (animal?.asset == null || !Livestock.Contains(animal.asset.kingdom_id_wild ?? "")) continue;
                data.herd += Yield(animal.asset).head;
                Remove(animal);
                animals.RemoveAt(i);
                limit--;
            }
        }
        // 牧群：自行补栏、繁殖、产出
        if (capacity > 0f)
            data.herd = Mathf.Min(capacity, data.herd + capacity * RestockPerMonth * (nomadic ? 1.5f : 1f) +
                                            data.herd * HerdGrowthPerMonth * (nomadic ? 1.5f : 1f));
        if (data.herd > 0f)
        {
            meat += data.herd * HerdMeatPerYear / 12f;
            leather += data.herd * HerdLeatherPerYear / 12f;
        }
        // 屠宰场：猎取领地里的野兽，宰掉多余的牧群
        if (slaughterhouses > 0)
        {
            // 按山林程度自动猎得的野味(折合中型野兽：肉 3、皮 1.5、骨 1)
            float game = slaughterhouses * HuntPerSlaughterhouseMonth * (0.3f + Wildness(city));
            meat += game * 3f;
            leather += game * 1.5f;
            bones += game;
            int limit = Mathf.RoundToInt(slaughterhouses * HuntPerSlaughterhouse);
            if (animals != null)
                for (int i = animals.Count - 1; i >= 0 && limit > 0; i--)
                {
                    Person animal = animals[i];
                    if (animal?.asset == null) continue;
                    (float m, float l, float b, _) = Yield(animal.asset);
                    meat += m;
                    leather += l;
                    bones += b;
                    Remove(animal);
                    animals.RemoveAt(i);
                    limit--;
                }
            if (capacity > 0f && data.herd > capacity * SlaughterAbove)
            {
                float culled = data.herd - capacity * SlaughterDownTo;
                data.herd -= culled;
                meat += culled * 3f;
                leather += culled * 1.5f;
                bones += culled;
            }
        }
        float productivity = FarmlandSystem.Productivity(city);
        meat *= productivity;
        leather *= productivity;
        bones *= productivity;
        PopulationEconomySystem.Deposit(city, data, "meat", meat);
        PopulationEconomySystem.Deposit(city, data, "leather", leather);
        PopulationEconomySystem.Deposit(city, data, "bones", bones);
        data.last_husbandry_meat = meat;
    }
#endif

    // 不计入死亡统计、不留尸体
    private static void Remove(Person animal)
    {
        try
        {
            animal.Die(true, DeathCause.Other, false, false);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][畜牧] 移除动物失败: {exception.Message}");
        }
    }
}
}
