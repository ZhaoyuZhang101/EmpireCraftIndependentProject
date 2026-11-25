// Generated from the EmpireCraft mod: Scripts/AI/KingdomAI/EmpireCraftKingdomBehCheckExam.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{
public class EmpireCraftKingdomBehCheckExam: GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();
#if EC_PENDING
    public override BehResult Execute(Realm pKingdom)
    {
        // 旧存档里船只/战争机器当了君主：撤下，由原版另选(新的册立已在 KingdomPatch.before_new_emperor 拦下)
        if (pKingdom.Ruler != null && pKingdom.Ruler.IsWarMachine()) pKingdom.RemoveRuler();
        pKingdom.CheckEmpire();
        var regime = pKingdom.GetRegime();
        if (regime == null) return BehResult.Continue;
        // 帝国：行了科举制度就按期开科，不看核心是不是按科举选任(核心都是世袭，以前因此一科都不开，见 KejuSystem)
        if (pKingdom.IsEmpire())
        {
            Empire empire = pKingdom.GetEmpire();
            if (empire != null && empire.CoreKingdom == pKingdom) KejuSystem.TryRegular(empire);
        }
        // 独立之国：四年一科(以前不看间隔，AI 每轮到一次就考一次，城城都落成几位考生)
        else if (regime.GetLeaderSelectMethod() == LeaderSelectMethod.Exam && !pKingdom.IsInEmpire() && pKingdom.IsNeedToExam())
        {
            foreach (City city in pKingdom.Cities)
            {
                ExamSystem.startExam(ExamSystem.ExamType.City, city);
            }
            ExamSystem.startExam(ExamSystem.ExamType.Province, pKingdom);
            pKingdom.UpdateExamTime();
        }

        return BehResult.Continue;
    }
#endif
}
}
