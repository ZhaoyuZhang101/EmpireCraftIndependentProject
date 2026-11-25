// Generated from the EmpireCraft mod: Scripts/GeneralSystems/SteppeOrderSystem.Feuds.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 2. 部落争草场(仿神罗私战"只争边城、不吞并、限期议和"，见 HolyRomanSystem.Feuds)：每年，相邻的两个游牧政权——
//    都是一方之主(独立的国或帝国核心)而不在同一帝国，或同一汗国的两个兀鲁思——为牧场打一场小仗(EmpireWarType.草场之争)：
//    · 开战率见 SteppeRules.FeudChance：两边草场压力(草原压力里牲畜粮食那一项)越大越容易打，攻方强出一半再加，比对方弱打折；
//      攻方兵力(国力)要有守方八成；部落阶段(原始政体)的不打(部落之间照部落联合的规矩)；同一对十年内不再打；
//      每国每年至多挑起一场，全天下每年至多 MaxFeudsPerYear 场；不在同一帝国的要过开战闸门(停战、同盟、岁币……见 WarGate)，
//      同一汗国的两个兀鲁思只看停战年限；
//    · 只争守方挨着攻方的非都城，至多两座(小的在前)，没有就打不起来；两家之间别的城打不下(见 BlocksPastureCity)；
//      不拉姻亲、不扩封建、不升级世界大战(见 HolyRomanSystem.ContainedWar)；
//    · 打满 FeudYears 年强制议和。攻方胜或议和：争的城里攻方占了一半以上地块的割给攻方；守方胜：攻方赔款、打下的争城退还；
//    · 劫掠被打退后反目的部与盟主之间也打这样一场(见 NomadRaidSystem.Hero)。
public static partial class SteppeOrderSystem
{
    private const int FeudYears = 2, FeudPairCooldownYears = 10, FeudMaxCities = 2, MaxFeudsPerYear = 4;
    private const float FeudMinStrength = 0.8f, FeudHeldShare = 0.5f;

    // 正在开争草场之战(startWar 前后)：开战钩子据此不拉姻亲、不扩封建、不升级世界大战
    public static bool Declaring;

    private static bool Alive(Realm kingdom) =>
        kingdom?.Record != null && !kingdom.IsGone() && !kingdom.IsWild && kingdom.HasCapital() && kingdom.Cities?.Count > 0;

    private static bool IsRealm(Realm kingdom) =>
        Alive(kingdom) && (kingdom.GetEmpire() is not Empire empire || empire.CoreKingdom == kingdom);

    private static Realm LiveKingdom(long id)
    {
        Realm kingdom = id >= 0 ? EmpireWorld.Current.RealmByKey(id) : null;
        return kingdom?.Record != null && !kingdom.IsGone() ? kingdom : null;
    }

    private static float YearsSince(double at) =>
        at < 0d || GameClock.Now < at ? float.MaxValue : GameClock.YearsSince(at);

    // 兀鲁思：汗国(游牧帝国)里游牧政体的成员(核心、行省、羁縻不算)
#if EC_PENDING
    private static bool IsUlus(Realm kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        if (!Alive(kingdom) || empire?.CoreKingdom == null || empire.CoreKingdom == kingdom || !NomadRaidSystem.IsNomad(empire.CoreKingdom))
            return false;
        if (kingdom.GetKingdomType() == KingdomType.YouMu_province || JimiSystem.IsJimiAdministration(kingdom) ||
            JimiSystem.IsJimiRegion(kingdom, empire)) return false;
        return kingdom.GetRegime()?.type is RegimeType.YouMu or RegimeType.NomadFeudalism;
    }
#endif

    // 能为草场兴兵：游牧、走出部落阶段、有君、不在打仗、不在起事
#if EC_PENDING
    private static bool FeudCapable(Realm kingdom) =>
        Alive(kingdom) && NomadRaidSystem.IsNomad(kingdom) && !TribalFissionService.IsTribal(kingdom) && kingdom.HasRuler() &&
        !kingdom.HasEnemies() && !kingdom.IsFactionRebelling() && !kingdom.IsLocalRebelling();
#endif

    // 两家能不能争草场(不看机会)：同为一方之主而不在同一帝国(过开战闸门)，或同一汗国的两个兀鲁思(只看停战年限)
#if EC_PENDING
    private static bool FeudPair(Realm attacker, Realm defender)
    {
        if (attacker == defender || !FeudCapable(attacker) || !FeudCapable(defender)) return false;
        if (IsRealm(attacker) && IsRealm(defender))
            return !attacker.IsInSameEmpire(defender) && WarGate.CanDeclare(attacker, defender);
        if (!IsUlus(attacker) || !IsUlus(defender) || attacker.GetEmpire() != defender.GetEmpire()) return false;
        double lastWarEnded = EmpireWorld.Current.diplomacy?.getRelation(attacker, defender)?.data?.timestamp_last_war_ended ?? -1d;
        return (lastWarEnded < 0d || GameClock.YearsSince(lastWarEnded) >= SimGlobals.m.minimum_years_between_wars) &&
               attacker.Ruler != defender.Ruler && !RoyalMarriageSystem.Allied(attacker, defender);
    }
#endif

    private static bool FeudCooling(Realm a, Realm b)
    {
        Dictionary<long, double> record = NomadRaidSystem.State(a).feud_at;
        return record != null && record.TryGetValue(b.Key, out double at) && YearsSince(at) < FeudPairCooldownYears;
    }

    // 争的城：守方挨着攻方的非都城，至多两座，小的在前
    private static List<City> Contested(Realm attacker, Realm defender) =>
        defender.Cities.Where(city => city?.Record != null && !city.IsGone() && city != defender.Capital &&
                                      city.NeighbourCities?.Any(other => other?.Realm == attacker) == true)
            .OrderBy(city => city.Zones?.Count ?? 0).ThenBy(city => city.Key).Take(FeudMaxCities).ToList();

    // 每年(NomadRaidSystem 算完草原压力后调用)
#if EC_PENDING
    private static void FeudsYearly(List<Realm> nomads)
    {
        int started = 0;
        foreach (Realm attacker in nomads.Where(FeudCapable).OrderBy(_ => EcRandom.value).ToList())
        {
            if (started >= MaxFeudsPerYear) return;
            if (!FeudCapable(attacker)) continue;
            double ours = attacker.GetNationalPower();
            float herd = NomadRaidSystem.State(attacker).herd;
            foreach (Realm defender in Neighbours(attacker).OrderBy(_ => EcRandom.value))
            {
                if (!FeudPair(attacker, defender) || FeudCooling(attacker, defender)) continue;
                double theirs = Math.Max(1d, defender.GetNationalPower());
                if (ours < theirs * FeudMinStrength) continue;
                float chance = SteppeRules.FeudChance(herd, NomadRaidSystem.State(defender).herd, ours / theirs);
                if (EcRandom.value >= chance) continue;
                if (!DeclareFeud(attacker, defender, "steppe_feud_history")) continue;
                started++;
                break;
            }
        }
    }
#endif

    private static List<Realm> Neighbours(Realm kingdom)
    {
        var result = new List<Realm>();
        foreach (City city in kingdom.Cities)
            foreach (Realm next in city?.NeighbourRealms ?? new HashSet<Realm>())
                if (next != null && next != kingdom && !result.Contains(next)) result.Add(next);
        return result;
    }

    // 劫掠被打退后反目的部与盟主打一场(见 NomadRaidSystem.Hero)：不掷机会、不看强弱与冷却
#if EC_PENDING
    public static bool Rift(Realm attacker, Realm defender) =>
        FeudPair(attacker, defender) && DeclareFeud(attacker, defender, "nomad_raid_rift_war_history");

    private static bool DeclareFeud(Realm attacker, Realm defender, string historyKey)
    {
        List<City> cities = Contested(attacker, defender);
        if (cities.Count == 0) return false;
        War war;
        Declaring = true;
        try
        {
            war = EmpireWorld.Current.StartWar(attacker, defender, WarKinds.Normal);
            if (war?.Record != null) war.SetEmpireWarType(EmpireWarType.草场之争);
        }
        finally
        {
            Declaring = false;
        }
        if (war?.Record == null) return false;
        string names = string.Join("、", cities.Select(city => city.GetCityName()));
        war.Name = string.Format(LM.Get("steppe_feud_war_name"), attacker.GetKingdomName(), cities[0].GetCityName());
        war.GetOrCreate().pasture = new PastureFeudState
        {
            attacker_id = attacker.Key, defender_id = defender.Key, attacker_name = attacker.GetKingdomName(),
            defender_name = defender.GetKingdomName(), city_ids = cities.Select(city => city.Key).ToList(), city_names = names
        };
        double now = GameClock.Now;
        (NomadRaidSystem.State(attacker).feud_at ??= new Dictionary<long, double>())[defender.Key] = now;
        (NomadRaidSystem.State(defender).feud_at ??= new Dictionary<long, double>())[attacker.Key] = now;
        Chronicle(string.Format(LM.Get(historyKey), attacker.GetKingdomName(), defender.GetKingdomName(), names), attacker, defender);
        LogService.LogInfo($"{LogTag} 争草场：{attacker.GetKingdomName()}(牲畜粮食压力{NomadRaidSystem.State(attacker).herd:0}，" +
                           $"国力{attacker.GetNationalPower():0})争{defender.GetKingdomName()}" +
                           $"(压力{NomadRaidSystem.State(defender).herd:0}，国力{defender.GetNationalPower():0})的{names}");
        return true;
    }
#endif

    private static List<War> FeudWars() =>
        EmpireWorld.Current.LiveWars.Where(war => war?.Record != null && war.Alive && !war.HasEnded() &&
                                           war.GetEmpireWarType() == EmpireWarType.草场之争).ToList();

    // 每月：打满两年强制议和
    private static void FeudsMonthly()
    {
        foreach (War war in FeudWars())
            if (WarSituation.WarYears(war) >= FeudYears)
                EmpireWorld.Current.EndWar(war, WarOutcome.Peace);
    }

    // 争草场进行中：两家之间不在争城清单里的城一律打不下(CityPatch 攻城结算调用)
    public static bool BlocksPastureCity(Realm newKingdom, City city)
    {
        Realm holder = city?.Realm;
        if (newKingdom?.Record == null || holder?.Record == null || holder == newKingdom) return false;
        try
        {
            foreach (War war in newKingdom.GetWars())
            {
                if (war?.Record == null || war.HasEnded() || war.GetEmpireWarType() != EmpireWarType.草场之争) continue;
                if (!(war.IsAttacker(newKingdom) && war.IsDefender(holder) || war.IsDefender(newKingdom) && war.IsAttacker(holder)))
                    continue;
                PastureFeudState record = war.GetOrCreate().pasture;
                return record != null && !record.city_ids.Contains(city.Key);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{LogTag} 争草场拦城失败: {exception.Message}");
        }
        return false;
    }

    // 收场(WarPatch.end_war)：攻方胜或议和，争城里攻方占了一半以上地块的割给攻方；守方胜，攻方赔款、打下的争城退还
#if EC_PENDING
    public static void OnWarEnded(War war, WarOutcome winner)
    {
        if (war?.Record == null || war.GetEmpireWarType() != EmpireWarType.草场之争) return;
        PastureFeudState record = war.GetOrCreate().pasture;
        if (record == null || record.settled) return;
        record.settled = true;
        try
        {
            Realm attacker = LiveKingdom(record.attacker_id), defender = LiveKingdom(record.defender_id);
            bool defendersWon = winner == WarOutcome.Defenders;
            var taken = new List<string>();
            int returned = 0;
            foreach (long id in record.city_ids)
            {
                City city = EmpireWorld.Current.CityByKey(id);
                if (city?.Record == null || city.IsGone() || attacker == null) continue;
                if (defendersWon)
                {
                    if (defender != null && city.Realm == attacker)
                    {
                        WarGoalSystem.QueueCession(city, defender);
                        returned++;
                    }
                    continue;
                }
                if (city.Realm == attacker) taken.Add(city.GetCityName());
                else if (city.Realm == defender && city.Zones?.Count > 0 &&
                         city.GetOccupiedTileZoneCount(attacker) >= city.Zones.Count * FeudHeldShare)
                {
                    WarGoalSystem.QueueCession(city, attacker);
                    taken.Add(city.GetCityName());
                }
            }
            string text;
            if (defendersWon)
            {
                if (attacker != null && defender != null) WarReparationsSystem.Impose(attacker, defender);
                text = string.Format(LM.Get("steppe_feud_lost_history"), record.attacker_name, record.defender_name, returned);
            }
            else
                text = taken.Count > 0
                    ? string.Format(LM.Get("steppe_feud_won_history"), record.attacker_name, record.defender_name, string.Join("、", taken))
                    : string.Format(LM.Get("steppe_feud_draw_history"), record.attacker_name, record.defender_name);
            Chronicle(text, attacker, defender);
            LogService.LogInfo($"{LogTag} 争草场收场({winner})：{text}");
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"{LogTag} {war.Name} 争草场收场失败: {exception.Message}");
        }
    }
#endif

    // 记一笔：世界日志一条，两家所在的帝国史书各一条
#if EC_PENDING
    private static void Chronicle(string text, params Realm[] involved)
    {
        Realm first = null;
        var empires = new HashSet<Empire>();
        foreach (Realm kingdom in involved)
        {
            if (kingdom?.Record == null || kingdom.IsGone()) continue;
            first ??= kingdom;
            Empire empire = kingdom.GetEmpire();
            if (empire?.data != null && !empire.IsGone() && empires.Add(empire))
                empire.RecordHistory(directContent: text, kingdomId: kingdom.Key);
        }
        TranslateHelper.LogEventOnly(text, first);
    }
#endif
}
}
