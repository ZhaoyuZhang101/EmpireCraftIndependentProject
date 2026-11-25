// Generated from the EmpireCraft mod: Scripts/GeneralSystems/UnificationDrive.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.

namespace EmpireCraft.Core
{

// 文化的统一倾向：分裂的天下里这一家有多想归于一统(0~1)。文化配置 setting.unification_drive，没写时 strong_unification 算 1、否则 0。
// 这是"想不想"，不是"能不能"，作用于：
//   · 问鼎门槛：久乱之后按倾向放宽(见 UnificationSystem.Bars)；
//   · 开战挑目标：同样合法的邻国里先打同一天下的(见 EmpireCraftPlotsAddition.GetWarTarget、WarTargetPatch)；
//   · 朝堂议程：谋求统一更急(见 ClaimAgendaSystem.UnificationUrgency)；
//   · 共主收拾残局：每年动手的机会更大(见 UnificationSystem.Pacify)；
//   · 统一战争开打以后(用户 2026-11-08 嫌华夏统一拖太久)：随占地加场数、推进更快、据有过半满三十年不收束、
//     大势已去的接壤小国不战而归附(见 UnificationSystem.MaxWarsOf、PushFactor、PressesOn、Submissions)。
// 不给兵：军饷、停战、姻亲、盟约这些开战闸门照旧，打不过的、打不起的照样不打。
public static class UnificationDrive
{
    public const float AgendaUrgency = 8f, PacifyBoost = 1f, PacifyChanceCap = 0.7f;

#if EC_PENDING
    public static float Of(Realm kingdom) =>
        kingdom?.Record == null || kingdom.IsGone() ? 0f : CultureService.GetRealmCulture(kingdom).GetUnificationDrive();
#endif

    // 这一家要先打这个邻国：有统一倾向，对方在同一个天下
#if EC_PENDING
    public static bool Prefers(Realm initiator, Realm target) =>
        Of(initiator) > 0f && UnificationSystem.SameRealm(initiator, target);

    public static float PacifyChance(Realm holder, float chance)
    {
        float boosted = chance * (1f + PacifyBoost * Of(holder));
        return boosted > PacifyChanceCap ? PacifyChanceCap : boosted;
    }
#endif
}
}
