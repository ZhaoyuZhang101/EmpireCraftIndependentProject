// Generated from the EmpireCraft mod: Scripts/GeneralSystems/RealmDiplomacySystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.

namespace EmpireCraft.Core
{

public static class RealmDiplomacySystem
{
    // 托管行政区不拥有出让国家主权的资格；地方自行称臣不能顺便将其从原帝国中解绑。
    public static bool CanChooseOverlord(Realm kingdom)
    {
        if (kingdom == null || kingdom.IsGone()) return false;
        // 殖民地只认自己的宗主国(见 ColonySystem)
        if (ColonySystem.IsColony(kingdom)) return false;
        if (kingdom.IsEmpire()) return true;
        var empire = kingdom.GetEmpire();
        if (kingdom.IsAdministrativeKingdomType() || (empire != null && kingdom.GetOrCreate().AdministrativeTitle >= 0)) return false;
        if (empire?.CoreKingdom?.GetRegime()?.type == RegimeType.LvLing) return false;
        return empire == null || kingdom.GetRegime()?.IsAllowDiplomacy() == true;
    }
}
}
