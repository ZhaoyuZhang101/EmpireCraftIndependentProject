// Ported from the EmpireCraft mod: Scripts/GeneralSystems/NativeDefenseSystem.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 御侮(用户 2026-10-27："本天下的本土帝国面对异族入侵更有号召力，更有战斗欲望，除非对方整体国力大于自己")：
//   · 本土帝国：帝国中央的文化与它都城的主流文化同族(CultureService.SameKin：同文化或同一天下标识)，入主异族之地的不算；
//   · 异族入侵：本帝国(中央或成员)在一场仗里是守方，主攻方(按所在帝国的中央)与本帝国不同族；
//   · 对方整体国力(帝国连同各成员，见 WarSituation.Power)大于本帝国的，以下都不生效；
//   · 战斗欲望：本帝国各军在本族的城(城里主流文化同族)上与入侵者交战，攻守战力 ×Ardor，没有军守城时团练抵抗同样加成
//     (见 FrontSystem.ResolveCity)；这场仗不求和，欠饷也困兽犹斗、倾国而战(见 WarGoalSystem.TrySueForPeace、FightingForSurvival)；
//   · 号召力：每场入侵之战每 RallyYears 年号召一次，同族的本土政权(都城文化也同族、不在本帝国里、与本帝国和入侵者都没在交战、
//     不和入侵者同盟、手上不忙、打得起仗、与本帝国或入侵者接壤)按国力从大到小至多 RallyMax 家，各有 RallyChance 举兵来援
//     (受着天命的共主号召 RallyMandateChance)；已和入侵者议和退出这场仗的不再来。
public static class NativeDefenseSystem
{
    public const float Ardor = 1.2f;
    private const int RallyMax = 3;
    private const float RallyChance = 0.5f, RallyMandateChance = 0.8f, RallyYears = 1f;

    private static object _world;
    private static double _lastMonth = -1d;
    // (本帝国中央, 入侵者中央) → 算的时间、结果；原版一个月 5 个世界秒，一个月算一次
    private static readonly Dictionary<(long, long), (double at, bool value)> Cache = new();
    // 号召过的入侵之战 → 号召的时间
    private static readonly Dictionary<long, double> Rallied = new();

#if EC_PENDING
    private static Realm ModRealm(Realm kingdom) => FrontSystem.Realm(kingdom);
#endif

    private static bool Alive(Realm kingdom) => kingdom?.Record != null && !kingdom.IsGone();

    private static void ResetWorldState()
    {
        Cache.Clear();
        Rallied.Clear();
        _lastMonth = -1d;
    }

#if EC_PENDING
    public static void Tick()
    {
        EmpireWorld world = EmpireWorld.Current;
        if (world?.wars == null || ModClass.IS_CLEAR) return;
        if (!ReferenceEquals(_world, world))
        {
            ResetWorldState();
            _world = world;
        }
        double now = world.Now();
        if (_lastMonth >= 0d && now >= _lastMonth && GameClock.MonthsSince(_lastMonth) < 1) return;
        _lastMonth = now;
        Cache.Clear();
        try
        {
            Rally(now);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][御侮] 号召失败: {exception.Message}");
        }
    }
#endif

    // 本土帝国的中央：帝国中央，文化与都城的主流文化同族
#if EC_PENDING
    public static bool IsNativeEmpire(Realm realm)
    {
        if (!Alive(realm) || realm.Capital == null) return false;
        Empire empire = realm.GetEmpire();
        return empire?.data != null && !empire.IsGone() && empire.CoreKingdom == realm && NativeBorn(realm);
    }
#endif

    // 本国的文化与都城的主流文化同族
#if EC_PENDING
    private static bool NativeBorn(Realm kingdom)
    {
        if (kingdom?.Capital == null) return false;
        string culture = CultureService.GetRealmCulture(kingdom), home = CultureService.GetMainCulture(kingdom.Capital);
        return CultureService.IsValidCulture(culture) && CultureService.IsValidCulture(home) && CultureService.SameKin(culture, home);
    }

    private static bool SameKin(Realm a, Realm b) =>
        CultureService.SameKin(CultureService.GetRealmCulture(a), CultureService.GetRealmCulture(b));
#endif

    // side 所在的本土帝国正遭 foe(按所在帝国的中央)这一异族入侵，对方整体国力又不大于本帝国
#if EC_PENDING
    public static bool Resisting(Realm side, Realm foe)
    {
        Realm native = ModRealm(side), invader = ModRealm(foe);
        if (!Alive(native) || !Alive(invader) || native == invader || EmpireWorld.Current == null) return false;
        double now = GameClock.Now;
        var key = (native.Key, invader.Key);
        if (Cache.TryGetValue(key, out var cached) && now >= cached.at && now - cached.at < 5d) return cached.value;
        bool value = IsNativeEmpire(native) && CultureService.IsValidCulture(CultureService.GetRealmCulture(invader)) &&
                     !SameKin(native, invader) && InvasionWars(native).Any(war => ModRealm(war.main_attacker) == invader) &&
                     WarSituation.Power(invader) <= WarSituation.Power(native);
        Cache[key] = (now, value);
        return value;
    }
#endif

    // 战斗欲望：side 的军在 city 上与 foe 交战(城是本族的城)
#if EC_PENDING
    public static float CombatFactor(Realm side, Realm foe, City city)
    {
        if (side == null || foe == null || city?.Record == null || !Resisting(side, foe)) return 1f;
        string culture = CultureService.GetMainCulture(city);
        // 国教在这片天下是本土宗教、又有"神国"之类的教义，再加(见 ReligionTenets.DefenseFactor，用户 2026-11-06)
        return CultureService.IsValidCulture(culture) && CultureService.SameKin(culture, CultureService.GetRealmCulture(ModRealm(side)))
            ? Ardor * ReligionTenets.DefenseFactor(ModRealm(side), city)
            : 1f;
    }
#endif

    // 本帝国(中央连同成员)作为守方的仗，主攻方与本帝国不同族
#if EC_PENDING
    private static IEnumerable<War> InvasionWars(Realm native)
    {
        Empire empire = native.GetEmpire();
        IEnumerable<Realm> members = empire?.kingdoms_list != null ? empire.kingdoms_list.Append(native).Distinct() : new[] { native };
        var seen = new HashSet<War>();
        foreach (Realm member in members.ToList())
        {
            if (!Alive(member)) continue;
            foreach (War war in member.getWars())
            {
                if (war?.data == null || war.hasEnded() || !war.isDefender(member) || !seen.Add(war)) continue;
                Realm attacker = war.main_attacker;
                if (Alive(attacker) && ModRealm(attacker) != native && CultureService.IsValidCulture(CultureService.GetRealmCulture(attacker)) &&
                    !SameKin(native, ModRealm(attacker)))
                    yield return war;
            }
        }
    }
#endif

    #region 号召

#if EC_PENDING
    private static void Rally(double now)
    {
        foreach (Empire empire in ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Empire>())
        {
            Realm native = empire?.CoreKingdom;
            if (empire?.data == null || empire.IsGone() || empire.IsArchived() || !IsNativeEmpire(native)) continue;
            foreach (War war in InvasionWars(native).ToList())
            {
                if (Rallied.TryGetValue(war.data.id, out double at) && now >= at && GameClock.YearsSince(at) < RallyYears) continue;
                Realm invader = ModRealm(war.main_attacker);
                if (!Resisting(native, invader)) continue;
                Rallied[war.data.id] = now;
                CallKin(empire, native, invader, war);
            }
        }
        foreach (long id in Rallied.Keys.ToList())
            if (EmpireWorld.Current.wars.get(id) is not War war || war.data == null || war.hasEnded()) Rallied.Remove(id);
    }

    private static void CallKin(Empire empire, Realm native, Realm invader, War war)
    {
        float chance = TianxiaSystem.MandateOf(native) != null ? RallyMandateChance : RallyChance;
        var joined = new List<Realm>();
        foreach (Realm kin in Candidates(native, invader, war).Take(RallyMax).ToList())
        {
            if (EcRandom.value >= chance) continue;
            try
            {
                war.joinDefenders(kin);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][御侮] {kin.GetKingdomName()} 参战失败: {exception.Message}");
                continue;
            }
            if (war.isDefender(kin)) joined.Add(kin);
        }
        if (joined.Count == 0) return;
        string text = string.Format(LM.Get("native_rally_log"), empire.GetEmpireFullName(), invader.GetKingdomName(),
            string.Join("、", joined.Select(kin => kin.GetKingdomName())));
        EventRecorder.Record(empire, text, empire.Emperor);
        TranslateHelper.LogEventMessage(text, native);
        LogService.LogInfo($"[EmpireCraft][御侮] {empire.GetEmpireName()} 抗 {invader.GetKingdomName()}，来援：" +
                           string.Join("、", joined.Select(kin => kin.GetKingdomName())));
    }
#endif

    // 应召的同族本土政权，国力大的在前。向入侵者称臣纳贡、做附庸的不应召(以前明清鼎革开局，刚降清的朝鲜头一个月就举兵抗清)
#if EC_PENDING
    private static IEnumerable<Realm> Candidates(Realm native, Realm invader, War war)
    {
        Alliance alliance = invader.hasAlliance() ? invader.getAlliance() : null;
        Empire lord = invader.GetEmpire();
        return EmpireWorld.Current.LiveRealms
            .Where(kin => Alive(kin) && kin.IsCivilized() && ModRealm(kin) == kin && kin != native && kin != invader &&
                          !war.hasKingdom(kin) && NativeBorn(kin) && SameKin(kin, native) &&
                          (lord == null || kin.GetTakenAllianceEmpire() != lord && kin.GetGivenAllianceEmpire() != lord) &&
                          !AncientWarfareCompatibility.Owns(kin) &&
                          (alliance == null || !kin.hasAlliance() || kin.getAlliance() != alliance) &&
                          !FrontSystem.Hostile(kin, native) && !FrontSystem.Hostile(kin, invader) &&
                          !WarSituation.TooBusyForWar(kin) && !CorpsSystem.CannotFundWar(kin) &&
                          !EmpirePeacePatch.HasLeft(kin, war, joiningAttackers: false) &&
                          (kin.IsNeighbourWith(native) || kin.IsNeighbourWith(invader)))
            .OrderByDescending(WarSituation.Power);
    }
#endif

    #endregion
}
}
