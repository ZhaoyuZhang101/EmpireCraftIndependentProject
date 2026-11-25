// Ported from the EmpireCraft mod: Scripts/GeneralSystems/WorldMap/ScenarioModernPolitics.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 剧本按国家定近代政治(用户 2026-11-10 "剧情模式加上日军侵华剧本""现代政体如果要做的符合历史但是受限于机制的话优先完善机制")。
// 以前政体只能按文化整体定：同为法兰克文化的英国君主立宪、法国共和，同为华夏的国民政府训政、陕甘宁边区共产党执政都写不出来。
// 剧本国家的 modern 块(见 WorldMapModern)：
//   · regime：republic 共和(现代政体，开局即已制宪、不走三年过渡) / constitutional 君主立宪(议会、首相) /
//     monarchy 君主国(本文化废君也不被当成新立的君主国强改共和)；
//   · ideology、party、one_party：执政理念、执政党名号、一党专政；parties：开局就有的别的政党；
//   · 名将名臣 post "pm"：首相、总理，开局第一届议会照剧本组阁；
//   · 国号照剧本锁定(自定国号)：不被军阀时期的"临时政府""某系军阀"、天下之名改掉。
// 另有 autonomy(地方实力派)：现代国家的成员保留军队与财政，随中央出征(见 MakeWarlordMember)。
public static class ScenarioModernPolitics
{
    private static string Regime(WorldMapPolity data) => data?.modern?.regime?.Trim().ToLowerInvariant() ?? "";

    public static PartyIdeology? ParseIdeology(string text) =>
        !string.IsNullOrWhiteSpace(text) && Enum.TryParse(text.Trim(), true, out PartyIdeology ideology) &&
        !IdeologyFamilies.IsAncient(ideology) ? ideology : null;

    // 建国(称帝)之前：写了共和的，核心国先改现代政体；写了君主国、立宪的记为剧本君主国
#if EC_PENDING
    public static void BeforeEmpire(Realm kingdom, WorldMapPolity data)
    {
        string regime = Regime(data);
        if (kingdom?.Record == null || kingdom.IsGone() || regime.Length == 0) return;
        KingdomExtension.KingdomExtraData extra = kingdom.GetOrCreate();
        if (regime == "republic")
        {
            if (kingdom.GetRegime()?.type != RegimeType.Modern)
            {
                kingdom.SetRegimeType(RegimeType.Modern);
                kingdom.LoadRegime();
                kingdom.SystemChange();
            }
            extra.regime_manually_selected = true;
        }
        else
        {
            extra.scenario_monarchy = true;
            // 本文化已废君(现代中国)时文化解析出现代政体：剧本的君主国改回本文化最近的君主政体
            if (kingdom.GetRegime()?.type is RegimeType.Modern or RegimeType.Republic &&
                InstitutionSystem.TryResolveMonarchyRegime(CultureService.GetRealmCulture(kingdom), out RegimeType monarchy))
            {
                kingdom.SetRegimeType(monarchy);
                kingdom.LoadRegime();
                kingdom.SystemChange();
                extra.regime_manually_selected = true;
            }
        }
    }
#endif

    // 称帝(组建政府)之后：共和国定建国理念、开局即已制宪；立宪国立宪；国号锁定
#if EC_PENDING
    public static void AfterEmpire(Realm kingdom, Empire empire, WorldMapPolity data, string name)
    {
        WorldMapModern modern = data?.modern;
        if (modern == null || kingdom?.Record == null || kingdom.IsGone()) return;
        if (!string.IsNullOrWhiteSpace(name)) kingdom.SetCustomCountryName(name);
        if (empire == null || empire.IsGone()) return;
        string regime = Regime(data);
        try
        {
            if (regime == "republic")
            {
                // 组建政府时又按文化解析了一遍政体(阿拉伯文化的土耳其回到阿拉伯政体，凯末尔成了"哈里发"、苏丹入朝)：这里再改回现代政体
                if (kingdom.GetRegime()?.type != RegimeType.Modern)
                {
                    kingdom.SetRegimeType(RegimeType.Modern);
                    kingdom.LoadRegime();
                    kingdom.SystemChange();
                    kingdom.GetOrCreate().regime_manually_selected = true;
                }
                PartyIdeology ideology = ParseIdeology(modern.ideology) ?? ModernStateFormationSystem.ResolveGovernmentIdeology(kingdom);
                RepublicSystem.SyncWithRegime(empire, foundingIdeology: ideology);
                RepublicSystem.SetFoundingIdeology(empire, ideology);
                // 开局前早已建国：不走临时政府、制宪的三年过渡
                ConstitutionalEconomyState state = empire.data.constitutional_economy;
                if (state != null)
                {
                    state.republic_transition_stage = 0;
                    state.republic_first_election_pending = false;
                    state.republic_since = GameClock.Now - 10 * 60d;
                }
                // 组建政府时已记了一笔"某某皇帝在位"，紧接着才改共和：这一任改记成共和国元首的任期(世系里以前写"陕甘宁边区皇帝")
                if (empire.data.currentHistory is { } term)
                {
                    term.is_republic = true;
                    term.office_title = RepublicSystem.GetHeadOfStateTitle(empire);
                }
            }
            else if (regime == "constitutional")
                ConstitutionalEconomySystem.EstablishScenarioConstitution(empire);
            EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][世界地图] {name} 定政体失败: {exception.Message}");
        }
    }
#endif

    // 进世界后(名将名臣已就位)：立执政党与别的政党、定中央占比、一党专政、首相
#if EC_PENDING
    public static void Settle(Realm kingdom, Empire empire, WorldMapPolity data, Person primeMinister, Func<string, Person> person)
    {
        WorldMapModern modern = data?.modern;
        if (modern == null || kingdom?.Record == null || kingdom.IsGone() || empire == null || empire.IsGone()) return;
        try
        {
            PartyIdeology ideology = ParseIdeology(modern.ideology) ?? WarlordEraSystem.GovernmentIdeology(empire);
            // 共和国的执政党魁是元首本人；君主立宪的是首相
            Person leader = Regime(data) == "republic" ? kingdom.Ruler ?? primeMinister : primeMinister ?? kingdom.Ruler;
            FixedFaction ruling = PartySystem.ScenarioFoundParty(empire, ideology, leader, WorldMapData.PickParts(modern.party ?? ""));
            if (ruling == null) return;
            var others = new List<FixedFaction>();
            foreach (WorldMapParty entry in modern.parties ?? new List<WorldMapParty>())
            {
                PartyIdeology? other = ParseIdeology(entry?.ideology);
                if (other == null || other == ideology) continue;
                FixedFaction party = PartySystem.ScenarioFoundParty(empire, other.Value,
                    string.IsNullOrWhiteSpace(entry.leader) ? null : person(entry.leader), WorldMapData.PickParts(entry.name ?? ""));
                if (party != null && party != ruling) others.Add(party);
            }
            // 政体模板里的派系并进执政党，不再被改组成一串额外的党
            PartySystem.FoldScenarioFactions(empire, ruling);
            SetRatios(empire.CoreKingdom, ruling, others);
            if (modern.oneParty && !RepublicSystem.IsOneParty(empire))
                PartyBanSystem.Close(empire, ruling, Enumerable.Empty<FixedFaction>(), "party_ban_one_party_history");
            ParliamentSystem.Update(empire);
            if (primeMinister != null && ParliamentSystem.HasParliament(empire))
                ParliamentSystem.ScenarioAppoint(empire, primeMinister, ruling);
            LogService.LogInfo($"[EmpireCraft][世界地图] {kingdom.GetKingdomName()} 执政 {ruling.Name}({ideology})" +
                               $"{(modern.oneParty ? "，一党" : "")}，另有 {others.Count} 党，首相 {ParliamentSystem.GetPrimeMinister(empire)?.FullName() ?? "无"}");
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][世界地图] {kingdom.GetKingdomName()} 立党失败: {exception.Message}");
        }
    }
#endif

    // 剧情事件改执政理念(日本 1940 解散各党、成立大政翼赞会)：a 的政府改由这一理念的党执政(写了名号的照名号)，
    // 共和国连同建国理念一起改；one_party 一党专政。首相(有议会的)或元首当党魁。做不了返回原因
#if EC_PENDING
    public static string ChangeIdeology(Realm kingdom, string ideologyText, string party, bool oneParty)
    {
        PartyIdeology? ideology = ParseIdeology(ideologyText);
        if (ideology == null) return "理念不对";
        Empire empire = kingdom?.GetEmpire();
        if (empire == null || empire.IsGone() || empire.CoreKingdom != kingdom) return "不是政府";
        Person leader = ParliamentSystem.GetPrimeMinister(empire) ?? kingdom.Ruler;
        FixedFaction ruling = PartySystem.ScenarioFoundParty(empire, ideology.Value, leader, party);
        if (ruling == null) return "立不了党";
        if (RepublicSystem.IsRepublic(empire)) RepublicSystem.SetFoundingIdeology(empire, ideology.Value);
        SetRatios(kingdom, ruling, new List<FixedFaction>());
        if (oneParty) PartyBanSystem.Close(empire, ruling, Enumerable.Empty<FixedFaction>(), "party_ban_one_party_history");
        // 不再一党专政(战后日本修宪)：重开党禁
        else if (RepublicSystem.IsOneParty(empire)) PartyBanSystem.Open(empire, "party_ban_reopened_constitution_history");
        if (leader != null && ParliamentSystem.HasParliament(empire)) ParliamentSystem.ScenarioAppoint(empire, leader, ruling);
        LogService.LogInfo($"[EmpireCraft][剧情事件] {kingdom.GetKingdomName()} 改由 {ruling.Name}({ideology}) 执政{(oneParty ? "，一党专政" : "")}");
        return null;
    }
#endif

    // 中央占比：执政党六成，剧本里别的政党分三成，其余派系分剩下的
#if EC_PENDING
    private static void SetRatios(Realm core, FixedFaction ruling, List<FixedFaction> others)
    {
        List<FixedFaction> factions = core?.GetRegime()?.GetPlayerFactions()?.Where(faction => faction != null).ToList();
        if (factions == null || factions.Count == 0) return;
        core.ReconcileFactionRatios(factions);
        Dictionary<FixedFaction, int> ratios = core.GetOrCreate().FactionRatio;
        List<FixedFaction> rest = factions.Where(faction => faction != ruling && !others.Contains(faction)).ToList();
        foreach (FixedFaction faction in factions)
            ratios[faction] = faction == ruling ? 60
                : others.Contains(faction) ? 30 / Math.Max(1, others.Count)
                : 10 / Math.Max(1, rest.Count);
        core.ClampFactionRatio();
    }
#endif

    // 地方实力派(民国的晋绥、桂系、川系、滇系、西北马家)：现代国家的成员原本是没有兵的省(Modern_province)，
    // 这里保留军队与财政(Modern_state)、首领由军中最能者接替(举能)，国号照剧本(晋绥、桂系)，行政改划不拆它的地盘；
    // 中央的仗照样随征(见 MemberWarAid)
#if EC_PENDING
    public static void MakeWarlordMember(Realm kingdom, WorldMapPolity data, string name)
    {
        Regime regime = kingdom?.GetRegime();
        if (regime == null || kingdom.IsGone()) return;
        regime.SetAllowArmy(true);
        regime.SetLeaderSelectMethod(LeaderSelectMethod.Army);
        TreasurySystem.EnsureCollectsTax(regime);
        KingdomExtension.KingdomExtraData extra = kingdom.GetOrCreate();
        extra.scenario_autonomy = true;
        if (!string.IsNullOrWhiteSpace(name)) kingdom.SetCustomCountryName(name);
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
        LogService.LogInfo($"[EmpireCraft][世界地图] {name} 开局为地方实力派：{kingdom.GetKingdomType()}");
    }
#endif
}
}
