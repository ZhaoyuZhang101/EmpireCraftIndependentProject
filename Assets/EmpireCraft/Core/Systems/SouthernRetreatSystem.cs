// Generated from the EmpireCraft mod: Scripts/GeneralSystems/SouthernRetreatSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 南明结成(用户 2026-11-08 "一旦满清占下北京或者三分之一的天下南边都自动组成南明""而不是不起民变"
// "要达到那种外族入侵然后原来的农民起义什么的都自主加入同一个旗帜下抗清的那种符合历史的感觉")：
//   华夏王朝的京师(帝国法理首都)落到异族手里，或异族占了本天下三分之一的城：朝廷还有人承统、还有 MinCities 城的，结成一次
//   (两次至少隔 CooldownYears 年)——
//   · 京师已失、朝廷在别处的，当场冠方位(南明、南宋这样，见 CapitalLossNamingSystem.ApplyNow)，收复京师照旧去掉；
//   · 南方反过本朝、从本朝分出去的同族政权(起义军、割据、自立的行省)陆续罢兵来投：ShelterYears 年里每年按入侵者的压力掷机会
//     (同剧本号召，见 ScenarioPlotRules.RallyChance：入侵者占的同族之城越多越容易，挨着、正和它打仗的更容易)，
//     一年至多 RallyMaxPerYear 家，国力要比朝廷小、朝廷够得着。以前结成当场全部来投(用户 2026-11-09 "南明成立后无论什么情况
//     原本的叛乱都全部并入了""清朝都还没打到长江呢""才刚入关")；剧本已在号召的(甲申之变)不另掷；
//   · 正统保底：当年 45，逐年降到正统崩坏线(见 MonarchyLegitimacy)。民变照常起，积弊照涨(积弊只能延缓，不能削减)。
//   以前是京师一破就"偏安十五年不起满额民变"，明朝末年反倒没有民变，南明还能反攻收复关外。
public static class SouthernRetreatSystem
{
    private static readonly Dictionary<long, double> Checked = new(), Rallied = new();
    private static object _world;

    public static bool Sheltered(Empire empire)
    {
        double at = empire?.data?.southern_retreat_at ?? -1d;
        return at >= 0d && EmpireWorld.Current != null && SouthernRetreatRules.Sheltered(YearsSince(at));
    }

    // 正统一项(见 MonarchyLegitimacy)：别的各项加起来不到保底的补齐
    public static int LegitimacyBonus(Empire empire, int others)
    {
        double at = empire?.data?.southern_retreat_at ?? -1d;
        return at < 0d || EmpireWorld.Current == null ? 0
            : SouthernRetreatRules.LegitimacyBonus(others, YearsSince(at), DynasticCycleRules.CollapseLegitimacy);
    }

    private static int YearsSince(double at) => GameClock.Now < at ? 0 : GameClock.YearsSince(at);

    private static bool Alive(Realm kingdom) => kingdom?.Record != null && !kingdom.IsGone();

    // 异族：本国(帝国按核心)的文化与朝廷的不同族
    private static bool Alien(Realm realm, string culture)
    {
        if (!Alive(realm)) return false;
        string other = CultureService.GetRealmCulture(realm);
        return CultureService.IsValidCulture(other) && !CultureService.SameKin(other, culture);
    }

    // 每月看一次(见 Empire.update)
#if EC_PENDING
    public static void Check(Empire empire)
    {
        if (empire?.data == null || EmpireWorld.Current == null || empire.IsGone() || empire.IsArchived()) return;
        if (!ReferenceEquals(_world, EmpireWorld.Current))
        {
            _world = EmpireWorld.Current;
            Checked.Clear();
            Rallied.Clear();
        }
        double now = GameClock.Now;
        if (Checked.TryGetValue(empire.Key, out double last) && now >= last && GameClock.MonthsSince(last) < 1) return;
        Checked[empire.Key] = now;
        Realm core = empire.CoreKingdom;
        if (!Alive(core) || core.Capital == null || core.Capital.IsGone()) return;
        string culture = CultureService.GetRealmCulture(core);
        if (!DynasticCycleSystem.Applies(empire) || InstitutionSystem.GetCultureLine(culture) != "Huaxia") return;
        EmpireData data = empire.data;
        int since = data.southern_retreat_at < 0d ? -1 : YearsSince(data.southern_retreat_at);
        // 南明诸镇：改藩镇、火并、降敌(一年一次，见 SouthernGarrisonSystem)
        SouthernGarrisonSystem.Update(empire);
        // 已结成：期内同族义军按入侵者的压力陆续来投(一年看一次)
        if (SouthernRetreatRules.Sheltered(since))
        {
            if (Rallied.TryGetValue(empire.Key, out double rallied) && now >= rallied && GameClock.YearsSince(rallied) < 1) return;
            Rallied[empire.Key] = now;
            Realm former = EmpireWorld.Current.RealmByKey(data.southern_ming_invader);
            if (Alive(former)) Rally(empire, FrontSystem.ModRealm(former) ?? former);
            return;
        }
        City capital = EmpireCoreManager.Get(empire)?.GetCoreCapital();
        Realm invader = null;
        bool holdsCapital = false, holdsThird = false;
        if (capital?.Realm != null && !capital.IsGone() && capital.Realm.GetEmpire() != empire)
        {
            Realm holder = FrontSystem.ModRealm(capital.Realm);
            if (Alien(holder, culture))
            {
                invader = holder;
                holdsCapital = true;
            }
        }
        EmpireCore lead = TianxiaSystem.Lead(EmpireCoreManager.Get(empire));
        if (invader == null && lead != null)
        {
            (Dictionary<Realm, int> held, int total) = TianxiaSystem.Holdings(lead);
            foreach (KeyValuePair<Realm, int> pair in held)
            {
                if (!Alien(pair.Key, culture) || !SouthernRetreatRules.HoldsThird(pair.Value, total)) continue;
                invader = pair.Key;
                holdsThird = true;
                break;
            }
        }
        // 打着为明复仇的旗号、与朝廷罢兵期间不结成(清军入关先打顺，次年南下才与南明开战)
        if (invader == null || !FrontSystem.Hostile(core, invader)) return;
        int cities = empire.kingdoms_list.Where(Alive).Sum(kingdom => kingdom.Cities?.Count ?? 0);
        bool successor = empire.Emperor != null && !empire.Emperor.IsGone() || core.HasHeir();
        if (!SouthernRetreatRules.Triggers(holdsCapital, holdsThird, successor, cities, since)) return;
        data.southern_retreat_at = now;
        data.southern_ming_invader = invader.Key;
        // 朝廷只剩名义：各省当场改为世袭藩镇
        SouthernGarrisonSystem.Enfeoff(empire);
        MonarchyLegitimacy.Invalidate(empire);
        CapitalLossNamingSystem.ApplyNow(empire);
        string text = holdsCapital
            ? string.Format(LM.Get("southern_retreat_history"), invader.GetKingdomName(), capital.GetCityName(), empire.GetEmpireFullName())
            : string.Format(LM.Get("southern_retreat_third"), invader.GetKingdomName(), empire.GetEmpireFullName());
        EventRecorder.Record(empire, text, empire.Emperor, core);
        TranslateHelper.LogEventMessage(text, core);
        LogService.LogInfo($"[EmpireCraft][南明] {empire.GetEmpireFullName()}：{invader.GetKingdomName()} " +
                           $"{(holdsCapital ? "据京师 " + capital.GetCityName() : "据天下三分之一")}，朝廷在 {core.Capital.GetCityName()}，" +
                           $"{cities} 城，正统 {empire.Legitimacy}，积弊 {DynasticCycleSystem.GetPressure(empire):0}");
    }
#endif

    // 南方反过本朝、从本朝分出去的同族政权来投：都城在入侵者都城以南，不是入侵者一边的(成员、朝贡、附庸、同盟)，
    // 国力比朝廷小、朝廷够得着；按入侵者的压力掷机会，一年至多 RallyMaxPerYear 家
    private const int RallyMaxPerYear = 2;

    private static List<Realm> Rally(Empire court, Realm invader)
    {
        var joined = new List<Realm>();
        Realm courtCore = court.CoreKingdom;
        if (!Alive(courtCore) || !Alive(invader) || ScenarioEventSystem.RallyingFor(courtCore)) return joined;
        string culture = CultureService.GetRealmCulture(courtCore);
        float pressure = ScenarioEventSystem.ConquestPressure(invader, culture);
        if (pressure < ScenarioPlotRules.RallyMinPressure) return joined;
        double courtPower = WarSituation.Power(courtCore);
        Empire invaderEmpire = invader.GetEmpire();
        Alliance alliance = invader.HasAlliance() ? invader.GetAlliance() : null;
        float? invaderY = invader.Capital != null && !invader.Capital.IsGone() ? invader.Capital.Center.y : null;
        foreach (Realm kingdom in EmpireWorld.Current.LiveRealms.ToList())
        {
            if (!Alive(kingdom) || !kingdom.IsCivilized() || kingdom == courtCore || kingdom == invader || kingdom.Capital == null) continue;
            // 起兵的领头人还在世的不来(见 ScenarioEventSystem.LedByRebelFounder)
            if (ScenarioEventSystem.LedByRebelFounder(kingdom)) continue;
            if (FrontSystem.ModRealm(kingdom) != kingdom || kingdom.GetEmpire() == court) continue;
            if (invaderEmpire != null && (kingdom.GetEmpire() == invaderEmpire || kingdom.GetTakenAllianceEmpire() == invaderEmpire ||
                                          kingdom.GetGivenAllianceEmpire() == invaderEmpire)) continue;
            if (alliance != null && kingdom.HasAlliance() && kingdom.GetAlliance() == alliance) continue;
            if (!CultureService.SameKin(CultureService.GetRealmCulture(kingdom), culture)) continue;
            if (!kingdom.HasRebelledAgainst(court) && kingdom.GetOrCreate().left_empire_id != court.Key) continue;
            if (!SouthernRetreatRules.South(kingdom.Capital.Center.y, invaderY)) continue;
            if (WarSituation.Power(kingdom) >= courtPower || !WarReach.Reachable(courtCore, kingdom)) continue;
            // 走投无路才来投(见 ScenarioEventSystem.Desperate)
            if (!ScenarioEventSystem.Desperate(kingdom, invader, pressure)) continue;
            float chance = ScenarioPlotRules.RallyChance(pressure, WarReach.Reachable(invader, kingdom), kingdom.IsAtWarWith(invader));
            if (EcRandom.value >= chance) continue;
            if (ScenarioEventSystem.Submit(kingdom, invader, courtCore, court, pressure)) joined.Add(kingdom);
            if (joined.Count >= RallyMaxPerYear) break;
        }
        if (joined.Count > 0)
            LogService.LogInfo($"[EmpireCraft][南明] 压力 {pressure:P0}，来投：{string.Join("、", joined.Select(kingdom => kingdom.GetKingdomName()))}");
        return joined;
    }
}
}
