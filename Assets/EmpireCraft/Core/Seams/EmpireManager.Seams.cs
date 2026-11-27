using System.Collections.Generic;

namespace EmpireCraft.Core
{
    // Hand-written half of EmpireManager: one empire's check (CheckOne), as the mod's, without its monthly update.
    //
    // The mod checked each empire is still standing (repairing what it can, the rest dissolved after the round) and, on
    // the base game's monthly manager update, ran the empire's own update beside it (Empire.update). Here an empire's year
    // runs from the scheduler (PortedSystems, the live part of that chain in the mod's order), so the check is all this
    // does, whatever it is asked: after a load (RepairAfterLoad) and on the monthly liveness pass alike.
    public partial class EmpireManager
    {
        private static void CheckOne(Empire current, bool runEmpireUpdates, List<Empire> dissolve)
        {
            if (current.IsArchived() || dissolve.Contains(current)) return;
            current.clearCursorOver();
            if (!current.checkActive() && !current.TryRepairState("EmpireManager.update fallback"))
                dissolve.Add(current);
        }
    }
}
