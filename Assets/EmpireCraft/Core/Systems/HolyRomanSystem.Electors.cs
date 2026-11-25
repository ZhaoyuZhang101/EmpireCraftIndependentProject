// Ported from the EmpireCraft mod: Scripts/GeneralSystems/HolyRomanSystem.Electors.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 选帝侯席位(封建制帝国，见 HasDiet)：世俗四席绑定领国(state.elector_realm_ids，-1 = 出缺)，不再每次按兵力重排；
// 教会三席照旧随主教领与宗教等级动态取(见 EmpireCraftEmpireBehCheckCabinet.SetCabinetForFeudalism)。
//   · 起算：第一次按当时在座的世俗选帝侯所在国填入(不足四家的按兵力补齐)，保持现状；
//   · 出缺：席位所在国亡了、离开帝国、成了自由城市或主教领；席位国当选成了中央照样留着(皇帝自家领国握一票)；
//   · 停权：席位国被帝国禁令放逐时不得列席、不得投票(Voters、ElectorRealms 已排除被禁者)；
//   · 褫夺：禁令执行成功后褫夺被禁者的席位，花权威 10；权威不到 10 就只停权、禁令解除后照旧；
//   · 授予：出缺满一年、权威 15 以上时花 15 授给亲近皇帝的诸侯——得分 = 对皇帝好感/100 + 与皇帝同族 0.5 + 皇帝党 0.2
//     + 城数名次(最大的 0.1、依次递减)；排除中央、自由城市、主教领、被禁者、已在座的；每年至多授一席；
//   · 兜底：权威不到 15、出缺满五年，按旧例补给兵力最强的普通诸侯(不花权威)，免得选帝侯越来越少；
//   · 世俗席至多四席；神力按钮"授予"(有出缺时，人选同 AI，花 15)、"褫夺"(只对被禁者，花 10)，权威不够照扣到 0。
public static partial class HolyRomanSystem
{
    public const int SecularSeats = 4, ChurchSeats = 3;
    public const int ElectorGrantThreshold = 15, ElectorGrantCost = 15, ElectorStripCost = 10;
    private const int ElectorGrantWaitYears = 1, ElectorFallbackYears = 5, ElectorLogSize = 10;

    // 能占世俗选帝侯席的诸侯：普通成员国(非自由城市、主教领、教会领)
#if EC_PENDING
    private static bool SecularSeatEligible(Empire empire, Realm kingdom) =>
        kingdom?.Record != null && !kingdom.IsGone() && kingdom.GetEmpire() == empire && Estate(kingdom) == EstateNone &&
        kingdom.GetRegime()?.GetReligionLevel() != ReligionLevel.High;
#endif

    private static List<long> Seats(HolyRomanState state) => state.elector_realm_ids ??= new List<long>();

    // 内阁维护用：世俗四席的在座者(按席位顺序；出缺、停权、无君的填 -1)
#if EC_PENDING
    public static List<long> SecularElectorKings(Empire empire)
    {
        HolyRomanState state = State(empire);
        EnsureSeats(empire, state);
        var result = new List<long>();
        foreach (long id in Seats(state))
        {
            Realm realm = id >= 0 ? EmpireWorld.Current.RealmByKey(id) : null;
            bool sitting = realm?.Record != null && !realm.IsGone() && realm.GetEmpire() == empire && !IsBanned(realm) &&
                           realm.HasRuler() && realm.Ruler?.Record != null;
            result.Add(sitting ? realm.Ruler.Key : -1L);
        }
        while (result.Count < SecularSeats) result.Add(-1L);
        return result;
    }
#endif

    // 第一次：按当时在座的世俗选帝侯所在国填入，不足的按兵力补齐(同旧例)
#if EC_PENDING
    private static void EnsureSeats(Empire empire, HolyRomanState state)
    {
        List<long> seats = Seats(state);
        if (seats.Count >= SecularSeats) return;
        if (seats.Count == 0)
            foreach (Person actor in empire.GetCabinetMembers())
            {
                Realm realm = actor?.Realm;
                if (seats.Count >= SecularSeats || realm == null || realm.Ruler != actor || realm == empire.CoreKingdom ||
                    !SecularSeatEligible(empire, realm) || seats.Contains(realm.Key)) continue;
                seats.Add(realm.Key);
            }
        foreach (Realm realm in StrongestPrinces(empire, seats))
        {
            if (seats.Count >= SecularSeats) break;
            seats.Add(realm.Key);
        }
        while (seats.Count < SecularSeats) seats.Add(-1L);
        if (seats.Contains(-1L) && state.elector_vacant_at < 0d) state.elector_vacant_at = GameClock.Now;
    }
#endif

    // 旧例：兵力最强的普通诸侯(非中央、非被禁、未在座)
#if EC_PENDING
    private static IEnumerable<Realm> StrongestPrinces(Empire empire, List<long> seated) =>
        Members(empire).Where(realm => SecularSeatEligible(empire, realm) && realm.HasRuler() && !IsBanned(realm) &&
                                       !seated.Contains(realm.Key))
            .OrderByDescending(realm => realm.countTotalWarriors());

    private static void ElectorLog(Empire empire, HolyRomanState state, string text, bool history)
    {
        state.elector_log ??= new List<string>();
        state.elector_log.Add(text);
        while (state.elector_log.Count > ElectorLogSize) state.elector_log.RemoveAt(0);
        if (history) Record(empire, text);
        LogService.LogInfo($"[EmpireCraft][神罗] {empire.GetEmpireFullName()} 选帝侯：{text}");
    }
#endif

    // 每年：出缺、授予、兜底
#if EC_PENDING
    private static void ElectorsYearly(Empire empire, HolyRomanState state)
    {
        EnsureSeats(empire, state);
        List<long> seats = Seats(state);
        double now = GameClock.Now;
        for (int i = 0; i < seats.Count; i++)
        {
            if (seats[i] < 0) continue;
            Realm realm = EmpireWorld.Current.RealmByKey(seats[i]);
            if (realm?.Record != null && !realm.IsGone() && realm.GetEmpire() == empire && Estate(realm) == EstateNone) continue;
            string name = realm?.Record != null ? realm.GetKingdomName() : "";
            seats[i] = -1L;
            ElectorLog(empire, state, string.Format(LM.Get("hre_elector_vacant_log"), name), history: false);
        }
        if (!seats.Contains(-1L))
        {
            state.elector_vacant_at = -1d;
            return;
        }
        if (state.elector_vacant_at < 0d || now < state.elector_vacant_at) state.elector_vacant_at = now;
        double years = GameClock.YearsSince(state.elector_vacant_at);
        // 帝位虚悬时不授席位
        if (years < ElectorGrantWaitYears || Interregnum(empire)) return;
        if (Authority(empire) >= ElectorGrantThreshold) GrantElector(empire, state, spend: true);
        else if (years >= ElectorFallbackYears) FallbackElector(empire, state);
    }
#endif

    // 亲近的诸侯：对皇帝好感、同族、皇帝党、城数名次
#if EC_PENDING
    public static Realm ElectorCandidate(Empire empire)
    {
        HolyRomanState state = State(empire);
        EnsureSeats(empire, state);
        Realm core = empire.CoreKingdom;
        List<Realm> pool = Members(empire).Where(realm => SecularSeatEligible(empire, realm) && realm.HasRuler() &&
                                                            !IsBanned(realm) && !Seats(state).Contains(realm.Key)).ToList();
        if (pool.Count == 0) return null;
        List<Realm> bySize = pool.OrderByDescending(realm => realm.Cities.Count).ToList();
        float Score(Realm realm)
        {
            float score = Opinion(realm, core) / 100f;
            if (SameHouse(realm.Ruler, empire.Emperor)) score += 0.5f;
            if (PapacySystem.Party(realm) == PapacySystem.PartyEmperor) score += 0.2f;
            score += 0.1f * (1f - bySize.IndexOf(realm) / (float)bySize.Count);
            return score;
        }
        return pool.OrderByDescending(Score).First();
    }
#endif

    // 授予一席(AI 与神力按钮)：花 15
#if EC_PENDING
    private static Realm GrantElector(Empire empire, HolyRomanState state, bool spend)
    {
        List<long> seats = Seats(state);
        int index = seats.IndexOf(-1L);
        Realm pick = index < 0 ? null : ElectorCandidate(empire);
        if (pick == null) return null;
        seats[index] = pick.Key;
        state.elector_vacant_at = seats.Contains(-1L) ? GameClock.Now : -1d;
        if (spend) Spend(empire, ElectorGrantCost, "hre_auth_elector_grant", pick.GetKingdomName());
        pick.Ruler?.RecordPersonalHistory(LM.Get("personal_history_became_elector"));
        FiscalDiagnostics.Count("神罗·授选帝侯");
        ElectorLog(empire, state, string.Format(LM.Get("hre_elector_granted_history"),
            empire.Emperor?.FullName() ?? empire.CoreKingdom.GetKingdomName(), pick.GetKingdomName()), history: true);
        return pick;
    }
#endif

    // 兜底：权威不足、久缺不补时按旧例给兵力最强的普通诸侯
#if EC_PENDING
    private static void FallbackElector(Empire empire, HolyRomanState state)
    {
        List<long> seats = Seats(state);
        int index = seats.IndexOf(-1L);
        Realm pick = index < 0 ? null : StrongestPrinces(empire, seats).FirstOrDefault();
        if (pick == null) return;
        seats[index] = pick.Key;
        state.elector_vacant_at = seats.Contains(-1L) ? GameClock.Now : -1d;
        FiscalDiagnostics.Count("神罗·授选帝侯");
        ElectorLog(empire, state, string.Format(LM.Get("hre_elector_fallback_history"), pick.GetKingdomName()), history: true);
    }
#endif

    // 褫夺被禁者的席位(禁令执行成功时自动；神力按钮)：花 10；auto 时权威不到 10 就只停权
#if EC_PENDING
    private static bool StripElector(Empire empire, HolyRomanState state, long realmId, bool auto)
    {
        List<long> seats = Seats(state);
        int index = realmId >= 0 ? seats.IndexOf(realmId) : -1;
        if (index < 0 || auto && Authority(empire) < ElectorStripCost) return false;
        string name = EmpireWorld.Current.RealmByKey(realmId)?.GetKingdomName() ?? state.banned_name;
        seats[index] = -1L;
        if (state.elector_vacant_at < 0d) state.elector_vacant_at = GameClock.Now;
        Spend(empire, ElectorStripCost, "hre_auth_elector_strip", name);
        FiscalDiagnostics.Count("神罗·褫夺选帝侯");
        ElectorLog(empire, state, string.Format(LM.Get("hre_elector_stripped_history"),
            empire.Emperor?.FullName() ?? empire.CoreKingdom.GetKingdomName(), name), history: true);
        return true;
    }

    public static Realm ActGrantElector(Empire empire)
    {
        if (!HasDiet(empire)) return null;
        HolyRomanState state = State(empire);
        EnsureSeats(empire, state);
        return Seats(state).Contains(-1L) ? GrantElector(empire, state, spend: true) : null;
    }

    public static bool ActStripElector(Empire empire)
    {
        if (!HasDiet(empire)) return false;
        HolyRomanState state = State(empire);
        return state.banned_kingdom_id >= 0 && StripElector(empire, state, state.banned_kingdom_id, auto: false);
    }
#endif

    // 界面用：世俗四席(席位国、在座者、状态 0 在座 / 1 停权 / 2 出缺)
#if EC_PENDING
    public static List<(Realm realm, Person holder, int status)> SecularSeatsView(Empire empire)
    {
        HolyRomanState state = State(empire);
        EnsureSeats(empire, state);
        var result = new List<(Realm, Person, int)>();
        foreach (long id in Seats(state))
        {
            Realm realm = id >= 0 ? EmpireWorld.Current.RealmByKey(id) : null;
            if (realm?.Record == null || realm.IsGone() || realm.GetEmpire() != empire)
                result.Add((null, null, 2));
            else result.Add((realm, realm.Ruler, IsBanned(realm) || !realm.HasRuler() ? 1 : 0));
        }
        return result;
    }
#endif

    // 界面用：教会席(内阁里不是世俗席在座者的人)
#if EC_PENDING
    public static List<Person> ChurchElectorsView(Empire empire)
    {
        var secular = new HashSet<long>(SecularElectorKings(empire).Where(id => id >= 0));
        return empire.GetCabinetMembers().Where(actor => actor?.Record != null && !secular.Contains(actor.Key))
            .Take(ChurchSeats).ToList();
    }

    public static bool HasVacantSeat(Empire empire)
    {
        HolyRomanState state = State(empire);
        EnsureSeats(empire, state);
        return Seats(state).Contains(-1L);
    }
#endif

    public static bool BannedHoldsSeat(Empire empire)
    {
        HolyRomanState state = State(empire);
        return state.banned_kingdom_id >= 0 && Seats(state).Contains(state.banned_kingdom_id);
    }

    // 年度诊断的选帝侯一段
#if EC_PENDING
    private static string ElectorStatus(Empire empire, HolyRomanState state)
    {
        List<(Realm realm, Person holder, int status)> seats = SecularSeatsView(empire);
        int church = ChurchElectorsView(empire).Count;
        string vacancy = state.elector_vacant_at >= 0d ? $"，出缺已{GameClock.YearsSince(state.elector_vacant_at)}年" : "";
        return $"在座{seats.Count(seat => seat.status == 0) + church}/7(教会{church}，世俗：" +
               string.Join("、", seats.Select(seat => seat.realm == null ? "出缺" : seat.realm.GetKingdomName() +
                                                                              (seat.status == 1 ? "(停权)" : ""))) + $"){vacancy}";
    }
#endif
}
}
