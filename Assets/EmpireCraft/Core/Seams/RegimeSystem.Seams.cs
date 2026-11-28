using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EmpireCraft.Core
{
    // Hand-written half of RegimeManager: reading a regime file with variants in it (see RegimeVariants).
    //
    // As the mod: each regime in a file is read on its own, one that cannot be read is skipped with a note. Besides:
    //   - a name the code does not know, with "base" naming a known regime, is a variant: it is kept under a regime value
    //     of its own, and its template behaves as the base (its type is the base's);
    //   - a kind of realm or city a file names that the code does not know is left out with a note (a variant uses its
    //     base's kinds; its own wording for them goes in its OfficialType.csv);
    //   - army titles the code does not know are kept by name (ArmyTitles).
    // Whether a regime is primitive goes by its base.
    public static partial class RegimeManager
    {
        private static void LoadRegimeFile(string filePath)
        {
            JObject root;
            try { root = JObject.Parse(File.ReadAllText(filePath)); }
            catch (Exception e)
            {
                LogService.LogWarning($"政体配置 {filePath} 格式有误，整个文件跳过: {e.Message}");
                return;
            }
            foreach (JProperty property in root.Properties())
            {
                if (property.Value is not JObject spec) continue;
                RegimeType type, behaves;
                bool variant = false;
                if (Enum.TryParse(property.Name, false, out type) && Enum.IsDefined(typeof(RegimeType), type)) behaves = type;
                else if ((string)spec["base"] is string baseName && Enum.TryParse(baseName, true, out behaves) && Enum.IsDefined(typeof(RegimeType), behaves))
                {
                    type = RegimeVariants.Register(property.Name, behaves);
                    variant = true;
                }
                else
                {
                    LogService.LogWarning($"政体 {property.Name}({filePath})不是已有政体，也没写 base(它的行为同哪个已有政体)，已跳过");
                    continue;
                }
                try
                {
                    var copy = (JObject)spec.DeepClone();
                    copy.Remove("base");
                    var armies = Tidy(copy, property.Name);
                    Regime regime = copy.ToObject<Regime>(JsonSerializer.CreateDefault());
                    if (regime == null) continue;
                    regime.type = behaves;
                    if (armies != null && regime.bureau_config != null) regime.bureau_config.armies = armies;
                    NormalizeRegime(regime);
                    regimes[type] = regime;
                    LogService.LogInfo("加载政体完成: " + property.Name + (variant ? "(变体，行为同 " + behaves + ")" : "") + " 来自 " + filePath);
                }
                catch (Exception e)
                {
                    LogService.LogWarning($"政体 {property.Name}({filePath})读取失败，已跳过: {e.Message}");
                }
            }
        }

        // Leaves out the kinds of realm and city the code does not know (with a note) and takes the army titles out to be
        // read by name; returns those (null when the file names none).
        private static Dictionary<ArmyOfficialType, BureauSetting> Tidy(JObject spec, string regime)
        {
            if (spec["bureau_config"] is not JObject bureau) return null;
            void Known<TEnum>(string field, string what) where TEnum : struct, Enum
            {
                if (bureau[field] is not JObject map) return;
                foreach (JProperty kind in map.Properties().ToList())
                    if (!Enum.TryParse(kind.Name, false, out TEnum value) || !Enum.IsDefined(typeof(TEnum), value))
                    {
                        LogService.LogWarning($"政体 {regime} 的{what} {kind.Name} 代码里没有，已略过(变体政体用基础政体的{what}，自己的叫法写在 OfficialType.csv)");
                        kind.Remove();
                    }
            }
            Known<KingdomType>("kingdoms", "国家类别");
            Known<CityType>("cities", "城市类别");
            if (bureau["armies"] is not JObject armies) return null;
            bureau.Remove("armies");
            var result = new Dictionary<ArmyOfficialType, BureauSetting>();
            foreach (JProperty title in armies.Properties())
            {
                BureauSetting setting = title.Value.ToObject<BureauSetting>(JsonSerializer.CreateDefault());
                if (setting != null) result[ArmyTitles.Of(title.Name)] = setting;
            }
            return result;
        }

        public static bool IsPrimitive(RegimeType? type) =>
            type.HasValue && RegimeVariants.BaseOf(type.Value) is RegimeType.Origin or RegimeType.HuaxiaOrigin;
    }
}
