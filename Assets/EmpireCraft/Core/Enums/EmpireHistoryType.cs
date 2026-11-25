// Ported from the EmpireCraft mod: Scripts/Enums/EmpireHistoryType.cs @ 55d6d4f8. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Core
{
public enum EmpireHistoryType
{
    new_empire_history,
    new_empire_history_west,
    new_emperor_history,
    new_emperor_history_west,
    emperor_die_history,
    emperor_left_history,
    powerful_minister_history,
    join_empire_history,
    give_posthumous_to_previous_emperor_history,
    change_capital_history,
    back_to_original_capital_history,
    rebuild_empire_history,
    war_declared_history,
    war_ended_attacker_victory_history,
    war_ended_defender_victory_history,
    war_ended_peace_history,
    join_taken_alliance_history,
    leave_taken_alliance_history
}
}
