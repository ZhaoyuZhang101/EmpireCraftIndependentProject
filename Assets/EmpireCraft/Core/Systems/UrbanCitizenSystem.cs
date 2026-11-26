// Generated from the EmpireCraft mod: Scripts/GeneralSystems/UrbanCitizenSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// High-level housing creates a limited urban citizen stratum instead of converting an
// entire city at once. Workers, merchants and privileged classes keep their own identity.
public static class UrbanCitizenSystem
{
    // 原版房屋等级 0~5；3 级起提供市民名额：3 级 1 个、4 级 2 个、5 级 3 个
    private const int MinimumHouseLevel = 3;

    public static bool IsCitizen(Person actor) => actor?.City != null && actor.City.Key >= 0 &&
        !actor.IsGone() && actor.Alive && actor.IsAdult() &&
        actor.GetOrCreate().urban_citizen_city_id == actor.City.Key;

    public static void UpdateCity(City city, List<Person> residents)
    {
        if (city == null || city.IsGone() || residents == null) return;
        int capacity = GetCapacity(city, residents.Count);
        List<Person> citizens = residents.Where(IsCitizen).ToList();
        foreach (Person actor in citizens.Where(actor => !IsEligible(actor)).ToList())
        {
            Release(actor);
            citizens.Remove(actor);
        }
        foreach (Person actor in citizens.OrderBy(actor => actor.Stats["intelligence"])
                     .ThenBy(actor => actor.Money).Take(Math.Max(0, citizens.Count - capacity)).ToList())
        {
            Release(actor);
            citizens.Remove(actor);
        }
        int annualLimit = Math.Max(1, (int)Math.Ceiling(residents.Count * 0.03d));
        int openings = Math.Min(capacity - citizens.Count, annualLimit);
        if (openings <= 0) return;
        foreach (Person actor in residents.Where(actor => !IsCitizen(actor) && IsEligible(actor))
                     .OrderByDescending(actor => actor.Stats["intelligence"])
                     .ThenByDescending(actor => actor.Money).ThenBy(actor => actor.Key).Take(openings))
        {
            actor.GetOrCreate().urban_citizen_city_id = city.Key;
            actor.SetSocialClass(SocialClass.Citizen);
        }
    }

    public static int GetCapacity(City city, int adultPopulation)
    {
        if (city?.Buildings == null || adultPopulation <= 0) return 0;
        // 高级民居：实体连同建筑台账里的(见 CityLedger)
        var built = new Dictionary<BuildingKind, int>();
        CityLedger.CollectCounts(city, built);
        int housing = 0;
        foreach (KeyValuePair<BuildingKind, int> pair in built)
            if (pair.Key.Type == "type_house" && pair.Key.UpgradeLevel >= MinimumHouseLevel)
                housing += (pair.Key.UpgradeLevel - MinimumHouseLevel + 1) * pair.Value;
        if (housing <= 0) return 0;
        int stage = Math.Max(0, Math.Min(3, (int)InstitutionSystem.GetFeature(
            CultureService.GetMainCulture(city), InstitutionFeatures.UrbanProductionStage)));
        float populationCap = stage >= 3 ? 0.45f : 0.35f;
        return Math.Min(housing, Math.Max(1, (int)Math.Ceiling(adultPopulation * populationCap)));
    }

    private static bool IsEligible(Person actor)
    {
        if (actor == null || actor.IsOnOffice() || actor.IsWarrior() ||
            EmpireCaftActorJudgeClass.IsManualWorkerJob(actor) || UrbanEmploymentSystem.IsEmployed(actor) ||
            LandEconomySystem.IsLandlord(actor) || actor.GetOrCreate().is_economic_merchant) return false;
        return !EmpireCaftActorJudgeClass.IsNoble(actor);
    }

    private static void Release(Person actor)
    {
        actor.GetOrCreate().urban_citizen_city_id = -1L;
        actor.SetSocialClass(EmpireCaftActorJudgeClass.JudgeClass(actor));
    }
}
}
