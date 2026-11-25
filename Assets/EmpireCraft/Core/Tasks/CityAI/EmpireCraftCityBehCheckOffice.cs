// Generated from the EmpireCraft mod: Scripts/AI/CityAI/EmpireCraftCityBehCheckOffice.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

public class EmpireCraftCityBehCheckOffice:GameAICityBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult Execute(City pCity)
    {
        if (pCity.HasRealm())
        {
            SyncOffice(pCity);
        }
        return BehResult.Continue;
        
    }
    private static void SyncOffice(City pCity)
    {
        OfficeObject office = pCity.GetOffice();
        if (office == null)
        {
            pCity.InitialRegime();
            return;
        }
        office.meta_object = pCity;
        office.is_local = true;
    }
    public void UpdateOffice(City pCity)
    {
        if (!pCity.HasRealm()) return;
        Realm pKingdom = pCity.Realm;
        if (pCity.Realm.GetRegime()==null) return;
        var regime = pKingdom.GetRegime();
        OfficeObject officeObject = new OfficeObject();
        switch (regime.type)
        {
            case RegimeType.Arabic:
                break;
            case RegimeType.Feudalism:
                break;
            case RegimeType.LvLing:
                break;
            case RegimeType.ZhouFeudalism:
                break;
        }
    }
}
}
