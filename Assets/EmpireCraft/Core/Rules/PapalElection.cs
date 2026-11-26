// Generated from the EmpireCraft mod: Scripts/GeneralSystems/PapalElection.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 教宗推举(用户 2026-10-27 选"改成推举、不再传子"：以前教宗就是教宗国的国君，照教宗国的继承法传给子孙)：
//   · 教宗国(普世教会的教廷所在、类型为教宗国)君位出缺时不走继承(见 EmpireCraftKingdomBehCheckKing)：由枢机推举——
//     人选出自教宗国都城与本教各主教领的都城，有教士特质的优先，再按威望；选上的入住圣城、加教士特质、改奉本教；
//   · 世俗强立：教宗国是某帝国的成员或附庸、或教廷之囚落在帝国手里(RealmNamingSystem.ControlsPapacy)时，那位皇帝有
//     ImposeChance 机会从帝都派自己的人选(同族优先)；
//   · 记下选立方式与时间(教宗正统的一项，见 PopeLegitimacy)，写进宗教大事记、世界日志与本人生平。
//   · 西方大分裂期间，奉对立教宗的主教领不参加推举；对立一线另由对立一边的枢机推举(见 PapacySystem.Antipope，人选同样按 PickCandidate)。
//   圣座帝国(教宗国自己做了帝国中央)不在此列，照帝国继承。
public static class PapalElection
{
    public const int ByConclave = 1, ByImposition = 2;
    private const float ImposeChance = 0.6f;

    public static bool TryHandleVacancy(Realm kingdom)
    {
        try
        {
            if (kingdom?.Record == null || kingdom.IsGone() || kingdom.HasRuler() || kingdom.Capital == null || kingdom.IsEmpire() ||
                kingdom.GetKingdomType() != KingdomType.Feudalism_papal_state || !kingdom.HasReligion()) return false;
            Religion religion = kingdom.Religion;
            if (!ReligionFaith.IsPapal(religion) || PapacySystem.Papacy(religion) != kingdom) return false;
            Empire patron = Patron(kingdom);
            bool imposed = patron?.CoreKingdom?.Capital != null && EcRandom.value < ImposeChance;
            Person pick = imposed
                ? Pick(new[] { patron.CoreKingdom.Capital }, patron.Emperor)
                : Pick(Conclave(kingdom, religion), null);
            if (pick == null)
            {
                imposed = false;
                pick = CityPopulationSystem.SpawnOfficial(kingdom.Capital, kingdom);
            }
            if (pick == null) return false;
            if (pick.City != kingdom.Capital) pick.JoinCity(kingdom.Capital);
            GraceEdictService.Crown(kingdom, pick);
            if (kingdom.Ruler != pick) return false;
            if (!pick.HasTrait("cleric")) pick.AddTrait("cleric");
            if (pick.Religion != religion) pick.SetReligion(religion);
            ReligionExtension.ReligionExtraData data = religion.GetOrCreate();
            data.LeaderID = pick.Key;
            data.PopeChosenBy = imposed ? ByImposition : ByConclave;
            data.PopeChosenAt = GameClock.Now;
            ReligionHeads.Note(religion);
            string text = imposed
                ? string.Format(LM.Get("pope_imposed_history"), patron.Emperor?.FullName() ?? patron.GetEmpireName(), pick.FullName(),
                    religion.Name)
                : string.Format(LM.Get("pope_elected_history"), kingdom.Capital.GetCityName(), pick.FullName(), religion.Name);
            ReligionChronicle.Record(religion, ReligionChronicle.KindPapacy, text);
            TranslateHelper.LogEventMessage(text, kingdom);
            pick.RecordPersonalHistory(text);
            LogService.LogInfo($"[EmpireCraft][教廷] {religion.Name} 新教宗 {pick.FullName()}({(imposed ? "世俗强立" : "枢机推举")})");
            return true;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][教廷] 推举教宗失败: {exception.Message}");
            return false;
        }
    }

    // 控制教廷的帝国：教宗国是其成员、附庸，或教廷之囚落在它手里
    private static Empire Patron(Realm papacy)
    {
        foreach (Empire empire in ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Empire>())
            if (empire?.data != null && !empire.IsGone() && !empire.IsArchived() && empire.CoreKingdom != papacy &&
                empire.Emperor != null && RealmNamingSystem.ControlsPapacy(empire, papacy)) return empire;
        return null;
    }

    // 枢机所在：教宗国都城与本教各主教领的都城
    private static IEnumerable<City> Conclave(Realm papacy, Religion religion)
    {
        yield return papacy.Capital;
        foreach (Realm kingdom in EmpireWorld.Current.LiveRealms.ToList())
            if (kingdom?.Record != null && !kingdom.IsGone() && kingdom != papacy && kingdom.Religion == religion &&
                kingdom.Capital != null && HolyRomanSystem.IsChurchEstate(kingdom) && !PapacySystem.FollowsAntipope(kingdom))
                yield return kingdom.Capital;
    }

    // 对立一线推举也用(见 PapacySystem.Antipope)
    internal static Person PickCandidate(IEnumerable<City> cities, Person patron) => Pick(cities, patron);

    private static Person Pick(IEnumerable<City> cities, Person patron) =>
        cities.SelectMany(Candidates).Distinct()
            .OrderByDescending(actor => actor.HasTrait("cleric"))
            .ThenByDescending(actor => patron != null && SameHouse(actor, patron))
            .ThenByDescending(actor => actor.Renown).FirstOrDefault();

    private static IEnumerable<Person> Candidates(City city) =>
        (city?.People ?? new List<Person>()).ToList().Where(actor => actor?.Record != null && !actor.IsGone() && actor.Alive &&
                                                                    actor.IsAdult() && !actor.IsRuler() && !actor.IsCityLeader() &&
                                                                    actor.IsFitToRule());

    private static bool SameHouse(Person a, Person b) =>
        a?.Record != null && b?.Record != null && SpecificClanManager.SameLineage(a.GetSpecificClan(), b.GetSpecificClan());
}
}
