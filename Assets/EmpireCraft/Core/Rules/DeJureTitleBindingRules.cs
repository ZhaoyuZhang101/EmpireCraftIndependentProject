// Ported from the EmpireCraft mod: Scripts/Layer/DeJureTitleBindingRules.cs @ e770dbbe. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
namespace EmpireCraft.Core
{
    public static class DeJureTitleBindingRules
    {
        public const int WeakMandateThreshold = 60;

        public static bool IsAdministrativeAcquisitionBlocked(bool isLvLing, bool isProvinceOrMilitary,
            int mandate)
        {
            return isLvLing && isProvinceOrMilitary && mandate > WeakMandateThreshold;
        }

        public static string GetLandedPeerageKey(bool isImperialClan)
        {
            return isImperialClan ? "default_peerages_2" : "tang_peerage_guogong";
        }
    }
}
