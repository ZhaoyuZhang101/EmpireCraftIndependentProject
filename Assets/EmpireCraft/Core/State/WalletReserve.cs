// Ported from the EmpireCraft mod: Scripts/Data/WalletReserve.cs @ 55d6d4f8. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
namespace EmpireCraft.Core
{

// Only confirmed existing actor balances, never stock valuations or new income.
// Cash and loot remain separate; nullable owner fields keep old saves unchanged.
public sealed class WalletReserve
{
    public long cash;
    public long loot;
}
}
