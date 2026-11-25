// Ported from the EmpireCraft mod: Scripts/GeneralSystems/IdeologySpreadSystem.cs#IdeologySpreadSystem.FeatureKey @ 55d6d4f8. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

public static partial class IdeologySpreadSystem
{
    public static string FeatureKey(PartyIdeology ideology) => $"ideology:{ideology}";
}
}
