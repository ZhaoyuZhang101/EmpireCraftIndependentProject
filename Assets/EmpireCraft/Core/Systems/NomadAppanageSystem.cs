// Generated from the EmpireCraft mod: Scripts/GeneralSystems/NomadAppanageSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using Vector2 = EmpireCraft.Core.EcVector2;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 诸子分帐(用户 2026-10-27 截图草原上的"领地"——喀尔喀、郭尔罗斯、阿史德、马尔哈拉……："这些游牧领地从来没有分裂过"
// "一点都不符合游牧传统")：汗死分兀鲁思只给游牧政体的汗国(帝国，见 EnfeoffmentHelper.UlusPartition)，独立的游牧国
// (兀鲁思自立、水土封建化后成了"领地")换君从不分家，存档里几国各换了二十来代君主一次没分过。现在：
//   · 不在帝国里的真游牧国(KingdomExtension.IsTrueNomad：游牧、汗国分封政体，本族游牧或水土游牧化)换了君主，
//     至少 MinCities 城、没在打仗、不是开国雄主新即位、本国 CooldownYears 年内没分过；
//   · 机会 BaseChance + PerCity × (折算城数 − MinCities)，至多 MaxChance；草原一城三五百个地块，折算城数 = 城数 + 地块数 / ZonesPerCity；
//   · 新汗的兄弟(先汗诸子：成年、不是君主、在本国)各领一帐，没有兄弟的由当地首领领一帐，至多 MaxSplits 帐：
//     离汗帐最远的非都城为帐，连成一片的本国城一并带走，每帐至多分到 1/(帐数+1)，汗帐至少留一半的城；
//     分出的帐独立、政体同汗帐；史书记一句。
public static class NomadAppanageSystem
{
    private const int MinCities = 2, MaxSplits = 3, ZonesPerCity = 200;
    private const float BaseChance = 0.3f, PerCity = 0.1f, MaxChance = 0.7f, CooldownYears = 15f;

    // 换了君主的国 → 新汗；setKing 补丁里不当场立国(原版可能正遍历国家列表)，下一帧由 Tick 分
    private static readonly Dictionary<long, long> Pending = new();
    private static object _world;

    private static bool Alive(Realm kingdom) => kingdom?.Record != null && !kingdom.IsGone();

    // 不是帝国的国家换了君主(KingdomPatch)
    public static void OnChieftainSucceeded(Realm kingdom, Person khan)
    {
        if (kingdom?.Record != null && khan?.Record != null) Pending[kingdom.Key] = khan.Key;
    }

    // 每帧(EmpireCraftStrategicScheduler)
#if EC_PENDING
    public static void Tick()
    {
        if (!ReferenceEquals(_world, EmpireWorld.Current))
        {
            _world = EmpireWorld.Current;
            Pending.Clear();
        }
        if (Pending.Count == 0 || EmpireWorld.Current == null) return;
        var due = Pending.ToList();
        Pending.Clear();
        foreach ((long kingdomId, long khanId) in due)
        {
            Realm kingdom = EmpireWorld.Current.RealmByKey(kingdomId);
            Person khan = EmpireWorld.Current.PersonByKey(khanId);
            try
            {
                if (khan == null || kingdom?.Ruler != khan || !Due(kingdom, khan)) continue;
                Partition(kingdom, khan);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][诸子分帐] {kingdom?.GetKingdomName()} 结算失败: {exception.Message}");
            }
        }
    }
#endif

    private static bool Due(Realm kingdom, Person khan)
    {
        // 不许自立(见 SecessionGate)：分出的帐独立，不分
        if (SecessionGate.Forbids("诸子分帐", kingdom.GetKingdomName())) return false;
        if (!Alive(kingdom) || khan?.Record == null || EmpireWorld.Current == null || kingdom.IsInEmpire() || kingdom.Capital == null ||
            !KingdomExtension.IsTrueNomad(kingdom) || AncientWarfareCompatibility.Owns(kingdom)) return false;
        int cities = kingdom.Cities?.Count ?? 0;
        if (cities < MinCities || kingdom.GetWars().Any(war => war != null && !war.HasEnded()) ||
            RulerTraitSystem.FounderShield(kingdom)) return false;
        double last = kingdom.GetOrCreate().nomad_split_at;
        if (last >= 0d && GameClock.Now >= last && GameClock.YearsSince(last) < CooldownYears) return false;
        float scale = cities + kingdom.Cities.Sum(city => city?.Zones?.Count ?? 0) / (float)ZonesPerCity;
        return EcRandom.value < Mathf.Min(MaxChance, BaseChance + PerCity * (scale - MinCities));
    }

#if EC_PENDING
    private static void Partition(Realm kingdom, Person khan)
    {
        int total = kingdom.Cities.Count;
        int keep = (total + 1) / 2;
        Queue<Person> brothers = new(Brothers(kingdom, khan));
        int splits = Mathf.Min(MaxSplits, Mathf.Max(1, brothers.Count), total - keep);
        if (splits <= 0) return;
        int share = Mathf.Max(1, total / (splits + 1));
        City seat = kingdom.Capital;
        Vector2 home = seat.SeatCell()?.PosV ?? Vector2.zero;
        RegimeType? regime = kingdom.GetRegime()?.type;
        var made = new List<Realm>();
        for (int i = 0; i < splits && kingdom.Cities.Count > keep; i++)
        {
            City seed = kingdom.Cities.Where(city => city?.Record != null && !city.IsGone() && city != seat)
                .OrderByDescending(city => Vector2.Distance(home, city.SeatCell()?.PosV ?? home)).FirstOrDefault();
            if (seed == null) break;
            int room = Mathf.Min(share, kingdom.Cities.Count - keep);
            List<City> cluster = Cluster(kingdom, seed, seat, room);
            Person brother = brothers.Count > 0 ? brothers.Dequeue() : null;
            Realm tent = brother != null ? Found(kingdom, cluster, brother)
                : AdministrativeControlSystem.ReleaseToLocal(kingdom, cluster, _ => "independent", out _);
            if (tent == null) break;
            if (regime.HasValue && tent.GetRegime()?.type != regime.Value)
            {
                tent.SetRegimeType(regime.Value);
                tent.LoadRegime();
            }
            EmpireCraft.Core.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(tent);
            made.Add(tent);
        }
        if (made.Count == 0) return;
        kingdom.GetOrCreate().nomad_split_at = GameClock.Now;
        string text = string.Format(LM.Get("nomad_appanage_history"), kingdom.GetKingdomName(), khan.FullName(),
            string.Join(LM.Get("schism_name_separator"), made.Select(tent => tent.GetKingdomName())));
        EventRecorder.Record(kingdom, text, khan);
        TranslateHelper.LogEventMessage(text, kingdom);
        LogService.LogInfo($"[EmpireCraft][诸子分帐] {kingdom.GetKingdomName()} 新汗 {khan.FullName()} 即位，分出 " +
                           $"{string.Join("、", made.Select(tent => $"{tent.GetKingdomName()}({tent.Cities?.Count ?? 0}城)"))}，汗帐剩 {kingdom.Cities.Count}/{total} 城");
    }
#endif

    // 新汗的兄弟(先汗诸子)：成年、不是君主、在本国，年长的在前
    private static List<Person> Brothers(Realm kingdom, Person khan)
    {
        PersonalClanIdentity identity = khan.GetPersonalIdentity();
        if (identity == null) return new List<Person>();
        bool Fit(Person actor) => actor?.Record != null && !actor.IsGone() && actor.Alive && !actor.IsRuler() && actor.Realm == kingdom &&
                                 !RealisticAgeSystem.IsMinor(actor) && actor.IsFitToRule();
        List<PersonalClanIdentity> siblings = KinRoster.Siblings(identity);
        List<Person> result = siblings.Where(KinRoster.OnMap).Select(person => person._actor).Where(Fit).Distinct().ToList();
        // 已经掷中要分帐了：纯净地图下兄弟多是虚拟族人，按长幼落成成年的补够帐数(以前地图上没有就交给当地首领立国)
        int wanted = MaxSplits - result.Count;
        if (wanted > 0)
            result.AddRange(KinRoster.Realize(siblings.Where(person => KinRoster.Virtual(person) && person.age >= KinRoster.AdultAge &&
                                                                       !person.disinherited && KinRoster.KingdomOf(person) == kingdom)
                    .OrderByDescending(person => person.age).Take(wanted), Fit)
                .Where(actor => !result.Contains(actor)));
        return result.OrderByDescending(actor => actor.Age()).ToList();
    }

    // 帐：seed 连同挨着的本国城，至多 room 座，不带汗帐的都城
    private static List<City> Cluster(Realm kingdom, City seed, City seat, int room)
    {
        var cluster = new List<City> { seed };
        var seen = new HashSet<City> { seed, seat };
        for (int i = 0; i < cluster.Count && cluster.Count < room; i++)
            foreach (City next in (IEnumerable<City>)cluster[i].NeighbourCities ?? Array.Empty<City>())
            {
                if (cluster.Count >= room) break;
                if (next?.Record == null || next.IsGone() || !seen.Add(next) || next.Realm != kingdom) continue;
                cluster.Add(next);
            }
        return cluster;
    }

    // 兄弟领帐：帐的头一座城由他自立为国，其余并入
    private static Realm Found(Realm kingdom, List<City> cluster, Person brother)
    {
        City seed = cluster[0];
        Realm tent;
        try
        {
            if (brother.City != seed) brother.JoinCity(seed);
            tent = seed.MakeOwnRealm(brother);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][诸子分帐] {seed.Name} 立帐失败: {exception.Message}");
            return null;
        }
        if (!Alive(tent) || seed.Realm != tent) return null;
        foreach (City city in cluster.Skip(1))
            if (city?.Record != null && !city.IsGone() && city.Realm == kingdom) city.JoinRealm(tent);
        return tent;
    }
}
}
