// Ported from the EmpireCraft mod: Scripts/Data/RoadTreatyData.cs @ e770dbbe. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// 通路之约的存档(见 RoadTreatySystem)：两国(国 id 小的在前，"a_b")交好的年数、有过友好时期的、议定通路之约的时间
public sealed class RoadTreatyData
{
    public Dictionary<string, float> friendly_years = new();
    public List<string> friendly = new();
    public Dictionary<string, double> treaties = new();
}
}
