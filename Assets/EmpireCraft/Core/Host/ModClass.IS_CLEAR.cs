// Ported from the EmpireCraft mod: Scripts/ModClass.cs#ModClass.IS_CLEAR,ModClass.EMPIRE_MANAGER,ModClass.KINGDOM_TITLE_MANAGER,ModClass.NARROW_SPACE,ModClass.SAVE_FREEZE,ModClass.WAR_END_YEAR,ModClass.WARLORD_ERA_YEARS,ModClass.REAL_NUM_SWITCH,ModClass.SIMPLE_NAMEPLATE_SWITCH,ModClass.EMPIRE_SHOW_ALLIANCE_SWITCH,ModClass.KINGDOM_TITLE_FREEZE,ModClass.TITLE_BEEN_DESTROY_TIME,ModClass.TITLE_MAX_CITIES,ModClass.CITY_MAX_ZONES,ModClass.SETTLEMENT_BASE_GOLD,ModClass.SETTLEMENT_PER_CITY_GOLD,ModClass.SETTLEMENT_RESERVE_GOLD,ModClass.SETTLEMENT_WOOD,ModClass.SETTLEMENT_STONE,ModClass.SETTLEMENT_MIN_HOUSEHOLDS,ModClass.SETTLEMENT_TARGET_HOUSEHOLDS,ModClass.SETTLEMENT_RETAIN_HOUSEHOLDS,ModClass.SETTLEMENT_MAX_MIGRATION_PERCENT,ModClass.SETTLEMENT_FOOD_YEARS,ModClass.SETTLEMENT_PREPARATION_MONTHS,ModClass.SETTLEMENT_COOLDOWN_MONTHS,ModClass.FEUDAL_UNION_REALM_LIMIT,ModClass.SAME_CULTURE_EMPIRE_LIMIT,ModClass.PERFORMANCE_HIGH_POPULATION_MODE,ModClass.PERFORMANCE_ADAPTIVE_THROUGHPUT_MODE,ModClass.PERFORMANCE_SKIP_HIDDEN_VISUALS,ModClass.PURE_MAP_MODE,ModClass.PERFORMANCE_SKIP_NAMEPLATE_OVERLAP,ModClass.KINGDOM_INSTITUTION_REFORMS,ModClass.MOD_DATA_VERSION,ModClass.CULTURE_NO_EMPIRE_SINCE,ModClass.FEUDAL_EMPIRE_LINEAGE,ModClass.MOD_ALLIANCE_IDS @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

public partial class ModClass
{
    public static bool IS_CLEAR = true;
}

public partial class ModClass
{
    public static EmpireManager EMPIRE_MANAGER;
}

public partial class ModClass
{
    public static KingdomTitleManager KINGDOM_TITLE_MANAGER;
}

public partial class ModClass
{
    public static string NARROW_SPACE = "\u200A";
}

public partial class ModClass
{
    public static bool SAVE_FREEZE = false;
}

public partial class ModClass
{
    public static int WAR_END_YEAR = 30;
}

public partial class ModClass
{
    // 现代政体统一过的帝国核心再分裂后，军阀时期持续多少年才承认分治；0 = 永远不分家
    public static int WARLORD_ERA_YEARS = 200;
}

public partial class ModClass
{
    public static bool REAL_NUM_SWITCH = false;
}

public partial class ModClass
{
    public static bool SIMPLE_NAMEPLATE_SWITCH = false;
}

public partial class ModClass
{
    public static bool EMPIRE_SHOW_ALLIANCE_SWITCH = false;
}

public partial class ModClass
{
    public static bool KINGDOM_TITLE_FREEZE = false;
}

public partial class ModClass
{
    public static int TITLE_BEEN_DESTROY_TIME = 50;
}

public partial class ModClass
{
    // 法理扩张(未冻结法理)时单个法理最多容纳的城市数，0 表示不限
    public static int TITLE_MAX_CITIES = 5;
}

public partial class ModClass
{
    // 和平扩张时单座城市的区域数上限；0 = 不限，不限制战争占领或玩家改地。
    public static int CITY_MAX_ZONES = 50;
}

public partial class ModClass
{
    public static int SETTLEMENT_BASE_GOLD = 200;
}

public partial class ModClass
{
    public static int SETTLEMENT_PER_CITY_GOLD = 25;
}

public partial class ModClass
{
    public static int SETTLEMENT_RESERVE_GOLD = 200;
}

public partial class ModClass
{
    public static int SETTLEMENT_WOOD = 20;
}

public partial class ModClass
{
    public static int SETTLEMENT_STONE = 10;
}

public partial class ModClass
{
    public static int SETTLEMENT_MIN_HOUSEHOLDS = 10;
}

public partial class ModClass
{
    public static int SETTLEMENT_TARGET_HOUSEHOLDS = 20;
}

public partial class ModClass
{
    public static int SETTLEMENT_RETAIN_HOUSEHOLDS = 30;
}

public partial class ModClass
{
    public static int SETTLEMENT_MAX_MIGRATION_PERCENT = 25;
}

public partial class ModClass
{
    public static int SETTLEMENT_FOOD_YEARS = 2;
}

public partial class ModClass
{
    public static int SETTLEMENT_PREPARATION_MONTHS = 6;
}

public partial class ModClass
{
    public static int SETTLEMENT_COOLDOWN_MONTHS = 36;
}

public partial class ModClass
{
    public static int FEUDAL_UNION_REALM_LIMIT = 3;
}

public partial class ModClass
{
    // 同一文化最多几个帝国并立(僭越称帝争正统，见 ImperialLegitimacyChallengeService)；玩家要求做成设置，默认 2
    public static int SAME_CULTURE_EMPIRE_LIMIT = 2;
}

public partial class ModClass
{
    public static bool PERFORMANCE_HIGH_POPULATION_MODE = true;
}

public partial class ModClass
{
    public static bool PERFORMANCE_ADAPTIVE_THROUGHPUT_MODE = true;
}

public partial class ModClass
{
    public static bool PERFORMANCE_SKIP_HIDDEN_VISUALS = true;
}

public partial class ModClass
{
    // 纯净地图模式(模组设置，下次启动生效，见 PureMapMode)
    public static bool PURE_MAP_MODE;
}

public partial class ModClass
{
    public static bool PERFORMANCE_SKIP_NAMEPLATE_OVERLAP = true;
}

public partial class ModClass
{
    // 独立王国、部落以简化朝堂推制度改革(见 InstitutionSystem.Realm.cs)；关掉就只有帝国推——原始政体的部落除外，
    // 部落不再是帝国，原始改革只能由各部推，关了照样推(见 InstitutionSystem.KingdomReformsEnabledFor)。
    // 设置里没有这一项(旧的 default_config.json)时按默认开
    public static bool KINGDOM_INSTITUTION_REFORMS = true;
}

public partial class ModClass
{
    public static int MOD_DATA_VERSION = 3;
}

public partial class ModClass
{
    // 文化 → 该文化从何时起没有帝国(用于"兜底称帝"的五十年时限)
    public static Dictionary<string, double> CULTURE_NO_EMPIRE_SINCE = new Dictionary<string, double>();
}

public partial class ModClass
{
    // 帝国名 → 用这个名字建立过的西方封建帝国 id(用于"第二帝国""第三帝国")
    public static Dictionary<string, List<long>> FEUDAL_EMPIRE_LINEAGE = new Dictionary<string, List<long>>();
}

public partial class ModClass
{
    public static HashSet<long> MOD_ALLIANCE_IDS = new HashSet<long>();
}
}
