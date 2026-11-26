// Generated from the EmpireCraft mod: Scripts/GeneralSystems/TradeWarSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 关税战(用户 2026-11-24 第四批里的"和平时期贸易战")：近代国家不打仗也能在通商上交手。
//   · 起因：一国高筑关税(保护关税)，货卖给它最多的那个国家每年有机会加征报复关税(自己正陷在萧条里的翻倍)；
//     萧条里以"高筑关税"为对策的，当场就问一遍(见 EconomicCycleSystem.Respond)；外交窗口里也能直接挑起、罢手；
//   · 互加关税：两国之间的关税翻倍(见 CommodityMarketSystem.TariffBetween)，双方商人怨气年年涨，好感 −15；
//   · 升级：每年有机会升到断绝通商(一方在萧条里更容易)，好感 −25、劳工也跟着受累；断上几年多半退回互加关税；
//   · 收场：挑头的不再高筑关税、或拖过六年，每年有机会议定关税、握手言和；两国开战、结成同一阵营也就不必再打关税战。
// 数据随世界格局存档(WorldOrderData.trade_wars)，每年在世界格局的年度结算里走一遍。
public static class TradeWarSystem
{
    public const int Tariffs = 1, Embargo = 2;
    private const float RetaliateChance = 0.25f, SlumpFactor = 2f;
    private const float SettleChance = 0.3f, LongSettleChance = 0.4f, EscalateChance = 0.15f, CoolChance = 0.5f;
    private const int LongYears = 6, EmbargoYears = 4;
    private const int TariffOpinion = -15, EmbargoOpinion = -25;
    private const float TariffMultiplier = 2f, MaxTariff = 0.9f;

    private static List<TradeWarData> Wars => WorldOrderSystem.Data.trade_wars;

    public static IReadOnlyList<TradeWarData> All => Wars;

    public static TradeWarData Between(Empire a, Empire b)
    {
        if (a == null || b == null || a == b || Wars.Count == 0) return null;
        foreach (TradeWarData war in Wars)
            if (war.a == a.Key && war.b == b.Key || war.a == b.Key && war.b == a.Key) return war;
        return null;
    }

    public static TradeWarData Between(Realm a, Realm b) =>
        Wars.Count == 0 ? null : Between(a?.GetEmpire(), b?.GetEmpire());

    // 断绝通商(见 MarketSystem.Embargoed)
    public static bool Cut(Realm a, Realm b) => Between(a, b)?.level >= Embargo;

    // 关税的倍数(见 CommodityMarketSystem.TariffBetween)
    public static float ApplyTariff(Realm buyer, Realm seller, float rate) =>
        Between(buyer, seller) == null ? rate : Mathf.Min(MaxTariff, rate * TariffMultiplier);

    public static int OpinionOf(Realm main, Realm target)
    {
        TradeWarData war = Between(main, target);
        return war == null ? 0 : war.level >= Embargo ? EmbargoOpinion : TariffOpinion;
    }

    private static Empire Living(long id)
    {
        Empire empire = ModClass.EMPIRE_MANAGER?.Get(id);
        return empire?.data == null || empire.IsGone() || empire.IsArchived() || empire.CoreKingdom == null ? null : empire;
    }

    public static Empire Other(TradeWarData war, Empire self) => war == null ? null : Living(war.a == self?.Key ? war.b : war.a);

    // 这两国能不能打关税战：返回不能的原因(语言键)，能则 null
    public static string CanStart(Empire a, Empire b)
    {
        if (a?.CoreKingdom == null || b?.CoreKingdom == null || a == b) return "diplomacy_reason_invalid";
        if (a.CoreKingdom.IsEnemy(b.CoreKingdom)) return "diplomacy_reason_at_war";
        if (Between(a, b) != null) return "trade_war_reason_already";
        if (CommodityMarketSystem.RootOf(a.CoreKingdom) == CommodityMarketSystem.RootOf(b.CoreKingdom)) return "diplomacy_reason_same_market";
        if (WorldOrderSystem.SameBloc(a.CoreKingdom, b.CoreKingdom)) return "trade_war_reason_same_bloc";
        // 关键行业操在对方资本手里的，不敢同它打关税战(见 CompanySystem)
        if (CompanySystem.Beholden(a.CoreKingdom, b.CoreKingdom)) return "trade_war_reason_beholden";
        return null;
    }

    // 挑起关税战(a 向 b 加征)：办不成返回原因键
    public static string Start(Empire a, Empire b, string cause)
    {
        string reason = CanStart(a, b);
        if (reason != null) return reason;
        double now = GameClock.Now;
        Wars.Add(new TradeWarData { a = a.Key, b = b.Key, since = now, level = Tariffs, level_since = now, cause = cause ?? "" });
        WorldOrderSystem.Record(string.Format(LM.Get("trade_war_started_" + (cause is "decree" or "seizure" ? cause : "tariff") + "_history"),
            a.GetEmpireFullName(), b.GetEmpireFullName()), a, new[] { b });
        return null;
    }

    // 罢手言和：办不成返回原因键
    public static string Settle(Empire a, Empire b)
    {
        TradeWarData war = Between(a, b);
        if (war == null) return "trade_war_reason_none";
        End(war, a, b, true);
        return null;
    }

    private static void End(TradeWarData war, Empire a, Empire b, bool record)
    {
        Wars.Remove(war);
        if (!record || a == null || b == null) return;
        WorldOrderSystem.Record(string.Format(LM.Get("trade_war_settled_history"), a.GetEmpireFullName(), b.GetEmpireFullName(),
            GameClock.YearsSince(war.since)), a, new[] { b });
    }

    // 一国刚高筑关税：货卖给它最多的那个国家当场掂量要不要报复
    internal static void Provoke(Empire protector, string cause)
    {
        Empire rival = BiggestSeller(protector);
        if (rival == null || CanStart(rival, protector) != null) return;
        float chance = RetaliateChance * 2f * (EconomicCycleSystem.InCrisis(rival) ? SlumpFactor : 1f);
        if (EcRandom.value < chance) Start(rival, protector, cause);
    }

    // 货卖给这个国家最多的外国(照上一年的市场账)；账上还没有买卖的，退而看通商的市场里最大的那个；市场还没算出来返回 null
    private static Empire BiggestSeller(Empire buyer)
    {
        CommodityMarketSystem.Market market = CommodityMarketSystem.MarketOf(buyer?.CoreKingdom);
        if (market == null) return null;
        foreach (KeyValuePair<long, float> pair in market.boughtFrom.OrderByDescending(pair => pair.Value))
        {
            if (pair.Value <= 0f) break;
            Empire seller = EmpireWorld.Current.RealmByKey(pair.Key)?.GetEmpire();
            if (seller != null && seller != buyer && !seller.IsGone() && !seller.IsArchived()) return seller;
        }
        Empire biggest = null;
        double power = 0d;
        foreach (long leaderId in market.linked)
        {
            Realm leader = EmpireWorld.Current.RealmByKey(leaderId);
            Empire other = leader?.GetEmpire();
            if (other == null || other == buyer || other.IsGone() || other.IsArchived()) continue;
            double size = CommodityMarketSystem.MarketOf(leader)?.power ?? 0d;
            if (biggest != null && size <= power) continue;
            biggest = other;
            power = size;
        }
        return biggest;
    }

    // 每年(WorldOrderSystem.Scan)
    internal static void Yearly(List<Empire> states, double now)
    {
        var modern = new HashSet<long>(states.Select(empire => empire.Key));
        foreach (TradeWarData war in Wars.ToList())
        {
            Empire a = Living(war.a), b = Living(war.b);
            // 亡了国、打起来了、结成同一阵营、并进同一个市场：关税战不了了之
            if (a == null || b == null || a.CoreKingdom.IsEnemy(b.CoreKingdom) || WorldOrderSystem.SameBloc(a.CoreKingdom, b.CoreKingdom) ||
                CommodityMarketSystem.RootOf(a.CoreKingdom) == CommodityMarketSystem.RootOf(b.CoreKingdom))
            {
                Wars.Remove(war);
                continue;
            }
            Evolve(war, a, b, now);
        }
        foreach (Empire protector in states)
        {
            Realm leader = CommodityMarketSystem.MarketOf(protector.CoreKingdom)?.leader ?? protector.CoreKingdom;
            if (CommodityMarketSystem.PolicyOf(leader) != TradePolicy.Protective) continue;
            Empire rival = BiggestSeller(protector);
            if (rival == null || !modern.Contains(rival.Key) || CanStart(rival, protector) != null) continue;
            float chance = RetaliateChance * (EconomicCycleSystem.InCrisis(rival) ? SlumpFactor : 1f);
            if (EcRandom.value < chance) Start(rival, protector, "tariff");
        }
    }

    private static void Evolve(TradeWarData war, Empire a, Empire b, double now)
    {
        bool embargo = war.level >= Embargo;
        foreach (Empire side in new[] { a, b })
        {
            ClaimPolicySystem.AddGrievance(side, SocialClass.Merchant, embargo ? 4f : 2f);
            if (embargo) ClaimPolicySystem.AddGrievance(side, SocialClass.Labour, 2f);
        }
        float roll = EcRandom.value;
        if (embargo)
        {
            if (war.level_since < 0d || GameClock.YearsSince(war.level_since) < EmbargoYears || roll >= CoolChance) return;
            war.level = Tariffs;
            war.level_since = now;
            WorldOrderSystem.Record(string.Format(LM.Get("trade_war_cooled_history"), a.GetEmpireFullName(), b.GetEmpireFullName()), a, new[] { b });
            return;
        }
        // 被报复的一方(b)不再高筑关税，或者拖得久了：有机会言和
        Realm leader = CommodityMarketSystem.MarketOf(b.CoreKingdom)?.leader ?? b.CoreKingdom;
        bool eased = war.cause != "decree" && CommodityMarketSystem.PolicyOf(leader) != TradePolicy.Protective;
        int years = GameClock.YearsSince(war.since);
        float settle = years >= LongYears ? LongSettleChance : eased ? SettleChance : 0f;
        if (roll < settle)
        {
            End(war, a, b, true);
            return;
        }
        float escalate = EscalateChance * (EconomicCycleSystem.InCrisis(a) || EconomicCycleSystem.InCrisis(b) ? SlumpFactor : 1f);
        if (roll < settle + escalate)
        {
            war.level = Embargo;
            war.level_since = now;
            WorldOrderSystem.Record(string.Format(LM.Get("trade_war_escalated_history"), a.GetEmpireFullName(), b.GetEmpireFullName()), a, new[] { b });
        }
    }
}
}
