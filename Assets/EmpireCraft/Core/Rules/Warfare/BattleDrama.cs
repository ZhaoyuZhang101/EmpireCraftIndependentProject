// Ported from the EmpireCraft mod: Scripts/GeneralSystems/Warfare/BattleDrama.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 战场个人戏(用户 2026-10-19 选"战场个人戏")：军团交战的胜负落到统领身上，写进生平、军报，名人与君主还进史书与世界日志。
//   · 每月战线结算(见 FrontSystem.ResolveCity)：一方大败(战况三比七以上)、或自己的军溃散(组织度见底)，这一方各军统领
//     各有几率阵亡(为胜方统领所斩)、被俘、突围；
//   · 被俘：君主、储君、有官爵封地的不降——赎回或遇害；其余的三成五归降敌国(换了国家，留在对方都城)、四成由本国出金赎回
//     (国库付不起就被杀或放归)、其余不屈而死；
//   · 城破(主城池陷落，见 FrontSystem.Surrender)：守在城里的统领三成殉国、三成被俘，其余趁乱出走；
//   · 大败溃散而生还、城破出走的统领，本国有机会按律追究(临阵脱逃、违抗军令，见 LawEvents)。
// 只结算有统领实体的军(关了「军团统领实体」就没有这些事)。
public static class BattleDrama
{
    private const float RoutRatio = 0.7f, SlainChance = 0.06f, CapturedChance = 0.05f, BreakoutChance = 0.05f;
    private const float RoutBonus = 1.5f;
    private const float DefectShare = 0.35f, RansomShare = 0.4f;
    private const float MartyrChance = 0.3f, FallCaptureChance = 0.3f;

    private static Person General(CorpsData corps)
    {
        if (corps == null || corps.general_id < 0 || EmpireWorld.Current == null) return null;
        Person actor = EmpireWorld.Current.PersonByKey(corps.general_id);
        return actor?.Record != null && !actor.IsGone() && actor.Alive ? actor : null;
    }

    // 每月一处战场结算后：输的一方(attackersLost)各军统领的下场
#if EC_PENDING
    public static void AfterBattle(City city, List<(Realm kingdom, CorpsData corps)> attackers,
        List<(Realm kingdom, CorpsData corps)> holders, float ratio)
    {
        if (city?.Record == null || !CorpsSystem.GeneralUnits) return;
        bool attackersLost = ratio <= 1f - RoutRatio, holdersLost = ratio >= RoutRatio;
        try
        {
            Settle(city, attackers, holders, attackersLost, true);
            Settle(city, holders, attackers, holdersLost, false);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][战场] {city.Name} 结算统领下场失败: {exception.Message}");
        }
    }

    private static void Settle(City city, List<(Realm kingdom, CorpsData corps)> side,
        List<(Realm kingdom, CorpsData corps)> enemy, bool beaten, bool attacking)
    {
        Person victor = enemy.Where(entry => General(entry.corps) != null).OrderByDescending(entry => entry.corps.strength)
            .Select(entry => General(entry.corps)).FirstOrDefault();
        Realm victorKingdom = enemy.OrderByDescending(entry => entry.corps.strength).Select(entry => entry.kingdom).FirstOrDefault();
        foreach ((Realm kingdom, CorpsData corps) in side.ToList())
        {
            Person general = General(corps);
            if (general == null) continue;
            bool routed = corps.org <= 10f || corps.strength < 1f;
            if (!beaten && !routed) continue;
            float scale = beaten && routed ? RoutBonus : 1f;
            float roll = EcRandom.value;
            if (roll < SlainChance * scale) Slain(city, kingdom, general, victor);
            else if (roll < (SlainChance + CapturedChance) * scale && victorKingdom != null) Captured(city, kingdom, general, victorKingdom, victor);
            else if (roll < (SlainChance + CapturedChance + BreakoutChance) * scale)
                Announce(kingdom, general, string.Format(LM.Get("battle_breakout"), city.GetCityName()), false);
            // 大败又溃散而统领生还：按律追究败将(临阵脱逃、违抗军令，见 LawEvents)
            else if (beaten && routed) LawEvents.Routed(kingdom, general, corps, attacking);
        }
    }
#endif

    // 阵亡：为胜方统领所斩
#if EC_PENDING
    private static void Slain(City city, Realm kingdom, Person general, Person victor)
    {
        string place = city.GetCityName(), name = general.FullName();
        string cause = victor != null
            ? string.Format(LM.Get("death_cause_battle_slain_by"), place, victor.FullName())
            : string.Format(LM.Get("death_cause_battle_slain"), place);
        if (victor != null)
            BiographySystem.Note(victor, string.Format(LM.Get("bio_battle_slew"), place, name), general);
        Report(kingdom, general, string.Format(LM.Get("battle_general_slain_log"), place, name,
            victor != null ? victor.FullName() : LM.Get("battle_enemy")));
        BiographySystem.Kill(general, cause, victor);
    }
#endif

    // 被俘：归降、赎回、不屈而死
#if EC_PENDING
    private static void Captured(City city, Realm kingdom, Person general, Realm captor, Person victor)
    {
        string place = city.GetCityName(), name = general.FullName();
        bool sovereign = general.IsRuler() || kingdom.HasHeir() && kingdom.GetHeir() == general;
        bool loyal = sovereign || general.IsOnOffice() || (general.GetOwnedTitle()?.Count ?? 0) > 0;
        float roll = EcRandom.value;
        if (!loyal && roll < DefectShare && captor.Capital?.Record != null && !captor.IsGone())
        {
            CorpsSystem.LoseGeneral(general);
            BiographySystem.Note(general, string.Format(LM.Get("bio_captured_defected"), place, captor.GetKingdomName()), victor);
            Report(kingdom, general, string.Format(LM.Get("battle_general_defected_log"), place, name, captor.GetKingdomName()));
            general.joinCity(captor.Capital);
            general.setKingdom(captor);
            return;
        }
        if (loyal || roll < DefectShare + RansomShare)
        {
            int price = Mathf.Max(30, Mathf.RoundToInt(general.renown * 2f)) * (sovereign ? 10 : 1);
            if (TreasurySystem.TrySpend(kingdom, price, TreasuryCategory.Reparations, discretionary: false))
            {
                captor.AddMoney(price, TreasuryCategory.Reparations);
                BiographySystem.Note(general, string.Format(LM.Get("bio_captured_ransomed"), place, captor.GetKingdomName(), price), victor);
                if (victor != null) PersonalTieSystem.MakeRivals(general, victor, PersonalTieSystem.CauseCaptured);
                Report(kingdom, general, string.Format(LM.Get("battle_general_ransomed_log"), place, name, price), sovereign);
                return;
            }
            if (EcRandom.value < 0.5f)
            {
                BiographySystem.Note(general, string.Format(LM.Get("bio_captured_released"), place, captor.GetKingdomName()), victor);
                if (victor != null) PersonalTieSystem.MakeRivals(general, victor, PersonalTieSystem.CauseCaptured);
                Report(kingdom, general, string.Format(LM.Get("battle_general_released_log"), place, name), sovereign);
                return;
            }
        }
        Report(kingdom, general, string.Format(LM.Get("battle_general_executed_log"), place, name, captor.GetKingdomName()), sovereign);
        BiographySystem.Kill(general, string.Format(LM.Get("death_cause_captive"), place, captor.GetKingdomName()),
            captor.Ruler, executed: true);
    }
#endif

    // 被围投降(见 EncirclementSystem)：统领按被俘处理
#if EC_PENDING
    public static void Surrendered(City city, Realm kingdom, CorpsData corps, Realm captor, List<(Realm kingdom, CorpsData corps)> besiegers)
    {
        if (city?.Record == null || captor?.Record == null || !CorpsSystem.GeneralUnits) return;
        Person general = General(corps);
        if (general == null) return;
        try
        {
            Person victor = besiegers?.Where(entry => General(entry.corps) != null).OrderByDescending(entry => entry.corps.strength)
                .Select(entry => General(entry.corps)).FirstOrDefault();
            Captured(city, kingdom, general, captor, victor);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][战场] {city.Name} 结算投降统领失败: {exception.Message}");
        }
    }
#endif

    // 城破：守城统领殉国、被俘或出走
#if EC_PENDING
    public static void CityFell(City city, Realm attacker, Realm owner)
    {
        if (city?.Record == null || owner == null || !CorpsSystem.GeneralUnits) return;
        try
        {
            Person victor = null;
            foreach ((Realm kingdom, CorpsData corps) in CorpsSystem.All().ToList())
            {
                if (corps.sector_city_id != city.Key || General(corps) == null) continue;
                if (victor == null && FrontSystem.Hostile(kingdom, owner) && corps.task == "attack") victor = General(corps);
            }
            foreach ((Realm kingdom, CorpsData corps) in CorpsSystem.All().ToList())
            {
                if (corps.sector_city_id != city.Key || corps.task != "defend" || FrontSystem.Hostile(kingdom, owner)) continue;
                Person general = General(corps);
                if (general == null) continue;
                float roll = EcRandom.value;
                if (roll < MartyrChance)
                {
                    string place = city.GetCityName();
                    Report(kingdom, general, string.Format(LM.Get("battle_general_martyr_log"), place, general.FullName()));
                    BiographySystem.Kill(general, string.Format(LM.Get("death_cause_martyr"), place), victor);
                }
                else if (roll < MartyrChance + FallCaptureChance && attacker != null)
                    Captured(city, kingdom, general, attacker, victor);
                // 趁乱出走：弃城之罪(临阵脱逃，见 LawEvents)
                else LawEvents.CityFled(kingdom, general, corps);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][战场] {city.Name} 城破结算统领失败: {exception.Message}");
        }
    }

    private static void Announce(Realm kingdom, Person general, string text, bool history)
    {
        BiographySystem.Note(general, text);
        FrontSystem.Log(kingdom, $"{general.FullName()}：{text}");
    }
#endif

    // 军报都记；名将名臣、君主、储君还进史书与世界日志
#if EC_PENDING
    private static void Report(Realm kingdom, Person general, string text, bool force = false)
    {
        FrontSystem.Log(kingdom, text);
        WarChronicle.RecordFor(kingdom, WarChronicle.Commander, text);
        bool notable = force || general.IsRuler() || FamousTalentSystem.IsFamous(general) ||
                       kingdom.HasHeir() && kingdom.GetHeir() == general;
        if (!notable) return;
        Empire empire = kingdom.GetEmpire();
        if (empire != null && !empire.IsGone()) EventRecorder.Record(empire, text, general, kingdom);
        else EventRecorder.Record(kingdom, text, general);
    }
#endif
}
}
