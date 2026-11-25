// Ported from the EmpireCraft mod: Scripts/HelperFunc/HighPopulationPerformance.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using Time = EmpireCraft.Core.FrameClock;

namespace EmpireCraft.Core
{

public static class HighPopulationPerformance
{
    private const int CivilianAiThreshold = 5000;
    private const int ExtremePopulationThreshold = 20000;
    private const float HighPopulationFrontLineCacheSeconds = 0.25f;
    private const float ExtremePopulationFrontLineCacheSeconds = 0.5f;

#if EC_PENDING
    public static int GetFrontLineCacheKey()
    {
        if (!ModClass.PERFORMANCE_HIGH_POPULATION_MODE || EmpireWorld.Current?.Living == null)
            return Time.frameCount;

        int unitCount = EmpireWorld.Current.Living.Count;
        if (unitCount < CivilianAiThreshold) return Time.frameCount;

        float cacheSeconds = unitCount >= ExtremePopulationThreshold
            ? ExtremePopulationFrontLineCacheSeconds
            : HighPopulationFrontLineCacheSeconds;
        return Mathf.FloorToInt(Time.realtimeSinceStartup / cacheSeconds);
    }
#endif
}
}
