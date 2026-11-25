// Ported from the EmpireCraft mod: Scripts/GeneralSystems/WartimeCustodySystem.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// 战时中央代管与战后分配(参照钢铁雄心：战时占领与最终归属分开)：
//   帝国成员在中央也参战的对外战争里打下的城，先交中央暂管，记下是谁打下的、哪一场战争；
//   该场战争结束后逐城结算最终归属：
//     旧都 → 中央；城市法理属于某成员 → 该成员；
//     中央衰弱(正统 < WeakCenterLegitimacy)且打下它的成员与之接壤 → 打下它的成员；
//     否则交给与它边界最长的成员(同长度时打下它的成员、中央优先)；没有成员与它接壤 → 中央直辖。
//   只把城交给与它接壤或持有其法理的成员，不会出现"山东道管到新疆"；成员内部再自行规划行政区。
//   独立盟国、封国(有自己宗主的诸侯)、帝国内战和叛乱不走代管，各自按原规则结算。
public static class WartimeCustodySystem
{
    private const int WeakCenterLegitimacy = 40;
    // 一次扫描最多结算几座城(易主有原版开销)，没结完的下个月继续
    private const int SettlePerScan = 4;
    private static double _lastScan = -1d;

    // 返回中央(需要代管)或 null(不代管)
#if EC_PENDING
    public static Realm CustodianFor(Realm capturer, Realm oldKingdom)
    {
        if (capturer == null || oldKingdom == null || !capturer.IsInEmpire()) return null;
        Empire empire = capturer.GetEmpire();
        Realm core = empire?.CoreKingdom;
        if (core == null || core.IsGone() || core == capturer) return null;
        if (ZhouEraSystem.RoyalReclaimBlocked(empire)) return null;
        if (capturer.IsInSameEmpire(oldKingdom)) return null;
        if (capturer.IsFactionRebelling() || capturer.IsLocalRebelling()) return null;
        if (FeudalVassalService.GetOverlord(capturer) != null) return null;
        if (!core.isInWarWith(oldKingdom)) return null;
        return core;
    }
#endif

    // 城市交给中央之前调用
#if EC_PENDING
    public static void Record(City city, Realm capturer, Realm oldKingdom, War war)
    {
        var data = city?.GetOrCreate();
        if (data == null) return;
        data.custody_capturer_id = capturer?.Key ?? -1L;
        data.custody_enemy_id = oldKingdom?.Key ?? -1L;
        data.custody_war_id = war?.Key ?? -1L;
        LogService.LogInfo($"[EmpireCraft][战时代管] {capturer?.Name} 打下 {city.Name}，交中央暂管");
    }
#endif

    public static void Clear(City city)
    {
        var data = city?.GetOrCreate();
        if (data == null) return;
        data.custody_capturer_id = data.custody_enemy_id = data.custody_war_id = -1L;
    }

    public static bool InCustody(City city) => city?.Record != null && city.GetOrCreate().custody_capturer_id >= 0;

    // 每月检查一次(挂在 CityPatch 的世界扫描轮转上)
#if EC_PENDING
    public static void TryMonthlyScan()
    {
        if (EmpireWorld.Current?.Cities == null || ModClass.IS_CLEAR) return;
        double now = GameClock.Now;
        if (_lastScan >= 0d && now >= _lastScan && GameClock.MonthsSince(_lastScan) < 1) return;
        _lastScan = now;
        int settled = 0;
        foreach (City city in EmpireWorld.Current.Cities)
        {
            if (settled >= SettlePerScan) break;
            if (city?.Record == null || city.IsGone() || !InCustody(city)) continue;
            try
            {
                if (TrySettle(city)) settled++;
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][战时代管] 结算 {city.Name} 失败: {exception.Message}");
                Clear(city);
            }
        }
    }
#endif

    public static void ResetWorldState() => _lastScan = -1d;

#if EC_PENDING
    private static bool TrySettle(City city)
    {
        var data = city.GetOrCreate();
        Realm holder = city.Realm;
        Empire empire = holder?.GetEmpire();
        // 代管期间城市又丢了、中央换了或帝国没了：代管作废
        if (holder == null || holder.IsGone() || empire == null || empire.IsGone() || empire.CoreKingdom != holder)
        {
            Clear(city);
            return false;
        }
        if (!WarOver(data.custody_war_id, holder, data.custody_enemy_id)) return false;
        Realm capturer = EmpireWorld.Current.RealmByKey(data.custody_capturer_id);
        Clear(city);
        Realm target = Allocate(city, empire, capturer);
        if (target == null || target == holder) return false;
        city.JoinRealm(target);
        FiscalDiagnostics.Count("战后分配");
        TranslateHelper.LogEventMessage(string.Format(LM.Get("wartime_custody_settled"),
            empire.GetEmpireFullName(), city.GetCityName(), target.Name), holder);
        return true;
    }

    private static bool WarOver(long warId, Realm core, long enemyId)
    {
        War war = warId >= 0 ? EmpireWorld.Current.wars.get(warId) : null;
        if (war != null && war.Alive && !war.hasEnded()) return false;
        // 同一敌人还在别的战争里与中央交战：等那场也打完再分
        Realm enemy = enemyId >= 0 ? EmpireWorld.Current.RealmByKey(enemyId) : null;
        return enemy == null || enemy.IsGone() || !core.isInWarWith(enemy);
    }

    private static Realm Allocate(City city, Empire empire, Realm capturer)
    {
        Realm core = empire.CoreKingdom;
        // 春秋战国：谁打下的归谁
        if (ZhouEraSystem.RoyalReclaimBlocked(empire) && capturer != core && CanReceive(capturer, empire, core))
            return capturer;
        // 收复旧都归中央
        City oldCapital = EmpireCoreManager.Get(empire)?.GetCoreCapital();
        if (oldCapital == city) return core;

        // 法理
        KingdomTitle title = city.GetTitle();
        Realm dejure = title?.main_kingdom;
        if (dejure == null && title?.owner != null && !title.owner.IsGone()) dejure = title.owner.Realm;
        if (CanReceive(dejure, empire, core)) return dejure;

        bool capturerOk = CanReceive(capturer, empire, core);
        Dictionary<Realm, int> border = BorderLengths(city, empire);
        if (capturerOk && empire.Legitimacy < WeakCenterLegitimacy && border.ContainsKey(capturer)) return capturer;

        Realm best = null;
        int bestLength = 0;
        foreach (KeyValuePair<Realm, int> pair in border)
        {
            if (pair.Key != core && !CanReceive(pair.Key, empire, core)) continue;
            int length = pair.Value;
            bool better = length > bestLength ||
                          length == bestLength && best != null && Rank(pair.Key, capturer, core) < Rank(best, capturer, core);
            if (!better) continue;
            best = pair.Key;
            bestLength = length;
        }
        return best ?? core;
    }
#endif

    private static int Rank(Realm kingdom, Realm capturer, Realm core) =>
        kingdom == capturer ? 0 : kingdom == core ? 1 : 2;

#if EC_PENDING
    private static bool CanReceive(Realm kingdom, Empire empire, Realm core)
    {
        if (kingdom == null || kingdom.IsGone() || kingdom.Capital == null) return false;
        if (kingdom == core) return true;
        if (!EmpireMembershipService.BelongsTo(empire, kingdom)) return false;
        if (kingdom.IsFactionRebelling() || kingdom.IsLocalRebelling() || kingdom.isInWarWith(core)) return false;
        // 治理能力：自己首府都濒临叛乱的成员不再接收新地
        var state = kingdom.Capital.GetOrCreate().stability;
        if (state != null && state.owner_id == kingdom.Key && state.stability < CityStabilityRules.RebellionThreshold) return false;
        return !EmpireCraft.Core.AncientWarfareCompatibility.Owns(kingdom);
    }
#endif

    // 与本城相邻的各成员共有多少段边界(按区域四向相邻计)
#if EC_PENDING
    private static Dictionary<Realm, int> BorderLengths(City city, Empire empire)
    {
        var result = new Dictionary<Realm, int>();
        if (city.zones == null) return result;
        foreach (TileZone zone in city.zones)
        {
            if (zone?.neighbours == null || zone.city != city) continue;
            foreach (TileZone neighbour in zone.neighbours)
            {
                City other = neighbour?.city;
                if (other == null || other == city || other.IsGone()) continue;
                Realm kingdom = other.Realm;
                if (kingdom == null || !EmpireMembershipService.BelongsTo(empire, kingdom)) continue;
                result.TryGetValue(kingdom, out int count);
                result[kingdom] = count + 1;
            }
        }
        return result;
    }
#endif
}
}
