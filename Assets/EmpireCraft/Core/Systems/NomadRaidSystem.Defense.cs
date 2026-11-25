// Generated from the EmpireCraft mod: Scripts/GeneralSystems/NomadRaidSystem.Defense.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 定居一方的应对(用户问答三项都要)：
//   · 花钱买和：劫掠开打满一个月起，遭劫之国(帝国的边地由中央拿主意、出钱)每月按 SteppeRules.BuyOffChance 掷一次——
//     已被洗劫的城越多、对方越强、手上还有别的仗越想买，自己强出一倍不买；开国雄主、中兴雄主在位不买。三种买法：
//       - 和亲岁币：买方是君主制帝国、来的是联盟、盟主还没收谁的岁币时 35% —— 宗室女嫁给盟主(找不到人也照样议和)，
//         从此每年送岁币(二十年，两国互不开战，见 FrontierRelations、WarGate)；
//       - 开互市：买方是帝国时 40% —— 与出兵各部开互市 MarketTreatyYears 年(期内遭劫过也照常通市)，另给半份买和金；
//       - 金帛：给一份买和金 = max(20，遭劫之国户数的四分之一，买方国库一成)，出不起就不买。
//     买和金按出兵多少分给各部，当场议和退兵、联盟解散(算得手，盟主照样记威望)；
//   · 长城关隘：开战时量一量两边交界——守方边上有城墙的地块占比 ×0.7、交界的城里有完好关城 +0.3(至多 1)，即关隘之固；
//     选目标时打分打折(至多六折)，洗劫所得按 1 − 0.5×关隘之固打折，出兵各部正在进攻的军每月折损 WallAttrition×关隘之固；
//   · 屯田实边：二十年内施行过屯田实边的帝国，与外国接壤的边城守得住——遭洗劫时金帛只拿七成、粮只拿四分之一、
//     人口只掳一半，这座城只算抢了半座。
public static partial class NomadRaidSystem
{
    private const float WallAttrition = 0.03f, HeqinOdds = 0.35f, MarketOdds = 0.4f, TuntianYears = 20f;
    private const int MarketTreatyYears = 10, MinRansom = 20;
    private const float SteadfastMoney = 0.7f, SteadfastFood = 0.25f, SteadfastPeople = 0.5f;

    // 守方长城关隘之固(0~1)：交界地块里有墙的占比，交界的城里有没有完好的关城
#if EC_PENDING
    private static float Defense(List<Realm> members, Realm target)
    {
        var raiders = new HashSet<Realm>(members);
        int frontier = 0, walled = 0;
        bool fort = false;
        foreach (City city in target.Cities)
        {
            if (city?.Record == null || city.IsGone() || city.NeighbourRealms == null ||
                !city.NeighbourRealms.Any(raiders.Contains)) continue;
            if (!fort && RuralSettlementSystem.InCity(city).Any(settlement => settlement.fort && !settlement.cleared &&
                                                                             settlement.ruined_at < 0d)) fort = true;
            foreach (Cell zone in city.border_zones)
            {
                if (zone == null) continue;
                bool facing = false, wall = TerrainResistance.HasWall(zone);
                foreach (Cell next in zone.Neighbours ?? Array.Empty<Cell>())
                {
                    if (next?.City?.Realm == null || !raiders.Contains(next.City.Realm)) continue;
                    facing = true;
                    if (!wall && TerrainResistance.HasWall(next)) wall = true;
                }
                if (!facing) continue;
                frontier++;
                if (wall) walled++;
            }
        }
        return SteppeRules.Defense(frontier > 0 ? walled / (float)frontier : 0f, fort);
    }
#endif

    // 屯田实边的边城：所在帝国二十年内施行过屯田实边，城与外国接壤
    private static bool Steadfast(City city)
    {
        Empire empire = city?.Realm?.GetEmpire();
        // 修筑城防同屯田实边(见 TempFac_修筑城防)(用户 2026-11-11 "增加更多的议题，比如开战，或者其他的符合利益的"，问答全选)
        bool Recent(TemporaryFactionType type) => empire.data.claim_done_at.TryGetValue(type.ToString(), out double at) &&
                                                  YearsSince(at) < TuntianYears;
        if (empire?.data?.claim_done_at == null || !Recent(TemporaryFactionType.屯田实边) && !Recent(TemporaryFactionType.修筑城防))
            return false;
        return city.NeighbourCities?.Any(other => other?.Realm != null && other.Realm.GetEmpire() != empire) == true;
    }

#if EC_PENDING
    static partial void MonthlyCourse(War war, RaidWarState state)
    {
        WallLosses(war, state);
        if (state.bought_off == 0 && WarSituation.WarYears(war) >= 1f / 12f) TryBuyOff(war, state);
    }
#endif

    // 关隘折损：出兵各部正在进攻的军
#if EC_PENDING
    private static void WallLosses(War war, RaidWarState state)
    {
        if (state.defense <= 0f || !CorpsSystem.Enabled) return;
        foreach (long id in state.members)
        {
            Realm member = LiveKingdom(id);
            if (member == null || !war.IsAttacker(member)) continue;
            foreach (CorpsData corps in CorpsSystem.Of(member))
                if (corps.task == "attack" && corps.strength > 0f)
                    CorpsSystem.Casualties(corps, corps.strength * WallAttrition * state.defense);
        }
    }
#endif

    #region 买和

#if EC_PENDING
    private static void TryBuyOff(War war, RaidWarState state)
    {
        Realm target = war.MainDefender;
        Realm payer = ModRealm(target);
        if (!Alive(target) || !Alive(payer) || !payer.HasRuler() || RulerTraitSystem.FounderReigns(payer)) return;
        List<Realm> raiders = state.members.Select(LiveKingdom).Where(member => member != null && war.IsAttacker(member)).ToList();
        if (raiders.Count == 0) return;
        double ratio = raiders.Sum(PowerOf) / Math.Max(1d, PowerOf(target));
        bool otherWars = payer.GetWars().Any(other => other?.Record != null && other != war && !other.HasEnded());
        if (EcRandom.value >= SteppeRules.BuyOffChance(state.sacked, ratio, otherWars)) return;
        int funds = StateSettlementSystem.DiscretionaryFunds(payer);
        int households = CityPopulationSystem.AbstractPopulationEnabled ? CityPopulationSystem.Households(target) : target.PopulationCount() ?? 0;
        int ransom = Math.Max(MinRansom, Math.Max(households / 4, payer.GetMoney() / 10));
        Realm leader = LiveKingdom(state.leader_id);
        Empire empire = payer.IsEmpire() ? payer.GetEmpire() : null;
        string text;
        int paid;
        if (empire != null && leader != null && raiders.Count > 1 && !leader.HasGivenAlliance() &&
            RegimeManager.IsMonarchy(payer.GetRegime()?.type) && !RepublicSystem.IsRepublic(empire) &&
            EcRandom.value < HeqinOdds)
        {
            Person bride = RoyalMarriageSystem.ArrangeHeqin(payer, leader);
            leader.JoinGivenAlliance(empire);
            paid = 0;
            state.bought_off = 3;
            text = bride != null
                ? string.Format(LM.Get("nomad_raid_heqin_history"), empire.GetEmpireName(), bride.FullName(), state.leader_name)
                : string.Format(LM.Get("nomad_raid_heqin_plain_history"), empire.GetEmpireName(), state.leader_name);
        }
        else if (empire != null && funds >= ransom / 2 && EcRandom.value < MarketOdds)
        {
            double now = GameClock.Now;
            foreach (Realm raider in raiders)
                foreach (Realm side in new[] { raider, ModRealm(raider) }.Distinct())
                {
                    SteppeState steppe = State(side);
                    steppe.market_treaty_with = payer.Key;
                    steppe.market_treaty_at = now;
                }
            paid = ransom / 2;
            state.bought_off = 2;
            text = string.Format(LM.Get("nomad_raid_market_history"), payer.GetKingdomName(), state.leader_name, paid);
        }
        else if (funds >= ransom)
        {
            paid = ransom;
            state.bought_off = 1;
            text = string.Format(LM.Get("nomad_raid_ransom_history"), payer.GetKingdomName(), paid, state.leader_name);
        }
        else
        {
            LogService.LogInfo($"{LogTag} {payer.GetKingdomName()} 想向{state.leader_name}买和，出不起{ransom}(可动用{funds})");
            return;
        }
        if (paid > 0)
        {
            payer.SubMoney(paid, TreasuryCategory.Tribute);
            foreach ((Realm member, float share) in Shares(war, state))
                member.AddMoney(Mathf.FloorToInt(paid * share), TreasuryCategory.Tribute);
        }
        state.ransom = paid;
        Chronicle(text, raiders.Concat(new[] { target, payer }));
        LogService.LogInfo($"{LogTag} {payer.GetKingdomName()} 向{state.leader_name}买和(第{state.bought_off}种，" +
                           $"已被洗劫{state.sacked:0.#}城，对方/本方{ratio:0.00})：{text}");
        EmpireWorld.Current.EndWar(war, WarOutcome.Peace);
    }
#endif

    #endregion
}
}
