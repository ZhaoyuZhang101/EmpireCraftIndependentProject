// Ported from the EmpireCraft mod: Scripts/ModClass.cs#ModClass.ALL_HISTORY_DATA @ 573aceb9. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

public partial class ModClass
{
    public static Dictionary<long, List<EmpireCraftHistory>> ALL_HISTORY_DATA = new Dictionary<long, List<EmpireCraftHistory>>();
}
}
