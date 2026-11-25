// Ported from the EmpireCraft mod: Scripts/GeneralSystems/RealmDiplomacySystem.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.

namespace EmpireCraft.Core
{

public static class RealmDiplomacySystem
{
    // 托管行政区不拥有出让国家主权的资格；地方自行称臣不能顺便将其从原帝国中解绑。
#if EC_PENDING
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
#endif
}
}
