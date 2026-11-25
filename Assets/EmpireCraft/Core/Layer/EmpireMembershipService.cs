// Ported from the EmpireCraft mod: Scripts/Layer/EmpireMembershipService.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System.Collections.Generic;
using System.Linq;
namespace EmpireCraft.Core
{

// 国家保存的 EmpireID 是运行时归属的唯一来源；帝国名单和城市名单只是派生数据。
public static class EmpireMembershipService
{
    public static bool BelongsTo(Empire empire, Realm kingdom) =>
        empire?.data != null && !empire.IsArchived() && kingdom?.Record != null && !kingdom.IsGone() &&
        kingdom.GetEmpireID() == empire.Key;

    // 帝国图层上的归属(用户 2026-10-22 "帝国内部的国家附庸外部的也应该纳入帝国版图")：成员国算它的帝国；
    // 帝国核心或成员国的封建附庸(附庸的附庸也算)也画进宗主所在帝国的版图。只管地图显示，附庸照旧不进成员名单、自有军政外交
#if EC_PENDING
    public static Empire MapEmpire(Realm kingdom)
    {
        if (kingdom?.Record == null || kingdom.IsGone()) return null;
        Empire empire = kingdom.IsInEmpire() ? kingdom.GetEmpire() : null;
        if (empire == null && FeudalVassalService.GetOverlord(kingdom) != null)
            empire = EmpireCoreControl.Sovereign(kingdom)?.GetEmpire();
        return empire?.data != null && !empire.IsGone() && !empire.IsArchived() ? empire : null;
    }
#endif

    // 帝国图层上画进这个帝国、却不是成员的附庸国
#if EC_PENDING
    public static IEnumerable<Realm> MapVassals(Empire empire)
    {
        if (empire?.data == null || EmpireWorld.Current?.LiveRealms == null) yield break;
        foreach (Realm kingdom in EmpireWorld.Current.LiveRealms)
            if (kingdom?.Record != null && !kingdom.IsGone() && !kingdom.IsInEmpire() && MapEmpire(kingdom) == empire)
                yield return kingdom;
    }

    public static IEnumerable<City> EnumerateCities(Empire empire)
    {
        if (empire?.data == null || empire.IsArchived() || AncientWarfareCompatibility.Owns(empire.CoreKingdom)) yield break;
        var seen = new HashSet<long>();
        IEnumerable<Realm> members = empire.kingdoms_list ?? Enumerable.Empty<Realm>();
        if (empire.CoreKingdom != null) members = members.Concat(new[] { empire.CoreKingdom });
        foreach (Realm member in members)
        {
            if (!BelongsTo(empire, member) || AncientWarfareCompatibility.Owns(member) || member.Cities == null) continue;
            foreach (City city in member.Cities)
                if (city != null && !city.IsGone() && city.Realm == member &&
                    !AncientWarfareCompatibility.OwnsObject(city) && seen.Add(city.Key)) yield return city;
        }
    }
#endif

    // 在加载任意帝国对象之前统一判定，避免后加载的旧名单覆盖前一个帝国的核心或成员。
    // 直辖核心优先；普通成员尊重国家自己的存档归属，明确独立的国家不因旧名单重新入帝国。
#if EC_PENDING
    public static void NormalizeLoadedMembership(IEnumerable<EmpireData> records, IEnumerable<Realm> kingdoms,
        ISet<long> kingdomsWithSavedData)
    {
        var active = records.Where(data => data != null && !data.archived)
            .GroupBy(data => data.id).ToDictionary(group => group.Key,
                group => group.OrderByDescending(data => data.timestamp_established_time).First());
        var living = kingdoms.Where(kingdom => kingdom?.Record != null && !kingdom.IsGone())
            .ToDictionary(kingdom => kingdom.Key);
        bool Protected(EmpireData data) => living.TryGetValue(data.empire, out Realm core) &&
            AncientWarfareCompatibility.Owns(core);
        long CoreId(EmpireData data) => living.ContainsKey(data.empire) ? data.empire : data.last_core_kingdom_id;
        var coreClaims = active.Values.Where(data => !Protected(data))
            .GroupBy(CoreId).ToDictionary(group => group.Key, group => group.ToList());
        var memberClaims = new Dictionary<long, List<EmpireData>>();
        foreach (EmpireData data in active.Values)
        {
            if (Protected(data)) continue;
            foreach (long kingdomId in (data.kingdoms ?? new List<long>()).Distinct())
            {
                if (!memberClaims.TryGetValue(kingdomId, out List<EmpireData> claims))
                    memberClaims[kingdomId] = claims = new List<EmpireData>();
                claims.Add(data);
            }
        }
        foreach (Realm kingdom in living.Values)
        {
            if (AncientWarfareCompatibility.Owns(kingdom)) continue;
            long savedOwner = kingdom.GetEmpireID();
            if (active.TryGetValue(savedOwner, out EmpireData savedEmpire) && Protected(savedEmpire)) continue;
            long owner = -1L;
            if (coreClaims.TryGetValue(kingdom.Key, out List<EmpireData> cores))
                owner = cores.OrderByDescending(data => data.id == savedOwner)
                    .ThenByDescending(data => data.timestamp_established_time).ThenBy(data => data.id).First().id;
            else if (kingdomsWithSavedData.Contains(kingdom.Key))
                owner = active.ContainsKey(savedOwner) ? savedOwner : -1L;
            else if (memberClaims.TryGetValue(kingdom.Key, out List<EmpireData> claims))
                owner = claims.OrderByDescending(data => data.id == savedOwner)
                    .ThenByDescending(data => data.timestamp_established_time).ThenBy(data => data.id).First().id;
            kingdom.SetEmpireID(owner);
        }
        foreach (EmpireData data in active.Values)
        {
            if (Protected(data)) continue;
            data.kingdoms = living.Values.Where(kingdom => !AncientWarfareCompatibility.Owns(kingdom) &&
                kingdom.GetEmpireID() == data.id).Select(kingdom => kingdom.id)
                .Concat((data.kingdoms ?? new List<long>()).Where(id => living.TryGetValue(id, out Realm kingdom) &&
                    AncientWarfareCompatibility.Owns(kingdom))).Distinct().OrderBy(id => id).ToList();
            // cities 是过期缓存；加载后从国家当前拥有的城市重建。
            data.cities = living.Values.Where(kingdom => kingdom.GetEmpireID() == data.id &&
                    !AncientWarfareCompatibility.Owns(kingdom))
                .SelectMany(kingdom => kingdom.cities ?? new List<City>())
                .Where(city => city != null && !city.IsGone() && city.kingdom?.GetEmpireID() == data.id &&
                    !AncientWarfareCompatibility.OwnsObject(city))
                .Select(city => city.id).Distinct().ToList();
        }
    }
#endif
}
}
