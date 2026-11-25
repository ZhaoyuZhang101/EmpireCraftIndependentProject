// Ported from the EmpireCraft mod: Scripts/Data/ExclaveMaintenanceData.cs @ e770dbbe. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
namespace EmpireCraft.Core
{

// 年度快照保存进国库所属政权；读档不能再收一次维护费。
public sealed class ExclaveMaintenanceData
{
    public double last_settled = -1d;
    public int treasury_after;
    public int weak_years;
    public int annual_cost;
    public int paid;
    public int unpaid;
    public int zones;
    public double domestic_balance;
}
}
