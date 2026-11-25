// Ported from the EmpireCraft mod: Scripts/Regimes/WesternPeerageRules.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.

namespace EmpireCraft.Core
{

public static class WesternPeerageRules
{
    public static bool TryGetRulerLevel(KingdomType kingdomType, out PeeragesLevel level)
    {
        switch (kingdomType)
        {
            case KingdomType.Feudalism_empire:
                level = PeeragesLevel.peerages_0;
                return true;
            case KingdomType.Feudalism_kingdom:
                level = PeeragesLevel.peerages_2;
                return true;
            case KingdomType.Feudalism_grand_duchy:
            case KingdomType.Feudalism_duchy:
            case KingdomType.default_country_post:
                // The office name still distinguishes Grand Duke from Duke.
                level = PeeragesLevel.peerages_3;
                return true;
            case KingdomType.Feudalism_march:
                level = PeeragesLevel.peerages_4;
                return true;
            case KingdomType.Feudalism_county:
                level = PeeragesLevel.peerages_5;
                return true;
            case KingdomType.Feudalism_papal_state:
                level = PeeragesLevel.peerages_6;
                return true;
            default:
                level = PeeragesLevel.peerages_6;
                return false;
        }
    }
}
}
