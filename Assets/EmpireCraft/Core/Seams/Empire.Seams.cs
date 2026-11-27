namespace EmpireCraft.Core
{
    // Hand-written half of Empire: the renown its core realm wins and loses in war.
    //
    // The mod added it to the core realm's stored renown (the base game kept a realm's renown as a number of its own):
    // an empire's victory adds, a defeat takes away, never below nothing. Here a realm's renown is worked out from its
    // ruler and city leaders (Realm.GetRenown), so what an empire wins in war is kept beside that (Realm.WarRenown) and
    // counted in.
    public partial class Empire
    {
        public void AddRenown(int value)
        {
            Realm core = CoreKingdom;
            if (core == null) return;
            core.WarRenown = System.Math.Max(0, core.WarRenown + value);
        }
    }
}
