namespace EmpireCraft.Core
{
    // Hand-written half of WarWeather: the base game's world ages (an ice age, an age of rain) made campaigns harder.
    // This world has no such ages.
    public static partial class WarWeather
    {
        private static bool IceAge => false;
        private static bool RainAge => false;
    }
}
