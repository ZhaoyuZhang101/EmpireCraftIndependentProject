// Ported from the EmpireCraft mod: Scripts/Layer/EmpireHistoryDisplay.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{
    public static class EmpireHistoryDisplay
    {
#if EC_PENDING
        public static string FullName(string snapshot, string shortName, string storedFullName,
            string storedShortName, string unknown)
        {
            if (!string.IsNullOrWhiteSpace(snapshot)) return snapshot.UseLocalizedNameSeparator();
            // Older histories only saved the short name. Reuse surviving data only for the same name.
            if (!string.IsNullOrWhiteSpace(storedFullName) &&
                string.Equals(shortName, storedShortName, StringComparison.Ordinal))
                return storedFullName.UseLocalizedNameSeparator();
            return string.IsNullOrWhiteSpace(shortName) ? unknown : shortName;
        }
#endif
    }
}
