// Ported from the EmpireCraft mod: Scripts/Data/WarReparation.cs @ e770dbbe. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
namespace EmpireCraft.Core
{

// 一笔战争赔款(见 WarReparationsSystem)：按月付给收款国，付不起顺延
public sealed class WarReparation
{
    public long creditor_kingdom_id = -1L;
    public long monthly;
    public int months_left;
}
}
