using System.Collections.Generic;

namespace EmpireCraft.Core
{
    // Pieces the mod's save and load (DataManager) name that this game answers itself.

    // The mod bound each of the base game's cultures to a family of naming rules by the culture's id. Here a culture
    // carries its family itself and the base save keeps it (EmpireSave, version 6), so there is nothing more to bind.
    public static class CulturePatch
    {
        public static Dictionary<long, string> ExportCultureBindings() => new Dictionary<long, string>();
        public static void ImportCultureBindings(Dictionary<long, string> bindings) { }
    }

    // The id of a game run on the mod's server core. No server core here: none.
    public static class CoreGameId
    {
        public static string Value;
    }

    // The mod's own world laws, saved with the world: each law's switch by id; a law the save does not name goes back
    // to its default (as EmpireCraftWorldLawLibrary.Capture/Apply did, over this game's WorldLaws).
    public static class EmpireCraftWorldLawLibrary
    {
        public static Dictionary<string, bool> Capture()
        {
            var result = new Dictionary<string, bool>();
            foreach (WorldLaw law in WorldLaws.All) result[law.Id] = law.Enabled;
            return result;
        }

        public static void Apply(Dictionary<string, bool> saved)
        {
            foreach (WorldLaw law in WorldLaws.All)
                law.Enabled = saved != null && saved.TryGetValue(law.Id, out bool on) ? on : law.Default;
        }
    }

    // The mod's read-only check of a loaded save, written to its log for players' bug reports. The tests check a loaded
    // world here (EmpireInvariants); nothing to add.
    public static class SaveIntegrityChecker
    {
        public static int Run(string trigger) => 0;
    }
}
