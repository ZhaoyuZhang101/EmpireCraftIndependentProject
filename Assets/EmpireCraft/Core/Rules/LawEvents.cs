// Generated from the EmpireCraft mod: Scripts/GeneralSystems/LawEvents.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 律法与实事(用户 2026-11-06 "律法系统感觉很不完善"，选"罪名来自真实发生的事")：以前除了每年给官员、城主、有爵者随机掷罪，
// 只有税收贪墨、计谋揭发会定罪，谋反、篡位、叛国、煽动叛乱、临阵脱逃、违抗军令、抢掠平民、宣扬异端这些罪名从来不会发生。
// 现在把游戏里真发生的事接到律法上。律法开关、各政体有没有这条律、管辖权都由 EmpireLawSystem 判断，这里只挑人、定罪名与几率。
//   当场拿获(EmpireLawSystem.Prosecute：不掷发现骰，按律论处，记卷宗)：
//   · 叛乱兵败(守方胜，或议和时叛军已经灭亡)：叛军首领活着落到朝廷手里——人在守方或守方所在帝国境内、不再是一国之君——
//     按谋反论处；地方独立、附庸独立按叛国，举教起事按煽动叛乱，藩王争位按篡位(见 OnWarEnded；首领见 TrackRebelLeader)；
//   · 不奉诏之战朝廷获胜、抗命的嗣君被废：按谋反论处；
//   · 宫门之变、诸王争位兵败的皇子：先按篡位论处(只处死本人，不连坐妻小；律法关闭、本政体无此律的不论)，之后照旧伏诛
//     (见 PrinceStruggleSystem)；
//   · 大军(满编 BigCorps 以上，义军、骑士团的不算)大败又溃散而统领生还：RoutChance 机会论处——全军覆没只身逃还、守御溃散
//     按临阵脱逃，攻城大败按违抗军令；城破时趁乱出走的守城统领 CityFledChance 机会按临阵脱逃论处；
//     同一国 MilitaryCooldownYears 年内至多论处一员败将(见 BattleDrama)；君主、储君、叛军的统领不论；
//   · 迫害异教时仍奉被禁之教的城主：PersecutionChance 机会按宣扬异端论处，每回至多 PersecutionMax 人(见 ReligiousPersecution)；
//   · 纵兵烧杀同一帝国(或正从本帝国叛出)的城池：攻城统领 SackChance 机会按抢掠平民论处(见 ScorchedEarthSystem)；
//   暗中犯罪(RecordCrime，日后由每年的查案发现；同一罪名二十年内只记一次)：
//   · 纵兵烧杀外国城池：攻城统领 SackRecordChance 机会记抢掠平民；
//   · 朝议"赈济灾荒"被层层克扣(到手不足 SkimLine)：灾城主官按克扣的比例记贪污，每回至多 SkimMax 人(见 TempFac_赈济灾荒)；
//   · 构陷宿敌去职记陷害；遣刺客杀了同国的人记杀人，刺杀本国君主记谋反(见 PersonalTieSystem)；
//   · 世家兼并郡望之田：AnnexChance 机会给在任官员、城主的族人记非法侵占土地(见 GentrySystem.Kin)。
// 罪名对应的刑罚照 EmpireLawConfig(谋反是死刑、夷三族，篡位只处死本人，临阵脱逃是死刑……)，各政体可在律法里删改。
public static class LawEvents
{
    private const float BigCorps = 3000f;
    private const float RoutChance = 0.04f, CityFledChance = 0.1f;
    private const int MilitaryCooldownYears = 2;
    private const float PersecutionChance = 0.35f;
    private const int PersecutionMax = 2;
    private const float SackChance = 0.3f, SackRecordChance = 0.5f;
    private const float SkimLine = 0.8f, SkimFactor = 0.5f;
    private const int SkimMax = 2;
    private const float AnnexChance = 0.05f;

    // 各国上次论处败将的时间(不存档；换了世界时间倒退的记录当作过期)
    private static readonly Dictionary<long, double> LastMilitaryCase = new();

    #region 通用

    // 同一国或同一帝国
    private static bool Within(Realm kingdom, Realm realm) =>
        kingdom?.Record != null && realm?.Record != null && !kingdom.IsGone() && !realm.IsGone() &&
        (kingdom == realm || kingdom.IsInSameEmpire(realm));

    private static bool Living(Person actor) => actor?.Record != null && !actor.IsGone() && actor.Alive;

    private static bool Prosecute(Person actor, LawType law, Realm court, string reasonKey)
    {
        if (!Living(actor) || court?.Record == null || court.IsGone()) return false;
        string name = actor.FullName();
        try
        {
            if (EmpireLawSystem.Prosecute(actor, law, court, reasonKey) == null) return false;
            LogService.LogInfo($"[律法][实事] {court.GetKingdomName()} 以{law}论处 {name}({reasonKey})");
            return true;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[律法][实事] 以{law}论处 {name} 失败: {exception.Message}");
            return false;
        }
    }

    // 暗中犯罪：君主不记(律法给君主记罪只加暴虐，这里都是臣下的事)
    private static bool Record(Person actor, LawType law)
    {
        if (!Living(actor) || actor.IsRuler()) return false;
        try
        {
            return actor.RecordCrime(law);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[律法][实事] 给 {actor.FullName()} 记{law}失败: {exception.Message}");
            return false;
        }
    }

    #endregion

    #region 叛乱兵败

    // 叛乱一类的仗(攻方是起事的一方)按什么罪论处；不是叛乱返回 null。
    // 不奉诏(朝廷是攻方)、伐不臣、征伐不服这些朝廷讨伐的仗不在这里
    private static LawType? RebelLaw(War war, EmpireWarType type)
    {
        switch (type)
        {
            case EmpireWarType.藩王索取皇位:
                return LawType.篡位;
            case EmpireWarType.地方独立:
            case EmpireWarType.附庸独立:
                return LawType.叛国;
            case EmpireWarType.宗教叛乱:
                return LawType.煽动叛乱;
            case EmpireWarType.派系叛乱:
            case EmpireWarType.地方叛乱:
            case EmpireWarType.民族叛乱:
            case EmpireWarType.清君侧:
                return LawType.谋反;
            case EmpireWarType.None:
                return war.GetKind() == WarKinds.Rebellion ? LawType.谋反 : (LawType?)null;
            default:
                return null;
        }
    }

    private static string RebelReason(LawType law) => law switch
    {
        LawType.篡位 => "law_reason_failed_claim",
        LawType.叛国 => "law_reason_failed_secession",
        LawType.煽动叛乱 => "law_reason_failed_sect_uprising",
        _ => "law_reason_failed_rebellion"
    };

    // WarPatch.update 里每次更新：记下叛军眼下的首领(叛军灭亡后国王没了，战后要按记下的找人)。
    // 每帧都走，出错也不能抛出去(会让原版 War.update 每帧报错)
    public static void TrackRebelLeader(War war)
    {
        try
        {
            Person leader = war?.Record == null || war.HasEnded() ? null : war.GetMainAttacker()?.Ruler;
            if (!Living(leader)) return;
            WarExtension.WarExtraData data = war.GetOrCreate();
            LawType? law = data == null ? null : RebelLaw(war, data.empireWarType);
            if (law == null) return;
            data.rebel_leader_id = leader.Key;
            NoteRebels(war, data, law == LawType.叛国);
        }
        catch (Exception)
        {
            // 记不上首领只是战后不论处，不影响战争
        }
    }

    // 起兵、自立记在叛乱一方各国之主身上(量刑天平的叛逆砝码)：主攻之外，随攻的本朝诸国(还在帝国里或就是从这个帝国叛出的)
    // 也记；外国来助的不记。每场仗每人只记一次(用户 2026-11-10 "接着做"自立的判定；以前只记主攻)
    private static void NoteRebels(War war, WarExtension.WarExtraData data, bool secession)
    {
        Realm main = war.GetMainAttacker();
        Empire home = war.GetMainDefender()?.GetEmpire();
        foreach (Realm kingdom in war.GetAttackers())
        {
            Person ruler = kingdom?.Record == null || kingdom.IsGone() ? null : kingdom.Ruler;
            if (!Living(ruler)) continue;
            bool own = kingdom == main || home != null && !home.IsGone() &&
                       (kingdom.GetEmpire() == home || kingdom.GetOrCreate().left_empire_id == home.Key);
            if (!own) continue;
            data.defiance_noted ??= new List<long>();
            if (data.defiance_noted.Contains(ruler.Key)) continue;
            data.defiance_noted.Add(ruler.Key);
            EmpireLawSystem.NoteDefiance(ruler, secession);
        }
    }

    // 战争结束(WarPatch.end_war，各项结局结算之后)：叛乱兵败、不奉诏平定后论处首领
    public static void OnWarEnded(War war, WarOutcome winner)
    {
        if (war?.Record == null || EmpireWorld.Current?.Living == null || !EmpireLawSystem.Enabled) return;
        try
        {
            WarExtension.WarExtraData data = war.GetOrCreate();
            if (data == null) return;
            if (data.empireWarType == EmpireWarType.不奉诏)
            {
                Defiance(war, data, winner);
                return;
            }
            LawType? law = RebelLaw(war, data.empireWarType);
            if (law == null || !RebelsLost(war, winner)) return;
            long id = data.rebel_leader_id >= 0 ? data.rebel_leader_id : data.republic_revolution_leader_id;
            Person leader = id >= 0 ? EmpireWorld.Current.PersonByKey(id) : null;
            Realm loyalist = war.GetMainDefender();
            if (Captive(leader, loyalist)) Prosecute(leader, law.Value, loyalist, RebelReason(law.Value));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[律法][实事] 战后论处叛乱首领失败({war.Name}): {exception.Message}");
        }
    }

    // 守方胜；或者议和、作罢时叛军已经灭亡(没有城了)
    private static bool RebelsLost(War war, WarOutcome winner)
    {
        if (winner == WarOutcome.Defenders) return true;
        if (winner != WarOutcome.Nobody) return false;
        Realm rebel = war.GetMainAttacker();
        return rebel?.Record == null || rebel.IsGone() || !rebel.Alive || rebel.CountCities() == 0;
    }

    // 落到朝廷手里：还活着，人在胜方(或胜方所在帝国)境内，不再是一国之君(还据着一国的不算被擒)
    private static bool Captive(Person actor, Realm victor)
    {
        if (!Living(actor) || victor?.Record == null || victor.IsGone()) return false;
        Realm held = actor.Realm;
        return held?.Record != null && held.Ruler != actor && Within(held, victor);
    }

    // 不奉诏：朝廷(攻方)获胜、改立了新君，被废的抗命嗣君以谋反论处
    private static void Defiance(War war, WarExtension.WarExtraData data, WarOutcome winner)
    {
        if (winner != WarOutcome.Attackers || data.investiture_heir_id < 0) return;
        Person heir = EmpireWorld.Current.PersonByKey(data.investiture_heir_id);
        Realm core = war.GetMainAttacker();
        if (Captive(heir, core)) Prosecute(heir, LawType.谋反, core, "law_reason_defiance_crushed");
    }

    // 宫门之变、诸王争位兵败(PrinceStruggleSystem)：以篡位论处；之后照旧伏诛(已被律法处死的不再杀)
    public static void FailedUsurper(Person prince, Realm core, string reasonKey)
    {
        if (!EmpireLawSystem.Enabled) return;
        Prosecute(prince, LawType.篡位, core, reasonKey);
    }

    #endregion

    #region 败将

    private static bool MilitaryCaseDue(Realm kingdom)
    {
        if (EmpireWorld.Current == null) return false;
        double now = GameClock.Now;
        return !LastMilitaryCase.TryGetValue(kingdom.Key, out double at) || now < at ||
               GameClock.YearsSince(at) >= MilitaryCooldownYears;
    }

    private static void MarkMilitaryCase(Realm kingdom)
    {
        if (EmpireWorld.Current != null) LastMilitaryCase[kingdom.Key] = GameClock.Now;
    }

    // 可以追究的败将：本国大军(不是义军、骑士团)的统领，不是君主、储君，本国不是正在起事的叛军，近两年没论处过败将
    private static bool Accountable(Realm kingdom, Person general, CorpsData corps) =>
        kingdom?.Record != null && !kingdom.IsGone() && Living(general) && general.Realm == kingdom && corps != null &&
        !corps.militia && corps.order_id < 0 && corps.full >= BigCorps && !general.IsRuler() &&
        !(kingdom.HasHeir() && kingdom.GetHeir() == general) && !CorpsSystem.Insurgent(kingdom) && MilitaryCaseDue(kingdom);

    // 战场结算(BattleDrama.Settle)：大败又溃散，统领没阵亡、没被俘、没有突围之功
    public static void Routed(Realm kingdom, Person general, CorpsData corps, bool attacking)
    {
        if (EcRandom.value >= RoutChance || !EmpireLawSystem.Enabled || !Accountable(kingdom, general, corps)) return;
        bool lost = corps.strength < 1f;
        LawType law = lost || !attacking ? LawType.临阵脱逃 : LawType.违抗军令;
        string reason = lost ? "law_reason_army_lost" : attacking ? "law_reason_rash_defeat" : "law_reason_army_routed";
        if (Prosecute(general, law, kingdom, reason)) MarkMilitaryCase(kingdom);
    }

    // 城破(BattleDrama.CityFell)：守城统领没殉国、没被俘，趁乱出走
    public static void CityFled(Realm kingdom, Person general, CorpsData corps)
    {
        if (EcRandom.value >= CityFledChance || !EmpireLawSystem.Enabled || !Accountable(kingdom, general, corps)) return;
        if (Prosecute(general, LawType.临阵脱逃, kingdom, "law_reason_city_abandoned")) MarkMilitaryCase(kingdom);
    }

    #endregion

    #region 纵兵烧杀

    // 城破时纵兵烧杀(ScorchedEarthSystem.OnCityCaptured)：攻城的统领担这一笔
    public static void Sacked(City city, Realm victim, Realm conqueror)
    {
        if (city?.Record == null || victim?.Record == null || conqueror?.Record == null || !EmpireLawSystem.Enabled) return;
        try
        {
            Person general = SackCommander(city, conqueror);
            if (!Living(general) || general.IsRuler() || general.Realm != conqueror) return;
            Empire empire = conqueror.GetEmpire();
            // 烧杀的是本帝国的城(或正从本帝国叛出的城)：朝廷当场追究；外国的城只暗中记一笔
            bool own = empire != null && !empire.IsGone() &&
                       (victim.GetEmpire() == empire ||
                        (victim.IsLocalRebelling() || victim.IsFactionRebelling()) && victim.RebellionOriginEmpire() == empire);
            if (own)
            {
                if (EcRandom.value < SackChance)
                    Prosecute(general, LawType.抢掠平民, empire.CoreKingdom ?? conqueror, "law_reason_sack");
            }
            else if (EcRandom.value < SackRecordChance) Record(general, LawType.抢掠平民);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[律法][实事] 烧杀论处失败({city.Name}): {exception.Message}");
        }
    }

    // 攻下这座城的统领：本国打这座城的各军里兵力最多、有统领实体的那支
    private static Person SackCommander(City city, Realm conqueror)
    {
        Person best = null;
        float most = 0f;
        foreach (CorpsData corps in CorpsSystem.Of(conqueror))
        {
            if (corps == null || corps.sector_city_id != city.Key || corps.general_id < 0 || corps.strength <= most) continue;
            Person general = EmpireWorld.Current.PersonByKey(corps.general_id);
            if (!Living(general)) continue;
            best = general;
            most = corps.strength;
        }
        return best;
    }

    #endregion

    #region 赈灾克扣

    // 灾城的主官：城的官职(州郡长官)在任者，没有就是城主
    private static Person Steward(City city)
    {
        Person officer = city?.GetOffice()?.GetActor();
        if (Living(officer) && !officer.IsRuler()) return officer;
        Person leader = city?.Leader;
        return Living(leader) && !leader.IsRuler() ? leader : null;
    }

    // 朝议"赈济灾荒"发粮(TempFac_赈济灾荒)：到手不足 SkimLine 时，灾城主官按克扣的比例暗中记贪污
    public static void ReliefSkimmed(IEnumerable<City> cities, float efficiency)
    {
        if (cities == null || efficiency >= SkimLine || !EmpireLawSystem.Enabled) return;
        float chance = (1f - efficiency) * SkimFactor;
        int recorded = 0;
        foreach (City city in cities.ToList())
        {
            if (recorded >= SkimMax) break;
            if (city?.Record == null || city.IsGone() || EcRandom.value >= chance) continue;
            if (Record(Steward(city), LawType.贪污)) recorded++;
        }
    }

    #endregion

    #region 迫害异教

    // 迫害(ReligiousPersecution.Persecute)：没有改宗的各城，城主仍奉被禁之教的，以宣扬异端论处
    public static void HereticLeaders(Realm realm, Religion faith, IEnumerable<City> cities)
    {
        if (realm?.Record == null || faith == null || cities == null || !EmpireLawSystem.Enabled) return;
        int charged = 0;
        foreach (City city in cities.ToList())
        {
            if (charged >= PersecutionMax) break;
            Person leader = city?.Record == null || city.IsGone() ? null : city.Leader;
            if (!Living(leader) || leader.IsRuler() || leader.Religion != faith || !Within(leader.Realm, realm)) continue;
            if (EcRandom.value >= PersecutionChance) continue;
            if (Prosecute(leader, LawType.宣扬异端, realm, "law_reason_persecution")) charged++;
        }
    }

    #endregion

    #region 个人恩怨、兼并

    // 构陷宿敌去职得逞(PersonalTieSystem.Frame)
    public static void Framed(Person actor)
    {
        if (EmpireLawSystem.Enabled) Record(actor, LawType.陷害);
    }

    // 遣刺客得手(PersonalTieSystem.Revenge，在行刺之前判定)：victimKingdom 是死者所在的国，同国或同一帝国才记；
    // 刺的是本国君主记谋反
    public static void Assassinated(Person actor, Realm victimKingdom, bool regicide)
    {
        if (!EmpireLawSystem.Enabled || !Living(actor) || !Within(victimKingdom, actor.Realm)) return;
        Record(actor, regicide ? LawType.谋反 : LawType.杀人);
    }

    // 世家兼并郡望之田(GentrySystem.Annex 实际占到了地)：在任官员、城主的族人仗势占田，暗中记非法侵占土地
    public static void ClanAnnexed(SpecificClan clan, City seat)
    {
        if (clan == null || seat?.Realm == null || EcRandom.value >= AnnexChance || !EmpireLawSystem.Enabled) return;
        Person culprit = clan.LivingPeople?
            .Where(person => person != null && !person.is_virtual)
            .Select(person => person._actor)
            .Where(actor => Living(actor) && actor.IsAdult() && !actor.IsRuler() && (actor.IsOnOffice() || actor.IsCityLeader()) &&
                            Within(actor.Realm, seat.Realm))
            .OrderByDescending(actor => actor.City == seat)
            .FirstOrDefault();
        Record(culprit, LawType.非法侵占土地);
    }

    #endregion
}
}
