// Generated from the EmpireCraft mod: Scripts/GeneralSystems/InstitutionRealm.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// 推行制度改革的"政权"：帝国，或独立王国/部落(简化朝堂：君主亲裁，以君威代替正统，见 RealmAuthority)。
// 制度仍然归文化所有(见 InstitutionSystem)，政权只持有"正在推的这一次改革"和"本政权落地过哪些节点的持久效果"；
// 以前只有帝国能推，部落时代只好把每个独立的部落都建成帝国(TribalFissionService.FormTribes)；10-26 起部落不再是帝国，
// 原始改革由各部落按王国推(用户 2026-10-26 选"部落不再是帝国")。
// 只是个轻量包装，用时现取，不进存档：帝国的状态存在 EmpireData，王国的存在 KingdomExtraData.institution_state。
public sealed class InstitutionRealm
{
    // 战国时离开周室的诸侯(ZhouEraSystem.IsDetachedVassal)算独立王国，自己推制度(用户定)；改成 false 就不算
    public const bool DetachedZhouVassalsAreSovereign = true;

    public readonly Empire Empire;
    public readonly Realm Realm;

    private InstitutionRealm(Empire empire, Realm kingdom)
    {
        Empire = empire;
        Realm = kingdom;
    }

    public static InstitutionRealm Of(Empire empire) => empire == null ? null : new InstitutionRealm(empire, null);

    // 帝国核心、成员国一律归到所在的帝国；其余(独立王国、附庸、部落)按本国，是否独立另看 IsSovereign
    public static InstitutionRealm Of(Realm kingdom)
    {
        if (kingdom == null) return null;
        if (kingdom.IsEmpire() || kingdom.IsInEmpire())
        {
            Empire empire = kingdom.GetEmpire();
            if (empire != null) return new InstitutionRealm(empire, null);
        }
        return new InstitutionRealm(null, kingdom);
    }

    // 能自己推制度的独立王国：开化、非野、有都城、君主是本国的(不是别国君主兼领)、不是帝国也不在帝国里、
    // 没有封建宗主、不奉部落共主、不归古战模组管。朝贡国照样算(朝贡不管内政)；战国离开周室的诸侯按上面的常量
#if EC_PENDING
    public static bool IsSovereignKingdom(Realm kingdom)
    {
        if (kingdom?.Record == null || kingdom.IsGone() || !kingdom.IsCivilized() || kingdom.IsWild) return false;
        if (EmpireCraft.Core.AncientWarfareCompatibility.Owns(kingdom)) return false;
        if (kingdom.Capital == null || kingdom.Capital.IsGone()) return false;
        if (kingdom.HasRuler() && kingdom.Ruler != null && kingdom.Ruler.Realm != kingdom) return false;
        if (kingdom.IsEmpire() || kingdom.IsInEmpire()) return false;
        if (FeudalVassalService.GetOverlord(kingdom) != null) return false;
        if (TribalFissionService.OverlordOf(kingdom) != null) return false;
        // 殖民地的制度随宗主国，不自己推(见 ColonySystem)
        if (ColonySystem.IsColony(kingdom)) return false;
#pragma warning disable CS0162
        if (!DetachedZhouVassalsAreSovereign && ZhouEraSystem.IsDetachedVassal(kingdom)) return false;
#pragma warning restore CS0162
        return true;
    }
#endif

    public bool IsEmpire => Empire != null;

    // 推制度的那个王国：帝国的核心王国，或王国本身
    public Realm Core => Empire != null ? Empire.CoreKingdom : Realm;

    public bool IsAlive => Empire != null
        ? Empire.data != null && !Empire.IsGone() && !Empire.IsArchived() && Empire.CoreKingdom != null &&
          !Empire.CoreKingdom.IsGone()
        : Realm?.Record != null && !Realm.IsGone();

    // 王国是独立政权才推制度；帝国总是
#if EC_PENDING
    public bool IsSovereign => Empire != null ? IsAlive : IsSovereignKingdom(Realm);
#endif

    // 制度文化：帝国按制度文化(征服王朝、统治多数派，见 InstitutionSystem.GetPrimaryCulture)，王国按本国文化；
    // 旧制犹存的按旧制所属的文化(见 LegacyInstitutionSystem)，推的也是那棵制度树
#if EC_PENDING
    public string Culture => Empire != null
        ? InstitutionSystem.GetPrimaryCulture(Empire)
        : CultureService.GetInstitutionCulture(Realm);
#endif

    // 制度效果、阶层人口所及的国家：帝国全体成员，王国只有自己
    public IEnumerable<Realm> Scope => Empire != null
        ? (IEnumerable<Realm>)Empire.kingdoms_hashset ?? new Realm[0]
        : Realm == null ? new Realm[0] : new[] { Realm };

    // 现有的改革状态(可能为空；要建出来用 InstitutionSystem.EnsureRealmState)
    public InstitutionEmpireState State => Empire != null
        ? Empire.data?.institution_state
        : Realm?.Record == null ? null : Realm.GetOrCreate().institution_state;

    // 推改革要够的威望：帝国看正统，王国看君威(见 RealmAuthority)
#if EC_PENDING
    public int Authority => Empire != null ? Empire.Legitimacy : RealmAuthority.Get(Realm);

    public string Name => Empire != null ? Empire.GetEmpireName() : Realm?.GetKingdomName() ?? "";
#endif

    public Person Ruler => Empire != null ? Empire.Emperor : Realm?.Ruler;

    // 字典键、日志用；帝国与王国编号各成一套，前缀分开
    public string Key => Empire != null ? "e" + Empire.Key : "k" + (Realm?.Key ?? -1L);

    // 记一笔：帝国写进史书；王国没有史书，只进日志(世界消息由调用方另发)
#if EC_PENDING
    public void Record(string content, long actorId = -1L, long kingdomId = -1L)
    {
        if (string.IsNullOrEmpty(content)) return;
        if (Empire != null) Empire.RecordHistory(directContent: content, actorId: actorId, kingdomId: kingdomId);
        else LogService.LogInfo($"[EmpireCraft][制度] {content}");
    }

    public bool Is(InstitutionRealm other) =>
        other != null && ReferenceEquals(Empire, other.Empire) && ReferenceEquals(Realm, other.Kingdom);

    public override bool Equals(object obj) => obj is InstitutionRealm other && Is(other);
#endif

    public override int GetHashCode() => Empire != null ? Empire.GetHashCode() : Realm?.GetHashCode() ?? 0;
}
}
