// Generated from the EmpireCraft mod: Scripts/GeneralSystems/PapacySystem.PapalState.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// 教宗国(Feudalism_papal_state)从哪里来：
//   · 类别判定(Feudalism 配置，排在各级封建领地之前)：封建制、不是帝国中央、握有本国所奉宗教的圣地、宗教等级政教合一，
//     在帝国之内的还要帝国施行了教权至上(或已进到神权国家)，独立的圣城之国不限(PapalRule)；
//   · 政教合一从哪来：本文化施行教权至上或神权国家(制度效果，帝国成员与独立王国都会同步)；圣城神权自立
//     (独立王国的圣城不是都城时分出来立国，是一城之国的都城时整国政教合一，见 EmpireCraftCityBehCheckReligion)；
//     帝国的「恢复圣地」诉求；
//   · 换政体从模板重建政体时，圣城之主的政教合一照旧留着(KeepTheocracy)，不然城邦一封建化，教宗国就没了；
//   · 每年一行诊断：各西方宗教的圣地在哪、归谁、为什么还不是教宗国(TryYearlyDiagnose)。
public static partial class PapacySystem
{
    // 教宗国的制度前提：帝国之内要帝国施行了教权至上或神权国家；不在帝国里的不限
    public static bool PapalRule(Empire empire) =>
        empire == null || InstitutionSystem.IsEnacted(empire, NodeClerical) ||
        InstitutionSystem.HasFeature(empire, InstitutionFeatures.TheocraticState);

    // 握有本国所奉宗教的圣地(没有教阶的宗教、地方教会、教派的中心不算，见 ReligionFaith、ReligionSphere)
    public static bool HoldsHolyCity(Realm kingdom)
    {
        if (kingdom?.Record == null || kingdom.IsGone() || !kingdom.HasReligion() || !HasSee(kingdom.Religion)) return false;
        City holy = kingdom.Religion.GetCity();
        return holy?.Record != null && !holy.IsGone() && holy.Realm == kingdom;
    }

    // 换政体前记下圣城之主的宗教等级，重建之后用 KeepTheocracy 还原政教合一
    public static ReligionLevel? PietyBeforeReload(Realm kingdom) =>
        HoldsHolyCity(kingdom) ? kingdom.GetRegime()?.GetReligionLevel() : null;

    public static void KeepTheocracy(Realm kingdom, ReligionLevel? before)
    {
        if (before != ReligionLevel.High || kingdom?.Record == null || kingdom.IsGone()) return;
        Regime regime = kingdom.GetRegime();
        if (regime?.options != null && regime.options.ContainsKey("option_religion_type") &&
            regime.GetReligionLevel() < ReligionLevel.High)
            regime.SetReligionLevel(ReligionLevel.High);
    }

    #region 诊断

    private static double _lastDiagnose = -1d;

    // 每年一行：各西方宗教(圣地在罗马一线文化的城，或归罗马一线的国家)的圣地、归属、卡在哪一条
    public static void TryYearlyDiagnose()
    {
        if (EmpireWorld.Current?.Religions == null || ModClass.IS_CLEAR) return;
        double now = GameClock.Now;
        if (_lastDiagnose >= 0d && now >= _lastDiagnose && GameClock.YearsSince(_lastDiagnose) < 1) return;
        _lastDiagnose = now;
        try
        {
            var parts = new List<string>();
            foreach (Religion religion in EmpireWorld.Current.Religions)
            {
                // 只看有教廷的(教派、地方教会那座城只是中心，立不了教宗国)
                if (religion?.data == null || religion.IsGone() || !HasSee(religion)) continue;
                City holy = religion.GetCity();
                if (holy?.Record == null || holy.IsGone()) continue;
                Realm holder = holy.Realm;
                bool western = InstitutionSystem.GetCultureLine(CultureService.GetMainCulture(holy)) == Line ||
                               holder?.Record != null && InstitutionSystem.GetCultureLine(CultureService.GetRealmCulture(holder)) == Line;
                if (!western) continue;
                string owner = holder?.Record == null || holder.IsWild ? "无主" : holder.GetKingdomFullName();
                parts.Add($"{religion.Name} 圣地{holy.GetCityName()}(归{owner})：{PapalStateBlocker(religion, holy, holder)}");
            }
            LogService.LogInfo(parts.Count == 0
                ? "[EmpireCraft][教廷诊断] 还没有以西方城为圣地的宗教"
                : $"[EmpireCraft][教廷诊断] 西方宗教 {parts.Count} 个：{string.Join("；", parts)}");
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][教廷诊断] 失败: {exception.Message}");
        }
    }

    // 圣地之主为什么还不是教宗国(按类别判定的先后，报第一条卡住的)
    private static string PapalStateBlocker(Religion religion, City holy, Realm holder)
    {
        if (holder?.Record == null || holder.IsWild || holder.IsGone()) return "圣地无主";
        if (holder.Religion != religion) return $"圣地之主改奉{(holder.HasReligion() ? holder.Religion.Name : "无")}，教廷无所寄";
        if (holder.GetKingdomType() == KingdomType.Feudalism_papal_state) return "已是教宗国";
        Empire empire = holder.IsInEmpire() || holder.IsEmpire() ? holder.GetEmpire() : null;
        if (holder.IsEmpire())
            return holy.IsCapital()
                ? $"圣地是{empire?.GetEmpireFullName()}的帝都，皇帝即教宗(可自授天下之名，不称神圣)"
                : $"圣地在{empire?.GetEmpireFullName()}皇帝自家领内，待「恢复圣地」分出(要先确立国教、施行教权至上)";
        RegimeType? regime = holder.GetRegime()?.type;
        if (regime != RegimeType.Feudalism) return $"圣地之主是{regime}政体，封建化之后才有教宗国";
        if (!PapalRule(empire)) return $"所在帝国{empire.GetEmpireFullName()}未施行教权至上";
        ReligionLevel level = holder.GetRegime().GetReligionLevel();
        if (level < ReligionLevel.High)
        {
            if (empire != null) return $"宗教等级{level}，待帝国制度同步或「恢复圣地」";
            return holder.Cities.Count > 1
                ? $"宗教等级{level}，多城之国以圣地为都，待本文化施行教权至上"
                : $"宗教等级{level}，下次城务检查政教合一";
        }
        return "条件已齐，待下月类别刷新";
    }

    #endregion
}
}
