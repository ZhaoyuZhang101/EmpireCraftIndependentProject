using System.Collections.Generic;

namespace EmpireCraft.Core
{
    // Hand-written halves for the mod's load: repairs of saves older versions of the mod wrote. A save of this game is
    // written by this game, so it has none of those faults to repair:
    //   - naming templates mixed up with word-list names (TemplateNameRepair), on cultures and on people, clans, cities;
    //   - book titles in the old form (BookNamingSystem.RepairLegacyNames);
    //   - realms that left an empire long ago still named as its province and coloured as it (LeftEmpireRepair).
    public static partial class TemplateNameRepair
    {
        public static void RepairCultures() { }
        public static void RepairNames() { }
    }

    public static partial class BookNamingSystem
    {
        public static int RepairLegacyNames() => 0;
    }

    public static partial class LeftEmpireRepair
    {
        public static void AfterLoad() { }
    }

    // A faction as it goes into the save: its members copied, the claims it raises left out (they are made again from the
    // regime when it loads). The mod also dropped its buttons and card; a faction has none here.
    public partial class FixedFaction
    {
        public FixedFaction ToSaveCopy()
        {
            var copy = (FixedFaction)MemberwiseClone();
            copy.Members = new List<long>(Members ?? new List<long>());
            copy.TemporaryFactions = null;
            return copy;
        }
    }
}
