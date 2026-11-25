// Generated from the EmpireCraft mod: Scripts/GeneralSystems/HaremSuccessionSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 后宫争储(华夏：律令制、分封制)：荒淫或昏聩之君膝下有两个以上皇子、且诸子不同母时，
//   宠妃每年有一成二(荒淫)或六分(昏聩)的可能让皇帝废长立幼，改立她的儿子为储君(册立储君，ResolveHeir 优先)；
//   被废的皇子有三成可能"暴毙"(夺嫡之祸)。
public static class HaremSuccessionSystem
{
    private const float DissoluteChance = 0.12f, FatuousChance = 0.06f, MurderChance = 0.3f;

#if EC_PENDING
    public static void Yearly(Empire empire)
    {
        try
        {
            Realm core = empire?.CoreKingdom;
            Person emperor = empire?.Emperor;
            if (core?.Record == null || emperor?.Record == null || !emperor.Alive ||
                core.GetRegime()?.type is not (RegimeType.LvLing or RegimeType.ZhouFeudalism)) return;
            // 秘密立储：后宫也不知道储君是谁(见 LateImperialSystem)
            if (LateImperialSystem.SecretSuccession(core)) return;
            float chance = RulerQualitySystem.Of(emperor) switch
            {
                RulerQualitySystem.Grade.Dissolute => DissoluteChance,
                RulerQualitySystem.Grade.Fatuous => FatuousChance,
                _ => 0f
            };
            if (chance <= 0f || EcRandom.value >= chance) return;
            bool Fit(Person child) => child?.Record != null && !child.IsGone() && child.Alive && child.IsMale() &&
                                     !child.IsRuler() && child.Realm?.GetEmpire() == empire;
            // 皇子、生母连族谱里的虚拟族人一起算(纯净地图下多不在地图上；以前只数地图上的儿子，几乎永远凑不够两个)；
            // 上面掷中了才把皇子落成实体
            List<PersonalClanIdentity> sonPeople = KinRoster.Children(emperor).Where(person => KinRoster.Counts(person,
                    virtualSon => virtualSon.sex == Sex.Male && KinRoster.KingdomOf(virtualSon)?.GetEmpire() == empire, Fit))
                .ToList();
            if (sonPeople.Count < 2) return;
            List<Person> sons = KinRoster.Realize(sonPeople, Fit);
            if (sons.Count < 2) return;
            Person heir = core.HasHeir() ? core.GetHeir() : null;
            Person passedOver = heir != null && sons.Contains(heir) ? heir : sons.OrderByDescending(son => son.Age()).First();
            long rivalMother = MotherOf(passedOver.GetPersonalIdentity())?.id ?? -1L;
            Person favourite = sons.Where(son => son != passedOver && MotherOf(son.GetPersonalIdentity()) is PersonalClanIdentity mother &&
                                                mother.id != rivalMother)
                .OrderBy(son => son.Age()).FirstOrDefault();
            // 得势的妃嫔是虚拟族人的，这时才落成(要写进她的生平)
            Person consort = Mother(favourite) ?? MotherOf(favourite?.GetPersonalIdentity())?.Realize();
            if (favourite == null || consort?.Record == null || consort.IsGone() || !consort.Alive) return;
            core.GetOrCreate().designated_heir_id = favourite.Key;
            core.SetHeir(favourite);
            PrinceStruggleSystem.Note(core, favourite, PrinceStruggleSystem.NoteDeposed);
            string text = string.Format(LM.Get("harem_heir_history"), consort.FullName(), emperor.FullName(),
                favourite.FullName(), passedOver.FullName());
            Record(empire, emperor, text);
            BiographySystem.Note(passedOver, BiographySystem.Format("bio_heir_passed_over", consort.FullName(), favourite.FullName()), favourite);
            BiographySystem.Note(consort, BiographySystem.Format("bio_consort_won_heir", favourite.FullName(), passedOver.FullName()), favourite);
            BiographySystem.Note(favourite, BiographySystem.Format("bio_became_heir", passedOver.FullName()), passedOver);
            if (EcRandom.value >= MurderChance) return;
            string name = passedOver.FullName();
            BiographySystem.Kill(passedOver, LM.Get("death_cause_suspicious"));
            Record(empire, emperor, string.Format(LM.Get("harem_heir_death_history"), name));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][后宫] 争储失败: {exception.Message}");
        }
    }
#endif

    private static Person Mother(Person child) =>
        child?.GetParents()?.FirstOrDefault(parent => parent?.Record != null && !parent.IsGone() && parent.IsFemale());

    // 族谱里的生母(在世的，含虚拟族人)
    private static PersonalClanIdentity MotherOf(PersonalClanIdentity child)
    {
        PersonalClanIdentity mother = child != null && child.mother > 0 ? SpecificClanManager.getPerson(child.mother) : null;
        return mother != null && mother.is_alive ? mother : null;
    }

    private static void Record(Empire empire, Person emperor, string text)
    {
        empire.RecordHistory(directContent: text, actorId: emperor.Key, kingdomId: empire.CoreKingdom.Key);
        TranslateHelper.LogEventMessage(text, empire.CoreKingdom);
    }
}
}
