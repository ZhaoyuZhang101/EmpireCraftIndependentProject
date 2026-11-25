// Generated from the EmpireCraft mod: Scripts/GeneralSystems/ReligiousPersecution.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 迫害与驱逐(用户 2026-10-26 选"迫害与驱逐"：灭佛没收寺产、驱逐异教徒、强迫改宗，换钱换统一，代价是稳定、人口外逃和起事)。
// 国家每年自行决定，不经派系提议：
//   · 谁：独立王国与帝国中央(帝国按全体成员的城)，境内异教之城至少 MinCities 座、或占城数 MinShare 以上；
//     每年几率：不宽容 IntolerantChance，收人头税 PollTaxChance，宽容的只在国库吃紧(不到半年经常收入)时 PurgeChance(灭佛)；
//     冷却 CooldownYears 年；
//   · 对象：境内奉的城最多的异教；
//   · 驱逐(国教排他、不宽容时一半机会，见 ReligionFaith.IsExclusive)：该教各城 ExpelShare 的背景人口出走，送到接壤的同教外国城，
//     没有就到接壤的外国城做难民(MigrationRelations：难民城五年稳定 −6)；城改奉国教；奉该教的国家十年内对本国好感 −40；
//   · 迫害：没收寺产(每户 ConfiscatePerHousehold 入国库)；一半的城当场改奉国教，其余十年稳定少 PersecutionStability、
//     当年各有 RevoltChance 机会举教起事(仍要稳定低于起事线，见 ReligiousTension)；这些城的城主仍奉该教的，
//     有机会以宣扬异端论处(见 LawEvents.HereticLeaders)。
// 本土之教(用户 2026-11-06 "古典时代全世界都信基督"，问答全按推荐)：被迫害的是这些城所在天下的本土宗教、国教又是外天下的
// (过半的城如此)，几率 ×HomelandChanceFactor(0.3)、不驱逐、只改 HomelandConvertShare(两成)的城——
// 以前外来的排他国教平均一次改掉本土教七成半的城，皈依后四十年推行期内七成会碰上一次，十年八年就把一个文明整片改过去。
public static class ReligiousPersecution
{
    private const int MinCities = 2;
    private const float MinShare = 0.15f;
    private const float IntolerantChance = 0.03f, PollTaxChance = 0.01f, PurgeChance = 0.01f;
    private const int CooldownYears = 30;
    private const float ExpelShare = 0.2f;
    private const int ConfiscatePerHousehold = 2;
    private const float ConvertShare = 0.5f;
    private const float HomelandChanceFactor = 0.3f, HomelandConvertShare = 0.2f;
    private const float RevoltChance = 0.25f;
    public const float PersecutionStability = 8f;
    public const int PersecutionYears = 10;

    public static float StabilityLoss(City city)
    {
        if (city?.Record == null) return 0f;
        double at = city.GetOrCreate().persecuted_at;
        return at >= 0d && GameClock.Now >= at && GameClock.YearsSince(at) < PersecutionYears
            ? PersecutionStability : 0f;
    }

    private static bool Alive(Realm kingdom) => kingdom?.Record != null && !kingdom.IsGone() && !kingdom.IsWild;

    private static Realm ModRealm(Realm kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

#if EC_PENDING
    public static void YearlyPass()
    {
        if (EmpireWorld.Current?.LiveRealms == null || ModClass.IS_CLEAR) return;
        foreach (Realm realm in EmpireWorld.Current.LiveRealms.ToList())
        {
            if (!Alive(realm) || ModRealm(realm) != realm || !realm.HasReligion() ||
                EmpireCraft.Core.AncientWarfareCompatibility.Owns(realm)) continue;
            var data = realm.GetOrCreate();
            if (data.persecution_at >= 0d && GameClock.Now >= data.persecution_at &&
                GameClock.YearsSince(data.persecution_at) < CooldownYears) continue;
            List<City> all = Cities(realm);
            List<City> heterodox = all.Where(ReligiousTension.IsHeterodox).ToList();
            if (heterodox.Count == 0) continue;
            IGrouping<Religion, City> target = heterodox.GroupBy(city => city.Religion).OrderByDescending(group => group.Count()).First();
            if (target.Count() < MinCities && target.Count() < all.Count * MinShare) continue;
            ReligiousTension.Policy policy = ReligiousTension.PolicyOf(realm);
            float chance = policy switch
            {
                ReligiousTension.Policy.Intolerant => IntolerantChance,
                ReligiousTension.Policy.PollTax => PollTaxChance,
                _ => Strapped(realm) ? PurgeChance : 0f
            };
            bool homeland = Homeland(realm.Religion, target.Key, target.ToList());
            if (homeland) chance *= HomelandChanceFactor;
            if (EcRandom.value >= chance) continue;
            data.persecution_at = GameClock.Now;
            bool expel = !homeland && policy == ReligiousTension.Policy.Intolerant && ReligionFaith.IsExclusive(realm.Religion) &&
                         EcRandom.value < 0.5f;
            if (expel) Expel(realm, target.Key, target.ToList());
            else Persecute(realm, target.Key, target.ToList(), homeland ? HomelandConvertShare : ConvertShare);
        }
    }
#endif

    private static List<City> Cities(Realm realm)
    {
        Empire empire = realm.GetEmpire();
        IEnumerable<Realm> members = empire != null && empire.CoreKingdom == realm
            ? empire.kingdoms_list.Where(Alive)
            : new[] { realm };
        return members.SelectMany(member => member.Cities).Where(city => city?.Record != null && !city.IsGone()).ToList();
    }

    // 被迫害的是本土之教：过半的城，该教是城所在天下的本土宗教，国教却不是这个天下的
    private static bool Homeland(Religion state, Religion faith, List<City> cities)
    {
        if (state?.data == null || faith?.data == null || !ReligionSphere.IsNative(faith) || cities.Count == 0) return false;
        int count = 0;
        foreach (City city in cities)
        {
            string sphere = ReligionSphere.SphereOfCity(city);
            if (ReligionTenets.AtHome(faith, sphere) && !ReligionTenets.AtHome(state, sphere)) count++;
        }
        return count * 2 > cities.Count;
    }

    // 国库吃紧：不到半年经常收入
    private static bool Strapped(Realm realm) => realm.GetMoney() < StateDebtSystem.AnnualStableIncome(realm) / 2;

    // 迫害：没收寺产、一半的城改宗，其余不稳、可能起事
#if EC_PENDING
    private static void Persecute(Realm realm, Religion faith, List<City> cities, float convertShare)
    {
        long seized = 0;
        int converted = 0;
        double now = GameClock.Now;
        var kept = new List<City>();
        foreach (City city in cities)
        {
            seized += (long)CityPopulationSystem.FiscalHouseholds(city) * ConfiscatePerHousehold;
            Religion state = city.Realm.Religion;
            if (EcRandom.value < convertShare && state != null)
            {
                BackgroundReligionSystem.Convert(city, state, faith);
                converted++;
                continue;
            }
            city.GetOrCreate().persecuted_at = now;
            ReligiousTension.TryPersecutionRevolt(city, RevoltChance);
            kept.Add(city);
        }
        // 没改宗的城：城主仍奉被禁之教的，以宣扬异端论处(起事分出去的不算，见 LawEvents.HereticLeaders)
        LawEvents.HereticLeaders(realm, faith, kept);
        ReligionDiagnostics.Add(ReligionDiagnostics.Persecuted, converted);
        if (seized > 0) realm.AddMoney((int)Mathf.Min(int.MaxValue, seized), TreasuryCategory.RecoveredFunds);
        string text = string.Format(LM.Get("persecution_log"), realm.GetKingdomName(), faith.Name, MoneyDisplay.Format(seized), converted);
        TranslateHelper.LogEventMessage(text, realm);
        ReligionChronicle.Record(faith, realm.Religion, ReligionChronicle.KindConversion, text);
        LogService.LogInfo($"[EmpireCraft][宗教] {realm.GetKingdomName()} 迫害{faith.Name}：{cities.Count} 城，没收 {seized}，改宗 {converted} 城");
    }
#endif

    // 驱逐：各城两成人口出走到同教邻国(没有就做难民)，城改奉国教，同教之国结怨
    private static void Expel(Realm realm, Religion faith, List<City> cities)
    {
        bool moved = false;
        Empire empire = realm.GetEmpire();
        foreach (City city in cities)
        {
            if (CityPopulationSystem.AbstractPopulationEnabled) moved |= Emigrate(city, faith, realm, empire);
            Religion state = city.Realm.Religion;
            if (state != null) BackgroundReligionSystem.Convert(city, state, faith);
        }
        if (moved) CityPopulationSystem.InvalidateHouseholdCaches();
        ReligionDiagnostics.Add(ReligionDiagnostics.Expelled, cities.Count);
        foreach (Realm other in EmpireWorld.Current.LiveRealms.ToList())
            if (Alive(other) && other != realm && ModRealm(other) == other && other.Religion == faith)
                MigrationRelations.Expelled(other, realm);
        string text = string.Format(LM.Get("expulsion_log"), realm.GetKingdomName(), faith.Name, cities.Count);
        TranslateHelper.LogEventMessage(text, realm);
        ReligionChronicle.Record(faith, realm.Religion, ReligionChronicle.KindConversion, text);
        LogService.LogInfo($"[EmpireCraft][宗教] {realm.GetKingdomName()} 驱逐{faith.Name}信众：{cities.Count} 城");
    }

    private static bool Emigrate(City city, Religion faith, Realm realm, Empire empire)
    {
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data?.groups == null) return false;
        List<City> abroad = city.NeighbourCities?.Where(other => other?.Record != null && !other.IsGone() && other.Realm != null &&
            !other.Realm.IsWild && ModRealm(other.Realm) != realm).ToList() ?? new List<City>();
        City home = abroad.FirstOrDefault(other => other.Religion == faith);
        City refuge = home ?? abroad.FirstOrDefault();
        PopulationParallelSystem.Discard(city);
        if (refuge != null) PopulationParallelSystem.Discard(refuge);
        bool moved = false;
        foreach (PopGroup group in data.groups.Where(group => group != null && group.Background > 0f &&
                     group.social_class != SocialClass.Army && group.social_class != SocialClass.Officer).ToList())
        {
            float removed = CityPopulationSystem.RemoveBackground(group, group.Background * ExpelShare);
            if (removed <= 0f) continue;
            moved = true;
            if (refuge != null)
                CityPopulationSystem.AddBackground(refuge, group.social_class, group.culture, group.species, group.ideology, removed);
        }
        if (moved && refuge != null && home == null) MigrationRelations.TakeRefugees(refuge);
        return moved;
    }
}
}
