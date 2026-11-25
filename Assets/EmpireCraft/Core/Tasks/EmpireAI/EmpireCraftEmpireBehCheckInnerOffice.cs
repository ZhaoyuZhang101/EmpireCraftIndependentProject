// Generated from the EmpireCraft mod: Scripts/AI/EmpireAI/EmpireCraftEmpireBehCheckInnerOffice.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{
public class EmpireCraftEmpireBehCheckInnerOffice: GameAIEmpireBase
{
    public override Type OriginalBeh => GetType();
#if EC_PENDING
    public override BehResult Execute(Realm pKingdom)
    {
        pKingdom.CheckEmpire();
        if (!pKingdom.IsEmpire()) return BehResult.Continue;
        Empire empire = pKingdom.GetEmpire();
        if (empire == null || empire.IsGone() || empire.IsArchived()) return BehResult.Continue;
        if (empire.data.centerOffice == null)
        {
            var core = empire.CoreKingdom;
            if (core == null) return BehResult.Continue;
            empire.data.centerOffice = new CenterOffice();
            empire.data.centerOffice.Init(core);
        }
        SelectOfficer(empire);
        StartCalcOfficePerformance(empire);
        CheckOfficePower(empire);
        return base.Execute(pKingdom);
    }

    public void CheckOfficePower(Empire empire)
    {
        if (empire == null || empire.IsGone() || empire.IsArchived()) return;
        var center = empire.data.centerOffice;
        if (center == null) return;
        empire.data.additions = new EmpireAddition();
        var offices = center.GetAllOffices(empire);
        if (offices == null) return;
        offices.Shuffle();
        for (int i = 0; i < offices.Count; i++)
        {
            offices[i]?.DetectPower(empire);
        }
    }
    private void StartCalcOfficePerformance(Empire pEmpire)
    {
        if (pEmpire == null || pEmpire.IsGone() || pEmpire.IsArchived()) return;
        var center = pEmpire.data.centerOffice;
        if (center == null) return;
        if (pEmpire.IsNeedToOfficeExam())
        {
            pEmpire.AddRenown(-(int)(pEmpire.CoreKingdom.getRenown() * 0.07));

            Dictionary<Person, double> pData = new Dictionary<Person, double>();
            List<Person> officers = center.GetAllOfficers(pEmpire);
            if (officers.Count > 0)
            {
                double sumPerformance = 0;
                double sumSquares = 0;
                foreach (Person actor in officers)
                {
                    OfficeIdentity identity = actor.GetIdentity();
                    if (identity == null) continue;
                    if (identity.performanceEvents == null) continue;
                    (PerformanceEvent pEvent, double pValue) performance = identity.performanceEvents.TriggerEvent(actor);
                    actor.editRenown(ActorExtension.CalculateAnnualInfluenceGain(performance.pValue));
                    //记录事件
                    pData[actor] = performance.pValue;
                    identity.TotalPerformance += performance.pValue;
                    sumPerformance += performance.pValue;
                    sumSquares += performance.pValue * performance.pValue;
                    // LogService.LogInfo($"{actor.name}{performance.pEvent.is_good}{performance.pEvent.eventType}绩效增加{performance.pValue},当前绩效{actor.GetIdentity().TotalPerformance}");
                    actor.ResetPerformance();
                }
                if (pData.Count > 0)
                {
                    double averagePerformance = sumPerformance / pData.Count;
                    double variancePerformance = (sumSquares / pData.Count) - averagePerformance * averagePerformance;
                    if (variancePerformance < 0)
                    {
                        variancePerformance = 0;
                    }
                    double standardDeviationPerformance = Math.Sqrt(variancePerformance); // 计算标准方差
                    foreach (var item in pData)
                    {
                        Person actor = item.Key;
                        double mark = item.Value;
                        if (mark >= averagePerformance + standardDeviationPerformance)
                        {
                            actor.AddOfficeExamLevel(EmpireExamLevel.HD);
                        }
                        else if (mark >= averagePerformance)
                        {
                            actor.AddOfficeExamLevel(EmpireExamLevel.CR);
                        }
                        else if (mark >= averagePerformance - standardDeviationPerformance)
                        {
                            actor.AddOfficeExamLevel(EmpireExamLevel.P);
                        }
                        else
                        {
                            actor.AddOfficeExamLevel(EmpireExamLevel.F);
                        }
                    }
                }
            }
            pEmpire.data.last_office_exam_timestamp = GameClock.Now;
        }
    }
#endif
    //三省六部官员选拔机制
#if EC_PENDING
    public void SelectOfficer(Empire pEmpire)
    {
        if (pEmpire == null || pEmpire.IsGone() || pEmpire.IsArchived()) return;
        var coreKingdom = pEmpire.CoreKingdom;
        if (coreKingdom == null) return;
        var center = pEmpire.data.centerOffice;
        if (center == null) return;
        var emperor = pEmpire.Emperor;
        // 责任政府下由执政联盟任命的官职(见 ParliamentSystem.AssignMinisters)不再按任期轮换
        bool partyCabinet = ParliamentSystem.PartyAppointsMinisters(pEmpire);
        if (partyCabinet && center.CoreOffices.Concat(center.Divisions)
                .Where(id => ParliamentSystem.PartyAppointsOffice(pEmpire, id))
                .Any(id => OfficeManager.Offices.TryGetValue(id, out OfficeObject office) && office.GetActor() == null))
            ParliamentSystem.FillVacantMinistries(pEmpire);
        foreach (var core in center.CoreOffices)
        {
            if (ParliamentSystem.PartyAppointsOffice(pEmpire, core)) continue;
            if (OfficeManager.Offices.TryGetValue(core, out var value))
            {
                int onTime = value.GetOnTime();
                if (onTime < 0 || onTime > 3 &&
                    !ParliamentSystem.IsCareerCivilServiceOffice(pEmpire, core))
                {
                    value.Select(coreKingdom);
                }
            }
        }
        foreach (var division in center.Divisions)
        {
            if (ParliamentSystem.PartyAppointsOffice(pEmpire, division)) continue;
            if (OfficeManager.Offices.TryGetValue(division, out var value))
            {
                int onTime = value.GetOnTime();
                if (onTime < 0 || onTime > 3 &&
                    !ParliamentSystem.IsCareerCivilServiceOffice(pEmpire, division))
                {
                    value.Select(coreKingdom);
                }
            }
        }
        foreach (var harem in center.Harems)
        {
            if (OfficeManager.Offices.TryGetValue(harem, out var value))
            {
                if (emperor != null && !emperor.IsGone())
                {
                    if (value.officeType == 13)
                    {
                        if (value.GetActor() != emperor.Spouse || !(emperor.HasSpouse()))
                        {
                            value.Select(coreKingdom);
                        }
                    }
                    else
                    {
                        var actor = value.GetActor();
                        int onTime = value.GetOnTime();
                        if (onTime < 0 || (actor != null && actor.age > 35))
                        {
                            value.Select(coreKingdom);
                        }
                    }
                }
                else
                {
                    value.RemoveActor();
                }
            }
        }
    }
#endif
}
}
