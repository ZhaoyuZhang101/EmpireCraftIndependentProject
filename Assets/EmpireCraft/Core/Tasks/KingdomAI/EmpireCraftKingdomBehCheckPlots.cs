// Generated from the EmpireCraft mod: Scripts/AI/KingdomAI/EmpireCraftKingdomBehCheckPlots.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;
#if EC_PENDING
using NCMS.Extensions;
#endif

namespace EmpireCraft.Core
{

public class EmpireCraftKingdomBehCheckPlots : GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();
#if EC_PENDING
    public override BehResult Execute(Realm pKingdom)
    {
        pKingdom.CheckEmpire();
        var ked = pKingdom.GetOrCreate();
        if (ked != null && ked.last_plots_check_ts > 0)
        {
            if (GameClock.MonthsSince(ked.last_plots_check_ts) < 1)
            {
                return BehResult.Continue;
            }
        }
        if (pKingdom.IsInEmpire())
        {
            CheckJoinWar(pKingdom);
        }
        // 朝贡国遭外敌攻打，宗主出兵保护(见 TributaryRelations)
        if (pKingdom.HasTakenAlliance()) TributaryRelations.Protect(pKingdom);
        // 跨文明通好之国遭第三国攻打，依约出兵(见 GreatPowerDiplomacy)
        if (pKingdom.IsEmpire()) GreatPowerDiplomacy.Aid(pKingdom);
        // 同阵营的国挨了阵营外的打，盟友来援(见 WorldOrderSystem)
        if (pKingdom.IsEmpire()) WorldOrderSystem.Aid(pKingdom);

        CheckProgress(pKingdom);
        CheckPowerfulMinisterPlot(pKingdom);
        //检测加入圣战
        CheckJoinReligionWar(pKingdom);
        CheckMainTitle(pKingdom);
        CheckRebellionWar(pKingdom);
        if (ked != null) ked.last_plots_check_ts = GameClock.Now;
        return BehResult.Continue;
    }

    private static void CheckPowerfulMinisterPlot(Realm kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        if (empire?.CoreKingdom != kingdom) return;
        Person minister = empire.GetPowerfulMinister();
        if (minister == null || minister.IsGone() || minister.plot?.isActive() == true) return;

        string plotId = empire.CanEmpressDowagerInstallSon(minister)
            ? "empress_dowager_install_son"
            : empire.CanPowerfulMinisterUsurp(minister)
                ? "minister_acquire_empire"
                : empire.CanPowerfulMinisterReceiveNineBestowments(minister)
                    ? "minister_receive_nine_bestowments"
                    : empire.CanPowerfulMinisterSeekDukedom(minister)
                        ? "minister_acquire_title"
                        : null;
        if (plotId == null) return;

        PlotKind plot = AssetManager.plots_library?.get(plotId);
        if (plot?.try_to_start_advanced == null) return;
        if (plot.try_to_start_advanced(minister, plot, true))
        {
            EmpireCraftDebugProbe.Hit("powerful_minister.plot_started", () =>
                $"empire={empire.GetEmpireFullName()}({empire.Key}), minister={minister.FullName()}" +
                $"({minister.Key}), stage={empire.data.powerful_minister_stage}, plot={plotId}");
        }
    }
#endif
    public void CheckMainTitle(Realm pKingdom)
    {
        pKingdom.ReconcileMainTitle();
    }
#if EC_PENDING
    public void CheckProgress(Realm pKingdom)
    {
        if (!pKingdom.HasEnemies())
        {
            pKingdom.EndFactionRebelling();
            pKingdom.EndLocalRebelling();
        }
        pKingdom.PushProgress();
    }
    public void CheckRebellionWar(Realm pKingdom)
    {
        if (!pKingdom.HasEnemies()) return;
        var war = pKingdom.GetWars().ToList().Find(w => w.MainAttacker == pKingdom&&w.GetEmpireWarType() == EmpireWarType.地方叛乱);
        var empire = war?.GetEmpireTarget();
        if  (empire == null) return;
        var faction = war.GetEmpireFaction();
        var rebelData = pKingdom.GetOrCreate();
        if (TryEndExclaveRebellionWar(pKingdom, war, rebelData)) return;
        // 分封制下的小乱：别的诸侯不来投(见 PettyRebellion)
        if (EmpireCraft.Core.PettyRebellion.IsPetty(pKingdom)) return;
        if (rebelData.rebellion_auto_expand_remaining < 0)
        {
            rebelData.rebellion_auto_expand_remaining = (int)Math.Ceiling(pKingdom.getMaxCities() * 1.5d);
        }
        foreach (var k in empire.kingdoms_list.ToList())
        {
            if (k == null || k.IsGone() || !k.HasRuler() || k.Ruler == null) continue;
            if (k.IsEmpire()) continue;
            if (!RebellionSystem.CanAttempt(k) || !CityStabilitySystem.CanRise(k.Capital)) continue;
            if (k == pKingdom) continue;
            if (!k.Ruler.HasFaction()) continue;
            if (k.GetHighestFactionRatio()!=faction) continue;
            if (k.getRenown()>pKingdom.getRenown()) continue;
            int joiningCityCount = k.Cities?.Count ?? 0;
            if (joiningCityCount <= 0 || rebelData.rebellion_auto_expand_remaining < joiningCityCount) continue;
            pKingdom.addRenown(-k.getRenown());
            TranslateHelper.LogJoinRebellionWar(k, pKingdom, empire);
            foreach (var c in k.Cities.ToList())
            {
                c.JoinRealm(pKingdom);
            }
            rebelData.rebellion_auto_expand_remaining -= joiningCityCount;
            if (rebelData.rebellion_auto_expand_remaining <= 0) return;
        }
        
    }
#endif

    private static bool TryEndExclaveRebellionWar(Realm rebels, War war,
        KingdomExtension.KingdomExtraData rebelData)
    {
        if (rebels == null || war == null || rebelData == null || !rebelData.rebellion_origin_city_isolated ||
            rebelData.rebellion_origin_city_value < 0) return false;
        double now = GameClock.Now;
        if (rebelData.last_rebellion_exclave_disengagement_roll >= 0 &&
            GameClock.YearsSince(rebelData.last_rebellion_exclave_disengagement_roll) < 1) return false;
        rebelData.last_rebellion_exclave_disengagement_roll = now;

        CityValueSnapshot origin = new CityValueSnapshot
        {
            Total = rebelData.rebellion_origin_city_value,
            IsIsolated = true
        };
        float chance = CityValueRules.GetExclaveDisengagementChance(origin, rebels.CountCities());
        if (chance <= 0f || !EcRandom.Chance(chance)) return false;
        EmpireWorld.Current.EndWar(war, WarOutcome.Peace);
        return true;
    }
    public void CheckJoinReligionWar(Realm pKingdom)
    {
        if (!pKingdom.HasReligion()) return;
        if (pKingdom.HasEnemies()) return;
        if (pKingdom.GetWars().Count()>0) return;
        foreach (var war in EmpireWorld.Current.LiveWars)
        {
            if (!war.MainAttacker?.HasReligion()??true) continue;
            if (war.MainDefender.IsGone()) continue;
            if (war.HasRealm(pKingdom)) continue;
            if (war.GetAttackers().Contains(pKingdom)) continue;
            if (war.GetEmpireWarType() == EmpireWarType.神圣)
            {
                if (pKingdom.Religion == war.MainAttacker?.Religion)
                {
                    // 圣战加上限与距离(见 ReligiousTension.CanJoinHolyWar)
                    if (pKingdom.IsOpinionGood(war.MainAttacker) && ReligiousTension.CanJoinHolyWar(pKingdom, war))
                    {
                        war.JoinAttackers(pKingdom);
                        TranslateHelper.LogJoinReligionWar(pKingdom, pKingdom.Religion);
                        return;
                    }
                }
            }
        }
    }
    
#if EC_PENDING
    public void CheckJoinWar(Realm pKingdom)
    {
        Empire empire = pKingdom.GetEmpire();
        if (empire == null || empire.CoreKingdom == null || empire.CoreKingdom.IsGone()) return;
        var regime = pKingdom.GetRegime();
        if (regime == null) return;
        // 成员挨打向中央求援、邻近成员挨打议驰援(中央不再自动援助成员，见 MemberWarAid)
        if (!pKingdom.IsEmpire()) MemberWarAid.TryRaise(pKingdom);
        // 分裂进程里割据的成员不随征(见 SchismSystem，用户 2026-10-21 选"先割据后独立")
        if (SchismSystem.IsBreakaway(pKingdom)) return;
        if (empire.CoreKingdom.GetWars().Any(w=>w.GetEmpireWarType()== EmpireWarType.藩王索取皇位)) return;
        // 帝国一级的冷却(timestamp_invite_war_cool_down)照旧看：称帝之初、中央应成员求援出兵后三个月内成员不随征
        // (见 Empire.CreateNewEmpire、MemberWarAid.AnswerRequest)。成员随征不再记在帝国上，改记在各成员自己身上，见 JoinCoolingDown
        if (!empire.CanJoinWar()) return;
        if (JoinCoolingDown(pKingdom)) return;
        // 战国：诸侯已称王自立，天子与诸侯不再自动互相参战(合纵连横见 ZhouEraSystem)
        if (ZhouEraSystem.Era(empire) >= ZhouEraSystem.Warring) return;
        if (empire.IsGone() || pKingdom.IsEmpire()) return;
        var coreKingdom = empire.CoreKingdom;
        if (pKingdom.IsAtWarWith(coreKingdom)) return;
        if (pKingdom.IsOnSameSide(coreKingdom)) return;
        if (pKingdom.GetWars().Any()) return;
        if (!coreKingdom.HasEnemies()) return;
        // 听命于中央的成员(行政区、军府等没有自主外交权的)是中央的一部分，中央开战一律随征，不看好感。
        // 有自主外交的诸侯这里本来就放行(进攻、守土都随征，只要自己没在打仗、不在冷却)；封建帝国别国打上门时诸侯开战当时就随征，
        // 不等这里每月一查(用户 2026-10-21 选"称帝后动员不变差"，见 EmpirePeacePatch.FollowsIntoWar、FeudalVassalService.ExpandSide)
        if (!EmpirePeacePatch.Obeys(empire, pKingdom) &&
            !pKingdom.IsOpinionGood(coreKingdom) && !regime.IsAllowDiplomacy()) return;
        bool joinedAny = false;
        foreach (War w in coreKingdom.GetWarsCached(true).ToArray())
        {
            // 中央援助成员的仗不随征(邻近成员要议驰援，见 MemberWarAid)
            if (MemberWarAid.IsMemberWar(w, empire, w.IsAttacker(coreKingdom))) continue;
            // 分裂进程的争位之战：阵营在开战时就分好了(见 SchismSystem.Proclaim)，别的成员观望
            if (SchismSystem.IsContestWar(w)) continue;
            joinedAny |= TryJoinWarOnSameSide(w, coreKingdom, pKingdom);
        }
        if (joinedAny)
        {
            // 西欧封建帝国按成员各自记；别的帝国照旧记在帝国上、一家随征后全帝国三个月(用户只给西欧放开，
            // 免得阿拉伯的世袭埃米尔、游牧兀鲁思随征也变快，抵消削弱阿拉伯)
            if (PerMemberJoin(empire)) JoinedWarAt[pKingdom.Key] = GameClock.Now;
            else empire.data.timestamp_invite_war_cool_down = GameClock.Now;
            TranslateHelper.LogJoinEmpireWar(pKingdom, empire);
        }
    }
#endif

    // 成员随中央参战的冷却：西欧封建帝国每个成员各自三个月(用户 2026-10-21 选"称帝后动员不变差")。以前记在帝国上
    // (timestamp_invite_war_cool_down)，一家成员随征后全帝国三个月内别家都不随征——称帝前附庸网是一齐参战的，
    // 称帝后反倒每三个月才进来一家。不存档：读档、换世界时清掉(见 ResetWorldState)，读档后各成员可以马上随征
    private static readonly Dictionary<long, double> JoinedWarAt = new();
    private const int JoinWarCooldownMonths = 3;

    public static void ResetWorldState() => JoinedWarAt.Clear();

    private static bool PerMemberJoin(Empire empire) => empire?.CoreKingdom?.GetRegime()?.type == RegimeType.Feudalism;

    private static bool JoinCoolingDown(Realm kingdom)
    {
        if (!JoinedWarAt.TryGetValue(kingdom.Key, out double at)) return false;
        // 记的时间晚于现在(换了世界没清到)按没有记过算
        if (GameClock.Now >= at && GameClock.MonthsSince(at) < JoinWarCooldownMonths) return true;
        JoinedWarAt.Remove(kingdom.Key);
        return false;
    }

    private static bool TryJoinWarOnSameSide(War war, Realm sideKingdom, Realm joiningKingdom)
    {
        if (war == null ||
            sideKingdom == null ||
            joiningKingdom == null ||
            joiningKingdom.IsGone() ||
            !war.Alive ||
            war.HasEnded() ||
            war.GetEmpireWarType() == EmpireWarType.劫掠 ||
            IsWarMember(war, joiningKingdom) ||
            // 春秋战国：周室诸侯之间的征伐不拉天子与别的诸侯下场(犯天子的勤王见 ZhouEraSystem)；南明诸镇火并同样
            ZhouEraSystem.IsInternalWar(war) || SouthernGarrisonSystem.IsInternalWar(war))
        {
            return false;
        }

        if (war.IsAttacker(sideKingdom))
        {
            war.JoinAttackers(joiningKingdom);
        }
        else if (war.IsDefender(sideKingdom))
        {
            war.JoinDefenders(joiningKingdom);
        }
        else
        {
            return false;
        }

        // joinAttackers/joinDefenders 可能因战争正在结算而拒绝加入。
        // 只有最终真的出现在阵营名单中，才允许播报和写入历史。
        return IsWarMember(war, joiningKingdom);
    }

    private static bool IsWarMember(War war, Realm kingdom)
    {
        if (war == null || kingdom == null)
            return false;

        return (war.Attackers != null && war.Attackers.Contains(kingdom)) ||
               (war.Defenders != null && war.Defenders.Contains(kingdom));
    }
}
}
