// Generated from the EmpireCraft mod: Scripts/AI/ActorAI/EmpireCraftActorCheckFaction.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

public class EmpireCraftActorCheckFaction : GameAIActorBase
{
    private static readonly Random _rng = new Random();

    public override Type OriginalBeh => GetType();

#if EC_PENDING
    public override BehResult Execute(Person pActor)
    {
        TryJoin(pActor);
        return BehResult.Continue;
    }
#endif

    // 年度补录：在任官员入派以前只靠人物自己的 AI(本行为)，可这套 AI 只在无小人模式的闲居者身上按时跑，
    // 城主、成员国君主、有小人模式下的官员几乎轮不到，派系里常年只有几个人。每年由帝国把还没入派的在任官员
    // 照同样的规矩过一遍，每帝国每年至多 MaxEnrollPerYear 人
    private const int MaxEnrollPerYear = 12;
    private static readonly Dictionary<long, double> LastEnroll = new();
    private static object _enrollWorld;

#if EC_PENDING
    public static int EnrollOfficials(Empire empire)
    {
        if (empire?.CoreKingdom == null || empire.IsGone() || empire.IsArchived() || EmpireWorld.Current == null) return 0;
        if (!ReferenceEquals(_enrollWorld, EmpireWorld.Current))
        {
            _enrollWorld = EmpireWorld.Current;
            LastEnroll.Clear();
        }
        double now = GameClock.Now;
        if (LastEnroll.TryGetValue(empire.Key, out double last) && now >= last && GameClock.YearsSince(last) < 1) return 0;
        LastEnroll[empire.Key] = now;
        int joined = 0;
        foreach (Person actor in empire.GetUnits()
                     .Where(actor => actor?.Record != null && !actor.IsGone() && actor.Alive && !actor.HasFaction() &&
                                     actor.IsOnOffice())
                     .OrderByDescending(actor => actor.Renown).ToList())
        {
            if (joined >= MaxEnrollPerYear) break;
            if (TryJoin(actor)) joined++;
        }
        return joined;
    }

    public static bool TryJoin(Person pActor)
    {
        if (pActor == null) return false;

        if (!pActor.IsAdult()) return false;
        if (pActor.HasFaction()) return false;
        if (!pActor.IsOnOffice()) return false;
        if (!pActor.HasOfficeIdentity()) return false;
        if (pActor.IsEmperor()) return false;

        Realm pKingdom = pActor.Realm;

        if (pKingdom == null) return false;
        if (pKingdom.IsGone()) return false;

        Empire empire = pKingdom.GetEmpire();

        if (empire == null)
        {
            return false;
        }

        var coreKingdom = empire.CoreKingdom;

        if (coreKingdom == null)
        {
            empire.CheckDissolve(null);
            return false;
        }

        Regime regime = coreKingdom.GetRegime();

        if (regime == null)
        {
            return false;
        }

        var factions = regime.GetPlayerFactions();

        if (factions == null || factions.Count == 0)
        {
            return false;
        }

        int kingdomFactionRatioTotal = pKingdom.GetFactionRatioTotal();

        // 如果当前国家已经存在任何派系影响力，那么官员必须加入一个已有影响力的派系
        bool mustJoinFaction = kingdomFactionRatioTotal > 0;

        var ps = new List<(FixedFaction fac, double p)>(factions.Count);

        foreach (var f in factions)
        {
            // 被禁的派系(党禁)不收人
            if (f == null || f.Ban)
            {
                continue;
            }

            int factionRatio = pKingdom.GetFactionRatioValue(f);

            if (factionRatio < 0)
            {
                factionRatio = 0;
            }

            if (factionRatio > 100)
            {
                factionRatio = 100;
            }

            // 如果国家已经有派系影响力，那么只从该国已有影响力的派系里抽
            if (mustJoinFaction && factionRatio <= 0)
            {
                continue;
            }

            double basePossibility = f.CalcPossibility(pActor);

            if (basePossibility < 0)
            {
                basePossibility = 0;
            }

            if (basePossibility > 1)
            {
                basePossibility = 1;
            }

            double factionInfluencePossibility = factionRatio / 100.0;

            double finalPossibility = basePossibility + factionInfluencePossibility;

            if (finalPossibility < 0)
            {
                finalPossibility = 0;
            }

            if (finalPossibility > 1)
            {
                finalPossibility = 1;
            }

            ps.Add((f, finalPossibility));
        }

        if (ps.Count == 0)
        {
            return false;
        }

        double sum = ps.Sum(t => t.p);

        if (sum <= 0)
        {
            return false;
        }

        // 国家已有派系影响力时，不能有“不加入”的概率
        // 国家没有派系影响力时，才允许不加入
        double noneWeight = mustJoinFaction ? 0.0 : Math.Max(0.0, 1.0 - sum);

        double r = _rng.NextDouble() * (sum + noneWeight);
        double acc = 0.0;

        FixedFaction chosen = null;

        foreach (var t in ps)
        {
            acc += t.p;

            if (r <= acc)
            {
                chosen = t.fac;
                break;
            }
        }

        if (chosen != null)
        {
            pActor.SetFaction(chosen);
            // TranslateHelper.LogOfficerJoinFaction(pActor.GetOffice(), pActor, chosen);
        }

        return chosen != null;
    }
#endif
}
}
