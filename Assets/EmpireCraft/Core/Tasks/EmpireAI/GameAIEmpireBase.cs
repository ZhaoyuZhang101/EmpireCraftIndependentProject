// Generated from the EmpireCraft mod: Scripts/AI/EmpireAI/GameAIEmpireBase.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

public abstract class GameAIEmpireBase : RealmTask
{
    public abstract Type OriginalBeh { get; }

#if EC_PENDING
    public override BehResult execute(Realm pObject)
    {
        if (!pObject.IsEmpire()) return BehResult.Stop;
        return BehResult.Continue;
    }
#endif
}
}
