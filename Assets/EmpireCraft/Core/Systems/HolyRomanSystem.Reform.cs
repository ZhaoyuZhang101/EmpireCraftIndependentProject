// Generated from the EmpireCraft mod: Scripts/GeneralSystems/HolyRomanSystem.Reform.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 帝国改革(原型 1495 年沃尔姆斯帝国议会)：西方制度树「帝国改革」节点施行后，各帝国还要在帝国议会提案颁行
//   (帝国权威 60 以上才付议、四年一议，通过扣 30，见 Diet 的 AgendaReform；旧档节点已施行的读档后直接算已颁行)：
//   · 永久和平：此后诸侯不得私战(颁行之前诸侯可为法理打小仗，见 Feuds)，纠纷一律交议会与法院；
//   · 帝国法院：诸侯为法理城起诉，审理、宣判、抗判见 Feuds；
//   · 帝国大区：成员国按都城所在分成几个大区(每四国一区，至多十区)，大区诸侯应征、缴摊派更可靠(抗命几率减半)，
//     帝国禁令由被禁者所在大区最强的诸侯执行；
//   · 议会投票整体 +0.1(诸侯认这套规矩)，正统 +3。
// 分割继承(原型神圣罗马帝国的小邦林立)：封建制帝国里世袭诸侯死后，有几个儿子就有两成可能把领国分给诸子
//   (长子承袭封国，其余城市按重要程度分给诸子，见 GraceEdictService)；选帝侯的领国不可分割(原型金玺诏书)。
public static partial class HolyRomanSystem
{
    public const string NodeImperialReform = "western_imperial_reform";
    // 三成五 → 两成：诸侯国越分越碎，王国一层永远整合不起来(用户 2026-10-21 选"减少碎片")
    private const float PartibleChance = 0.2f;
    private const int MembersPerCircle = 4, MaxCircles = 10, CircleRefreshYears = 10;
    public const int ReformLegitimacy = 3;

    // 本帝国已颁行(帝国级)；旧档还没起算权威时按文化节点算(第一次年结即迁移成颁行，见 EnsureAuthority)
#if EC_PENDING
    public static bool ReformEnacted(Empire empire)
    {
        HolyRomanState state = State(empire);
        if (state == null) return false;
        return state.reform_at >= 0d || state.authority == LegacyAuthority && ReformNodeEnacted(empire);
    }
#endif

    // 文化制度节点(颁行的前提)
#if EC_PENDING
    public static bool ReformNodeEnacted(Empire empire) =>
        empire?.data != null && InstitutionSystem.IsEnacted(empire, NodeImperialReform);
#endif

    // AI 选改革的加分(见 InstitutionSystem.TryStartAiReform)：有帝国议会的选帝制帝国偏向先推帝国改革、后推中央集权君主制
    // (两者同在第五档，中央集权所需的官僚、常备军技术先到)。两者已不再互斥，先推哪个都不锁死另一个；加分由选帝制 30、
    // 世袭制 12 减为 15、0——以前加分再加互斥，西方文化一推帝国改革就永远推不了中央集权，王国一层整合不起来
    // (用户 2026-10-21 选"西欧能整合、帝国能倒")
    public const string NodeCentralizedMonarchy = "western_centralized_monarchy";

#if EC_PENDING
    public static float ReformBias(Empire empire, InstitutionNodeConfig node)
    {
        if (node == null || node.id != NodeImperialReform && node.id != NodeCentralizedMonarchy || !HasDiet(empire)) return 0f;
        float lean = IsElective(empire) ? 15f : 0f;
        return node.id == NodeImperialReform ? lean : -lean;
    }
#endif

    public static int Circle(Empire empire, Realm kingdom)
    {
        HolyRomanState state = State(empire);
        return state?.circles != null && kingdom != null && state.circles.TryGetValue(kingdom.Key, out int circle) ? circle : -1;
    }

    // 大区诸侯：施行帝国改革后分了大区的成员国
#if EC_PENDING
    private static bool InCircle(Empire empire, Realm member) => ReformEnacted(empire) && Circle(empire, member) >= 0;
#endif

    public static int CircleCount(Empire empire) => State(empire)?.circles?.Values.Distinct().Count() ?? 0;

#if EC_PENDING
    private static void ReformYearly(Empire empire, HolyRomanState state)
    {
        if (!ReformEnacted(empire))
        {
            state.circles?.Clear();
            state.circles_at = -1d;
            return;
        }
        List<Realm> members = Members(empire).Where(member => member.Capital != null).ToList();
        state.circles ??= new Dictionary<long, int>();
        bool changed = members.Count != state.circles.Count || members.Any(member => !state.circles.ContainsKey(member.Key));
        if (changed || state.circles_at < 0d || GameClock.YearsSince(state.circles_at) >= CircleRefreshYears)
            AssignCircles(state, members);
    }
#endif

    // 按都城位置分区(简单 k 均值，种子按国号顺序取，结果稳定)
#if EC_PENDING
    private static void AssignCircles(HolyRomanState state, List<Realm> members)
    {
        state.circles.Clear();
        state.circles_at = GameClock.Now;
        if (members.Count == 0) return;
        int k = Mathf.Clamp(Mathf.RoundToInt(members.Count / (float)MembersPerCircle), 1, MaxCircles);
        List<Realm> ordered = members.OrderBy(member => member.Key).ToList();
        var centers = new List<Vector2>();
        for (int i = 0; i < k; i++) centers.Add(ordered[i * ordered.Count / k].Capital.city_center);
        var assignment = new Dictionary<Realm, int>();
        for (int round = 0; round < 6; round++)
        {
            foreach (Realm member in ordered)
            {
                Vector2 at = member.Capital.city_center;
                int best = 0;
                for (int i = 1; i < k; i++)
                    if ((centers[i] - at).sqrMagnitude < (centers[best] - at).sqrMagnitude) best = i;
                assignment[member] = best;
            }
            for (int i = 0; i < k; i++)
            {
                List<Vector2> points = assignment.Where(pair => pair.Value == i).Select(pair => pair.Key.Capital.city_center).ToList();
                if (points.Count > 0) centers[i] = new Vector2(points.Average(p => p.x), points.Average(p => p.y));
            }
        }
        foreach (var pair in assignment) state.circles[pair.Key.Key] = pair.Value;
    }
#endif

    // 分割继承(GraceEdictService.TryHandleVacancy 调用，每次出缺只掷一次)；掷中后还要有两个以上的儿子、都城外有城才分
#if EC_PENDING
    public static bool RollPartible(Empire empire, Realm fief)
    {
        if (!HasDiet(empire) || fief?.Record == null) return false;
        string blocked = fief.GetRegime()?.type != RegimeType.Feudalism ? "不是封建制"
            : Estate(fief) != EstateNone ? "自由城市或主教领"
            : fief.Cities.Count < 2 ? "只有一城"
            : Seats(State(empire)).Contains(fief.Key) ||
              empire.data.CabinetMembers != null && empire.GetCabinetMembers().Any(actor => actor?.Realm == fief) ? "选帝侯领国不可分"
            : null;
        bool rolled = blocked == null && EcRandom.value < PartibleChance;
        PartibleTally.TryGetValue(empire.data.id, out var tally);
        PartibleTally[empire.data.id] = (tally.vacant + 1, tally.rolled + (rolled ? 1 : 0),
            rolled ? tally.blocked : blocked ?? "没掷中(两成)");
        return rolled;
    }
#endif

    // 分割继承的年度计数(诊断用，不存档)：帝国 → (诸侯出缺几次, 掷中几次, 最近一次没分的缘由)
    private static readonly Dictionary<long, (int vacant, int rolled, string blocked)> PartibleTally = new();
}
}
