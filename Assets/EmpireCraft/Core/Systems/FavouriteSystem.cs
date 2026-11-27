// Generated from the EmpireCraft mod: Scripts/GeneralSystems/FavouriteSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Linq;

namespace EmpireCraft.Core
{

// 宠臣(用户 2026-10-19 选"功高震主与野心"：皇帝的宠臣专权招人怨，成为清君侧的靶子)。每年(RoyalHouseSystem 年度，每个君主国帝国)：
//   · 皇帝没有宠臣时有几率宠信一名在任官员(庸弱、昏、荒淫三成，暴君一成五，寻常八分，贤君不会)——贪婪、有野心、名望低的更易得宠；
//     得宠时与当朝首辅结怨；
//   · 宠臣在：他的派系每年权势 +2，所在城腐败上升，其余派系怨望 +3(怨望够了就会清君侧，见 FactionPurgeSystem)；
//   · 清君侧得手：宠臣一并被诛；
//   · 换了皇帝：新君半数诛杀先帝宠臣，否则罢黜。
public static class FavouriteSystem
{
    private const int FactionGain = 2;
    private const float GrievanceGain = 3f, Corruption = 0.02f;

    public static Person Of(Empire empire)
    {
        if (empire?.data == null || empire.data.favourite_id < 0 || EmpireWorld.Current == null) return null;
        Person actor = EmpireWorld.Current.PersonByKey(empire.data.favourite_id);
        return actor?.Record != null && !actor.IsGone() && actor.Alive ? actor : null;
    }

    public static void Yearly(Empire empire)
    {
        Person emperor = empire?.Emperor;
        Realm core = empire?.CoreKingdom;
        if (emperor?.Record == null || !emperor.Alive || core?.Record == null) return;
        try
        {
            Person favourite = Of(empire);
            if (favourite != null && empire.data.favourite_emperor_id != emperor.Key)
            {
                Fall(empire, favourite, emperor);
                return;
            }
            if (favourite == null)
            {
                empire.data.favourite_id = -1L;
                TryPick(empire, emperor, core);
                return;
            }
            Sway(empire, favourite, core);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][宠臣] {empire.GetEmpireName()} 结算失败: {exception.Message}");
        }
    }

    private static void TryPick(Empire empire, Person emperor, Realm core)
    {
        float chance = RulerQualitySystem.Of(emperor) switch
        {
            RulerQualitySystem.Grade.Wise => 0f,
            RulerQualitySystem.Grade.Weak or RulerQualitySystem.Grade.Fatuous or RulerQualitySystem.Grade.Dissolute => 0.3f,
            RulerQualitySystem.Grade.Tyrant => 0.15f,
            _ => 0.08f
        };
        if (EcRandom.value >= chance) return;
        Person favourite = core.People.Where(actor => actor?.Record != null && !actor.IsGone() && actor.Alive && actor != emperor &&
                                                    actor.IsOnOffice() && !(core.HasHeir() && core.GetHeir() == actor))
            .OrderByDescending(actor => (actor.HasTrait("greedy") ? 2f : 0f) + (actor.HasTrait("ambitious") ? 1f : 0f) +
                                        EcRandom.value * 2f - Mathf.Min(actor.Renown, 100f) / 100f)
            .FirstOrDefault();
        if (favourite == null) return;
        empire.data.favourite_id = favourite.Key;
        empire.data.favourite_emperor_id = emperor.Key;
        string text = string.Format(LM.Get("favourite_rise_history"), emperor.FullName(), favourite.FullName());
        EventRecorder.Record(empire, text, emperor);
        BiographySystem.Note(favourite, string.Format(LM.Get("bio_favourite_rise"), emperor.FullName()), emperor);
        Person leader = empire.GetCabinetLeader();
        if (leader?.Record != null && leader != favourite && leader.Alive)
            PersonalTieSystem.MakeRivals(favourite, leader, PersonalTieSystem.CauseFavourite);
    }

    private static void Sway(Empire empire, Person favourite, Realm core)
    {
        FixedFaction faction = favourite.GetFaction();
        if (faction != null) core.TryIncreaseFactionRatio(faction, FactionGain);
        favourite.City?.AddCorruptionRate(Corruption);
        foreach (FixedFaction other in core.GetRegime()?.GetPlayerFactions() ?? Enumerable.Empty<FixedFaction>())
        {
            if (other == null || other == faction) continue;
            var state = CourtSystem.FactionState(empire, other);
            state.grievance = Mathf.Min(100f, state.grievance + GrievanceGain);
        }
    }

    // 新君即位：半数诛杀先帝宠臣，否则罢黜
    private static void Fall(Empire empire, Person favourite, Person emperor)
    {
        empire.data.favourite_id = -1L;
        string name = favourite.FullName();
        if (EcRandom.value < 0.5f)
        {
            EventRecorder.Record(empire, string.Format(LM.Get("favourite_executed_history"), emperor.FullName(), name), emperor);
            BiographySystem.Kill(favourite, string.Format(LM.Get("death_cause_favourite"), emperor.FullName()), emperor, executed: true);
            return;
        }
        favourite.GetOffice()?.RemoveActor();
        EventRecorder.Record(empire, string.Format(LM.Get("favourite_dismissed_history"), emperor.FullName(), name), emperor);
        BiographySystem.Note(favourite, string.Format(LM.Get("bio_favourite_dismissed"), emperor.FullName()), emperor);
    }

    // 清君侧得手(见 FactionPurgeSystem)：宠臣一并被诛
    public static void OnPurge(Empire empire, Person avenger)
    {
        Person favourite = Of(empire);
        if (favourite == null) return;
        empire.data.favourite_id = -1L;
        EventRecorder.Record(empire, string.Format(LM.Get("favourite_purged_history"), favourite.FullName()), favourite);
        BiographySystem.Kill(favourite, LM.Get("death_cause_favourite_purged"), avenger, executed: true);
    }
}
}
