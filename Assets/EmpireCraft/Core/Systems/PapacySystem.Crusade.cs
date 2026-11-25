// Generated from the EmpireCraft mod: Scripts/GeneralSystems/PapacySystem.Crusade.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Vector2 = EmpireCraft.Core.EcVector2;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 圣战(每个宗教每年随教权结算检定一次；同一宗教同时只有一场，打完冷却 15 年)：
//   · 号召者：教廷；圣地丢了没有教廷时，由本宗教最强的国家(帝国按中央)号召。分出去的教派、地方教会不号召(号召力在本宗，见 CallsHolyWar)。
//   · 目标(依次检定，满足一项就有一半可能号召)：
//       收复圣地——圣战目标(CrusadeCity：本宗另记的那处，如耶路撒冷；没记就是圣地)被异教国家占着(教权 30 以上)；
//       援救同教——同宗教国家被异教国家攻打、已经丢了城(教权 50 以上)，讨伐攻方；
//       讨伐异端——被绝罚满两年、拒不悔罪的帝国(教权 50 以上)；
//       东征异教——教权 80 以上、号召者太平无事时，打与本宗教国家接壤的异教国家里城最多的一个。
//   · 响应：同宗教各国(帝国按中央，被绝罚的、大分裂中奉对立教宗的除外，见 PapacySystem.Antipope)按 35%、教宗加冕过 +25%、西方文化 +10%、离号召者每 20 块地 -5% 检定加入攻方，最多八国；
//     被讨伐方的同教国家(异教时)按三成检定加入守方，最多五国(护教圣战)。攻下的城改信攻方宗教(见 CityPatch)。
//   · 结局：开战目标为割地，攻方占了一半以上的城归占领者；收复圣地时圣地交给教廷(没有教廷就交给号召者)，
//     收复的是另记的那处时骑士团立国，没有骑士团就归出力最多的同教国。
//     攻方胜：教权 +15，参战的帝国正统上升；攻方未胜：教权 -10，圣战失利次数 +1(宗教改革的苗头)。
public static partial class PapacySystem
{
    public const int CrusadeHolyLand = 1, CrusadeRescue = 2, CrusadeHeretic = 3, CrusadeInfidel = 4;

    private const float CrusadeChance = 0.5f;
    private const int CrusadeCooldownYears = 15;
    private const int MaxResponders = 8, MaxDefenders = 5;
    private const double SecondsPerYear = 60d;

    public static War ActiveCrusade(Religion religion)
    {
        if (religion?.data == null) return null;
        long id = religion.GetOrCreate().CrusadeWarId;
        War war = id >= 0 ? EmpireWorld.Current.WarByKey(id) : null;
        return war != null && war.Alive && !war.HasEnded() ? war : null;
    }

    // 圣战要收复的城：本宗另记了一处的取那处(真实地图天主教会是耶路撒冷，圣地仍是罗马——用户 2026-11-08 选
    // "罗马是圣地，耶城作圣战目标")，没记就是圣地。教派随本宗
#if EC_PENDING
    public static City CrusadeCity(Religion religion)
    {
        Religion root = ReligionSphere.Root(religion);
        if (root?.data == null) return null;
        long id = root.GetOrCreate().CrusadeCityId;
        City city = id >= 0 ? EmpireWorld.Current.CityByKey(id) : null;
        return city?.Record != null && !city.IsGone() ? city : root.GetCity();
    }
#endif

    // 圣战目标被异教占着：圣地被本宗以外的人占着就算(教派占着圣地也要收复)；另记的那处(耶路撒冷)被同出一宗的教派占着不算
#if EC_PENDING
    private static bool HolyLandLost(Religion religion, City city)
    {
        Realm holder = city?.Realm;
        if (city?.Record == null || city.IsGone() || holder?.Record == null || holder.IsWild || holder.Religion == religion) return false;
        return city == religion.GetCity() || ReligionSphere.Root(holder.Religion) != ReligionSphere.Root(religion);
    }

    private static void TryCrusade(Religion religion)
    {
        if (!CallsHolyWar(religion)) return;
        var data = religion.GetOrCreate();
        double now = GameClock.Now;
        if (ActiveCrusade(religion) != null || now < data.CrusadeCooldownUntil) return;
        float authority = Authority(religion);
        Realm papacy = Papacy(religion);
        Realm caller = papacy != null && !IsCaptive(religion) ? papacy : Strongest(religion);
        if (caller == null || caller.HasEnemies() && papacy != null) return;
        (Realm target, int kind) = FindTarget(religion, caller, authority);
        if (target == null || target == caller || EcRandom.value >= CrusadeChance) return;
        Declare(religion, caller, target, kind);
    }

    private static (Realm target, int kind) FindTarget(Religion religion, Realm caller, float authority)
    {
        City holy = CrusadeCity(religion);
        if (authority >= 30f && HolyLandLost(religion, holy)) return (ModRealm(holy.Realm), CrusadeHolyLand);
        // 讨伐异端：宗教改革分出去的国教会
        if (authority >= 40f && HereticTarget(religion) is Realm heretic) return (heretic, CrusadeHeretic);
        if (authority < 50f) return (null, 0);
        foreach (War war in EmpireWorld.Current.LiveWars)
        {
            if (war == null || war.HasEnded()) continue;
            Realm attacker = war.MainAttacker, defender = war.MainDefender;
            if (attacker?.Record == null || defender?.Record == null || defender.Religion != religion ||
                attacker.Religion == religion || !attacker.HasReligion()) continue;
            if (WarSituation.CitiesLost(war, defender) > 0) return (ModRealm(attacker), CrusadeRescue);
        }
        foreach (Empire empire in ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Empire>())
        {
            // 对立教宗下的绝罚不算(见 PapacySystem.Antipope)
            if (empire?.data == null || empire.IsArchived() || ReligionOf(empire) != religion || !IsExcommunicated(empire) ||
                State(empire).banned_by_rival) continue;
            if (GameClock.YearsSince(State(empire).excommunicated_at) >= 2) return (empire.CoreKingdom, CrusadeHeretic);
        }
        if (authority < 80f || caller.HasEnemies()) return (null, 0);
        Realm best = null;
        foreach (Realm kingdom in EmpireWorld.Current.LiveRealms)
        {
            if (kingdom?.Record == null || kingdom.IsGone() || kingdom.IsWild || !kingdom.HasReligion() ||
                kingdom.Religion == religion || ModRealm(kingdom) != kingdom) continue;
            if (!religion.Realms.Any(member => member != null && member.IsNeighbourWith(kingdom))) continue;
            if (best == null || kingdom.Cities.Count > best.Cities.Count) best = kingdom;
        }
        return (best, CrusadeInfidel);
    }
#endif

    private static Realm ModRealm(Realm kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    // 本宗教最强的国家(帝国按中央)
#if EC_PENDING
    private static Realm Strongest(Religion religion) =>
        EmpireWorld.Current.LiveRealms.Where(kingdom => kingdom?.Record != null && !kingdom.IsGone() && !kingdom.IsWild &&
                                              kingdom.Religion == religion && ModRealm(kingdom) == kingdom)
            .OrderByDescending(kingdom => kingdom.GetEmpire()?.countCities() ?? kingdom.Cities.Count).FirstOrDefault();

    private static void Declare(Religion religion, Realm caller, Realm target, int kind)
    {
        // 远距离不开战：发起国(帝国按全体成员)与目标接壤或隔海相望才发(见 StraitReach.Within)
        if (!StraitReach.Within(caller, target)) return;
        War war = EmpireWorld.Current.StartWar(caller, target, WarKinds.Normal);
        if (war == null) return;
        var data = religion.GetOrCreate();
        data.CrusadeCount++;
        data.CrusadeWarId = war.Key;
        war.SetEmpireWarType(EmpireWarType.神圣, pre: religion.Name);
        WarExtension.WarExtraData snapshot = war.GetOrCreate();
        snapshot.crusade_religion_id = religion.data.id;
        snapshot.crusade_kind = kind;
        City holyLand = CrusadeCity(religion);
        snapshot.crusade_city_id = holyLand?.Key ?? -1L;
        WarGoalSystem.Preset(war, WarGoal.割地);
        war.Name = string.Format(LM.Get("crusade_war_name"), data.CrusadeCount, religion.Name);
        string goal = kind switch
        {
            CrusadeHolyLand => string.Format(LM.Get("crusade_kind_holy"), holyLand?.GetCityName() ?? ""),
            CrusadeRescue => LM.Get("crusade_kind_rescue"),
            CrusadeHeretic => LM.Get("crusade_kind_heretic"),
            _ => LM.Get("crusade_kind_infidel")
        };
        string caller_name = Pope(religion) is Person pope && Papacy(religion) == caller
            ? string.Format(LM.Get("crusade_caller_pope"), pope.FullName())
            : caller.GetKingdomName();
        TranslateHelper.LogEventMessage(string.Format(LM.Get("crusade_called_history"), caller_name, goal,
            target.GetKingdomName(), religion.Name), caller);
        string calledText = string.Format(LM.Get("crusade_called_history"), caller_name, goal, target.GetKingdomName(), religion.Name);
        RecordReligion(caller, calledText);
        ReligionChronicle.Record(religion, ReligionChronicle.KindHolyWar, calledText);
        Rally(religion, war, caller, target);
    }

    private static void Rally(Religion religion, War war, Realm caller, Realm target)
    {
        City seat = caller.Capital;
        int joined = 0;
        // 教宗正统高，响应的多(见 PopeLegitimacy)
        float popeBonus = PopeLegitimacy.Applies(religion)
            ? (PopeLegitimacy.Get(religion) - 50) * PopeLegitimacy.RallyPerPoint : 0f;
        foreach (Realm kingdom in EmpireWorld.Current.LiveRealms.ToList())
        {
            if (joined >= MaxResponders) break;
            if (kingdom?.Record == null || kingdom.IsGone() || kingdom.IsWild || kingdom == caller || kingdom.Religion != religion ||
                ModRealm(kingdom) != kingdom || ModRealm(kingdom) == ModRealm(target) || war.HasRealm(kingdom) ||
                kingdom.IsAtWarWith(caller)) continue;
            Empire empire = kingdom.GetEmpire();
            if (empire != null && IsExcommunicated(empire) || FollowsAntipope(kingdom)) continue;
            float chance = 0.35f + popeBonus;
            if (empire != null && IsCrowned(empire) && !State(empire).self_crowned) chance += 0.25f;
            // 西方文化、阿拉伯一线(吉哈德)的集结机会多一成
            if (empire != null ? IsWestern(empire) || IsCaliphal(empire)
                    : WagesHolyWar(CultureService.GetRealmCulture(kingdom))) chance += 0.1f;
            chance += OrderHostBonus(kingdom, religion);
            // 两位哈里发并立：对立哈里发本国不应圣地哈里发之召，奉对立哈里发的减半(见 IslamicSchismSystem)
            chance *= IslamicSchismSystem.RallyFactor(kingdom, religion, caller);
            if (seat != null && kingdom.Capital != null)
                chance -= Vector2.Distance(kingdom.Capital.Center, seat.Center) / 8f / 20f * 0.05f;
            if (EcRandom.value >= chance) continue;
            war.JoinAttackers(kingdom);
            joined++;
            TranslateHelper.LogEventMessage(string.Format(LM.Get("crusade_join_log"), kingdom.GetKingdomName(),
                target.GetKingdomName()), kingdom);
        }
        // 护教圣战：被讨伐方的同教国家来援(异教之间才有)
        if (!target.HasReligion() || target.Religion == religion) return;
        int defenders = 0;
        foreach (Realm kingdom in EmpireWorld.Current.LiveRealms.ToList())
        {
            if (defenders >= MaxDefenders) break;
            if (kingdom?.Record == null || kingdom.IsGone() || kingdom.IsWild || kingdom == target ||
                kingdom.Religion != target.Religion || ModRealm(kingdom) != kingdom || war.HasRealm(kingdom) ||
                EcRandom.value >= 0.3f) continue;
            war.JoinDefenders(kingdom);
            defenders++;
            TranslateHelper.LogEventMessage(string.Format(LM.Get("crusade_defend_log"), kingdom.GetKingdomName(),
                target.GetKingdomName()), kingdom);
        }
    }
#endif

    // 战争结束(WarPatch 在开战目标结算之后调用)：圣地交接、教权与正统
#if EC_PENDING
    public static void OnWarEnded(War war, WarOutcome winner)
    {
        try
        {
            WarExtension.WarExtraData snapshot = war?.GetOrCreate();
            if (snapshot == null || snapshot.crusade_religion_id < 0) return;
            Religion religion = EmpireWorld.Current.Religions.Get(snapshot.crusade_religion_id);
            if (religion?.data == null) return;
            var data = religion.GetOrCreate();
            data.CrusadeWarId = -1L;
            data.CrusadeCooldownUntil = GameClock.Now + CrusadeCooldownYears * SecondsPerYear;
            (int attackerGain, int defenderGain) = WarGoalSystem.Gains(war);
            // 讨伐异端打完：教随国定
            if (snapshot.crusade_kind == CrusadeHeretic) ReligiousPeace(religion);
            bool won = winner == WarOutcome.Attackers || winner == WarOutcome.Peace && attackerGain >= defenderGain + 1;
            Realm caller = war.MainAttacker;
            if (won)
            {
                AddAuthority(religion, 15f);
                foreach (Realm kingdom in war.Attackers.ToList())
                    if (kingdom?.Record != null && !kingdom.IsGone() && kingdom.IsEmpire()) kingdom.GetEmpire()?.AddMandate(25);
                // 收复圣地：圣地交给教廷；没有教廷时由骑士团在圣地立国，没有骑士团就交给号召者。
                // 收复的是圣地以外另记的那处(耶路撒冷)：不交教廷，骑士团立国，没有骑士团就归出力最多的同教国(十字军国家)
                City holy = snapshot.crusade_kind == CrusadeHolyLand ? EmpireWorld.Current.CityByKey(snapshot.crusade_city_id) : null;
                bool see = holy != null && holy == religion.GetCity();
                if (holy?.Record != null && !holy.IsGone() && holy.Realm?.Religion != religion &&
                    !((!see || Papacy(religion) == null) && TryFoundOrderState(religion, holy)))
                {
                    Realm receiver = see ? Papacy(religion) : LeadingCrusader(war, religion);
                    receiver ??= caller?.Record != null && !caller.IsGone() ? caller : null;
                    if (receiver != null)
                    {
                        holy.JoinRealm(receiver);
                        holy.SetReligion(religion);
                    }
                }
                // 圣战告捷，可能成立骑士团(驻在号召国)
                if (caller?.Record != null && !caller.IsGone() && caller.Religion == religion) TryFoundOrder(religion, caller);
                if (caller?.Record != null && !caller.IsGone())
                    RecordReligion(caller, string.Format(LM.Get("crusade_won_history"), war.Name, religion.Name));
                ReligionChronicle.Record(religion, ReligionChronicle.KindHolyWar,
                    string.Format(LM.Get("crusade_won_history"), war.Name, religion.Name));
            }
            else
            {
                AddAuthority(religion, -10f);
                data.CrusadeLosses++;
                if (caller?.Record != null && !caller.IsGone())
                    RecordReligion(caller, string.Format(LM.Get("crusade_lost_history"), war.Name, religion.Name));
                ReligionChronicle.Record(religion, ReligionChronicle.KindHolyWar,
                    string.Format(LM.Get("crusade_lost_history"), war.Name, religion.Name));
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][圣战] 结算失败: {exception.Message}");
        }
    }
#endif

    // 攻方里奉本宗教、城最多的(教廷除外)
#if EC_PENDING
    private static Realm LeadingCrusader(War war, Religion religion)
    {
        Realm papacy = Papacy(religion), best = null;
        foreach (Realm kingdom in war?.Attackers?.ToList() ?? new List<Realm>())
        {
            if (kingdom?.Record == null || kingdom.IsGone() || kingdom.IsWild || kingdom == papacy || kingdom.Religion != religion) continue;
            if (best == null || kingdom.Cities.Count > best.Cities.Count) best = kingdom;
        }
        return best;
    }

    private static void RecordReligion(Realm kingdom, string text)
    {
        Empire empire = kingdom?.GetEmpire();
        if (empire != null) empire.RecordHistory(directContent: text, actorId: kingdom.Ruler?.Key ?? -1L, kingdomId: kingdom.Key);
    }
#endif
}
}
