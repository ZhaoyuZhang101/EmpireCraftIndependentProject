using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace EmpireCraft.Core
{
    // Regimes a regime file adds beyond the ones the code knows: a variant of a known regime (user 2026-11-28, plan A).
    //
    // A variant is written in a regime folder like any regime (Scripts/Regimes/Configs/<folder>/SystemConfig.json), under a
    // name of its own, with "base" naming the known regime it behaves as. Everything the code decides by regime follows the
    // base (a realm's regime object carries the base type, so every check of a regime's type sees the base); its name, its
    // offices, the names of its kinds of realm and city, its factions come from its own file. Its own wording is looked up
    // first under its own name (<variant>_<kind of realm> for a kind of realm, <variant>_officiallevel_<n> for a rank) in
    // its folder's OfficialType.csv, then the base's.
    //
    // A realm keeps the variant as its regime type: a value of the regime enum of the variant's own, outside the named
    // ones, made from the variant's name so it is the same in every run and in a save (a clash moves it on by one).
    // Culture rules and institution trees may name a variant wherever they name a regime.
    //
    // Army titles a regime file names that the code does not know are kept the same way (ArmyTitles): nothing decides by
    // them, they are only shown.
    public static class RegimeVariants
    {
        const int First = 100000, Span = 800000;
        static readonly Dictionary<string, RegimeType> ByName = new Dictionary<string, RegimeType>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<RegimeType, (string name, RegimeType baseType)> ByValue = new Dictionary<RegimeType, (string, RegimeType)>();

        public static RegimeType Register(string name, RegimeType baseType)
        {
            if (ByName.TryGetValue(name, out RegimeType known)) { ByValue[known] = (name, baseType); return known; }
            int value = First + (int)(Hash(name) % Span);
            while (Enum.IsDefined(typeof(RegimeType), (RegimeType)value) || ByValue.ContainsKey((RegimeType)value)) value++;
            var type = (RegimeType)value;
            ByName[name] = type;
            ByValue[type] = (name, baseType);
            return type;
        }

        public static bool IsVariant(RegimeType type) => ByValue.ContainsKey(type);
        public static RegimeType BaseOf(RegimeType type) => ByValue.TryGetValue(type, out var v) ? v.baseType : type;
        public static string NameOf(RegimeType type) => ByValue.TryGetValue(type, out var v) ? v.name : type.ToString();
        public static bool IsKnown(RegimeType type) => Enum.IsDefined(typeof(RegimeType), type) || ByValue.ContainsKey(type);
        public static IEnumerable<string> Names => ByName.Keys;

        // A regime by name: a known one, or a variant some regime file adds.
        public static bool TryParse(string name, out RegimeType type)
        {
            type = default;
            if (string.IsNullOrWhiteSpace(name)) return false;
            name = name.Trim();
            if (Enum.TryParse(name, true, out RegimeType known) && Enum.IsDefined(typeof(RegimeType), known)) { type = known; return true; }
            return ByName.TryGetValue(name, out type);
        }

        // The variant a realm's regime is, or null for a known regime.
        public static string VariantOf(Realm realm)
        {
            if (realm?.Record == null) return null;
            RegimeType type = realm.GetOrCreate().regimeType;
            return ByValue.TryGetValue(type, out var v) ? v.name : null;
        }

        // The text key of a kind of realm: the variant's own wording first (<variant>_<kind>, the kind without its regime's
        // name in front), else as the mod names it (DynastyNaming).
        public static string KingdomTypeKey(Realm kingdom, KingdomType type)
        {
            string variant = VariantOf(kingdom);
            if (variant != null)
            {
                string kind = type.ToString(); int cut = kind.IndexOf('_');
                string key = variant + (cut >= 0 ? kind.Substring(cut) : "_" + kind);
                if (LM.Has(key)) return key;
            }
            return DynastyNaming.KingdomTypeKey(kingdom, type);
        }

        // The text key of a rank: the variant's own (<variant>_officiallevel_<n>) first, else as the mod names it.
        public static string RankKey(RegimeType regime, int officeType, Entity meta)
        {
            Realm owner = meta switch { Realm k => k, City town => town.Realm, _ => null };
            string variant = VariantOf(owner) ?? (ByValue.TryGetValue(regime, out var v) ? v.name : null);
            if (variant != null && LM.Has(variant + "_officiallevel_" + officeType)) return variant + "_officiallevel_" + officeType;
            return DynastyNaming.RankKey(BaseOf(regime), officeType, meta);
        }

        // The text key of a realm's regime name: the variant's name when its folder gives it a text, else the base's.
        public static string DisplayKey(Realm realm)
        {
            string variant = VariantOf(realm);
            if (variant != null && LM.Has(variant)) return variant;
            return realm?.Record == null ? null : BaseOf(realm.GetOrCreate().regimeType).ToString();
        }

        static uint Hash(string name)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in name.ToLowerInvariant()) { h ^= c; h *= 16777619; }
                return h;
            }
        }

        // Reads a regime written by name (culture rules, saves of the mod's data) as a known regime or a variant. Numbers
        // read as they are. Writing is left as it was (a number).
        public sealed class Reader : JsonConverter
        {
            public override bool CanWrite => false;
            public override bool CanConvert(Type objectType) => objectType == typeof(RegimeType) || objectType == typeof(RegimeType?);
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => throw new NotSupportedException();
            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return objectType == typeof(RegimeType?) ? null : (object)default(RegimeType);
                if (reader.TokenType == JsonToken.Integer) return (RegimeType)Convert.ToInt32(reader.Value);
                string name = reader.Value?.ToString();
                if (TryParse(name, out RegimeType type)) return type;
                throw new JsonSerializationException("政体 " + name + " 既不是已有政体，也没有哪个政体配置以它为名(变体政体要在 SystemConfig.json 里写 base)");
            }
        }
    }

    // Army titles a regime file names beyond the ones the code knows, kept by name with a value of their own (as the variants).
    public static class ArmyTitles
    {
        static readonly Dictionary<string, ArmyOfficialType> ByName = new Dictionary<string, ArmyOfficialType>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<ArmyOfficialType, string> Names = new Dictionary<ArmyOfficialType, string>();

        public static ArmyOfficialType Of(string name)
        {
            if (Enum.TryParse(name, true, out ArmyOfficialType known) && Enum.IsDefined(typeof(ArmyOfficialType), known)) return known;
            if (ByName.TryGetValue(name, out ArmyOfficialType kept)) return kept;
            uint h = 2166136261; unchecked { foreach (char c in name.ToLowerInvariant()) { h ^= c; h *= 16777619; } }
            int value = 100000 + (int)(h % 800000);
            while (Enum.IsDefined(typeof(ArmyOfficialType), (ArmyOfficialType)value) || Names.ContainsKey((ArmyOfficialType)value)) value++;
            var type = (ArmyOfficialType)value;
            ByName[name] = type; Names[type] = name;
            return type;
        }

        // The name an army title is written under (its text key).
        public static string NameOf(ArmyOfficialType type) => Names.TryGetValue(type, out string name) ? name : type.ToString();
    }
}
