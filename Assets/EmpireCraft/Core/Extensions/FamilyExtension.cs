// Ported from the EmpireCraft mod: Scripts/GameClassExtensions/FamilyExtension.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EmpireCraft.Core.WarExtension;

namespace EmpireCraft.Core
{
public static class FamilyExtension
{
    public class FamilyExtraData : ExtraDataBase
    {
        public bool is_culture_applied { get; set; } = false;
    }
#if EC_PENDING
    public static void ApplyCulture(this Family family)
    {
        family.GetOrCreate().is_culture_applied = true;
    }

    public static bool HasCultureApplied(this Family family)
    {
        return family.GetOrCreate().is_culture_applied;
    }

    public static FamilyExtraData GetOrCreate(this Family family, bool isSave=false)
    {
        return family.GetOrCreate<Family, FamilyExtraData>(isSave);
    }
#endif
}
}
