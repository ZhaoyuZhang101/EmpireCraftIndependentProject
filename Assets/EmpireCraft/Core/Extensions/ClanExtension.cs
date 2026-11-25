// Generated from the EmpireCraft mod: Scripts/GameClassExtensions/ClanExtension.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Vector2 = EmpireCraft.Core.EcVector2;
namespace EmpireCraft.Core
{
public static class ClanExtension
{
    public class ClanExtraData:ExtraDataBase
    {
        public string historical_empire_name;
        //Historical empire position
        public float x = -1L;
        public float y = -1L;
        public long original_capital;
        public long specific_clan_id = -1L;
    }


    public static ClanExtraData GetOrCreate(this Clan a, bool isSave = false)
    {
        var ed = a.GetOrCreate<Clan, ClanExtraData>(isSave);
        return ed;
    }
    public static bool HasSpecificClan(this Clan a)
    {
        if (a == null) return false;
        return a.GetOrCreate().specific_clan_id != -1L;
    }
    public static SpecificClan GetSpecificClan(this Clan a)
    {
        if (a == null) return null;
        var ed = a.GetOrCreate();
        return SpecificClanManager.Get(ed.specific_clan_id);
    }
    public static void SetSpecificClan(this Clan a, SpecificClan specificClan)
    {
        if (a == null) return;
        a.GetOrCreate().specific_clan_id = specificClan.id;
    }
    public static void RemoveSpecificClan(this Clan a)
    {
        if (a == null) return;
        a.GetOrCreate().specific_clan_id = -1L;
    }

    // 这个氏族的族人该姓什么(给新生儿、入族的人起姓用)：有对应宗族的取宗族记的姓(族谱的姓)，
    // 不从原版氏族名里拆——原版氏族名现在显示的是"氏"(如华夏姬姓分出的"鲁氏"，见 SpecificClan.VanillaClanName)；
    // 没有对应宗族的照旧从氏族名里取
#if EC_PENDING
    public static string GetMemberSurname(this Clan clan)
    {
        if (clan?.Record == null) return "";
        string surname = clan.GetSpecificClan()?.name;
        return !string.IsNullOrWhiteSpace(surname) ? surname : clan.GetClanName();
    }

    public static string GetClanName(this Clan clan, Sex sex = Sex.None, bool hasSexPost = false)
    {
        if (clan?.Record == null || string.IsNullOrWhiteSpace(clan.Name)) return "";
        var nameParts = clan.Name.SplitNameParts();
        string culture = null;
        if (ConfigData.speciesCulturePair.TryGetValue(clan.species_id, out culture))
        {
            if (OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting))
            {
                if (setting?.Clan != null && setting.Clan.name_pos >= 0 && nameParts.Length > setting.Clan.name_pos)
                {
                    return hasSexPost ? nameParts[setting.Clan.name_pos] + LM.Get($"{culture}_sex_post_{sex.ToString()}"): nameParts[setting.Clan.name_pos];
                }
            }
        }
        return hasSexPost && !string.IsNullOrWhiteSpace(culture)
            ? nameParts[0] + LM.Get($"{culture}_sex_post_{sex}")
            : nameParts[0];
    }
#endif

    public static bool HasHistoryEmpire(this Clan a)
    {
        string name = GetOrCreate(a).historical_empire_name;
        return name != null && name!="";
    }

    public static void ClearHistoricalName(this Clan __instance)
    {
        GetOrCreate(__instance).id = __instance.Key;
        GetOrCreate(__instance).historical_empire_name = "";
    }

    public static string GetHistoryEmpireName(this Clan __instance)
    {
        return GetOrCreate(__instance).historical_empire_name;
    }   

    public static City GetHistoryCapital(this Clan __instance)
    {
        return EmpireWorld.Current.CityByKey(GetOrCreate(__instance).original_capital);
    }    
    public static void SetHistoryEmpireName(this Clan __instance, string name)
    {
        GetOrCreate(__instance).historical_empire_name = name;
    }

    public static Vector2 GetHistoryEmpirePos(this Clan __instance)
    {
        return new Vector2(GetOrCreate(__instance).x, GetOrCreate(__instance).y);
    }    
    public static void SetHistoryEmpirePos(this Clan __instance, Vector2 pos)
    {
        GetOrCreate(__instance).x = pos.x;
        GetOrCreate(__instance).y = pos.y;
    }

}
}
