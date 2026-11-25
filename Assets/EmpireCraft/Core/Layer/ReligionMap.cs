// Generated from the EmpireCraft mod: Scripts/Layer/ReligionMap.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Color = EmpireCraft.Core.EcColor;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 宗教图层(用户 2026-10-27 定稿示意图 Tasks/ui-mockups/religion-wide.png ⑦⑧)。原版宗教图层的地块配色照旧(每教一色)：
//   · 铭牌：原版方框铭牌换成模组文字铭牌(铭牌资源在 EmpireCraftNamePlateLibrary 里登记)——按城所奉的宗教分组，每教一条顺着
//     所占城轮廓弯曲的文字，上淡金下本教色、深色描边；悬停的城所奉之教不透明高亮；
//   · 悬停城市：模组提示(本城信何教、圣地、国教与宗教政策、异教之城的稳定扣减与起事风险、遭迫害、民间教门风险)；
//   · 原版宗教提示框(各窗口里的宗教链接、铭牌)换成模组的(类型、天下、地位、圣地、教首与正统、教权、信众、圣战、分出的教会)；
//   · 点地块、点宗教链接：原版本就开宗教窗口，经 ReligionWindowRedirectPatch 改开模组宗教窗口。
public static class ReligionMap
{
    public const string TooltipType = "empirecraft_religion_city";
#if EC_PENDING
    private static readonly Dictionary<long, TerritoryLabelStyle> Styles = new();
#endif
    private static readonly Dictionary<Religion, List<City>> Groups = new();

#if EC_PENDING
    public static void ResetWorldState() => Styles.Clear();
#endif

    // 接管原版宗教图层的悬停提示(EmpireCraftNamePlateLibrary 登记铭牌时一并调用)
#if EC_PENDING
    public static void Configure()
    {
        MetaTypeAsset asset = MetaType.Religion.getAsset();
        if (asset == null) return;
        asset.check_cursor_tooltip = (zone, meta, option) =>
        {
            City city = zone?.city;
            if (city?.Record == null || city.IsGone() || city.Realm == null || city.Realm.IsWild ||
                EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(city)) return false;
            Tooltip.hideTooltip(city, true, TooltipType);
            Tooltip.show(city, TooltipType, new TooltipData
            {
                city = city, kingdom = city.Realm, tooltip_scale = 0.7f, is_sim_tooltip = true
            });
            return true;
        };
    }
#endif

    // 文字铭牌：每教一条
#if EC_PENDING
    public static void SubmitLabels()
    {
        Groups.Clear();
        foreach (City city in EmpireWorld.Current.Cities)
        {
            if (city?.Record == null || city.IsGone() || !city.HasReligion() ||
                EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(city)) continue;
            if (!Groups.TryGetValue(city.Religion, out List<City> cities)) Groups[city.Religion] = cities = new List<City>();
            cities.Add(city);
        }
        Religion hovered = EmpireWorld.Current?.getMouseTilePosCachedFrame()?.zone_city?.religion;
        foreach (KeyValuePair<Religion, List<City>> pair in Groups)
        {
            if (!ReligionDisplay.Alive(pair.Key)) continue;
            TerritoryLabelRenderer.SubmitCities($"religion:{pair.Key.data.id}", pair.Key.Name, pair.Value, StyleOf(pair.Key),
                pair.Key == hovered);
        }
    }

    private static TerritoryLabelStyle StyleOf(Religion religion)
    {
        if (Styles.TryGetValue(religion.data.id, out TerritoryLabelStyle style)) return style;
        Color main = ReligionDisplay.MainColor(religion);
        style = new TerritoryLabelStyle
        {
            text_color = new Color(1f, 1f, 1f, 0.85f),
            outline_color = new Color(main.r * 0.25f, main.g * 0.25f, main.b * 0.25f, 0.86f),
            size_multiplier = 0.9f,
            territory_padding = 0.95f,
            min_font_size = 6,
            font_style = FontStyle.Normal,
            use_gold_gradient = true,
            gold_top = new Color32(255, 246, 214, 255),
            gold_bottom = Color.Lerp(main, Color.white, 0.15f),
            bridge_internal_gaps = true,
            arc_curvature = 0.1f
        };
        Styles[religion.data.id] = style;
        return style;
    }
#endif

    private const string Plain = "#D8D2B8", Bad = "#E07A5F", Good = "#7CD27C";

#if EC_PENDING
    private static void Line(Tooltip tooltip, string labelKey, string value, string hex = Plain) =>
        tooltip.addLineText(LM.Get(labelKey), value, hex, pPercent: false, pLocalize: false);
#endif

    // 宗教图层悬停城市
#if EC_PENDING
    public static void ShowTooltip(Tooltip tooltip, string type, TooltipData data)
    {
        City city = data?.city;
        if (city?.Record == null || city.IsGone()) return;
        Realm kingdom = city.Realm;
        Religion faith = city.HasReligion() ? city.Religion : null;
        tooltip.clear();
        tooltip.setTitle(city.GetCityName(), "religion_city_tooltip", "#FFFFFF");
        if (kingdom?.Record != null) Line(tooltip, "religion_tip_kingdom", kingdom.GetKingdomName(), kingdom.GetColor()?.HexText ?? Plain);
        if (faith == null)
        {
            Line(tooltip, "religion_tip_faith", LM.Get("religion_tip_no_faith"), "#8FA0A8");
        }
        else
        {
            // 圣地一个天下一处；教派、地方教会自己那座城只是中心(见 ReligionSphere"教派与圣地")
            bool holy = ReligionSphere.HolySite(faith) == city;
            bool center = !holy && ReligionSphere.Center(faith) == city;
            string label = holy ? string.Format(LM.Get("religion_tip_holy"), faith.Name)
                : center ? string.Format(LM.Get("religion_tip_center"), faith.Name) : faith.Name;
            Line(tooltip, "religion_tip_faith", label, ReligionDisplay.TextHex(faith));
            if (holy && PapacySystem.Pope(faith) is Person head)
                Line(tooltip, "religion_tip_head", $"{ReligionDisplay.HeadTitle(faith)} {head.FullName()}", "#F3C34A");
            // 本土还是外来(用户 2026-11-06，见 ReligionTenets、BackgroundReligionSystem)：本土惰性倍数；外来的跨天下势能折扣与改信几率折扣
            string sphere = ReligionSphere.SphereOfCity(city);
            if (sphere.Length > 0)
            {
                if (ReligionTenets.AtHome(faith, sphere))
                    Line(tooltip, "religion_tip_native", string.Format(LM.Get("religion_tip_native_value"), ReligionTenets.HomeFactor(faith, sphere)), Good);
                else
                    Line(tooltip, "religion_tip_native", string.Format(LM.Get("religion_tip_foreign_value"), ReligionSphere.CrossFactor(faith, sphere),
                        BackgroundReligionSystem.ForeignChance(faith, sphere)), "#E9A85B");
            }
        }
        // 别教的圣战目标(耶路撒冷之于天主教会)
        foreach (Religion religion in EmpireWorld.Current.Religions)
        {
            if (!ReligionDisplay.Alive(religion) || religion == faith || religion.GetOrCreate().CrusadeCityId != city.Key ||
                !PapacySystem.CallsHolyWar(religion)) continue;
            Line(tooltip, "religion_crusade_city", religion.Name, ReligionDisplay.TextHex(religion));
        }
        if (kingdom?.Record != null && !kingdom.IsWild)
        {
            ReligiousTension.Policy policy = ReligiousTension.PolicyOf(kingdom);
            string policyHex = policy == ReligiousTension.Policy.Tolerant ? Good : policy == ReligiousTension.Policy.PollTax ? "#E9A85B" : Bad;
            Line(tooltip, "religion_tip_state", string.Format(LM.Get("religion_tip_state_value"),
                kingdom.HasReligion() ? kingdom.Religion.Name : LM.Get("label_none"), LM.Get("religion_policy_" + policy)), policyHex);
            // 国教在这城的推行：跨天下折扣 × 政令控制力(山后、隔无人区的边地不随朝廷改宗)
            if (kingdom.HasReligion() && kingdom.Religion != faith)
            {
                float cross = ReligionSphere.CrossFactor(kingdom.Religion, ReligionSphere.SphereOfCity(city));
                float control = BackgroundReligionSystem.ControlFactor(city);
                Line(tooltip, "religion_tip_push", string.Format(LM.Get("religion_tip_push_value"), cross * control, cross, control),
                    cross * control >= 0.5f ? Plain : "#E9A85B");
            }
            if (ReligiousTension.IsHeterodox(city))
            {
                Line(tooltip, "religion_tip_heterodox", string.Format(LM.Get("religion_tip_stability"), ReligiousTension.StabilityLoss(city)), Bad);
                float persecuted = ReligiousPersecution.StabilityLoss(city);
                if (persecuted > 0f)
                    Line(tooltip, "stability_persecution_detail", string.Format(LM.Get("religion_tip_stability"), persecuted), Bad);
                if (policy != ReligiousTension.Policy.Tolerant && ReligiousTension.CanRevolt(city))
                    Line(tooltip, "religion_tip_risk", LM.Get("religion_tip_revolt"), Bad);
            }
            if (SectUprisingSystem.AtRisk(city)) Line(tooltip, "religion_tip_folk", LM.Get("religion_tip_sect"), "#E9A85B");
        }
        if (faith != null) tooltip.addBottomDescription(string.Format(LM.Get("religion_tip_click"), faith.Name));
    }
#endif

    // 原版宗教提示框换成模组的(EmpireCraftTooltipLibrary 以 "religion" 登记)
#if EC_PENDING
    public static void ShowReligionTooltip(Tooltip tooltip, string type, TooltipData data)
    {
        Religion religion = data?.religion;
        if (!ReligionDisplay.Alive(religion)) return;
        tooltip.clear();
        tooltip.setTitle(religion.Name, "religion", ReligionDisplay.TextHex(religion));
        (string standing, string standingHex) = ReligionDisplay.Standing(religion);
        Line(tooltip, "religion_tip_type", $"{ReligionDisplay.FaithName(religion)} · {ReligionDisplay.SphereName(religion)}");
        Line(tooltip, "religion_tip_standing", standing, standingHex);
        // 圣地一个天下一处：教派随本宗，地方教会没有，它们自己那座城只是中心
        City holy = ReligionSphere.HolySite(religion), center = ReligionSphere.Center(religion);
        bool local = ReligionSphere.IsLocalChurch(religion);
        Line(tooltip, "papacy_holy_city", holy != null
            ? $"{holy.GetCityName()}({holy.Realm?.GetKingdomName() ?? ""})"
            : LM.Get(local ? "label_none" : "papacy_see_lost"), holy != null || local ? Plain : Bad);
        if (center != null && center != holy)
            Line(tooltip, "religion_center_city", $"{center.GetCityName()}({center.Realm?.GetKingdomName() ?? ""})");
        if (PapacySystem.CallsHolyWar(religion) && PapacySystem.CrusadeCity(religion) is City target && target != holy)
            Line(tooltip, "religion_crusade_city", $"{target.GetCityName()}({target.Realm?.GetKingdomName() ?? ""})",
                target.Realm?.Religion == religion ? Plain : Bad);
        if (ReligionFaith.HasHierarchy(religion) && !PapacySystem.HasSee(religion))
            Line(tooltip, "religion_tip_head", ReligionDisplay.HierarchyValue(religion), Plain);
        if (PapacySystem.HasSee(religion))
        {
            Person head = PapacySystem.Pope(religion);
            string headText = head != null ? head.FullName() : LM.Get("label_none");
            if (head != null && PopeLegitimacy.Applies(religion))
                headText += " · " + string.Format(LM.Get("religion_pope_legitimacy_short"), PopeLegitimacy.Get(religion));
            Line(tooltip, "religion_tip_head", $"{ReligionDisplay.HeadTitle(religion)} {headText}", "#F3C34A");
            float authority = PapacySystem.Authority(religion);
            Line(tooltip, "papacy_authority", $"{authority:0}", authority >= 60f ? Good : authority >= 35f ? Plain : Bad);
        }
        List<City> cities = religion.Cities?.Where(city => city?.Record != null && !city.IsGone()).ToList() ?? new List<City>();
        float people = cities.Sum(city => (float)city.CountLivingPopulation());
        int states = EmpireWorld.Current.LiveRealms.Count(kingdom => kingdom?.Record != null && !kingdom.IsGone() && !kingdom.IsWild &&
                                                                 kingdom.Religion == religion);
        Line(tooltip, "religion_tip_followers", string.Format(LM.Get("religion_tip_followers_value"), UIHelper.ShortCount(people),
            cities.Count, states));
        if (PapacySystem.ActiveCrusade(religion) is War crusade) Line(tooltip, "religion_row_crusade", crusade.Name, Bad);
        List<Religion> children = ReligionDisplay.Children(religion);
        if (children.Count > 0)
            Line(tooltip, "religion_tip_children", string.Join("、", children.Select(child => child.Name)), ReligionDisplay.Purple);
        tooltip.addBottomDescription(string.Format(LM.Get("religion_tip_footer"), ReligionDisplay.ExclusiveName(religion),
            ReligionDisplay.MissionName(religion), religion.Age()));
    }
#endif
}
}
