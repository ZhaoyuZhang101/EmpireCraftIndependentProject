// Generated from the EmpireCraft mod: Scripts/GeneralSystems/CoreCapitalRestorationSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

// 还于旧都：帝国核心记录建国时的首都(EmpireCore.CoreCapital)；持有该正统的帝国一旦把旧都
// 纳入自己的势力范围(本帝国任一成员持有)，中央就迁都回去——旧都不必在核心法理范围内。
//   旧都在成员国手里时先划归中央再迁(还于旧都)；成员只剩这一座城、城在战时代管中、
//   正被占领、或持有者正与中央交战(内战/叛乱)时不动。
//   旧档核心缺少记录首都的，用现有 GetRepresentativeCity 补录。
// 每月检查一次(挂 CityPatch 的世界扫描轮转)；迁回后失都改称系统自会恢复国号。选帝制帝国不还都(帝都随当选家族)。
public static class CoreCapitalRestorationSystem
{
    private static double _lastScan = -1d;

    public static void TryMonthlyScan()
    {
        if (EmpireWorld.Current == null || ModClass.EMPIRE_MANAGER == null || ModClass.IS_CLEAR) return;
        double now = GameClock.Now;
        if (_lastScan >= 0d && now >= _lastScan && GameClock.MonthsSince(_lastScan) < 1) return;
        _lastScan = now;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            try
            {
                Check(empire);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][还于旧都] 检查失败: {exception.Message}");
            }
        }
    }

    // 还于旧都后的首都锚定：帝国核心王国坐镇记录的旧都时，封号同步与迁都决议不再把首都
    // 迁回主封号首府——否则它们和本系统每月互相迁都，世界日志反复刷"还于旧都"
    public static bool IsAnchored(Realm kingdom)
    {
        if (kingdom?.Capital == null) return false;
        // 奉诏迁都定下的都城(见 RulerDecrees)同样锚定
        if (kingdom.Capital.Record != null && kingdom.GetOrCreate().decreed_capital == kingdom.Capital.Key) return true;
        Empire empire = kingdom.GetEmpire();
        if (empire == null || empire.CoreKingdom != kingdom) return false;
        return EmpireCoreManager.Get(empire)?.GetCoreCapital() == kingdom.Capital;
    }

    // 迁都后行政区随都城走：
    //   古代：王畿(核心主封号)切到新都所在法理，旧王畿在随后的重组里按法理划出去成为正常行政区("换直隶省")；
    //   现代：不动主封号，重组时直辖市只留首都一城(见 AdministrationReorganizationSystem 的现代分支)。
    //   两种政体都排一次自动行政区重组；正统不足或战时由 Empire.update 的 Resume 在条件恢复后继续。
    internal static void AlignAdministration(Empire empire, Realm core, City capital)
    {
        bool modern = core.GetRegime()?.type == RegimeType.Modern;
        KingdomTitle newHomeTitle = capital.GetTitle();
        if (!modern && newHomeTitle != null && !newHomeTitle.IsGone() && core.GetMainTitle() != newHomeTitle)
            core.SetMainTitle(newHomeTitle);
        // 迁回旧都不等于重划预设的全部省界；只保留上面的迁都与主封号同步。
        if (WorldMapWilderness.FixedLayout) return;
        empire.data.administrative_reorganization_pending = true;
        AdministrationReorganizationSystem.Resume(empire);
    }

    private static void Check(Empire empire)
    {
        if (empire?.data == null || empire.IsGone() || empire.IsArchived() || ZhouEraSystem.RoyalReclaimBlocked(empire))
            return;
        Realm core = empire.CoreKingdom;
        if (core == null || core.IsGone()) return;
        // 选帝制(颁布金玺诏书)：帝都跟当选的家族走(见 HolyRomanSystem)。以前每次选帝后旧都都被从上一家手里划归中央、迁都回去，
        // 家族领地成了空话，世界日志一换皇帝就刷一次"还于旧都"
        if (HolyRomanSystem.IsElective(empire)) return;
        EmpireCore empireCore = EmpireCoreManager.Get(empire);
        if (empireCore == null) return;
        // 旧档缺记录首都时补录(现任首都或第一块法理的首府)
        City capital = EmpireCoreManager.GetRepresentativeCity(empireCore);
        if (capital == null || capital.IsGone() || capital == core.Capital) return;
        Realm owner = capital.Realm;
        // 还没成为自己的势力范围：等
        if (owner == null || owner.IsGone() || !EmpireMembershipService.BelongsTo(empire, owner)) return;
        // 代管、被占领、持有者与中央交战或正在叛乱时不动
        if (WartimeCustodySystem.InCustody(capital)) return;
        if (capital.GetOrCreate().OccupiedStatus?.Count > 0) return;
        if (owner != core && (owner.IsAtWarWith(core) || owner.IsFactionRebelling() || owner.IsLocalRebelling())) return;
        if (owner != core)
        {
            // 旧都在成员手里：划归中央再迁。成员只剩这一座城就等于灭国，不拿
            if ((owner.Cities?.Count ?? 0) <= 1) return;
            capital.JoinRealm(core);
            if (capital.Realm != core) return;
        }
        core.SetCapital(capital);
        if (core.Capital != capital) return;
        AlignAdministration(empire, core, capital);
        FiscalDiagnostics.Count("还于旧都");
        EventRecorder.Record(empire, string.Format(LM.Get("empire_return_capital_history"),
            empire.GetEmpireFullName(), capital.GetCityName()), empire.Emperor);
    }
}
}
