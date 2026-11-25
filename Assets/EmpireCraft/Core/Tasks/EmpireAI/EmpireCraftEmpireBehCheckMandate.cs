// Generated from the EmpireCraft mod: Scripts/AI/EmpireAI/EmpireCraftEmpireBehCheckMandate.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Linq;

namespace EmpireCraft.Core
{

public class EmpireCraftEmpireBehCheckMandate : GameAIEmpireBase
{
    public override Type OriginalBeh => GetType();

#if EC_PENDING
    public override BehResult Execute(Realm pKingdom)
    {
        pKingdom.CheckEmpire();
        if (!pKingdom.IsEmpire()) return BehResult.Continue;
        Empire empire = pKingdom.GetEmpire();
        if (empire?.CoreKingdom == null) return BehResult.Continue;
        RulerTraitSystem.CheckRestorer(empire);
        ReignRecordSystem.OnYear(empire);
        RitesSystem.Yearly(empire);
        GuoyeSystem.Yearly(empire);
        BannerSystem.Yearly(empire);
        EthnicRankSystem.Yearly(empire);
        LateImperialSystem.Yearly(empire);
        ImperialSealSystem.Yearly(empire);
        CliqueSystem.Yearly(empire);
        // 还没入派的在任官员(城主、成员国君主等)按入派规矩补录，朋党归不进去的也在这里补
        EmpireCraftActorCheckFaction.EnrollOfficials(empire);
        AncientSchoolsSystem.Yearly(empire);
        JimiTributeSystem.Yearly(empire);
        // 今上为还没定谥的先帝定谥号、庙号(见 Empire.ConferPosthumousNames)
        if (empire.Emperor != null && empire.IsNeedToSetPosthumous()) empire.ConferPosthumousNames(empire.Emperor);
        if (empire.Emperor != null && empire.IsNeedToIncreaseMandate())
        {
            empire.AddMandate(1);
            empire.data.last_increase_mandate_timestamp = world.Now();
            foreach (var memberKingdom in empire.kingdoms_list.ToList())
            {
                if (memberKingdom == null || memberKingdom.IsGone()) continue;
                foreach (var title in memberKingdom.GetControlledTitle())
                {
                    if (title == null || title.IsGone()) continue;
                    EmpireCoreManager.TryAbsorbTitle(empire, title);
                }
            }
            // 不再用国库购买 99999 好感；成员态度由实际正统、政体与政治关系决定。
            foreach (var member in empire.kingdoms_list)
                if (member != null && !member.IsGone()) member.EndMaintainGoodOpinion();
        }

        return BehResult.Continue;
    }
#endif
}
}
