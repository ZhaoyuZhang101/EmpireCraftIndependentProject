// Generated from the EmpireCraft mod: Scripts/GeneralSystems/ReligionConversionSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 统治者皈依(用户 2026-10-26 选"统治者皈依"：克洛维、弗拉基米尔受洗，汗国皈依伊斯兰、佛教)。以前君主从不主动改宗，
// 国教只会被原版"国王改国教"决策顺手带走(已拦，见 ReligionPatch)：
//   · 每年，独立王国与帝国中央看周边：接壤(帝国按全体成员)、不在交战的国家所奉的世界性宗教(普世教会、伊斯兰、法教、三教，
//     见 ReligionFaith.IsWorldFaith)，按对方城数 / 本国城数(至多 MaxStrengthRatio)加权；境内奉该教的城按占比 ×2 加权；
//   · 皈依几率 = BaseChance × 权重 × 意愿(多神、萨满、无教 1，三教、法教、神道 0.15，一神教 0.03；握着本教圣地的再 ×0.1)；
//     皈依后 CooldownYears 年不再皈依；
//   · 皈依：君主、国家、都城改奉；帝国中央皈依的，帝国国教同改，成员国君主每年 MemberFollowChance 随之改宗；
//     此后 ConversionPushYears 年国教势能加倍(无小人模式见 BackgroundReligionSystem.StateFactor；有小人时每年 ManualCityChance 逐城改宗)。
//   · 入主从俗(用户 2026-10-27 选"做"，伊儿汗国改宗伊斯兰、元崇佛)：君主奉的是民族宗教(不是教会、伊斯兰、法教)，治下过半人口
//     奉另一个天下的本土大教，每年 AdoptSubjectsChance 改奉治下的大教，不看皈依冷却；握着本教圣地的不改。
//   · 别的系统借用的口子(这里的规则照旧)：宗教改革的诸侯择教、君主率国改奉新教会，东西大分裂随之改宗的帝国中央，都记下
//     religion_converted_at(见 PapacySystem.Reformation、ReligionSchismSystem)——独立王国、帝国中央因此 ConversionPushYears 年内
//     国教势能加倍(RecentlyConverted)、成员国陆续随中央(FollowRealm)、有小人时逐城改宗(ManualCityConversion)，CooldownYears 年不再皈依。
// 2026-11-06(用户"古典时代全世界都信基督"，问答全按推荐，传播"要考虑距离和地形""和通信技术挂钩")：
//   · 通达：邻国的权重 × min(1, 通达强度/0.6)(ConnectivitySystem.LearnStrength，消息口径，不算电报)；不接壤但通达在"往来"(0.25)
//     以上的(隔海、隔无人区)也算；通达层没算好时照旧只看接壤、不打折；
//   · 本土抗皈依：国教是本天下的本土宗教、国家文化也在本天下的君主，皈依外天下的宗教意愿 ×0.3(加成"中")，
//     神国之类的教义再折(ReligionTenets.Resist)；同一天下里的宗教(古神与启示宗教)照旧；
//   · 以融合御同化：本国国教从对方一系融过教义的，意愿再 ×0.85ⁿ(至多 ×0.55)；权重 ≥1 却没成的，本国国教取对方仪轨
//     (融合接触 +10，见 SyncretismSystem)，皈依后新教收旧教的本地圣徒(+20)；
//   · 本天下正教在本天下也是候选(被外来宗教皈依过去的国，在本土正教包围下可以复归)；升格的民族宗教同此；
//     两段式的启示宗教还没成正教时，对本天下奉古神的君主每年另有 realm_pull 的拉力(不必接壤，见 NativeFaithSystem)；
//   · 普世宗教君主从俗：国教是外天下的普世宗教、治下过半人口奉本天下正教的，每年 NativeReturnChance 复归(冷却照旧)；
//   · 有小人时逐城改宗也看天下：外天下宗教 × 跨天下折扣(0.25/0.05)，本城奉本天下宗教的再 ×0.5。
public static class ReligionConversionSystem
{
    private const float AdoptSubjectsChance = 0.1f;
    public const int ConversionPushYears = 40;
    private const int CooldownYears = 50;
    private const float BaseChance = 0.02f;
    private const float MaxStrengthRatio = 3f;
    private const float MinorityWeight = 2f;
    private const float HolyCityReluctance = 0.1f;
    private const float MemberFollowChance = 0.1f;
    private const float ManualCityChance = 0.05f;
    // 普世宗教君主从俗(复归本天下正教)
    private const float NativeReturnChance = 0.03f;
    // 皈依没成也算"见识过"：权重到这么多
    private const float FailedNoteWeight = 1f;

    private static bool Alive(Realm kingdom) => kingdom?.Record != null && !kingdom.IsGone() && !kingdom.IsWild;

    private static Realm ModRealm(Realm kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    private static bool Within(double at, int years) =>
        at >= 0d && GameClock.Now >= at && GameClock.YearsSince(at) < years;

    // 本国(帝国按中央)近年皈依、而且本国仍奉中央皈依的宗教
    public static bool RecentlyConverted(Realm kingdom)
    {
        Realm realm = ModRealm(kingdom);
        return Alive(realm) && kingdom.HasReligion() && kingdom.Religion == realm.Religion &&
               Within(realm.GetOrCreate().religion_converted_at, ConversionPushYears);
    }

    private static List<Realm> Members(Realm realm)
    {
        Empire empire = realm.GetEmpire();
        return empire != null && empire.CoreKingdom == realm
            ? empire.kingdoms_list.Where(Alive).ToList()
            : new List<Realm> { realm };
    }

    private static int Cities(Realm realm) => Members(realm).Sum(member => member.Cities.Count);

    // 每年一次(挂在 ReligiousTension 的年度扫描上)
    public static void YearlyPass()
    {
        if (EmpireWorld.Current?.LiveRealms == null || ModClass.IS_CLEAR) return;
        List<Realm> realms = EmpireWorld.Current.LiveRealms.Where(kingdom => Alive(kingdom) && ModRealm(kingdom) == kingdom &&
            !EmpireCraft.Core.AncientWarfareCompatibility.Owns(kingdom)).ToList();
        foreach (Realm realm in realms)
        {
            try
            {
                if (!TryAdoptSubjectsFaith(realm) && !TryReturnToNative(realm)) TryConvert(realm, realms);
                FollowRealm(realm);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][宗教] {realm.Name} 皈依结算失败: {exception.Message}");
            }
        }
        if (!BackgroundReligionSystem.Active) ManualCityConversion(realms);
    }

    private static void TryConvert(Realm realm, List<Realm> realms)
    {
        Person king = realm.Ruler;
        if (king?.Record == null || !king.Alive || Within(realm.GetOrCreate().religion_converted_at, CooldownYears)) return;
        Religion current = realm.HasReligion() ? realm.Religion : null;
        string sphere = ReligionSphere.SphereOfCulture(CultureService.GetRealmCulture(realm));
        bool holdsHoly = PapacySystem.HoldsHolyCity(realm);
        List<Realm> members = Members(realm);
        int own = Mathf.Max(1, members.Sum(member => member.Cities.Count));
        var weights = new Dictionary<Religion, float>();
        bool weighted = ConnectivitySystem.Ready;
        foreach (Realm other in realms)
        {
            if (other == realm || !other.HasReligion() || other.Religion == current || !Candidate(other.Religion, sphere) ||
                realm.IsAtWarWith(other)) continue;
            // 帝国中央的 IsNeighbourWith 已按全体成员的城算
            bool adjacent = Members(other).Any(realm.IsNeighbourWith);
            float reach = 1f;
            if (weighted)
            {
                float strength = ConnectivitySystem.LearnStrength(realm, other, telecom: false);
                if (strength < 0f)
                {
                    if (!adjacent) continue;
                }
                else
                {
                    if (!adjacent && strength < ConnectivitySystem.Config.contact_strength) continue;
                    reach = Mathf.Clamp01(strength / Mathf.Max(0.05f, ConnectivitySystem.Config.close_strength));
                }
            }
            else if (!adjacent) continue;
            if (reach <= 0f) continue;
            Add(weights, other.Religion, Mathf.Min(MaxStrengthRatio, Cities(other) / (float)own) * reach);
        }
        // 境内的少数派
        var minorities = new Dictionary<Religion, int>();
        foreach (Realm member in members)
        foreach (City city in member.Cities)
            if (city?.Record != null && city.HasReligion() && city.Religion != current && Candidate(city.Religion, sphere))
            {
                minorities.TryGetValue(city.Religion, out int count);
                minorities[city.Religion] = count + 1;
            }
        foreach (KeyValuePair<Religion, int> pair in minorities) Add(weights, pair.Key, MinorityWeight * pair.Value / own);
        // 两段式：启示宗教还没成正教时，拉本天下奉古神的君主(不必接壤)
        Religion revealed = NativeFaithSystem.RevealedOf(sphere);
        if (revealed != null && revealed != current && current != null && current == ReligionSphere.OrthodoxOf(sphere))
            Add(weights, revealed, ReligionTenets.Revelation.realm_pull);
        // 哈里发国只改奉伊斯兰(别的教派可以，见 CaliphateFaith)；按 权重 × 意愿 挑
        Religion best = null;
        float bestWeight = 0f, bestWilling = 0f;
        foreach (KeyValuePair<Religion, float> pair in weights)
        {
            if (!CaliphateFaith.MayAdopt(realm, pair.Key)) continue;
            float willing = Willingness(current, pair.Key, sphere, holdsHoly);
            if (best != null && pair.Value * willing <= bestWeight * bestWilling) continue;
            best = pair.Key;
            bestWeight = pair.Value;
            bestWilling = willing;
        }
        if (best == null) return;
        if (EcRandom.value >= BaseChance * bestWeight * bestWilling)
        {
            if (current != null && bestWeight >= FailedNoteWeight) SyncretismSystem.NoteFailedConversion(current, best);
            return;
        }
        Convert(realm, best, current);
    }

    // 皈依意愿：类型(多神萨满无教 1、三教法教神道 0.15、一神教 0.03) × 握圣地 0.1 × 本土抗皈依(外天下宗教 ×0.3、神国再折) × 融合
    public static float Willingness(Religion current, Religion candidate, string realmSphere, bool holdsHoly)
    {
        float willing = ReligionFaith.Receptiveness(current);
        if (holdsHoly) willing *= HolyCityReluctance;
        if (current?.data == null || candidate?.data == null) return willing;
        if (HomeFaith(current, realmSphere) && ReligionSphere.SphereOf(candidate) != realmSphere)
            willing *= ReligionTenets.Home.resist * (1f - ReligionTenets.Resist(current));
        return willing * ReligionTenets.Pull(current, candidate);
    }

    // 国教是本天下的本土宗教(国家文化的天下)
    private static bool HomeFaith(Religion religion, string realmSphere) =>
        !string.IsNullOrEmpty(realmSphere) && ReligionSphere.IsNative(religion) && ReligionSphere.SphereOf(religion) == realmSphere;

    // 普世宗教君主从俗：国教是外天下的普世宗教(不是自己天下的)，治下过半人口奉本天下正教——复归本土
    private static bool TryReturnToNative(Realm realm)
    {
        Person king = realm.Ruler;
        Religion current = realm.HasReligion() ? realm.Religion : null;
        if (king?.Record == null || !king.Alive || current == null || !ReligionFaith.IsUniversal(current) ||
            PapacySystem.HoldsHolyCity(realm) || Within(realm.GetOrCreate().religion_converted_at, CooldownYears)) return false;
        string sphere = ReligionSphere.SphereOfCulture(CultureService.GetRealmCulture(realm));
        if (sphere.Length == 0 || ReligionSphere.SphereOf(current) == sphere) return false;
        Religion native = ReligionSphere.OrthodoxOf(sphere);
        if (native == null || native == current || !CaliphateFaith.MayAdopt(realm, native)) return false;
        float total = 0f, faithful = 0f;
        foreach (Realm member in Members(realm))
        foreach (City city in member.Cities)
        {
            if (city?.Record == null || city.IsGone()) continue;
            float count = Mathf.Max(1, city.CountLivingPopulation());
            total += count;
            if (city.Religion == native) faithful += count;
        }
        if (total <= 0f || faithful * 2f < total || EcRandom.value >= NativeReturnChance) return false;
        Convert(realm, native, current, "native_revert_log");
        return true;
    }

    // 入主从俗：民族宗教的君主治下过半人口奉另一个天下的本土大教
    private static bool TryAdoptSubjectsFaith(Realm realm)
    {
        Person king = realm.Ruler;
        Religion current = realm.HasReligion() ? realm.Religion : null;
        if (king?.Record == null || !king.Alive || current == null || ReligionFaith.IsUniversal(current) ||
            PapacySystem.HoldsHolyCity(realm)) return false;
        var people = new Dictionary<Religion, float>();
        float total = 0f;
        foreach (Realm member in Members(realm))
        foreach (City city in member.Cities)
        {
            if (city?.Record == null || city.IsGone()) continue;
            float count = Mathf.Max(1, city.CountLivingPopulation());
            total += count;
            if (!city.HasReligion()) continue;
            people.TryGetValue(city.Religion, out float sum);
            people[city.Religion] = sum + count;
        }
        if (people.Count == 0) return false;
        KeyValuePair<Religion, float> top = people.OrderByDescending(pair => pair.Value).First();
        Religion faith = top.Key;
        if (faith == current || top.Value * 2f < total || !ReligionSphere.IsNative(faith) || ReligionSphere.IsLocalChurch(faith) ||
            ReligionSphere.SphereOf(faith) == ReligionSphere.SphereOf(current) || !CaliphateFaith.MayAdopt(realm, faith)) return false;
        if (EcRandom.value >= AdoptSubjectsChance) return false;
        Convert(realm, faith, current, "ruler_adopts_subjects_log");
        return true;
    }

    private static bool Candidate(Religion religion) =>
        religion?.data != null && !religion.IsGone() && ReligionFaith.IsWorldFaith(religion) && !ReligionSphere.IsLocalChurch(religion);

    // 对某天下的君主：世界性宗教，加上本天下的正教、升格了的本天下民族宗教
    private static bool Candidate(Religion religion, string realmSphere)
    {
        if (Candidate(religion)) return true;
        if (religion?.data == null || religion.IsGone() || string.IsNullOrEmpty(realmSphere) ||
            ReligionSphere.SphereOf(religion) != realmSphere) return false;
        return ReligionSphere.OrthodoxOf(realmSphere) == religion || ReligionTenets.IsOrganized(religion) && !ReligionSphere.IsLocalChurch(religion);
    }

    private static void Add(Dictionary<Religion, float> weights, Religion religion, float weight)
    {
        weights.TryGetValue(religion, out float current);
        weights[religion] = current + weight;
    }

    // 君主率国皈依(异文明帝国复归本土大教也走这里，见 ReligionSchismSystem.TryNativeTurns)
    internal static void Convert(Realm realm, Religion faith, Religion old, string textKey = "ruler_conversion_log")
    {
        Person king = realm.Ruler;
        king.SetReligion(faith);
        realm.SetReligion(faith);
        if (realm.Capital?.Record != null && !realm.Capital.IsGone() && realm.Capital.Religion != faith)
            BackgroundReligionSystem.Convert(realm.Capital, faith, realm.Capital.Religion);
        Empire empire = realm.GetEmpire();
        if (empire != null && empire.CoreKingdom == realm) empire.Religion = faith;
        realm.GetOrCreate().religion_converted_at = GameClock.Now;
        string text = string.Format(LM.Get(textKey), king.FullName(), realm.GetKingdomName(), faith.Name);
        TranslateHelper.LogEventMessage(text, realm);
        king.RecordPersonalHistory(text);
        ReligionChronicle.Record(faith, old, ReligionChronicle.KindConversion, text);
        SyncretismSystem.NoteConversion(old, faith);
        ReligionDiagnostics.Add(textKey switch
        {
            "ruler_conversion_log" => ReligionDiagnostics.Ruler,
            "ruler_adopts_subjects_log" => ReligionDiagnostics.AdoptSubjects,
            "native_revert_log" => ReligionDiagnostics.NativeRevert,
            _ => ReligionDiagnostics.OtherRealm
        });
        LogService.LogInfo($"[EmpireCraft][宗教] {realm.GetKingdomName()} 由{old?.Name ?? "无教"}皈依{faith.Name}");
    }

    // 中央皈依后，成员国君主陆续随之改宗
    private static void FollowRealm(Realm realm)
    {
        if (!Within(realm.GetOrCreate().religion_converted_at, ConversionPushYears) || !realm.HasReligion()) return;
        Empire empire = realm.GetEmpire();
        if (empire == null || empire.CoreKingdom != realm) return;
        foreach (Realm member in empire.kingdoms_list.ToList())
        {
            if (!Alive(member) || member == realm || member.Religion == realm.Religion ||
                EcRandom.value >= MemberFollowChance) continue;
            member.SetReligion(realm.Religion);
            if (member.Ruler?.Record != null && member.Ruler.Alive) member.Ruler.SetReligion(realm.Religion);
            if (member.Capital?.Record != null && !member.Capital.IsGone() && member.Capital.Religion != realm.Religion)
                BackgroundReligionSystem.Convert(member.Capital, realm.Religion, member.Capital.Religion);
        }
    }

    // 有小人时(没有背景人口传教)：皈依后的国家每年逐城改宗；哈里发国过了皈依期也慢慢改(见 CaliphateFaith)
    private static void ManualCityConversion(List<Realm> realms)
    {
        foreach (Realm realm in realms)
        {
            float chance = Within(realm.GetOrCreate().religion_converted_at, ConversionPushYears) ? ManualCityChance
                : CaliphateFaith.Attracts(realm) ? CaliphateFaith.ManualCityChance : 0f;
            if (chance <= 0f) continue;
            foreach (Realm member in Members(realm))
            {
                if (!member.HasReligion() || member.Religion != realm.Religion) continue;
                foreach (City city in member.Cities.ToList())
                {
                    if (city?.Record == null || city.IsGone() || city.Religion == member.Religion ||
                        city.HasReligion() && city.Religion.GetCity() == city) continue;
                    // 外天下的宗教按跨天下折扣，本城奉本天下宗教的再减半，融过它的教义的再折(2026-11-06)
                    string sphere = ReligionSphere.SphereOfCity(city);
                    float local = chance * ReligionSphere.CrossFactor(member.Religion, sphere);
                    if (city.HasReligion())
                    {
                        if (ReligionTenets.AtHome(city.Religion, sphere) && !ReligionTenets.AtHome(member.Religion, sphere)) local *= 0.5f;
                        local *= ReligionTenets.Pull(city.Religion, member.Religion);
                    }
                    if (EcRandom.value >= local) continue;
                    BackgroundReligionSystem.Convert(city, member.Religion, city.Religion);
                    ReligionDiagnostics.Add(ReligionDiagnostics.Manual);
                }
            }
        }
    }
}
}
