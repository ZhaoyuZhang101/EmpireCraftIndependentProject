// Ported from the EmpireCraft mod: Scripts/Data/RebellionCauseData.cs @ e770dbbe. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
namespace EmpireCraft.Core
{

// 起事前的快照：离开帝国、拆城或改名之后仍能查到当时的原因。
public sealed class RebellionCauseData
{
    public string reason_key = "";
    public string detail = "";
    public string origin_name = "";
    public string city_name = "";
    public long origin_empire_id = -1L;
    public int legitimacy = -1;
    public int? loyalty;
    public float? stability;
    public double timestamp = -1d;
}
}
