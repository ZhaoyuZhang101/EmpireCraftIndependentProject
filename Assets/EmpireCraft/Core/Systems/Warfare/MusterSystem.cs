// Generated from the EmpireCraft mod: Scripts/GeneralSystems/Warfare/MusterSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using Vector2 = EmpireCraft.Core.EcVector2;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 叛乱初起的集结期：叛军先动手，被叛的一方要先集结大军，叛军有机会在大军到来前打下一个省。
//   · 叛乱类战争(原版起义、派系/地方/民族/宗教叛乱、地方独立、附庸独立、清君侧)开打时掷定集结期 0~5 个月：
//     基础 1~3 个月(按战争定，存档稳定；以前基础可以是 0，叛军一点反应时间都没有)；叛军都城离被叛方都城远(超过 120 格)+1；帝国正统不足 40 +1；
//     被叛方已在举国总动员 -1；
//   · 集结期间，被叛方对这伙叛军：不反攻(不分兵去打叛军占的城)；只有当时就在受攻城附近(六块地块以内)的军能赶来守城，
//     远处的军还在集结；仓促应战，守方战力 ×0.75；
//   · 开打与集结完毕各在战线纪事里记一笔；集结期满后照常平叛。
// 叛军是攻方、被叛方是守方(起义与各类叛乱都是叛军宣战)。
public static class MusterSystem
{
    public const int LocalZones = 6;
    private const float SurprisedDefense = 0.75f;
    private const int MaxMonths = 5;
    private const float FarDistance = 120f;
    private const float WeakMandate = 40f;

    private static readonly HashSet<EmpireWarType> Rebellions = new()
    {
        EmpireWarType.派系叛乱, EmpireWarType.地方叛乱, EmpireWarType.民族叛乱, EmpireWarType.宗教叛乱,
        EmpireWarType.地方独立, EmpireWarType.附庸独立, EmpireWarType.清君侧
    };

    public static bool IsRebellion(War war) =>
        war?.Record != null && (war.GetKind() == WarKinds.Rebellion || Rebellions.Contains(war.GetEmpireWarType()));

    // 这场叛乱的集结期(月)，第一次问到时掷定并记在战争上
#if EC_PENDING
    public static int Months(War war)
    {
        var data = war.GetOrCreate();
        if (data.muster_months >= 0) return data.muster_months;
        int months = 1 + (int)(Math.Abs(war.Key * 7919L) % 3);
        Realm rebel = war.MainAttacker, loyal = FrontSystem.ModRealm(war.MainDefender);
        if (rebel?.Capital != null && loyal?.Capital != null &&
            Vector2.Distance(rebel.Capital.Center, loyal.Capital.Center) > FarDistance) months++;
        if (loyal?.GetEmpire() is Empire empire && empire.Mandate < WeakMandate) months++;
        if (loyal != null && WarScopeSystem.Level(loyal) >= WarScopeSystem.Total) months--;
        data.muster_months = Mathf.Clamp(months, 0, MaxMonths);
        return data.muster_months;
    }

    private static bool Underway(War war) =>
        war?.Record != null && war.Alive && !war.HasEnded() && IsRebellion(war) &&
        GameClock.MonthsSince(war.StartedStamp) < Months(war);
#endif

    // loyal 一方还在集结、对 rebel 这伙叛军不能全力应战
#if EC_PENDING
    public static bool Mustering(Realm loyal, Realm rebel)
    {
        if (loyal?.Record == null || rebel?.Record == null || loyal == rebel) return false;
        Realm realm = FrontSystem.ModRealm(loyal);
        foreach (War war in rebel.GetWars())
        {
            if (!Underway(war)) continue;
            bool loyalDefends = war.IsDefender(loyal) || war.IsDefender(realm);
            if (loyalDefends && war.IsAttacker(rebel)) return true;
        }
        return false;
    }
#endif

    // 守方仓促应战的战力系数
#if EC_PENDING
    public static float DefenseFactor(Realm defender, Realm attacker) =>
        Mustering(defender, attacker) ? SurprisedDefense : 1f;
#endif

    // 每月(战线结算前)：新起的叛乱记"仓促应战"，集结期满记"大军集结完毕"
#if EC_PENDING
    public static void MonthTick()
    {
        foreach (War war in EmpireWorld.Current.LiveWars.ToList())
        {
            if (war?.Record == null || !war.Alive || war.HasEnded() || !IsRebellion(war)) continue;
            var data = war.GetOrCreate();
            Realm rebel = war.MainAttacker, loyal = FrontSystem.ModRealm(war.MainDefender);
            if (rebel?.Record == null || loyal?.Record == null) continue;
            bool fresh = data.muster_months < 0;
            int months = Months(war);
            if (fresh && months > 0)
                FrontSystem.Log(loyal, string.Format(LM.Get("corps_log_muster_start"), rebel.GetKingdomName(),
                    loyal.GetKingdomName(), months));
            if (months > 0 && !data.muster_done && GameClock.MonthsSince(war.StartedStamp) >= months)
            {
                data.muster_done = true;
                FrontSystem.Log(loyal, string.Format(LM.Get("corps_log_muster_done"), loyal.GetKingdomName(),
                    rebel.GetKingdomName()));
            }
        }
    }
#endif
}
}
