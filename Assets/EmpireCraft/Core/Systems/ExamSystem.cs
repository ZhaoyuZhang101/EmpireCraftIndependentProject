// Generated from the EmpireCraft mod: Scripts/GeneralSystems/ExamSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System.Collections.Generic;
using System.Linq;
#if EC_PENDING
using NCMS.Extensions;
#endif

namespace EmpireCraft.Core
{
public static class ExamSystem
{
    public enum ExamType
    {
        City, //乡试
        Province, //会试
        Empire, //殿试
        Office //官员晋级
    }
    // 个人经历：现代国家的科举就是公务员考试，记作"通过某级公务员考试"
#if EC_PENDING
    private static string PassedText(Person actor, string level)
    {
        bool modern = actor?.kingdom?.GetRegime()?.type == EmpireCraft.Core.RegimeType.Modern;
        return LM.Get(modern ? $"personal_history_civil_exam_{level}_passed" : $"personal_history_exam_{level}_passed");
    }

    public static void startExam(ExamType type, Entity nano)
    {
        switch (type) 
        {
            case ExamType.City:
                cityExamPrepare(nano);
                break;
            case ExamType.Province:
                provinceExamPrepare(nano);
                break;
            case ExamType.Empire:
                empireExamPrepare(nano);
                break;
        }
    }
    public static void cityExamPrepare(Entity nano)
    {
        City city = (City)nano;
        Dictionary<Person, double> MarksData = new Dictionary<Person, double>();
        // 无小人模式：城里几乎没有平民实体，从人口里召集几位读书人赴考(识字人口越多、有藏书处的城越多)；
        // 落榜的下次并入时回到人口数据里，中举的有了功名就一直保留为实体(见 CityPopulationSystem.IsNotable)
        if (CityPopulationSystem.AbstractPopulationEnabled)
        {
            int examinees = Mathf.Clamp(1 + CityPopulationSystem.LiterateHouseholds(city) / 3 +
                                                    (city.hasBookSlots() ? 1 : 0), 1, 6);
            for (int i = 0; i < examinees; i++)
                if (CityPopulationSystem.SpawnScholar(city) == null) break;
        }
        foreach(Person actor in city.units.ToList())
        {
            double mark = 0;
            if (!actor.IsAdult()) continue;
            if (actor.isOfficer()) continue;
            if (actor.IsCityLeader()) continue;
            if (actor.IsRuler()) continue;
            if (actor.hasTrait("jingshi")) continue;
            if (actor.hasTrait("juren")) continue;
            if (actor.hasTrait("gongshi")) continue;
            // 只记中榜，不记每次赴考：屡试不第的考生每次都记一条，存档会膨胀到几百 MB
            mark = actor.startCityExam();
            if (mark > 0)
            {
                MarksData.Add(actor, mark);
            }
        }
        var sorted = MarksData.OrderByDescending(kv=>kv.Value).ToList();
        int takeNum = 3;
        if (city.GetExamPassPersonIDs().Count()< takeNum)
        {
            takeNum -= city.GetExamPassPersonIDs().Count();
        } else
        {
            takeNum = 1;
        }
        if (sorted.Count> takeNum)
        {
            sorted.Take(takeNum).ForEach(item=>
            {
                item.Key.addTrait("juren");
                item.Key.RecordPersonalHistory(PassedText(item.Key, "city"));
                OfficeIdentity identity = new OfficeIdentity
                {
                    actor_id = item.Key.Key
                };
                item.Key.SetIdentity(identity, true);
                identity.TotalPerformance += 100;
            });
        } else
        {
            sorted.ForEach(item =>
            {
                item.Key.addTrait("juren");
                item.Key.RecordPersonalHistory(PassedText(item.Key, "city"));
                OfficeIdentity identity = new OfficeIdentity
                {
                    actor_id = item.Key.Key
                };
                item.Key.SetIdentity(identity, true);
                identity.TotalPerformance += 100;
            });
        }
    }
    public static void provinceExamPrepare(Entity nano)
    {
        Realm province = (Realm)nano;
        Dictionary<Person, double> MarksData = new Dictionary<Person, double>();
        foreach(City city in province.Cities)
        {
            foreach (Person actor in city.units.FindAll(a => a != null && !a.IsGone() && a.hasTrait("juren")))
            {
                // 只记中榜，不记每次赴考：屡试不第的考生每次都记一条，存档会膨胀到几百 MB
                double mark = actor.startProvinceExam();
                MarksData[actor] = mark;
            }
        }
        var sorted = MarksData.OrderByDescending(kv => kv.Value).ToList();
        int takeNum = 1;
        if (sorted.Count() > takeNum)
        {
            sorted.Take(takeNum).ForEach(item =>
            {
                bool newlyPassed = !item.Key.hasTrait("gongshi");
                item.Key.addTrait("gongshi");
                if (newlyPassed)
                {
                    item.Key.RecordPersonalHistory(PassedText(item.Key, "province"));
                }
                OfficeIdentity identity = EnsureExamIdentity(item.Key);
                identity.TotalPerformance += 200;
            });
        }
        else
        {
            sorted.ForEach(item =>
            {
                bool newlyPassed = !item.Key.hasTrait("gongshi");
                item.Key.addTrait("gongshi");
                if (newlyPassed)
                {
                    item.Key.RecordPersonalHistory(PassedText(item.Key, "province"));
                }
                OfficeIdentity identity = EnsureExamIdentity(item.Key);
                identity.TotalPerformance += 200;
            });
        }

    }

    public static void empireExamPrepare(Entity nano)
    {
        Empire empire = (Empire)nano;
        Dictionary<Person, double> MarksData = new Dictionary<Person, double>();
        if (empire == null || empire.IsGone()) return;
        // 殿试及第者以当朝首辅为座主(见 PersonalTieSystem)
        Person examiner = empire.GetCabinetLeader();
        foreach (Realm province in empire.kingdoms_hashset.Where(province => province != null && !province.IsGone()))
        {
            foreach (Person actor in province.AllGongshi().Where(actor => actor != null && !actor.IsGone()))
            {
                if (!MarksData.TryGetValue(actor, out double m))
                {
                    // 只记中榜，不记每次赴考：屡试不第的考生每次都记一条，存档会膨胀到几百 MB
                    double mark = actor.startEmpireExam();
                    MarksData.Add(actor, mark);
                }
            }
        }
        var sorted = MarksData.OrderByDescending(kv => kv.Value).ToList();
        int takeNum = 1;
        if (sorted.Count() > takeNum)
        {
            sorted.Take(takeNum).ForEach(item => { 
                bool newlyPassed = !item.Key.hasTrait("jingshi");
                item.Key.addTrait("jingshi");
                if (newlyPassed)
                {
                    item.Key.RecordPersonalHistory(PassedText(item.Key, "empire"));
                    if (examiner != null && examiner.Alive && examiner != item.Key)
                        PersonalTieSystem.MakePatron(item.Key, examiner, PersonalTieSystem.CauseExam);
                }
                OfficeIdentity identity = EnsureExamIdentity(item.Key);
                identity.TotalPerformance += 300;
            });
        }
        else
        {
            sorted.ForEach(item => {
                bool newlyPassed = !item.Key.hasTrait("jingshi");
                item.Key.addTrait("jingshi");
                if (newlyPassed)
                {
                    item.Key.RecordPersonalHistory(PassedText(item.Key, "empire"));
                    if (examiner != null && examiner.Alive && examiner != item.Key)
                        PersonalTieSystem.MakePatron(item.Key, examiner, PersonalTieSystem.CauseExam);
                }
                OfficeIdentity identity = EnsureExamIdentity(item.Key);
                identity.TotalPerformance += 300;
            });
        }
    }

    private static OfficeIdentity EnsureExamIdentity(Person actor)
    {
        OfficeIdentity identity = actor.GetIdentity();
        if (identity != null) return identity;
        identity = new OfficeIdentity { actor_id = actor.Key };
        actor.SetIdentity(identity, true);
        return identity;
    }

    public static double startCityExam(this Person actor) 
    {
        double mark = 0;
        //乡试
        ActorAbility ability = new ActorAbility(actor);
        mark = ability.intelligence;
        return mark;
    }

    public static double startProvinceExam(this Person actor) 
    {
        double mark = 0;
        //会试
        ActorAbility ability = new ActorAbility(actor);
        mark = ability.intelligence + ability.diplomacy;
        return mark;
    }

    public static double startEmpireExam(this Person actor) 
    {
        double mark = 0;
        //殿试
        ActorAbility ability = new ActorAbility(actor);
        mark = ability.intelligence + ability.diplomacy + ability.stewardship;
        return mark;
    }
#endif
}

public class ActorAbility
{
    public double intelligence;
    public double stewardship;
    public double diplomacy;
#if EC_PENDING
    public ActorAbility(Person a)
    {
        diplomacy = a.stats["diplomacy"];
        stewardship = a.stats["stewardship"];
        intelligence = a.stats["intelligence"];
        //double warfare = a.stats["warfare"];
        //double mana = a.stats["mana"];
        //double stamina = a.stats["stamina"];
        //double skill_spell = a.stats["skill_spell"];
        //double skill_combat = a.stats["skill_combat"];
        //double range = a.stats["range"];
        //double damage = a.stats["damage"];
        //double scale = a.stats["scale"];
        //double offspring = a.stats["offspring"];
        //double mass = a.stats["mass"];
        //double throwing_range = a.stats["throwing_range"];
        //double lifespan = a.stats["lifespan"];
        //double mass_2 = a.stats["mass_2"];
        //double armor = a.stats["armor"];
        //double attack_speed = a.stats["attack_speed"];
    }
#endif
}
}
