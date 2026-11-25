// Generated from the EmpireCraft mod: Scripts/GeneralSystems/PapacySystem.Events.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 教廷的大事件(每年随教权与帝国结算)：
//   · 教廷之囚：圣地落在世俗强国(帝国中央或成员)手里。挟持者的君主由教宗加冕(教廷在本帝国，见 Coronation)；
//     与挟持者交战的帝国每年按 教权/100 的概率遭绝罚(教宗受挟)；被挟持满 20 年出现对立教宗(大分裂，真有一位对立教宗与两边服从，
//     见 PapacySystem.Antipope)，大分裂期间教权目标再 -20；圣地脱离挟持即告结束(大分裂另由公会议收场)；
//   · 宗教改革脱离罗马(君主率国改宗，用户 2026-11-06 选"更合史实的宗教改革"后不再是唯一的路，见 PapacySystem.Reformation)：
//     文化施行了宗教改革的西方帝国(教廷所在的、皇帝仍奉旧教的选帝制帝国除外)每年 RealmBreakChance + 境内新教之城占比 ×RealmShareFactor
//     机会脱离——境内某支新教会已占 AdoptShare 以上就改奉那支(君主、中央、都城改奉，成员国与各城随之慢慢改)，否则以皇帝为首另立国教会
//     (新宗教，圣地在帝都；帝国各国、各城、城里的人当场改奉)；没施行的，境内新教之城过半也有 PeopleBreakChance。两条路都没收教产
//     (两年经常收入)；母宗教教权 -8(改奉现成新教会 -6)。以前本文化一施行宗教改革，头一年就整个帝国改宗。国教会记下母宗教，
//     此后 30 年内母宗教的教廷可号召讨伐异端(教权 40 以上)；讨伐异端的圣战打完(不论胜负)即"教随国定"，母宗教 50 年内不再讨伐异端；
//   · 骑士团国世俗化：骑士团国所在文化施行了宗教改革或世俗化，大团长改为世袭公国，骑士团解散；
//   · 圣座帝国：教廷(教皇国)自己做了帝国中央——国号改为"某某圣座"，正统按教权计((教权-50)/2)，不需要加冕。
public static partial class PapacySystem
{
    private const int SchismYears = 20;
    private const float SchismPenalty = 20f;
    private const int HereticWindowYears = 30;
    private const int ReligiousPeaceYears = 50;
    private const float ChurchLandsYears = 2f;

#if EC_PENDING
    public static bool IsHolySee(Empire empire)
    {
        Religion religion = ReligionOf(empire);
        Realm papacy = Papacy(religion);
        return papacy != null && empire?.CoreKingdom == papacy && papacy.GetKingdomType() == KingdomType.Feudalism_papal_state;
    }
#endif

    // 每年随 UpdateReligion：教廷之囚的起止、大分裂(对立教宗)起头。
    // 对立教宗：教廷之囚满 SchismYears 年，或教宗正统低于 PopeLegitimacy.AntipopeBelow 时每年 AntipopeChance，扶立得成才起
    // (见 PapacySystem.Antipope.StartSchism)；站队、对立一线推举、公会议弥合与分立按年另结(SchismYearly)
#if EC_PENDING
    private static void UpdateCaptivity(Religion religion)
    {
        var data = religion.GetOrCreate();
        double now = GameClock.Now;
        bool captive = IsCaptive(religion);
        if (!captive && data.CaptiveSince >= 0d) data.CaptiveSince = -1d;
        else if (captive && data.CaptiveSince < 0d)
        {
            data.CaptiveSince = now;
            Realm captor = ModRealm(Papacy(religion));
            string text = string.Format(LM.Get("papal_captivity_log"), religion.Name, captor?.GetKingdomName() ?? "");
            TranslateHelper.LogEventMessage(text, captor);
            ReligionChronicle.Record(religion, ReligionChronicle.KindPapacy, text);
            return;
        }
        // 大分裂进行中：收场按年另结(见 SchismYearly)
        if (data.Schism) return;
        bool papal = PopeLegitimacy.Applies(religion);
        bool longCaptive = captive && now >= data.CaptiveSince && GameClock.YearsSince(data.CaptiveSince) >= SchismYears;
        bool weakPope = papal && PopeLegitimacy.Get(religion) < PopeLegitimacy.AntipopeBelow &&
                        EcRandom.value < PopeLegitimacy.AntipopeChance;
        if (!longCaptive && !weakPope) return;
        StartSchism(religion, longCaptive ? SchismByCaptivity : SchismByWeakPope);
    }
#endif

    // 教宗受挟：与挟持者交战的帝国遭绝罚
#if EC_PENDING
    private static bool CaptorBan(Empire empire, PapalRelationState state, Religion religion)
    {
        if (!IsCaptive(religion)) return false;
        Realm captor = ModRealm(Papacy(religion));
        if (captor == null || captor == empire.CoreKingdom || !empire.CoreKingdom.IsAtWarWith(captor) ||
            EcRandom.value >= Authority(religion) / 100f) return false;
        Excommunicate(empire, state, "papal_ban_reason_captor");
        return true;
    }
#endif

    // 宗教改革脱离罗马(君主率国改宗)：改奉境内已占上风的新教会，或另立国教会
#if EC_PENDING
    private static void TryBreakWithRome(Empire empire)
    {
        if (!IsWestern(empire) || HolyRomanSystem.KeepsOldFaith(empire)) return;
        PapalRelationState state = State(empire);
        Religion old = ReligionOf(empire);
        if (state == null || old == null || state.national_church_id >= 0 || IsReformedChurch(old)) return;
        bool enacted = InstitutionSystem.IsEnacted(empire, NodeReformation);
        (Religion church, float share) = ReformedShare(empire, old);
        float chance = enacted ? RealmBreakChance + share * RealmShareFactor : share >= 0.5f ? PeopleBreakChance : 0f;
        if (chance <= 0f || EcRandom.value >= chance) return;
        if (church != null && share >= AdoptShare) AdoptRealmReformation(empire, church, old);
        else if (enacted) BreakWithRome(empire);
    }
#endif

    // 另立国教会：本文化施行宗教改革后君主率国脱离、境内又没有占上风的新教会时(TryBreakWithRome)
#if EC_PENDING
    public static bool BreakWithRome(Empire empire)
    {
        // 选帝制帝国的皇帝不随宗教改革脱离罗马，由诸侯另立新教会(见 HolyRomanSystem.SeedReformation)
        if (!IsWestern(empire) || HolyRomanSystem.KeepsOldFaith(empire)) return false;
        PapalRelationState state = State(empire);
        Religion old = ReligionOf(empire);
        if (state == null || state.national_church_id >= 0 || old == null) return false;
        Realm papacy = Papacy(old);
        if (papacy == null || ModRealm(papacy) == empire.CoreKingdom || papacy.GetEmpire() == empire) return false;
        Person head = empire.Emperor;
        City capital = empire.CoreKingdom.Capital;
        if (head == null || capital == null) return false;
        head.JoinCity(capital);
        Religion church = EmpireWorld.Current.Religions.NewReligion(head, false);
        if (church?.data == null) return false;
        church.data.name = string.Format(LM.Get("national_church_name"), empire.GetEmpireName());
        church.SetCity(capital);
        var churchData = church.GetOrCreate();
        churchData.ParentReligionId = old.data.id;
        churchData.SplitAt = GameClock.Now;
        churchData.SplitOrigin = new SplitOriginData
        {
            kind = OriginNational, founder_id = head.Key, founder_name = head.FullName(), city_id = capital.Key,
            city_name = capital.GetCityName(), at = churchData.SplitAt
        };
        state.national_church_id = church.data.id;
        foreach (Realm kingdom in empire.kingdoms_list.ToList())
        {
            if (kingdom?.Record == null || kingdom.IsGone()) continue;
            kingdom.SetReligion(church);
            foreach (City city in kingdom.Cities.ToList())
            {
                if (city?.Record == null || city.IsGone()) continue;
                city.SetReligion(church);
                foreach (Person actor in city.People.ToList())
                    if (actor?.Record != null && !actor.IsGone() && actor.Religion == old) actor.SetReligion(church);
            }
        }
        empire.Religion = church;
        long lands = (long)(StateDebtSystem.AnnualStableIncome(empire.CoreKingdom) * ChurchLandsYears);
        if (lands > 0) empire.CoreKingdom.AddMoney((int)Math.Min(int.MaxValue, lands), TreasuryCategory.RecoveredFunds);
        AddAuthority(old, -8f);
        string text = string.Format(LM.Get("papal_reformation_history"), empire.GetEmpireFullName(), old.Name, church.Name,
            MoneyDisplay.Format(lands));
        Record(empire, text);
        ReligionChronicle.Clear(church);
        ReligionChronicle.Record(old, church, ReligionChronicle.KindSchism, text);
        return true;
    }
#endif

    // 讨伐异端的目标：从本宗教分出去、30 年内、不在"教随国定"期间
#if EC_PENDING
    private static Realm HereticTarget(Religion religion)
    {
        var data = religion.GetOrCreate();
        double now = GameClock.Now;
        if (now < data.ReligiousPeaceUntil) return null;
        foreach (Religion other in EmpireWorld.Current.Religions)
        {
            if (other?.data == null || other == religion) continue;
            var split = other.GetOrCreate();
            if (split.ParentReligionId != religion.data.id || split.SplitAt < 0d ||
                GameClock.YearsSince(split.SplitAt) > HereticWindowYears) continue;
            // 只讨伐整个政权改宗的(国教会)；帝国诸侯另立的新教会归帝国内的宗教战争(见 HolyRomanSystem.Leagues)，
            // 不然教廷会去讨伐仍奉旧教的皇帝
            Realm head = Papacy(other);
            if (head != null && ModRealm(head).Religion == other) return ModRealm(head);
        }
        return null;
    }
#endif

    // 讨伐异端的圣战打完、帝国诸侯订立和约：教随国定
#if EC_PENDING
    public static void ReligiousPeace(Religion religion)
    {
        religion.GetOrCreate().ReligiousPeaceUntil = GameClock.Now + ReligiousPeaceYears * SecondsPerYear;
        TranslateHelper.LogEventMessage(string.Format(LM.Get("papal_religious_peace_log"), religion.Name), Papacy(religion));
    }
#endif

    // 骑士团国世俗化
#if EC_PENDING
    private static bool SecularizeOrderState(KnightOrderData order)
    {
        Realm state = order.state_kingdom_id >= 0 ? EmpireWorld.Current.RealmByKey(order.state_kingdom_id) : null;
        if (state?.Record == null || state.IsGone()) return false;
        string culture = CultureService.GetInstitutionCulture(state);
        if (!InstitutionSystem.IsEnacted(culture, NodeReformation) && !InstitutionSystem.IsEnacted(culture, NodeSecularization))
            return false;
        string name = string.Format(LM.Get("order_duchy_name"), state.Capital?.GetCityName() ?? state.GetKingdomName());
        state.Name = name;
        order.dissolved = true;
        TranslateHelper.LogEventMessage(string.Format(LM.Get("order_secularized_log"), order.name, name), state);
        return true;
    }
#endif

    // 皇帝就是教宗的帝国(见 PopeIsEmperor)：天下空缺时教宗自授天下之名(RealmNamingSystem ④)；天下之名已归别家时国号改"某某圣座"，
    // 不再一直叫"教宗国"
#if EC_PENDING
    private static void NameHolySee(Empire empire, PapalRelationState state)
    {
        if (state.holy_see_named || !PopeIsEmperor(empire) || RealmNamingSystem.HoldsUniversal(empire) ||
            RealmNamingSystem.Enabled && RealmNamingSystem.Holder(empire) == null) return;
        City holy = HolyCity(ReligionOf(empire));
        if (holy == null) return;
        state.holy_see_named = true;
        empire.SetEmpireName(string.Format(LM.Get("holy_see_empire_name"), holy.GetCityName()));
        Record(empire, string.Format(LM.Get("holy_see_history"), empire.GetEmpireFullName()));
    }
#endif
}
}
