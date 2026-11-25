// Generated from the EmpireCraft mod: Scripts/HelperFunc/OverallHelperFunc.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EmpireCraft.Core
{
    public static class OverallHelperFunc
    {
        public static class IdGenerator
        {
            private static long _lastId = DateTime.UtcNow.Ticks;

            public static long NextId()
            {
                return Interlocked.Increment(ref _lastId);
            }
        }
        public static string GetCultureFromSpecies(string species)
        {
            // 物种未知(读档途中王国数据还没就绪等)时返回空：调用方按"文化无效"处理、之后再解析。
            // 不能兜底成西方——GetRealmCulture 会把它存成王国文化并扩散到全图，旧存档的华夏会整片变成西方
            if (string.IsNullOrWhiteSpace(species)) return "";
            if (ConfigData.speciesCulturePair.TryGetValue(species, out var insertCulture))
            {
                return insertCulture ?? "";
            }
            else
            {
                return "";
            }
        }
        public static string GetLocal(this string key)
        {
            return LM.Get(key);
        }
        public static string AppendWithNarrowSpace(this string textA, string textB)
        {
            return JoinNameParts(textA, textB);
        }

        public static string NamePartSeparator
        {
            get
            {
                // English uses a paired narrow-space separator for compatibility with
                // the mod's existing name parsers; CJK names remain compact.
                return IsEnglishLanguage()
                    ? ModClass.NARROW_SPACE + ModClass.NARROW_SPACE
                    : ModClass.NARROW_SPACE;
            }
        }

        public static bool IsEnglishLanguage()
        {
            string language = LM.Language;
            return !string.IsNullOrWhiteSpace(language) &&
                   language == "en";
        }

        public static string JoinNameParts(params string[] parts)
        {
            if (parts == null || parts.Length == 0) return "";
            return string.Join(NamePartSeparator, parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        private static readonly char[] NarrowSplit = { '\u200A' };

        public static string[] SplitNameParts(this string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
            // 不带分隔的名字最多(一段)：直接回，不走切分(这个函数拼国名时一帧要调上千回)
            if (text.IndexOf('\u200A') < 0) return new[] { text.Trim() };
            string[] parts = text.Split(NarrowSplit, StringSplitOptions.RemoveEmptyEntries);
            int kept = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length > 0) parts[kept++] = part;
            }
            if (kept != parts.Length) Array.Resize(ref parts, kept);
            return parts;
        }

        public static string ReduceNarrowSpaces(this string text)
        {
            return text.UseLocalizedNameSeparator();
        }

        public static string UseLocalizedNameSeparator(this string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string[] parts = text.SplitNameParts();
            return parts.Length <= 1 ? text.Trim() : JoinNameParts(parts);
        }

        public static string WrapEnglishDisplayName(string text, float maxLineWeight = 11f)
        {
            if (!IsEnglishLanguage() || string.IsNullOrWhiteSpace(text) || text.IndexOf('\n') >= 0) return text ?? "";
            string[] words = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2 || MeasureEnglishText(text) <= maxLineWeight) return text;

            int splitIndex = -1;
            for (int i = 1; i < words.Length - 1; i++)
            {
                if (string.Equals(words[i], "of", StringComparison.OrdinalIgnoreCase))
                {
                    splitIndex = i + 1;
                    break;
                }
            }
            if (splitIndex < 1 || splitIndex >= words.Length)
            {
                float bestDifference = float.MaxValue;
                for (int i = 1; i < words.Length; i++)
                {
                    float left = MeasureEnglishText(string.Join(" ", words.Take(i)));
                    float right = MeasureEnglishText(string.Join(" ", words.Skip(i)));
                    float difference = Mathf.Abs(left - right);
                    if (difference >= bestDifference) continue;
                    bestDifference = difference;
                    splitIndex = i;
                }
            }
            if (splitIndex < 1 || splitIndex >= words.Length) return text;
            return JoinNameParts(words.Take(splitIndex).ToArray()) + "\n" +
                   JoinNameParts(words.Skip(splitIndex).ToArray());
        }

        private static float MeasureEnglishText(string text)
        {
            float width = 0f;
            if (string.IsNullOrEmpty(text)) return width;
            for (int i = 0; i < text.Length; i++)
            {
                char character = text[i];
                if (char.IsWhiteSpace(character)) width += 0.35f;
                else if (char.IsUpper(character)) width += 0.72f;
                else width += 0.58f;
            }
            return width;
        }

        public static bool UsesEnglishCountryTypePrefix(string cultureName)
        {
            if (!IsEnglishLanguage()) return false;
            if (!string.IsNullOrWhiteSpace(cultureName) &&
                OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(cultureName, out Setting setting) &&
                setting?.Kingdom != null)
            {
                return setting.Kingdom.english_type_prefix;
            }

            // New cultures and species should follow English grammar unless their
            // culture configuration explicitly opts into suffix-style names.
            return true;
        }

        public static string FormatCountryTypeName(string coreName, string typeName, string cultureName)
        {
            coreName = coreName.UseLocalizedNameSeparator();
            if (string.IsNullOrWhiteSpace(typeName)) return coreName;
            if (UsesEnglishCountryTypePrefix(cultureName)) return JoinNameParts("The", typeName, "of", coreName);
            // 地名本身已带类别字时不再重复(唐制"道"：关内道 + 道 → 关内道，而不是"关内道 道"；同 FormatKingdomFullName)
            return coreName != null && coreName.TrimEnd().EndsWith(typeName.Trim(), global::System.StringComparison.Ordinal)
                ? coreName
                : JoinNameParts(coreName, typeName);
        }

        public static bool TryExtractEnglishPrefixedCountryName(string fullName, out string coreName)
        {
            coreName = "";
            if (string.IsNullOrWhiteSpace(fullName)) return false;
            Match match = Regex.Match(fullName.Trim(), @"^The\s+(.+?)\s+of\s+(.+)$", RegexOptions.IgnoreCase);
            if (!match.Success) return false;
            coreName = match.Groups[2].Value.Trim();
            return !string.IsNullOrWhiteSpace(coreName);
        }

        public static string FormatKingdomFullName(string coreName, string typeName, string cultureName)
        {
            if (string.IsNullOrWhiteSpace(coreName)) return "";
            if (string.IsNullOrWhiteSpace(typeName)) return coreName;
            string result;
            if (UsesEnglishCountryTypePrefix(cultureName))
            {
                result = JoinNameParts("The", typeName, "of", coreName);
            }
            else
            {
                // 地名本身已带类别字时不再重复(幽州 + 州 → 幽州，而不是"幽州州")
                result = coreName.TrimEnd().EndsWith(typeName.Trim(), global::System.StringComparison.Ordinal)
                    ? coreName
                    : JoinNameParts(coreName, typeName);
            }
            return result.ReduceNarrowSpaces();
        }

        public static string FormatEmpireFullName(string coreName, string typeName, string localizedPrefix,
            string cultureName)
        {
            if (string.IsNullOrWhiteSpace(coreName)) return "";
            if (UsesEnglishCountryTypePrefix(cultureName) && !string.IsNullOrWhiteSpace(typeName))
            {
                return JoinNameParts("The", localizedPrefix, typeName, "of", coreName);
            }
            return JoinNameParts(localizedPrefix, coreName, typeName);
        }

        public static string FormatCityFullName(string coreName, string typeName)
        {
            string result = coreName ?? "";
            if (!string.IsNullOrWhiteSpace(typeName))
            {
                result = JoinNameParts(result, typeName);
            }
            return result.ReduceNarrowSpaces();
        }

        public static string StripLocalizedTypeSuffix(string fullName, string typeName)
        {
            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(typeName)) return fullName?.Trim() ?? "";
            string value = fullName.Trim();
            string[] separators = { ModClass.NARROW_SPACE + ModClass.NARROW_SPACE, ModClass.NARROW_SPACE, " " };
            foreach (string separator in separators)
            {
                string suffix = separator + typeName;
                if (value.EndsWith(suffix, StringComparison.Ordinal))
                    return value.Substring(0, value.Length - suffix.Length).Trim();
            }
            return string.Equals(value, typeName, StringComparison.Ordinal) ? "" : value;
        }

        public static string ResolveEmpireTypeKey(Regime regime, Realm coreKingdom)
        {
            string key = null;
            if (regime != null && RegimeManager.IsPrimitive(regime.type) && coreKingdom != null &&
                EmpireCraft.Core.DynastyNaming.TribalNaming(coreKingdom))
            {
                key = EmpireCraft.Core.DynastyNaming.HuaxiaOriginEmpireKey(coreKingdom);
            }
            else if (regime != null && regime.centre_empire_separate)
            {
                key = $"{regime.type}_empire";
                // 元首制：共和国改称帝国(见 Empire.IsPrincipate)
                if (regime.type == RegimeType.ClassicalRepublic && coreKingdom?.GetEmpire() is EmpireCraft.Core.Empire principate &&
                    principate.IsPrincipate() && LM.Has(key + "_principate"))
                    key += "_principate";
                // 文化专属称谓(阿兹特克不称匈鲁斯……见 DynastyNaming.CultureVariant)
                if (coreKingdom != null &&
                    EmpireCraft.Core.DynastyNaming.CultureVariant(coreKingdom, key) is string cultureKey)
                    key = cultureKey;
                // 日本线：部族连合(进入大和王权后称王权)、朝廷(见 DynastyNaming.LineSuffix)
                else if (coreKingdom != null && EmpireCraft.Core.DynastyNaming.LineSuffix(coreKingdom) is string lineSuffix)
                {
                    string staged = regime.type == RegimeType.Origin &&
                                    EmpireCraft.Core.InstitutionSystem.IsEnacted(
                                        EmpireCraft.Core.CultureService.GetRealmCulture(coreKingdom), "japan_yamato_kingship")
                        ? key + lineSuffix + "_yamato"
                        : key + lineSuffix;
                    if (LM.Has(staged)) key = staged;
                }
            }
            else if (coreKingdom != null && !coreKingdom.IsGone())
            {
                key = EmpireCraftKingdomBehCheckKingdomType.CalcKingdomType(coreKingdom).ToString();
                // 文化专属称谓(印度不称哈里发国……见 DynastyNaming.CultureVariant)
                key = EmpireCraft.Core.DynastyNaming.CultureVariant(coreKingdom, key) ?? key;
            }

            if (string.IsNullOrWhiteSpace(key)) return "EmpireText";
            string translated = LM.Get(key);
            if (string.IsNullOrWhiteSpace(translated) || string.Equals(translated, key, StringComparison.Ordinal))
                return "EmpireText";
            return key;
        }

        private static readonly Dictionary<string, string> DirectPrefixKeyMap = new()
        {
            { "神圣", "empire_prefix_holy" },
            { "神聖", "empire_prefix_holy" },
            { "Holy", "empire_prefix_holy" },
            { "大", "great" },
            { "Great", "great" },
            { "西", "Western" },
            { "West", "Western" },
            { "东", "Eastern" },
            { "東", "Eastern" },
            { "East", "Eastern" },
            { "南", "Southern" },
            { "South", "Southern" },
            { "北", "Northern" },
            { "North", "Northern" },
            { "前", "Former" },
            { "Former", "Former" },
            { "后", "Later" },
            { "後", "Later" },
            { "Later", "Later" },
        };

        public const string HolyPrefixKey = "empire_prefix_holy";

        // 国号前缀是教宗授的"神圣"(神授君权，见 TempFac_神授君权)：不是方位词，西方第二帝国、失都改称都不去掉它
        public static bool IsHolyPrefix(EmpireData data)
        {
            string chosen = data?.prefix_manual == true ? data.prefix_choice : data?.directPre;
            if (string.IsNullOrWhiteSpace(chosen)) return false;
            string raw = chosen.Trim();
            return (DirectPrefixKeyMap.TryGetValue(raw, out string mapped) ? mapped : raw) == HolyPrefixKey;
        }

        // 国号冠的是开国之君的姓(前朝用过的国号再用、世界法则「姓氏国号」开着，见 RealmNamingSystem.ReusePrefix)：
        // 不当前缀，放在国号后面的括号里，如"秦朝(刘)"(用户 2026-10-27 定)。姓不是方位、"后"、"神圣"这类前缀词，也没有翻译键
        public static string SurnameOf(EmpireData data)
        {
            if (data == null || data.prefix_manual) return "";
            string raw = data.directPre?.Trim();
            if (string.IsNullOrEmpty(raw) || !string.Equals(raw, data.founding_prefix?.Trim(), StringComparison.Ordinal)) return "";
            if (DirectPrefixKeyMap.ContainsKey(raw) || raw == HolyPrefixKey || LM.Has(raw)) return "";
            return raw;
        }

        // 国号后面的"(姓)"，没有为空
        public static string SurnameSuffix(EmpireData data)
        {
            string surname = SurnameOf(data);
            return surname.Length == 0 ? "" : "(" + surname + ")";
        }

        public static string LocalizeDirectPrefix(EmpireData data)
        {
            // 帝国设置里手选了前缀：以它为准，系统自动加的方位词不再生效
            string chosen = data?.prefix_manual == true ? data.prefix_choice : data?.directPre;
            if (data == null || string.IsNullOrWhiteSpace(chosen)) return "";
            // 开国之君的姓放在国号后面(见 SurnameSuffix)
            if (SurnameOf(data).Length > 0) return "";
            string raw = chosen.Trim();
            string key = DirectPrefixKeyMap.TryGetValue(raw, out string mapped) ? mapped : raw;
            // 没有这个翻译键的不查(查了原版要报"missing text")
            string translated = LM.Has(key) ? LM.Get(key) : "";
            if (!string.IsNullOrWhiteSpace(translated) && !string.Equals(translated, key, StringComparison.Ordinal))
            {
                return translated;
            }
            return IsEnglishLanguage() ? "" : raw;
        }
#if EC_PENDING
        public static EmpireAddition CalcPower(this Person officer, OfficerPowerType type, Empire empire)
        {
            EmpireAddition additions = new();
            switch (type)
            {
                case OfficerPowerType.审核:
                    if (!officer.IsGone())
                    {
                        if (officer.IsSameFactionWithEmpire(empire))
                        {
                            additions.addition[OfficerPowerType.审核] = officer.Intelligence*2;
                        }
                        else
                        {
                            if (officer.GetFaction() == null)
                            {
                                additions.addition[OfficerPowerType.审核] = officer.Intelligence;
                            }
                            else
                            {
                                additions.addition[OfficerPowerType.审核] = -officer.Intelligence;
                            }
                        }
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.审核] = 0;
                    }
                    break;
                case OfficerPowerType.军事:
                    if (officer != null)
                    {
                        additions.addition[OfficerPowerType.军事] = officer.Warfare;
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.军事] = 0;
                    }
                    break;
                case OfficerPowerType.建设:
                    if (officer != null)
                    {
                        additions.addition[OfficerPowerType.建设] = officer.Stewardship;
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.建设] = 0;
                    }
                    break;
                case OfficerPowerType.教育:
                    if (officer != null)
                    {
                        additions.addition[OfficerPowerType.教育] = officer.Intelligence;
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.教育] = 0;
                    }
                    break;
                case OfficerPowerType.天子护理:
                    if (officer != null)
                    {
                        additions.addition[OfficerPowerType.天子护理] = officer.level*5;
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.天子护理] = 0;
                    }
                    break;
                case OfficerPowerType.天子政教:
                    if (officer != null)
                    {
                        additions.addition[OfficerPowerType.天子政教] = officer.level*5;
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.天子政教] = 0;
                    }
                    break;
                case OfficerPowerType.天子智教:
                    if (officer != null)
                    {
                        additions.addition[OfficerPowerType.天子智教] = officer.Intelligence;
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.天子智教] = 0;
                    }
                    break;
                case OfficerPowerType.宗教:
                    if (officer != null)
                    {
                        additions.addition[OfficerPowerType.宗教] = officer.Intelligence;
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.宗教] = 0;
                    }
                    break;
                case OfficerPowerType.礼仪:
                    if (officer != null)
                    {
                        additions.addition[OfficerPowerType.礼仪] = officer.Stewardship;
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.礼仪] = 0;
                    }
                    break;
                case OfficerPowerType.财政:
                    if (officer != null)
                    {
                        additions.addition[OfficerPowerType.财政] = officer.Stewardship;
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.财政] = 0;
                    }
                    break;
                case OfficerPowerType.人事:
                    if (officer != null)
                    {
                        additions.addition[OfficerPowerType.人事] = officer.Stewardship;
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.人事] = 0;
                    }
                    break;
                case OfficerPowerType.生育:
                    if (officer != null)
                    {
                        // 为皇帝所生的在世子女，按族谱数(虚拟族人也算；以前只数地图上的、又靠 getParents 认父亲)
                        long emperorIdentity = empire?.Emperor?.GetPersonalIdentity()?.id ?? -1L;
                        additions.addition[OfficerPowerType.生育] = EmpireCraft.Core.KinRoster.Children(officer)
                            .Count(child => emperorIdentity > 0 && (child.father == emperorIdentity || child.mother == emperorIdentity) ||
                                            EmpireCraft.Core.KinRoster.OnMap(child) &&
                                            child._actor.GetParents().Any(p => p.IsEmperor()));
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.生育] = 0;
                    }
                    break;
                }
            return additions;
        }
#endif
        public static bool IsSameFactionWithEmpire(this Person pActor, Empire empire)
        {
            var dominate = empire.CoreKingdom.GetRegime().GetDominateFaction();
            if (pActor != null)
            {
                if (pActor.GetFaction() == dominate)
                {
                    return true;
                }
            }
            return false;
        }
#if EC_PENDING
        public static void SetFamilyCityPre(this Family family, bool has_pre = true)
        {
            if (family.Record.custom_data_bool == null)
            {
                family.Record.custom_data_bool = new CustomDataContainer<bool>();
            }
            family.Record.custom_data_bool.dict["has_city_pre"] = has_pre;
        }
        public static bool HasBeenSetBefored(this Family family)
        {
            if (family.Record.custom_data_bool == null)
            {
                family.Record.custom_data_bool = new CustomDataContainer<bool>();
            }
            return family.Record.custom_data_bool.dict.ContainsKey("has_city_pre");
        }
#endif
        public static  List<Person> SearchUnitHelper(string content, List<Person> actors)
        {
            List<Person> actorsPool = new List<Person>();
            if (content == "")
            {
                return actorsPool;
            }
            foreach (Person actor in actors)
            {
                if (actor.IsFitToRule())
                {
                    string culture = ConfigData.speciesCulturePair.TryGetValue(actor.Asset.Id, out string culturePair)? culturePair:"Western";
                    string merit = "";
                    string honoraryOfficial = "";
                    string PeeragesLevel = "";
                    string officialLevel = "";
                    string kingdomName = actor.Realm.Name;
                    string cityName = actor.City.Name;
                    string officer = actor.isOfficer() ? "officer" + LM.Get("actor_officer") : "";
                    string name = "";
                    int age = -1;
                    string educationLevel;
                    OfficeIdentity identity = actor.GetIdentity();
                    if (identity!=null)
                    {
                        merit = string.Join("_", culture, "meritlevel", identity.peerageType.ToString(), identity.meritLevel);
                        merit += LM.Get(merit);
                        honoraryOfficial = string.Join("_", culture, "honoraryofficial", identity.peerageType.ToString(), identity.honoraryOfficial);
                        honoraryOfficial += LM.Get(honoraryOfficial) + OfficialRankSystem.SearchText(actor);
                        officialLevel = string.Join("_", culture, identity.officialLevel.ToString());
                        officialLevel += LM.Get(officialLevel);
                    }
                    educationLevel = (actor.HasTrait("jingshi") ? "trait_jingshi" : "") +"/" +(actor.HasTrait("gongshi") ? "trait_gongshi" : "") +"/"+ (actor.HasTrait("juren")?"trait_juren":"");
                    educationLevel += string.Join("/", educationLevel.Split('/').Select(c=>LM.Get(c)));
                    PeeragesLevel = string.Join("_", culture, actor.GetPeeragesLevel().ToString());
                    PeeragesLevel += TranslateHelper.PeerageName(PeeragesLevel);
                    name = actor.FullName();
                    age = actor.Age();
                    List<string> searchContent = new List<string>()
                    {
                        merit, honoraryOfficial, officialLevel, PeeragesLevel, name, age.ToString(), educationLevel, kingdomName, cityName, officer
                    };
                    bool isSatisfied = searchContent.ToList().Any(t =>t.Contains(content))||(int.TryParse(content, out int num) && num>=age);
                    if (isSatisfied) actorsPool.Add(actor);
                }
            }
            return actorsPool;
        }

        public static bool HasChangeToGiveBirth(Person pMotherTarget, Person pFatherTarget)
        {
            int mNum = (int)pMotherTarget.Stats["birth_rate"];
            int fNum = (int)pFatherTarget.Stats["birth_rate"];
            float num2 = 0.5f;
            for (int i = 0; i < (mNum+fNum)/2; i++)
            {
                if (!EcRandom.Chance(num2))
                {
                    return true;
                };
                num2 *= 0.85f;
            }
            return false;
        }
    public static List<(ClanRelation, PersonalClanIdentity)> SearchPersonalClanIdentityHelper(string content, List<(ClanRelation, PersonalClanIdentity)> cIdentities)
        {
            List<(ClanRelation, PersonalClanIdentity)> identityPool = new List<(ClanRelation, PersonalClanIdentity)>();
            if (content == "")
            {
                return cIdentities;
            }
            foreach (var cIdentity in cIdentities)
            {
                string culture = ConfigData.speciesCulturePair.TryGetValue(cIdentity.Item2.species, out string culturePair)? culturePair:"Western";
                string merit = "";
                string honoraryOfficial = "";
                string PeeragesLevel = "";
                string officialLevel = "";
                string kingdomName = "";
                string cityName = "";
                string provinceName = "";
                string officer = "";
                string officeName = "";
                string name = cIdentity.Item2.name;
                string educationLevel = "";
                if (cIdentity.Item2.is_alive && cIdentity.Item2._actor != null)
                {
                    Person actor = cIdentity.Item2._actor;
                    OfficeIdentity identity = actor.GetIdentity();
                    kingdomName = actor.Realm.Name;
                    cityName = actor.City.Name;
                    officer = actor.isOfficer() ? "officer" + LM.Get("actor_officer") : "";
                    if (identity!=null)
                    {
                        merit = string.Join("_", culture, "meritlevel", identity.peerageType.ToString(), identity.meritLevel);
                        merit += LM.Get(merit);
                        honoraryOfficial = string.Join("_", culture, "honoraryofficial", identity.peerageType.ToString(), identity.honoraryOfficial);
                        honoraryOfficial += LM.Get(honoraryOfficial) + OfficialRankSystem.SearchText(actor);
                        officialLevel = string.Join("_", culture, identity.officialLevel.ToString());
                        officialLevel += LM.Get(officialLevel);
                    }
                    educationLevel = (actor.HasTrait("jingshi") ? "trait_jingshi" : "") +"/" +(actor.HasTrait("gongshi") ? "trait_gongshi" : "") +"/"+ (actor.HasTrait("juren")?"trait_juren":"");
                    educationLevel += string.Join("/", educationLevel.Split('/').Select(c=>LM.Get(c)));
                    PeeragesLevel = string.Join("_", culture, actor.GetPeeragesLevel().ToString());
                    officeName = actor.GetOffice()?.GetOfficeName()??"";
                    PeeragesLevel += TranslateHelper.PeerageName(PeeragesLevel);
                }
                else
                {
                    merit = cIdentity.Item2.merit + LM.Get(cIdentity.Item2.merit);
                    honoraryOfficial = cIdentity.Item2.honoraryOfficial + LM.Get(cIdentity.Item2.honoraryOfficial);
                    PeeragesLevel = cIdentity.Item2.PeeragesLevel + TranslateHelper.PeerageName(cIdentity.Item2.PeeragesLevel);
                    officialLevel = cIdentity.Item2.officialLevel + LM.Get(cIdentity.Item2.officialLevel);
                    kingdomName = cIdentity.Item2.kingdomName;
                    cityName = cIdentity.Item2.cityName;
                    officeName = cIdentity.Item2.officeName;
                    educationLevel = cIdentity.Item2.educationLevel + string.Join("/", cIdentity.Item2.educationLevel.Split('/').Select(c=>LM.Get(c)));;
                }
                List<string> searchContent = new List<string>()
                {
                    merit, honoraryOfficial, officialLevel, PeeragesLevel, name, educationLevel, kingdomName, cityName, provinceName, officer, officeName
                };
                bool isSatisfied = searchContent.ToList().Any(t =>t.Contains(content));
                if (isSatisfied) identityPool.Add(cIdentity);
            }
            return identityPool;
        }
    }
    
}
