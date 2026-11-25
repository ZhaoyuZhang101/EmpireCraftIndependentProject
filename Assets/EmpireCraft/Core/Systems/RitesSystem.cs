// Generated from the EmpireCraft mod: Scripts/GeneralSystems/RitesSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;

namespace EmpireCraft.Core
{

// 祭祀与占卜(用户 2026-10-16："还有祭祀""还有占卜事件")：施行了"祭祀"制度(华夏部落联盟以后，见 Huaxia.json)的政权——
//   · 岁祭：共主(君主)每年立坛祭祀天地祖先，耗国库(每城八、至少二十)；祭了法统 +2；国库拿不出、祭祀不举，法统 -3，
//     五年之内只记一次"岁祭不举"；
//   · 卜岁：岁祭时龟卜来岁，大吉(一成)法统再 +2、记史，凶(两成)法统 -1、记史，其余是平常的兆，不记；
//   · 战前卜：宣战时先卜，吉(六成)法统 +1、本国各军组织度 +10(到满为止)，凶(两成五)各军组织度 -10、法统 -1；吉凶都记史。
public static class RitesSystem
{
    public const string FeatureSacrifice = "sacrifice", FeatureDivination = "divination";
    private const int CostPerCity = 8, CostMin = 20, LapseLogYears = 5;

    private static bool Has(string culture, string feature) =>
        CultureService.IsValidCulture(culture) && InstitutionSystem.GetFeature(culture, feature) > 0f;

#if EC_PENDING
    private static string RulerName(Realm kingdom) => kingdom?.king?.getName() ?? kingdom?.GetKingdomName() ?? "";
#endif

    // 每年(帝国例行检查，见 EmpireCraftEmpireBehCheckMandate)
#if EC_PENDING
    public static void Yearly(Empire empire)
    {
        Realm core = empire?.CoreKingdom;
        if (core == null || core.IsGone() || !Has(InstitutionSystem.GetPrimaryCulture(empire), FeatureSacrifice)) return;
        double now = GameClock.Now;
        if (empire.data.last_sacrifice_at >= 0d && now >= empire.data.last_sacrifice_at &&
            GameClock.YearsSince(empire.data.last_sacrifice_at) < 1f) return;
        empire.data.last_sacrifice_at = now;
        int cost = Mathf.Max(CostMin, CostPerCity * empire.countCities());
        if (!TreasurySystem.TrySpend(core, cost, TreasuryCategory.Rites))
        {
            empire.AddMandate(-3);
            if (empire.data.last_sacrifice_lapse_at < 0d || GameClock.YearsSince(empire.data.last_sacrifice_lapse_at) >= LapseLogYears)
            {
                empire.data.last_sacrifice_lapse_at = now;
                Record(empire, core, string.Format(LM.Get("rites_sacrifice_lapsed"), RulerName(core)));
            }
            return;
        }
        empire.AddMandate(2);
        if (!Has(InstitutionSystem.GetPrimaryCulture(empire), FeatureDivination)) return;
        float omen = Random.value;
        if (omen < 0.1f)
        {
            empire.AddMandate(2);
            Record(empire, core, string.Format(LM.Get("rites_year_auspicious"), RulerName(core)));
        }
        else if (omen >= 0.8f)
        {
            empire.AddMandate(-1);
            Record(empire, core, string.Format(LM.Get("rites_year_ominous"), RulerName(core)));
        }
    }
#endif

    // 宣战(WarPatch.new_war)：施行了祭祀的攻方先卜
#if EC_PENDING
    public static void OnWarDeclared(War war)
    {
        Realm attacker = war?.MainAttacker, defender = war?.MainDefender;
        if (attacker == null || attacker.IsGone() || defender == null || !Has(CultureService.GetInstitutionCulture(attacker), FeatureDivination))
            return;
        float omen = Random.value;
        if (omen >= 0.6f && omen < 0.75f) return;
        bool good = omen < 0.6f;
        foreach (CorpsData corps in CorpsSystem.Of(attacker))
            if (corps != null) corps.org = Mathf.Clamp(corps.org + (good ? 10f : -10f), 0f, 100f);
        Empire empire = attacker.IsEmpire() ? attacker.GetEmpire() : null;
        empire?.AddMandate(good ? 1 : -1);
        string content = string.Format(LM.Get("rites_war_divination"), RulerName(attacker), defender.GetKingdomName(),
            LM.Get(good ? "rites_omen_good" : "rites_omen_bad"));
        Record(empire ?? attacker.GetEmpire(), attacker, content);
    }

    private static void Record(Empire empire, Realm kingdom, string content)
    {
        LogService.LogInfo("[EmpireCraft][祭祀] " + content);
        if (empire != null && !empire.IsGone()) empire.RecordHistory(directContent: content, kingdomId: kingdom?.Key ?? -1L);
    }
#endif
}
}
