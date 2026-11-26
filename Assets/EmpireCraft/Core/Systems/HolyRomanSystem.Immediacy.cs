// Generated from the EmpireCraft mod: Scripts/GeneralSystems/HolyRomanSystem.Immediacy.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 陪臣化与帝国直属(封建制帝国，见 HasDiet；原型 1803 年以前零星的陪臣化与皇帝对帝国直属小邦的保护)：
//   · 请愿：大诸侯(四城以上或国力前三，非被禁、非自由城市、非主教领，中央也算)请求并吞与之接壤的一城小邦(普通诸侯，
//     非中央、非选帝侯国；被禁的也可以)；每年 8% × (1.5 − 权威/100)，每帝国三年至多一次；请愿挂一年，下次年结裁处；
//   · 帝国直属：裁处之前皇帝可花权威 10 驳回。AI：请愿者不是皇族、权威 40 以上、小邦对皇帝好感不坏时七成驳回；
//     请愿者是皇族不驳回(议会里中央投赞成)；神力按钮"维护"照扣到 0；
//   · 路线：已颁行帝国改革且请愿者对那座城有法理——走帝国法院(判给几率同诉讼另 +0.1)；否则付帝国议会(议程 mediatize：
//     一两城的小诸侯 −0.4、自由城市 −0.2、大诸侯 +0.2、与请愿者同族 +0.5、与小邦同族 −0.5)；
//   · 陪臣化：小邦的城并入请愿者，原君主留任该城城主(陪臣)；权威 −3(皇族得益则不扣)；
//   · 典押留底与赎回：典押自由城市(见 Members)记下押价与受押国；三十年内中央国库有押价×1.2 的三倍、权威 20 以上时，
//     每年三成赎回——付押价×1.2 给受押国，城重新立为自由城市，权威 +3；受押国已亡、城已不在帝国则作废；
//     满三十年未赎转为陪臣化。旧档之前典押掉的城没有记录，不能赎回。
public static partial class HolyRomanSystem
{
    public const string AgendaMediatize = "mediatize";
    public const int VetoThreshold = 10, VetoCost = 10;
    private const float VetoChance = 0.7f, MediatizeCourtBonus = 0.1f, RedeemChance = 0.3f;
    private const float RedeemPremium = 1.2f, RedeemReserve = 3f;
    private const int PetitionGapYears = 3, BigPrinceCities = 4, BigPrinceRank = 3, VetoAiAuthority = 40;
    private const int AuthMediatized = -3, AuthRedeemed = 3, PawnYearsLimit = 30, RedeemAuthority = 20, MediatizationLogSize = 10;
    public const string RouteDiet = "diet", RouteCourt = "court", RoutePawn = "pawn", RouteVeto = "veto";

#if EC_PENDING
    private static void ImmediacyYearly(Empire empire, HolyRomanState state)
    {
        state.pawns ??= new List<PawnRecord>();
        state.mediatizations ??= new List<MediatizationRecord>();
        PawnsYearly(empire, state);
        if (state.petition_at >= 0d) ResolvePetition(empire, state);
        else TryPetition(empire, state);
    }
#endif

    #region 请愿与裁处

    // 大诸侯：四城以上或国力前三
    private static List<Realm> BigPrinces(Empire empire)
    {
        List<Realm> realms = empire.kingdoms_list.Where(kingdom => kingdom?.Record != null && !kingdom.IsGone() &&
                                                                     kingdom.GetEmpire() == empire)
            .OrderByDescending(kingdom => kingdom.GetNationalPower()).ToList();
        return realms.Where((kingdom, rank) => (kingdom.Cities.Count >= BigPrinceCities || rank < BigPrinceRank) &&
                                               Estate(kingdom) == EstateNone && !IsBanned(kingdom) && kingdom.HasRuler() &&
                                               !Rebelling(kingdom)).ToList();
    }

    // 请愿对象：接壤的一城小邦(普通诸侯，非中央、非选帝侯国)
    private static Realm SmallNeighbour(Empire empire, HolyRomanState state, Realm lord) =>
        Members(empire).Where(small => small != lord && Estate(small) == EstateNone && small.Cities.Count == 1 &&
                                       small.Capital != null && !Seats(state).Contains(small.Key) && small.IsNeighbourWith(lord) &&
                                       !AtWar(small) && !small.IsAtWarWith(lord))
            .OrderBy(small => small.GetNationalPower()).FirstOrDefault();

    private static void TryPetition(Empire empire, HolyRomanState state)
    {
        if (state.last_petition_at >= 0d && GameClock.YearsSince(state.last_petition_at) < PetitionGapYears) return;
        if (EcRandom.value >= ImperialAuthorityRules.PetitionChance(Authority(empire))) return;
        foreach (Realm lord in BigPrinces(empire).OrderBy(_ => EcRandom.value))
        {
            if (AtWar(lord)) continue;
            Realm small = SmallNeighbour(empire, state, lord);
            if (small == null) continue;
            state.petition_lord_id = lord.Key;
            state.petition_small_id = small.Key;
            state.petition_at = state.last_petition_at = GameClock.Now;
            Record(empire, string.Format(LM.Get("hre_mediatize_petition_history"), lord.GetKingdomName(), small.GetKingdomName()));
            LogService.LogInfo($"[EmpireCraft][神罗] {empire.GetEmpireFullName()} 陪臣化请愿：{lord.GetKingdomName()}请并{small.GetKingdomName()}，" +
                               $"权威{Authority(empire)}");
            return;
        }
    }

    public static (Realm lord, Realm small) PendingPetition(Empire empire)
    {
        HolyRomanState state = State(empire);
        if (state == null || state.petition_at < 0d) return (null, null);
        Realm lord = LiveKingdom(state.petition_lord_id), small = LiveKingdom(state.petition_small_id);
        bool valid = lord != null && small != null && lord.GetEmpire() == empire && small.GetEmpire() == empire &&
                     small != empire.CoreKingdom && small.Cities.Count == 1;
        return valid ? (lord, small) : (null, null);
    }

    private static void ClearPetition(HolyRomanState state)
    {
        state.petition_lord_id = state.petition_small_id = -1L;
        state.petition_at = -1d;
    }

    // 裁处(请愿后的下一次年结)：皇帝驳回 → 法院 → 议会
#if EC_PENDING
    private static void ResolvePetition(Empire empire, HolyRomanState state)
    {
        (Realm lord, Realm small) = PendingPetition(empire);
        if (lord == null)
        {
            ClearPetition(state);
            return;
        }
        Realm core = empire.CoreKingdom;
        bool imperial = ImperialHouse(empire, lord);
        int authority = Authority(empire);
        if (!imperial && authority >= VetoAiAuthority && Opinion(small, core) >= 0 && EcRandom.value < VetoChance)
        {
            Veto(empire, state, lord, small);
            return;
        }
        if (ReformEnacted(empire) && ClaimsCity(lord, lord.GetRealmTitleIds(), small.Capital))
        {
            ClearPetition(state);
            float grant = ImperialAuthorityRules.CourtGrantChance(IsElectorRealm(empire, lord), false, imperial,
                ImperialHouse(empire, small), authority, false, MediatizeCourtBonus);
            if (EcRandom.value < grant) Mediatize(empire, state, lord, small, RouteCourt);
            else
            {
                NoteMediatization(state, small.GetKingdomName(), lord.GetKingdomName(), RouteCourt, blocked: true);
                Record(empire, string.Format(LM.Get("hre_mediatize_court_refused_history"), lord.GetKingdomName(), small.GetKingdomName()));
            }
            return;
        }
        // 议会：表决时要知道请愿者，付议后再清
        DietRecord record = Convene(empire, AgendaMediatize, small);
        if (record != null && !record.passed)
            NoteMediatization(state, small.GetKingdomName(), lord.GetKingdomName(), RouteDiet, blocked: true);
        ClearPetition(state);
    }
#endif

    // 维护帝国直属：花权威 10 驳回请愿
    private static void Veto(Empire empire, HolyRomanState state, Realm lord, Realm small)
    {
        ClearPetition(state);
        Spend(empire, VetoCost, "hre_auth_immediacy", small.GetKingdomName());
        NoteMediatization(state, small.GetKingdomName(), lord.GetKingdomName(), RouteVeto, blocked: true);
        Record(empire, string.Format(LM.Get("hre_immediacy_history"), empire.Emperor?.FullName() ?? empire.CoreKingdom.GetKingdomName(),
            small.GetKingdomName(), lord.GetKingdomName()));
        FiscalDiagnostics.Count("神罗·直属驳回");
    }

    // 陪臣化：小邦的城并入请愿者，原君主留任城主；返回议会纪事用的结果
    private static string Mediatize(Empire empire, HolyRomanState state, Realm lord, Realm small, string route)
    {
        string smallName = small.GetKingdomName(), lordName = lord.GetKingdomName();
        Person ruler = small.Ruler;
        City seat = small.Capital;
        foreach (City city in small.Cities.ToList())
            if (city?.Record != null && !city.IsGone() && city.Realm == small) city.JoinRealm(lord);
        if (seat?.Realm != lord) return LM.Get("hre_mediatize_failed");
        try
        {
            if (ruler?.Record != null && ruler.Alive && !ruler.IsGone() && !ruler.IsRuler() && ruler.Realm == lord)
            {
                if (ruler.City != seat) ruler.JoinCity(seat);
                seat.SetLeader(ruler, true);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][神罗] 陪臣留任城主失败: {exception.Message}");
        }
        if (!ImperialHouse(empire, lord)) ChangeAuthority(empire, AuthMediatized, "hre_auth_mediatized", smallName, lordName);
        string text = string.Format(LM.Get("hre_mediatized_history"), smallName, lordName, ruler?.FullName() ?? "");
        ruler?.RecordPersonalHistory(text);
        Record(empire, text);
        NoteMediatization(state, smallName, lordName, route, blocked: false);
        FiscalDiagnostics.Count("神罗·陪臣化");
        LogService.LogInfo($"[EmpireCraft][神罗] {empire.GetEmpireFullName()} 陪臣化({route})：{text}");
        return string.Format(LM.Get("hre_diet_mediatize_outcome"), smallName, lordName);
    }

    private static void NoteMediatization(HolyRomanState state, string small, string lord, string route, bool blocked)
    {
        state.mediatizations ??= new List<MediatizationRecord>();
        state.mediatizations.Add(new MediatizationRecord
        {
            at = GameClock.Now, small = small, lord = lord, route = route, blocked = blocked
        });
        while (state.mediatizations.Count > MediatizationLogSize) state.mediatizations.RemoveAt(0);
    }

    // 议会议陪臣化的立场(Diet.Stance 调用)
    private static float MediatizeStance(Empire empire, HolyRomanState state, DietVoter voter, Realm small)
    {
        Realm realm = voter.realm, lord = LiveKingdom(state.petition_lord_id);
        if (realm == small) return -1f;
        if (realm == lord) return 1f;
        // 中央：请愿者是皇族就赞成，否则维护帝国直属
        if (realm == empire.CoreKingdom) return lord != null && ImperialHouse(empire, lord) ? 1f : -1f;
        float score = 0f;
        if (voter.college == CollegeCities) score -= 0.2f;
        else if (realm.Cities.Count <= 2) score -= 0.4f;
        else if (realm.Cities.Count >= BigPrinceCities) score += 0.2f;
        if (lord != null && SameHouse(realm.Ruler, lord.Ruler)) score += 0.5f;
        if (SameHouse(realm.Ruler, small.Ruler)) score -= 0.5f;
        return score;
    }

    #endregion

    #region 典押

    // 典押留底(PawnFreeCity 调用)
    private static void NotePawn(HolyRomanState state, City city, Realm holder, int price)
    {
        state.pawns ??= new List<PawnRecord>();
        state.pawns.Add(new PawnRecord
        {
            city_id = city.Key, city_name = city.GetCityName(), holder_id = holder.Key, holder_name = holder.GetKingdomName(),
            price = price, at = GameClock.Now
        });
    }

    private static long RedeemPrice(PawnRecord record) => (long)(record.price * RedeemPremium);

    private static void PawnsYearly(Empire empire, HolyRomanState state)
    {
        Realm core = empire.CoreKingdom;
        foreach (PawnRecord record in state.pawns.ToList())
        {
            City city = EmpireWorld.Current.CityByKey(record.city_id);
            Realm holder = LiveKingdom(record.holder_id);
            if (city?.Record == null || city.IsGone() || holder == null || holder.GetEmpire() != empire || city.Realm != holder)
            {
                state.pawns.Remove(record);
                LogService.LogInfo($"[EmpireCraft][神罗] {empire.GetEmpireFullName()} 典押作废：{record.city_name}(受押{record.holder_name})");
                continue;
            }
            if (GameClock.YearsSince(record.at) >= PawnYearsLimit)
            {
                state.pawns.Remove(record);
                NoteMediatization(state, record.city_name, record.holder_name, RoutePawn, blocked: false);
                Record(empire, string.Format(LM.Get("hre_pawn_lapsed_history"), record.city_name, record.holder_name));
                continue;
            }
            long price = RedeemPrice(record);
            if (city != holder.Capital && core.GetMoney() >= price * RedeemReserve && Authority(empire) >= RedeemAuthority &&
                !Interregnum(empire) && EcRandom.value < RedeemChance)
                Redeem(empire, state, record, city, holder);
        }
    }

    // 赎回：付押价×1.2 给受押国，城重新立为自由城市
    private static bool Redeem(Empire empire, HolyRomanState state, PawnRecord record, City city, Realm holder)
    {
        Realm core = empire.CoreKingdom;
        long price = RedeemPrice(record);
        if (city == holder.Capital || core.GetMoney() < price) return false;
        Person leader = Burgher(city, holder.Ruler);
        if (leader == null) return false;
        Realm free = SpinOff(empire, city, leader, LeaderSelectMethod.Vote, EstateFreeCity);
        if (free == null) return false;
        core.SubMoney((int)price, TreasuryCategory.Other);
        holder.AddMoney((int)price, TreasuryCategory.Other);
        state.pawns.Remove(record);
        ChangeAuthority(empire, AuthRedeemed, "hre_auth_redeemed", record.city_name);
        Record(empire, string.Format(LM.Get("hre_pawn_redeemed_history"), record.city_name, record.holder_name, MoneyDisplay.Format(price)));
        FiscalDiagnostics.Count("神罗·赎回典押");
        return true;
    }

    #endregion

    #region 上帝视角

    // 维护帝国直属：驳回待决的请愿
    public static bool ActDefendImmediacy(Empire empire)
    {
        (Realm lord, Realm small) = PendingPetition(empire);
        if (lord == null) return false;
        Veto(empire, State(empire), lord, small);
        return true;
    }

    // 赎回最早一笔付得起的典押
    public static bool ActRedeem(Empire empire)
    {
        HolyRomanState state = State(empire);
        if (state?.pawns == null || !HasDiet(empire)) return false;
        foreach (PawnRecord record in state.pawns.ToList())
        {
            City city = EmpireWorld.Current.CityByKey(record.city_id);
            Realm holder = LiveKingdom(record.holder_id);
            if (city?.Record == null || holder == null || city.Realm != holder || holder.GetEmpire() != empire) continue;
            if (Redeem(empire, state, record, city, holder)) return true;
        }
        return false;
    }

    #endregion

    // 年度诊断的帝国直属一段
    private static string ImmediacyStatus(Empire empire, HolyRomanState state)
    {
        (Realm lord, Realm small) = PendingPetition(empire);
        int done = state.mediatizations?.Count(record => !record.blocked) ?? 0, blocked = state.mediatizations?.Count(record => record.blocked) ?? 0;
        string pending = lord != null ? $"待决：{lord.GetKingdomName()}请并{small.GetKingdomName()}" :
            state.last_petition_at >= 0d && GameClock.YearsSince(state.last_petition_at) < PetitionGapYears
                ? $"距上次请愿{GameClock.YearsSince(state.last_petition_at)}年(要满{PetitionGapYears}年)"
                : $"每年请愿几率{ImperialAuthorityRules.PetitionChance(Authority(empire)):P1}";
        return $"{pending}；近来陪臣化{done}起、受阻{blocked}起；典押中{state.pawns?.Count ?? 0}城";
    }
}
}
