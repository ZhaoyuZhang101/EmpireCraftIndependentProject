namespace EmpireCraft.Core
{
    // Hand-written half of CityExpansionPatch: ZoneLimit. The mod read the cap on a city's cells from its settings file
    // each second. Here the setting is ModClass.CITY_MAX_ZONES itself.
    public partial class CityExpansionPatch
    {
        public static int ZoneLimit => ModClass.CITY_MAX_ZONES;
    }
}
