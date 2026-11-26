// Generated from the EmpireCraft mod: Scripts/GamePatches/EmpirePeacePatch.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 帝国中央议和，听命于中央的成员一起停战：
//   原版主攻(守)国退出战争时会从同侧另挑一国接任，战争照打——中央单方面议和后仗由行政区接着打，
//   中央又因"成员在打仗"被拉回去(见 EmpireCraftKingdomBehCheckPlots.CheckJoinWar)。
//   现在中央退出某场战争(议和或战败)、或整场战争结束时，帝国里没有自主外交权的成员(行政区、军府等)
//   一并退出与中央这些对手的战争；中央与对手记下停战时间，此前就在打的旧仗中央不再因成员回去参战。
public class EmpirePeacePatch : GamePatch
{
    public ModDeclare declare { get; set; }

    public sealed class Snapshot
    {
        public Empire Empire;
        public List<Realm> Foes;
    }

    private static bool _cascading;

#if EC_PENDING
    public void Initialize()
    {
        var harmony = new Harmony(nameof(EmpirePeacePatch));
        harmony.Patch(AccessTools.Method(typeof(War), nameof(War.removeFromWar)),
            prefix: new HarmonyMethod(typeof(EmpirePeacePatch), nameof(BeforeRemove)),
            postfix: new HarmonyMethod(typeof(EmpirePeacePatch), nameof(AfterRemove)));
        harmony.Patch(AccessTools.Method(typeof(WarManager), nameof(WarManager.endWar)),
            prefix: new HarmonyMethod(typeof(EmpirePeacePatch), nameof(BeforeEnd)) { priority = Priority.First },
            postfix: new HarmonyMethod(typeof(EmpirePeacePatch), nameof(AfterEnd)));
    }
#endif

    private static Snapshot Capture(War war, Realm kingdom)
    {
        if (war?.Record == null || kingdom == null || kingdom.IsGone() || !kingdom.IsEmpire()) return null;
        Empire empire = kingdom.GetEmpire();
        if (empire == null || empire.IsGone()) return null;
        bool attacker = war.IsAttacker(kingdom);
        if (!attacker && !war.IsDefender(kingdom)) return null;
        List<Realm> foes = (attacker ? war.Defenders : war.Attackers)
            .Where(foe => foe != null && !foe.IsGone() && foe.GetEmpire() != empire).ToList();
        return foes.Count == 0 ? null : new Snapshot { Empire = empire, Foes = foes };
    }

    public static void BeforeRemove(War __instance, Realm pKingdom, out Snapshot __state) =>
        __state = __instance != null && __instance.Alive && !__instance.HasEnded() ? Capture(__instance, pKingdom) : null;

#if EC_PENDING
    public static void AfterRemove(Snapshot __state) => Cascade(__state);
#endif

    public static void BeforeEnd(War pWar, WarOutcome pWinner, out List<Snapshot> __state)
    {
        __state = null;
        // 同盟把两场战争并成一场(Merged)不是议和：参战各方转到合并后的战争里接着打
        if (pWinner == WarOutcome.Merged) return;
        if (pWar?.Record == null || !pWar.Alive || pWar.HasEnded()) return;
        foreach (Realm kingdom in pWar.Attackers.Concat(pWar.Defenders).ToList())
        {
            Snapshot snapshot = Capture(pWar, kingdom);
            if (snapshot != null) (__state ??= new List<Snapshot>()).Add(snapshot);
        }
    }

    // 战争补丁可能拦下"结束"(如索取法理未达成)：真结束了才连带停战
#if EC_PENDING
    public static void AfterEnd(War pWar, List<Snapshot> __state)
    {
        if (__state == null || pWar == null || !pWar.HasEnded()) return;
        foreach (Snapshot snapshot in __state) Cascade(snapshot);
    }
#endif

    // 听命于中央的成员：没有自主外交权的成员，或朝廷任命(不世袭)的行政区、军府；
    // 世袭的藩镇、封国保留自主外交与兵权，正在叛乱的也不算；
    // 分裂进程里割据的成员(见 SchismSystem，用户 2026-10-21 选"先割据后独立")名存实亡，也不算——议和不随中央停战、
    // 中央不替它付饷、不奉诏出征、不求援勤王都经这里
    public static bool Obeys(Empire empire, Realm member)
    {
        if (member?.Record == null || member.IsGone() || member == empire.CoreKingdom || member.GetEmpire() != empire ||
            member.IsFactionRebelling() || member.IsLocalRebelling()) return false;
        if (EmpireCraft.Core.SchismSystem.IsBreakaway(member)) return false;
        Regime regime = member.GetRegime();
        if (regime == null) return false;
        // 选帝制下皇帝家族领地薄弱：比皇帝自家领国城多的诸侯不听调遣
        if (EmpireCraft.Core.HolyRomanSystem.Defies(empire, member)) return false;
        if (!regime.IsAllowDiplomacy()) return true;
        return member.IsAdministrativeKingdomType() && regime.GetLeaderSelectMethod() != LeaderSelectMethod.Succession;
    }

    // 随中央参战(只用于"随不随征"这一处判定：开战、有人加入时当场拉人的 FeudalVassalService.ExpandSide)：
    // 听命的成员之外，封建帝国(中央行西方封建)的诸侯在中央守土的仗里——别国打上门来、中央是主守方——也随中央参战
    // (用户 2026-10-21 选"称帝后动员不变差")。称帝前附庸网打仗是一个整体，宗主挨打附庸一齐上阵；称帝后诸侯有自主外交、
    // 不算听命，只能等王国 AI 每月逐个随征(见 CheckJoinWar)，帝国反倒比附庸网难动员。进攻战照旧(听命的当场随征，诸侯走 CheckJoinWar)。
    // 不改 Obeys 本身：议和连带停战、中央代付军饷、奉诏出征、求援与驰援都看 Obeys，改了诸侯守土时这些也会跟着变
    // (中央替诸侯付饷、调走诸侯的兵)，诸侯在这些事上的自主照旧
    public static bool FollowsIntoWar(Empire empire, Realm member, War war)
    {
        if (empire?.CoreKingdom == null) return false;
        if (Obeys(empire, member)) return true;
        Realm core = empire.CoreKingdom;
        if (war?.Record == null || war.HasEnded() || member?.Record == null || member.IsGone() || member == core ||
            member.GetEmpire() != empire || member.IsFactionRebelling() || member.IsLocalRebelling() ||
            member.IsAtWarWith(core)) return false;
        if (core.GetRegime()?.type != RegimeType.Feudalism) return false;
        // 选帝制下不听调遣的大诸侯(见 HolyRomanSystem.Defies)、分裂进程里割据的诸侯(见 SchismSystem)守土也不随征
        if (EmpireCraft.Core.HolyRomanSystem.Defies(empire, member) ||
            EmpireCraft.Core.SchismSystem.IsBreakaway(member)) return false;
        Realm attacker = war.GetMainAttacker();
        return war.GetMainDefender() == core && attacker?.Record != null && !attacker.IsGone() && attacker.GetEmpire() != empire &&
               war.GetKind() != WarKinds.Rebellion && !NotDefence.Contains(war.GetEmpireWarType());
    }

    // 中央是主守方但不算"别国打上门"的仗：劫掠来去一阵风(王国 AI 随征本来就不进劫掠之战，见 CheckJoinWar)；
    // 叛乱、独立、争位这些家里的事诸侯不必一齐上阵
    private static readonly HashSet<EmpireWarType> NotDefence = new()
    {
        EmpireWarType.劫掠, EmpireWarType.地方独立, EmpireWarType.派系叛乱, EmpireWarType.地方叛乱, EmpireWarType.民族叛乱,
        EmpireWarType.宗教叛乱, EmpireWarType.清君侧, EmpireWarType.藩王索取皇位, EmpireWarType.附庸独立, EmpireWarType.不奉诏,
        EmpireWarType.对立国王, EmpireWarType.私战
    };

#if EC_PENDING
    private static void Cascade(Snapshot snapshot)
    {
        if (snapshot == null || _cascading || EmpireWorld.Current == null) return;
        _cascading = true;
        try
        {
            double now = GameClock.Now;
            Realm core = snapshot.Empire.CoreKingdom;
            var foes = new HashSet<Realm>(snapshot.Foes);
            foreach (Realm foe in foes) SetTruce(core, foe, now);
            ClearMutualOccupation(core, foes);
            foreach (Realm member in snapshot.Empire.kingdoms_list.ToList())
            {
                // 听命的成员，以及应议会征召参加帝国战争的诸侯(见 HolyRomanSystem.Diet)
                if (!Obeys(snapshot.Empire, member) &&
                    !EmpireCraft.Core.HolyRomanSystem.InImperialWar(snapshot.Empire, member)) continue;
                bool left = false;
                foreach (War war in member.GetWars().ToList())
                {
                    if (war?.Record == null || !war.Alive || war.HasEnded()) continue;
                    var opponents = war.IsAttacker(member) ? war.Defenders : war.Attackers;
                    if (!opponents.Any(foes.Contains)) continue;
                    war.Leave(member);
                    left = true;
                }
                if (left) ClearMutualOccupation(member, foes);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 中央议和连带成员停战失败: {exception.Message}");
        }
        finally
        {
            _cascading = false;
        }
    }
#endif

    // 停战后双方互相留下的占领(地块与攻城进度)清掉：整场战争结束时战争补丁会清，单方面退出时不会
#if EC_PENDING
    private static void ClearMutualOccupation(Realm kingdom, HashSet<Realm> foes)
    {
        if (kingdom == null || kingdom.IsGone()) return;
        foreach (City city in kingdom.Cities.ToList())
            city?.ClearOccupationBy(occupier => foes.Contains(occupier) && !occupier.IsAtWarWith(kingdom));
        foreach (Realm foe in foes)
        {
            if (foe == null || foe.IsGone() || foe.IsAtWarWith(kingdom)) continue;
            foreach (City city in foe.Cities.ToList())
                city?.ClearOccupationBy(occupier => occupier == kingdom);
        }
    }
#endif

    private static void SetTruce(Realm a, Realm b, double now)
    {
        if (a == null || b == null || a == b || a.IsGone() || b.IsGone()) return;
        EmpireWorld.Current.Diplomacy.GetRelation(a, b).Record.LastWarEnded = now;
    }

    // 这场仗是不是中央已经议和退出过的旧仗(开战早于中央与对方停战)
    public static bool IsWarAlreadyLeft(Realm core, War war, Realm member) =>
        member != null && war?.Record != null && HasLeft(core, war, joiningAttackers: war.IsAttacker(member));

    // 要加入某一方的国家，是否已经和另一方议和、退出过这场仗(开战早于双方停战)：
    // 自动参战(中央援助成员、周室攘夷/尊王/合纵)都不再把它拉回同一场旧仗
    public static bool HasLeft(Realm kingdom, War war, bool joiningAttackers)
    {
        if (kingdom == null || war?.Record == null || EmpireWorld.Current?.Diplomacy == null) return false;
        var opponents = joiningAttackers ? war.Defenders : war.Attackers;
        foreach (Realm foe in opponents)
        {
            if (foe == null || foe.IsGone() || foe == kingdom) continue;
            double ended = EmpireWorld.Current.Diplomacy.GetRelation(kingdom, foe).Record.LastWarEnded;
            if (ended > 0d && war.StartedStamp <= ended) return true;
        }
        return false;
    }
}
}
