using System.Collections.Generic;

namespace EmpireCraft.Core
{
    // Hand-written halves for colour. In the mod two services juggled the base game's colour
    // library: families of shades for an overlord and its subjects, repairs when a colour
    // index went out of range, a fresh colour for a breakaway empire. Here a realm simply
    // has a colour (set when it is founded, changed with Realm.UpdateColor), so:
    //   - an empire wears the colour of its core realm;
    //   - a title has a colour of its own, worked out from its key;
    //   - nothing recolours subjects after their overlord: every realm keeps its own colour.
    // How a map shades a sphere of influence is for the client to decide when it draws one.

    // An empire wears its core realm's colour already; there is nothing to copy over.
    public static class EmpireColorPatch
    {
        public static void SyncFromCore(Empire empire) { }
    }

    public static class KingdomColorService
    {
        public static bool KeepsOwnColor(Realm member) => true;
        public static void EnsureDistinctInZhou(Realm member) { }
        public static void ApplySphereFamily(Empire sphere) { }
        public static void ApplyGarrisonFamily(Empire empire, IEnumerable<Realm> garrisons) { }
        public static bool ApplyTribalFamilies() => false;
        public static void Restore(Realm realm, int preferredColorId = -1, Tint formerOverlordColor = null, bool useOriginalColor = true) { }
    }

    public static class VassalColorService
    {
        public static void Apply(Realm subject, Realm lord) { }
        public static void Clear(Realm subject) { }
        public static bool IsComposite(Tint color) => false;
        public static void TickReassert() { }
    }

    public partial class EmpireManager
    {
        public static void EnsureDistinctColor(Empire empire) { }
    }

    public partial class Empire
    {
        public Palette GetColorLibrary() => Palette.Realms;
        public override Tint GetColor() => CoreKingdom == null ? null : CoreKingdom.GetColor();
    }

    public partial class KingdomTitle
    {
        Tint _tint;
        public Palette GetColorLibrary() => Palette.Titles;
        public override Tint GetColor() => _tint ?? (_tint = Palette.Titles.ColorFor(Key));
        public void GenerateColor() { _tint = null; }
        public PersonKind GetActorAsset() => PersonKind.Human;
    }
}
