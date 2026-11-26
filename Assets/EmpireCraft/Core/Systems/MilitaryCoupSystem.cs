// Generated from the EmpireCraft mod: Scripts/GeneralSystems/MilitaryCoupSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 兵变：武官势大而正统低落时，众将拥立大将，旧君退位；元首制同样受军队拥立影响。
//   · 兵权：本国(帝国按中央)各军团兵力里，某员大将(成年、非宗室、不是君主)统领的占比；身为战时统帅的至少按四成五算；
//   · 每年检定：正统不到 50、兵权四成以上，且由头(幼主在位、军饷欠发、大将威望高过君主)至少占两条时，
//     概率 = (50 − 正统)/50 × (兵权 − 0.4)/0.6 × BaseChance，幼主在位加倍，大将有野心或奸恶再加五成；
//     开国雄主登基头 15 年、上次兵变后 20 年内不发生。
//     以前没有由头也行、基数大：两三座城的小国一员大将统领全军(兵权 1)，正统 30 就每年四成概率兵变；
//   · 发动：大将入主都城登基(照常走易姓的后果：旧宗室诸侯起兵、新朝开国)，
//     以大将驻地(所统最大军团的驻城)的法理名号为新国号，没有就沿用旧号；
//     王朝积弊、六弊减半(改朝换代三档之"减半"，用户 2026-10-26，见 DynasticCycleSystem)。
public static class MilitaryCoupSystem
{
    private const int LegitimacyLine = 50;
    private const float PowerLine = 0.4f;
    private const float CommanderShare = 0.45f;
    private const int CooldownYears = 20;
    private const float BaseChance = 0.25f;

    internal static bool Applies(Empire empire)
    {
        RegimeType? regime = empire?.CoreKingdom?.GetRegime()?.type;
        return empire?.data != null && !empire.IsGone() && !empire.IsArchived() && empire.CoreKingdom != null &&
               !empire.CoreKingdom.IsGone() && !RepublicSystem.IsRepublic(empire) && !TransmigrantSystem.Governs(empire) &&
               !ModernLegitimacy.Applies(empire) &&
               (RegimeManager.IsMonarchy(regime) || empire.IsPrincipate()) &&
               regime != RegimeType.Modern;
    }

    public static void Yearly(Empire empire)
    {
        try
        {
            if (!Applies(empire)) return;
            ConstitutionalEconomyState state = empire.data.constitutional_economy;
            if (state == null || EmpireWorld.Current == null) return;
            double now = GameClock.Now;
            if (state.last_coup_at >= 0d && now >= state.last_coup_at && GameClock.YearsSince(state.last_coup_at) < CooldownYears)
                return;
            int legitimacy = empire.Legitimacy;
            if (legitimacy >= LegitimacyLine || !RebellionSystem.CanAttempt(empire.CoreKingdom)) return;
            (Person general, float share, City seat) = Strongest(empire);
            if (general == null || share < PowerLine) return;
            Person emperor = empire.Emperor;
            bool minor = RealisticAgeSystem.IsMinor(emperor);
            // 由头：主少国疑、欠饷军心浮动、将领功高震主，至少占两条
            int pretexts = (minor ? 1 : 0) + (!CorpsSystem.WellPaid(empire.CoreKingdom) ? 1 : 0) +
                           (emperor == null || general.Renown > emperor.Renown ? 1 : 0);
            if (pretexts < 2) return;
            float chance = (LegitimacyLine - legitimacy) / (float)LegitimacyLine * (share - PowerLine) / (1f - PowerLine) * BaseChance;
            if (minor) chance *= 2f;
            if (general.HasTrait("ambitious") || general.HasTrait("evil")) chance *= 1.5f;
            if (EcRandom.value >= chance) return;
            Execute(empire, state, general, seat);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][兵变] 失败: {exception.Message}");
        }
    }

    // 宫廷政变而宗室无人可立：禁军统领自立(见 PalaceArmySystem)
    internal static void PalaceSeize(Empire empire, ConstitutionalEconomyState state, Person general, City seat)
    {
        if (!Applies(empire) || general?.Record == null || !Eligible(empire, general)) return;
        Execute(empire, state, general, seat);
    }

    private static bool Eligible(Empire empire, Person actor) =>
        actor?.Record != null && !actor.IsGone() && actor.Alive && actor.IsAdult() && !actor.IsRuler() &&
        actor.Realm == empire.CoreKingdom &&
        !SpecificClanManager.SameLineage(actor.GetSpecificClan(), empire.EmpireSpecificClan);

    // 兵权最重的大将、他的兵权占比、驻地
    private static (Person general, float share, City seat) Strongest(Empire empire)
    {
        Realm core = empire.CoreKingdom;
        List<CorpsData> corps = CorpsSystem.Of(core);
        float total = corps.Sum(item => Mathf.Max(0f, item.strength));
        var commanded = new Dictionary<long, (float strength, CorpsData biggest)>();
        foreach (CorpsData item in corps)
        {
            if (item.general_id < 0) continue;
            commanded.TryGetValue(item.general_id, out var entry);
            float strength = Mathf.Max(0f, item.strength);
            commanded[item.general_id] = (entry.strength + strength,
                entry.biggest == null || strength > entry.biggest.strength ? item : entry.biggest);
        }
        Person best = null;
        float bestShare = 0f;
        City seat = null;
        foreach (var pair in commanded)
        {
            Person actor = EmpireWorld.Current.PersonByKey(pair.Key);
            if (!Eligible(empire, actor) || total <= 0f) continue;
            float share = pair.Value.strength / total;
            if (share <= bestShare) continue;
            best = actor;
            bestShare = share;
            seat = CorpsSystem.Home(pair.Value.biggest);
        }
        // 战时统帅节制诸军
        Person commander = WarCommandSystem.Commander(core);
        if (Eligible(empire, commander))
        {
            float share = commanded.TryGetValue(commander.Key, out var own) && total > 0f ? own.strength / total : 0f;
            share = Mathf.Max(share, CommanderShare);
            if (share > bestShare)
            {
                best = commander;
                bestShare = share;
                seat = own.biggest != null ? CorpsSystem.Home(own.biggest) : commander.City;
            }
        }
        return (best, bestShare, seat);
    }

    private static void Execute(Empire empire, ConstitutionalEconomyState state, Person general, City seat)
    {
        Realm core = empire.CoreKingdom;
        City capital = core.Capital;
        if (capital == null) return;
        Person former = empire.Emperor;
        string oldName = empire.GetEmpireName();
        // 元首更替沿用帝国国号，不把军队拥立写成以驻地法理命名的新王朝。
        bool principate = empire.IsPrincipate();
        string newName = principate ? oldName : seat != null && seat.hasTitle() ? seat.GetTitle()?.data?.name : null;
        // 华夏：照地名起的法理不当帝国号(用户 2026-11-08)，留给篡位定号(封国名，没有就美号，见 RealmNamingSystem.NameUsurper)
        if (!principate && seat != null && RealmNamingSystem.IsHuaxia(empire) && RealmNamingSystem.PlaceTitle(seat.GetTitle()))
            newName = null;
        string place = (seat ?? general.City ?? capital).GetCityName();
        state.last_coup_at = GameClock.Now;
        PrinceStruggleSystem.Note(core, general, PrinceStruggleSystem.NoteMilitaryCoup);
        if (former != null && former.Key != general.Key) core.RemoveRuler();
        general.JoinCity(capital);
        general.SetRealm(core);
        // 军阀兵变禅让上位(赵匡胤式)：改朝换代按"减半"一档，王朝积弊、六弊减半，另加篡位的一笔
        // (用户 2026-10-26，见 DynasticCycleSystem.OnDynastyChanged)
        DynasticCycleSystem.Expect(empire, DynasticCycleSystem.Transition.Halve, () => core.SetRuler(general));
        if (empire.Emperor?.Key != general.Key) return;
        if (!string.IsNullOrWhiteSpace(newName) && newName != empire.GetEmpireName())
        {
            empire.SetEmpireName(newName);
            if (empire.data.currentHistory != null)
            {
                empire.data.currentHistory.empire_name = newName;
                empire.data.currentHistory.dynasty_name = newName;
            }
        }
        string text = string.Format(LM.Get(principate ? "coup_history_principate" : "coup_history"), place, general.FullName(),
            former?.FullName() ?? LM.Get("label_none"), empire.GetEmpireName());
        empire.RecordHistory(directContent: text, actorId: general.Key, kingdomId: core.Key);
        TranslateHelper.LogEventMessage(text, core);
        general.RecordPersonalHistory(string.Format(LM.Get("personal_history_coup_leader"), place, oldName),
            relatedActorId: former?.Key ?? -1L);
        former?.RecordPersonalHistory(string.Format(LM.Get("personal_history_coup_deposed"), general.FullName()),
            relatedActorId: general.Key);
    }
}
}
