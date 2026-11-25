// Ported from the EmpireCraft mod: Scripts/GeneralSystems/SmallStateSubmissionSystem.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 小国归附(用户 2026-10-27 截图欧洲、西亚版图星罗棋布："这个地块真的太碎了一点都不符合历史实际"，问答四条全选)：
// 一两座城的独立小国，陆上四邻大半(MinShare)是同一家(按宗主、帝国中央算)，那一家国力是它的 PowerRatio 倍以上，
// 两家不在交战——每年 Chance 向它称臣：行封建附庸的为附庸，收朝贡的帝国为朝贡国(版图随宗主色)，都不整国吞并。
// 立国不满 GraceYears 年的、开国雄主在位的、城邦、正在打仗的不判；每年每个大国至多收 PerLordYearly 家。
public static class SmallStateSubmissionSystem
{
    private const int MaxCities = 2, GraceYears = 10, PerLordYearly = 1;
    private const float MinShare = 0.75f, PowerRatio = 5f, Chance = 0.1f;

    private static object _world;
    private static double _lastYear = -1d;

    private static bool Alive(Realm kingdom) => kingdom?.Record != null && !kingdom.IsGone();

#if EC_PENDING
    public static void Tick()
    {
        EmpireWorld world = EmpireWorld.Current;
        if (world?.LiveRealms == null || ModClass.IS_CLEAR) return;
        // 世界法则「禁止AI归附」(见 DiplomacyLaws)
        if (DiplomacyLaws.VassalBlocked) return;
        if (!ReferenceEquals(_world, world))
        {
            _world = world;
            _lastYear = -1d;
        }
        double now = world.Now();
        if (_lastYear >= 0d && now >= _lastYear && GameClock.YearsSince(_lastYear) < 1) return;
        bool first = _lastYear < 0d;
        _lastYear = now;
        // 读档后头一年不判(免得一读档就成批称臣)
        if (first) return;
        try
        {
            var taken = new Dictionary<long, int>();
            foreach (Realm small in world.LiveRealms.ToList())
            {
                if (!Candidate(small) || EcRandom.value >= Chance) continue;
                Realm lord = Dominant(small);
                if (lord == null || taken.TryGetValue(lord.Key, out int count) && count >= PerLordYearly) continue;
                if (!Submit(lord, small)) continue;
                taken[lord.Key] = count + 1;
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][小国归附] 结算失败: {exception.Message}");
        }
    }

    private static bool Candidate(Realm small)
    {
        if (!Alive(small) || small.IsWild || !small.IsCivilized() || !small.HasRuler() || small.Capital == null) return false;
        int cities = small.Cities?.Count ?? 0;
        if (cities == 0 || cities > MaxCities || small.IsInEmpire() || small.IsEmpire() ||
            FeudalVassalService.GetOverlord(small) != null || small.GetTakenAllianceEmpire() != null) return false;
        if (small.getWars().Any(war => war != null && !war.hasEnded()) || CityStateService.IsCityState(small) ||
            RulerTraitSystem.FounderShield(small) || UnificationSystem.InGrace(small) ||
            AncientWarfareCompatibility.Owns(small)) return false;
        double founded = small.FoundedStamp;
        return founded >= 0d && GameClock.Now >= founded && GameClock.YearsSince(founded) >= GraceYears;
    }
#endif

    // 陆上四邻里占 MinShare 以上的那一家(宗主、帝国中央)，国力够、两家不交战
#if EC_PENDING
    private static Realm Dominant(Realm small)
    {
        var contacts = new Dictionary<Realm, int>();
        int total = 0;
        foreach (City city in small.Cities)
            foreach (TileZone zone in city?.border_zones ?? new HashSet<TileZone>())
                foreach (TileZone next in zone?.neighbours ?? Array.Empty<TileZone>())
                {
                    Realm other = next?.city?.kingdom;
                    if (!Alive(other) || other == small || other.IsWild) continue;
                    Realm top = EmpireCoreControl.Sovereign(other);
                    if (!Alive(top)) continue;
                    contacts.TryGetValue(top, out int count);
                    contacts[top] = count + 1;
                    total++;
                }
        if (total == 0) return null;
        var best = contacts.OrderByDescending(pair => pair.Value).First();
        Realm lord = best.Key;
        if (best.Value < total * MinShare || lord.isInWarWith(small) || FrontSystem.Hostile(lord, small)) return null;
        return WarSituation.Power(lord) >= WarSituation.Power(small) * PowerRatio ? lord : null;
    }
#endif

    // 行封建附庸的为附庸，收朝贡的帝国为朝贡国；都不行就不归附。
    // 附庸按和平收附庸的规矩(FeudalVassalService.Bind：宗主收得了附庸、层级、冷却……)——以前用强制附庸，宗主文化行封建附庸、
    // 政体却不是封建的(收不得附庸)，当月月度同步就把它放了，不留记录：世界日志说称臣、地图上仍是独立之国
    // (玩家："就是这样称臣又独立""也没有进附庸和属国"，用户 2026-10-27 转)
#if EC_PENDING
    public static bool Submit(Realm lord, Realm small)
    {
        if (FeudalVassalService.TooFar(lord, small)) return false;
        bool bound = FeudalVassalService.UsesFeudalVassals(lord)
            ? FeudalVassalService.Bind(lord, small)
            : TributaryRelations.Submit(lord, small);
        if (!bound) return false;
        string lordName = lord.GetEmpire()?.GetEmpireName() ?? lord.GetKingdomName();
        string text = string.Format(LM.Get("small_state_submit_history"), small.GetKingdomName(), lordName);
        EventRecorder.Record(lord, text);
        TranslateHelper.LogEventMessage(text, lord);
        LogService.LogInfo($"[EmpireCraft][小国归附] {small.GetKingdomName()}({small.Cities.Count}城) 向 {lordName} 称臣");
        return true;
    }
#endif
}
}
