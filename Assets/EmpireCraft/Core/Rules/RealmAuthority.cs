// Ported from the EmpireCraft mod: Scripts/GeneralSystems/RealmAuthority.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using Time = EmpireCraft.Core.FrameClock;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 君威：独立王国/部落推制度改革时代替正统(帝国的正统见 MonarchyLegitimacy)。算式见 RealmAuthorityRules；
// 这里只取已结算的数(国库、腐败、苛政快照、君主资质与声望、天命、战争)，不扫人口，界面频繁读也不卡
public static class RealmAuthority
{
    private const float CacheSeconds = 1f;
    private static readonly Dictionary<long, (int value, float at)> Cache = new();
    private static object _cacheWorld;

#if EC_PENDING
    public static int Get(Realm kingdom)
    {
        if (kingdom?.Record == null || kingdom.IsGone() || EmpireWorld.Current == null) return 0;
        if (!ReferenceEquals(_cacheWorld, EmpireWorld.Current))
        {
            _cacheWorld = EmpireWorld.Current;
            Cache.Clear();
        }
        float now = Time.unscaledTime;
        if (Cache.TryGetValue(kingdom.Key, out var cached) && now >= cached.at && now - cached.at < CacheSeconds)
            return cached.value;
        int value = RealmAuthorityRules.Clamp(Breakdown(kingdom).Sum(item => item.value));
        Cache[kingdom.Key] = (value, now);
        return value;
    }
#endif

    // 君威的各项来源(界面悬停明细用)；键复用正统明细里已有的说法，君威独有的另起
#if EC_PENDING
    public static List<(string key, int value)> Breakdown(Realm kingdom)
    {
        var items = new List<(string key, int value)>();
        if (kingdom?.Record == null || kingdom.IsGone()) return items;
        items.Add(("realm_authority_base", RealmAuthorityRules.Base));
        Person ruler = kingdom.Ruler;
        bool reigning = ruler != null && !ruler.IsGone() && ruler.Alive;
        items.Add(reigning ? RulerQualitySystem.LegitimacyItem(ruler) : ("mandate_interregnum", RealmAuthorityRules.Interregnum));
        int treasury = RealmAuthorityRules.Treasury(kingdom.GetTreasuryBalance());
        if (treasury != 0) items.Add((treasury > 0 ? "mandate_solvent_treasury" : "mandate_debt", treasury));
        int corruption = RealmAuthorityRules.Corruption(CorruptionSystem.GetRate(kingdom));
        if (corruption != 0) items.Add(("mandate_corruption", corruption));
        int misrule = RealmAuthorityRules.Misrule(HarshRuleSystem.GetBurden(kingdom));
        if (misrule != 0) items.Add(("mandate_misrule", misrule));
        int prestige = reigning ? RealmAuthorityRules.Prestige(ruler.Record?.renown ?? 0) : 0;
        if (prestige != 0) items.Add(("realm_authority_prestige", prestige));
        // 受着某个天下的天命(天下共主，见 TianxiaSystem)
        if (TianxiaSystem.MandateOf(kingdom) != null) items.Add(("mandate_tianxia_holder", RealmAuthorityRules.MandateHolder));
        if (InRebellion(kingdom)) items.Add(("realm_authority_rebellion", RealmAuthorityRules.Rebellion));
        return items;
    }
#endif

    // 叛乱未平：本国正在起兵(派系、地方叛乱)，或正被叛军攻打
#if EC_PENDING
    public static bool InRebellion(Realm kingdom)
    {
        if (kingdom?.Record == null || kingdom.IsGone()) return false;
        if (kingdom.IsFactionRebelling() || kingdom.IsLocalRebelling()) return true;
        foreach (War war in kingdom.getWars())
            if (war != null && !war.hasEnded() && war.main_defender == kingdom && MusterSystem.IsRebellion(war))
                return true;
        return false;
    }
#endif

    public static void Invalidate(Realm kingdom = null)
    {
        if (kingdom == null) Cache.Clear();
        else Cache.Remove(kingdom.Key);
    }
}
}
