// Ported from the EmpireCraft mod: Scripts/Layer/AdministrativeDivisionNameRules.cs @ e770dbbe. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

public static class AdministrativeDivisionNameRules
{
    public static long SelectProvinceNameHolder(long capitalControllerId,
        IEnumerable<long> administrationIds)
    {
        if (administrationIds == null) return -1L;
        List<long> ids = administrationIds.Where(id => id >= 0).Distinct().OrderBy(id => id).ToList();
        if (ids.Count == 0) return -1L;
        return ids.Contains(capitalControllerId) ? capitalControllerId : ids[0];
    }
}
}
