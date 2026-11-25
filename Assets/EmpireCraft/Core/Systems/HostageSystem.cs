// Generated from the EmpireCraft mod: Scripts/GeneralSystems/HostageSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Linq;

namespace EmpireCraft.Core
{

// 质子(群友 2026-11-22 "某国闻大军压境，惊惧不已……朝贡/附庸/送质子，质子为继承人，居住在都城")：
//   · 兵临城下称臣的(见 WarMuster)、打输了被迫称臣纳贡或收作附庸的(见 WarGoalSystem 和约)，把储君送到宗主都城为质；
//     没立储的送君主最年长的儿女，膝下无人就不送；
//   · 质子在一天，这一国不求独立(FeudalVassalService.CanSeekIndependence)、不自行退出朝贡(KingdomBehCheckEmpire)；
//   · 质子回国即位：久居上国，在位头 AmityYears 年照旧不求独立、不退出朝贡；
//   · 质子身故、宗属关系没了(宗主亡国、被放、改换门庭)：质约作废，不再补送——质子只管得住一代人。
//   质子只是记在这一国的数据里(谁、在谁那里、哪年送的)，人不搬家：纯净地图下人物本来就不在地图上，
//   搬了反倒牵动继承(储君要在本国才立得上)。
public static class HostageSystem
{
    public const int AmityYears = 10;

    private static bool Alive(Realm kingdom) => kingdom?.Record != null && !kingdom.IsGone();

    private static Person Living(long id)
    {
        Person actor = id >= 0 ? EmpireWorld.Current?.PersonByKey(id) : null;
        return actor?.Record != null && !actor.IsGone() && actor.Alive ? actor : null;
    }

    // 眼下的宗主：封建附庸的领主，或朝贡的那个帝国的中央
    private static Realm Lord(Realm subject) =>
        FeudalVassalService.GetOverlord(subject) ?? subject.GetTakenAllianceEmpire()?.CoreKingdom;

    // 在别国为质的这个人是谁的质子(界面、个人提示用)；不是为 null
    public static Person HostageOf(Realm subject) =>
        Alive(subject) ? Living(subject.GetOrCreate().hostage_actor_id) : null;

    // 质约还管着这一国：质子在宗主那里，或质子回国即位不满 AmityYears 年
    public static bool Binds(Realm subject)
    {
        if (!Alive(subject) || EmpireWorld.Current == null) return false;
        var data = subject.GetOrCreate();
        if (data.hostage_holder_id < 0L || Lord(subject)?.Key != data.hostage_holder_id) return false;
        Person hostage = Living(data.hostage_actor_id);
        if (hostage != null && hostage != subject.Ruler) return true;
        double crowned = data.hostage_crowned_at;
        return crowned >= 0d && GameClock.Now >= crowned && GameClock.YearsSince(crowned) < AmityYears;
    }

    // 称臣时送质子；送成了返回 true
#if EC_PENDING
    public static bool Send(Realm lord, Realm subject)
    {
        if (!Alive(lord) || !Alive(subject) || lord == subject || EmpireWorld.Current == null) return false;
        var data = subject.GetOrCreate();
        if (Living(data.hostage_actor_id) != null && data.hostage_holder_id == lord.Key) return false;
        Person king = subject.Ruler;
        Person hostage = subject.HasHeir() ? subject.GetHeir() : null;
        if (hostage?.Record == null || hostage.IsGone() || !hostage.Alive || hostage == king)
            hostage = king?.GetChildren(false)?.Where(child => child?.Record != null && !child.IsGone() && child.Alive && child != king)
                .OrderByDescending(child => child.Age()).FirstOrDefault();
        if (hostage == null) return false;
        data.hostage_actor_id = hostage.Key;
        data.hostage_holder_id = lord.Key;
        data.hostage_since = GameClock.Now;
        data.hostage_crowned_at = -1d;
        string lordName = lord.GetEmpire()?.GetEmpireName() ?? lord.GetKingdomName();
        string seat = lord.Capital?.GetCityName() ?? lordName;
        string text = string.Format(LM.Get("hostage_sent_history"), subject.GetKingdomName(), hostage.FullName(), lordName, seat);
        EventRecorder.Record(lord, text);
        hostage.RecordPersonalHistory(string.Format(LM.Get("personal_history_hostage_sent"), lordName, seat));
        LogService.LogInfo($"[EmpireCraft][质子] {subject.GetKingdomName()} 送 {hostage.FullName()} 入质 {lordName}");
        return true;
    }
#endif

    private static void Clear(KingdomExtension.KingdomExtraData data)
    {
        data.hostage_actor_id = -1L;
        data.hostage_holder_id = -1L;
        data.hostage_since = -1d;
        data.hostage_crowned_at = -1d;
    }

    // 每年(见 RoyalHouseSystem.Tick)：质子即位、身故、宗属关系没了
#if EC_PENDING
    public static void Yearly()
    {
        if (EmpireWorld.Current?.LiveRealms == null) return;
        foreach (Realm subject in EmpireWorld.Current.LiveRealms.ToList())
        {
            if (!Alive(subject)) continue;
            var data = subject.GetOrCreate();
            if (data.hostage_holder_id < 0L) continue;
            try
            {
                Realm lord = Lord(subject);
                Person hostage = Living(data.hostage_actor_id);
                if (lord == null || lord.Key != data.hostage_holder_id)
                {
                    // 宗属关系没了：放归
                    hostage?.RecordPersonalHistory(LM.Get("personal_history_hostage_released"));
                    Clear(data);
                    continue;
                }
                if (data.hostage_crowned_at >= 0d)
                {
                    if (GameClock.Now < data.hostage_crowned_at || GameClock.YearsSince(data.hostage_crowned_at) >= AmityYears)
                        Clear(data);
                    continue;
                }
                if (hostage == null)
                {
                    Clear(data);
                    continue;
                }
                if (hostage != subject.Ruler) continue;
                // 质子回国即位
                data.hostage_crowned_at = GameClock.Now;
                string lordName = lord.GetEmpire()?.GetEmpireName() ?? lord.GetKingdomName();
                EventRecorder.Record(lord, string.Format(LM.Get("hostage_crowned_history"), hostage.FullName(), lordName, subject.GetKingdomName()));
                hostage.RecordPersonalHistory(string.Format(LM.Get("personal_history_hostage_crowned"), lordName));
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][质子] {subject.GetKingdomName()} 年度结算失败: {exception.Message}");
            }
        }
    }
#endif
}
}
