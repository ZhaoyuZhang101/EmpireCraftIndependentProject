// Generated from the EmpireCraft mod: Scripts/Layer/EmpireHistoryDisplay.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{
    public static class EmpireHistoryDisplay
    {
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
    }
}
