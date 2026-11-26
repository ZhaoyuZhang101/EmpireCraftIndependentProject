// Generated from the EmpireCraft mod: Scripts/GeneralSystems/TemplateNameRepair.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EmpireCraft.Core
{

// 字表名混进名字的修复(用户 2026-10-27 截图：教宗叫"Unitnamesmale1"、皇帝叫"Clannames清水明日香")。
// 文化起名模板的各组本该是字表(Locales/Cultures/Culture_X/XUnitNamesMale1.csv)里的字，读不到字表时以前把字表名本身
// 写进了模板(见 CulturePatch.setGroup)，原版起名器照着起出"Unitnamesmale1""Clannames 氏""Countrynames"。
// 模板随原版存档存下，读档只给记了绑定的文化重套模板，没记绑定的旧档就一直坏着，新生的人、新起的氏族都是这种名字。
// 读档时：坏了的模板重套；在世的人、氏族、家族、宗族与族谱、名册里的这类名字按各自文化重起。史书里已写下的旧名不改。
// 原版按物种起的英文名(Foxen、Vien……)同样算没按文明起名(用户 10-29 "姓氏为什么始终没有按照文明来同步"：日本的征夷大将军叫
// "Foxen正")：中文界面下各文明的名都是汉字，名字里带英文字母的就是原版起的，按本人文明重起；除读档外每年也查一遍(见 NameCultureRepair)
public static class TemplateNameRepair
{
    private static readonly Regex RawName = new("(?:unit|clan|city|country|family|middenclan)(?:nick)?names",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private const int Retries = 5;

    // 读档时族谱里每个人都要看几遍：先按子串粗筛，含"names"的才上正则
    public static bool IsRaw(string name) => !string.IsNullOrEmpty(name) &&
                                             (name.IndexOf("names", StringComparison.OrdinalIgnoreCase) >= 0 && RawName.IsMatch(name) ||
                                              Vanilla(name));

    // 中文界面下带英文字母的名字：原版按物种起的，没按文明起名(英文界面各文明的名本来就是字母，不算)
    private static bool Vanilla(string name)
    {
        string language = LM.Language;
        if (language != "cz" && language != "ch") return false;
        foreach (char c in name)
            if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z') return true;
        return false;
    }

    // 起名模板坏了的文化重套模板(读档读完文化绑定之后)
#if EC_PENDING
    public static void RepairCultures()
    {
        if (EmpireWorld.Current?.Cultures == null) return;
        int repaired = 0;
        foreach (Culture culture in EmpireWorld.Current.Cultures)
        {
            if (culture?.data == null || !HasRawTemplate(culture)) continue;
            string name = Cultures.FamilyOf(culture);
            if (!CultureService.IsValidCulture(name)) continue;
            Cultures.SetFamily(culture, name, addTraits: false);
            repaired++;
        }
        if (repaired > 0) LogService.LogInfo($"[EmpireCraft][起名修复] {repaired} 个文化的起名模板混进了字表名，已重套");
    }
#endif

    private static readonly EntityKind[] NamedTypes =
        { EntityKind.Realm, EntityKind.Clan, EntityKind.Family, EntityKind.City, EntityKind.Person };

#if EC_PENDING
    private static bool HasRawTemplate(Culture culture)
    {
        foreach (EntityKind type in NamedTypes)
        {
            NameParts data = Cultures.NameParts(culture, type);
            if (data?.groups == null) continue;
            foreach (var group in data.groups)
                if (IsRaw(group.Value?.characters_string)) return true;
        }
        return false;
    }
#endif

    // 已经起坏的名字按文化重起(读档读完宗族、族谱之后)
#if EC_PENDING
    public static void RepairNames()
    {
        if (EmpireWorld.Current == null) return;
        int clans = RepairClans();
        int specific = RepairSpecificClans();
        int families = RepairFamilies();
        int actors = RepairActors();
        int people = RepairPeople();
        int kingdoms = RepairKingdoms();
        int talents = FamousTalentSystem.RefreshNames(IsRaw);
        if (clans + specific + families + actors + people + kingdoms + talents > 0)
            LogService.LogInfo($"[EmpireCraft][起名修复] 字表名当成的名字已按文化重起：氏族 {clans}、宗族 {specific}、" +
                               $"家族 {families}、人物 {actors}、族谱 {people}、国名 {kingdoms}、名册 {talents}");
    }
#endif

    // 原版氏族名("Clannames 氏")：按族人的文化重起，宗族的姓跟着改
    private static int RepairClans()
    {
        int count = 0;
        foreach (Clan clan in EmpireWorld.Current.Clans)
        {
            if (clan?.Record == null || AncientWarfareCompatibility.OwnsObject(clan.People.FirstOrDefault())) continue;
            if (IsRaw(clan.Name))
            {
                NameParts names = Cultures.NameParts(CultureOf(clan), EntityKind.Clan);
                string name = Generate(() => names?.GenerateName()?.UseLocalizedNameSeparator());
                if (string.IsNullOrEmpty(name)) continue;
                clan.Name = name;
                count++;
            }
            SpecificClan specific = clan.GetSpecificClan();
            if (specific != null && IsRaw(specific.name) && !IsRaw(clan.GetClanName())) specific.name = clan.GetClanName();
        }
        return count;
    }

    // 没有原版氏族的宗族(整族虚拟、或氏族已散)：按族人的文化起个姓
    private static int RepairSpecificClans()
    {
        int count = 0;
        foreach (SpecificClan specific in SpecificClanManager._specificClans.ToList())
        {
            if (specific == null || !IsRaw(specific.name)) continue;
            string culture = specific.SnapshotPeople()
                .FirstOrDefault(person => CultureService.IsValidCulture(person?.culture))?.culture;
            string surname = Generate(() => SurnameOf(CultureService.GenerateCulturalName(EntityKind.Clan, specific.id, culture), culture));
            if (string.IsNullOrEmpty(surname)) continue;
            specific.name = surname;
            count++;
        }
        return count;
    }

    // 氏族名里的姓(同 ClanExtension.GetClanName：按文化配置的 name_pos 取一段)
    private static string SurnameOf(string clanName, string culture)
    {
        string[] parts = (clanName ?? "").SplitNameParts();
        if (parts.Length == 0) return null;
        int position = OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture ?? "", out Setting setting) &&
                       setting?.Clan != null && setting.Clan.name_pos >= 0 && setting.Clan.name_pos < parts.Length
            ? setting.Clan.name_pos
            : 0;
        return parts[position];
    }

    // 原版家族名("某城 Clannames 家")：城名 + 宗族的姓 + 家
#if EC_PENDING
    private static int RepairFamilies()
    {
        int count = 0;
        foreach (Family family in EmpireWorld.Current.families)
        {
            if (family?.Record == null || !IsRaw(family.Record.Name)) continue;
            Person member = family.units.FirstOrDefault(actor => actor?.data != null && !actor.IsGone());
            // 动物的群(原版也叫家族)不按文明起名
            if (member == null || AncientWarfareCompatibility.OwnsObject(member) || member.Realm?.IsCivilized() != true) continue;
            string surname = member.HasClan() ? member.Clan.GetMemberSurname() : null;
            string name;
            if (!string.IsNullOrEmpty(surname) && !IsRaw(surname))
            {
                name = OverallHelperFunc.JoinNameParts(member.City?.Name ?? "", surname, LM.Get("Family"));
                family.Record.Name = name;
                family.SetFamilyCityPre(member.City?.Record != null);
            }
            else
            {
                NameParts names = Cultures.NameParts(member.Culture, EntityKind.Family);
                name = Generate(() => names?.GenerateName()?.UseLocalizedNameSeparator());
                if (string.IsNullOrEmpty(name)) continue;
                family.Record.Name = name;
                family.SetFamilyCityPre(false);
            }
            count++;
        }
        return count;
    }
#endif

    // 实体人物：名坏了按文化重起，姓坏了取氏族、家族的姓(同 ActorPatch.set_actor_culture)
#if EC_PENDING
    private static int RepairActors()
    {
        int count = 0;
        foreach (Person actor in EmpireWorld.Current.Living)
        {
            if (actor?.Record == null || actor.IsGone() || AncientWarfareCompatibility.OwnsObject(actor)) continue;
            // 动物不按文明起名
            if (actor.Realm?.IsCivilized() != true) continue;
            Name name = actor.GetModName();
            if (name == null) continue;
            bool first = IsRaw(name.firstName), family = IsRaw(name.familyName);
            if (!first && !family && !IsRaw(actor.Name)) continue;
            Culture culture = actor.Culture ?? CultureService.GetNativeCultureObject(CultureService.GetActorCulture(actor));
            if (first)
            {
                NameParts names = Cultures.NameParts(culture, EntityKind.Person);
                string given = PrehistoricClanSystem.AncientGivenName(TechnologySystem.GetCultureOf(actor), actor.IsMale()) ??
                               Generate(() => names?.GenerateName(actor.IsMale() ? Sex.Male : Sex.Female));
                if (given != null) name.firstName = given;
            }
            if (family) name.familyName = Surname(actor, culture) ?? "";
            name.SetName(actor);
            count++;
        }
        return count;
    }

    private static string Surname(Person actor, Culture culture)
    {
        string surname = actor.HasClan() ? actor.Clan.GetMemberSurname() : null;
        if (string.IsNullOrEmpty(surname) || IsRaw(surname)) surname = actor.HasFamily() ? actor.Family.GetFamilyName() : null;
        if (!string.IsNullOrEmpty(surname) && !IsRaw(surname)) return surname;
        NameParts names = Cultures.NameParts(culture, EntityKind.Family);
        return SurnameOf(Generate(() => names?.GenerateName()), CultureService.GetActorCulture(actor));
    }
#endif

    // 族谱里的人(含已故、虚拟族人)：在世有实体的跟实体，否则名按文化重起、姓取宗族的姓
    private static int RepairPeople()
    {
        int count = 0;
        foreach (SpecificClan specific in SpecificClanManager._specificClans.ToList())
        {
            if (specific == null) continue;
            foreach (PersonalClanIdentity person in specific.SnapshotPeople())
            {
                if (person == null) continue;
                bool given = IsRaw(person.given_name), surname = IsRaw(person.surname), clanName = IsRaw(person.clanName),
                    household = IsRaw(person.familyName);
                if (!given && !surname && !clanName && !household && !IsRaw(person.name)) continue;
                Person actor = person.is_alive ? person._actor : null;
                if (actor?.Record != null && !actor.IsGone() && !IsRaw(actor.Name))
                {
                    person.given_name = actor.GetModName()?.firstName ?? person.given_name;
                    person.surname = actor.GetModName()?.familyName ?? person.surname;
                    person.name = actor.FullName();
                }
                else
                {
                    if (given)
                    {
                        string generated = Generate(() => CultureService.GenerateCulturalName(EntityKind.Person, person.id, person.culture, person.sex));
                        string[] parts = (generated ?? "").SplitNameParts();
                        person.given_name = parts.Length > 0 ? parts[parts.Length - 1] : generated ?? "";
                    }
                    if (surname || string.IsNullOrWhiteSpace(person.surname))
                        person.surname = IsRaw(specific.name) ? "" : specific.name ?? "";
                    person.name = string.IsNullOrWhiteSpace(person.surname) ? person.given_name
                        : OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(person.culture ?? "", out Setting rule) && rule?.Unit?.is_invert == true
                            ? OverallHelperFunc.JoinNameParts(person.given_name, person.surname)
                            : OverallHelperFunc.JoinNameParts(person.surname, person.given_name);
                }
                if (clanName) person.clanName = IsRaw(specific.name) ? "" : specific.name ?? "";
                // 户名(某某家)：姓 + 家
                if (household)
                    person.familyName = IsRaw(person.surname) || string.IsNullOrWhiteSpace(person.surname) ? ""
                        : OverallHelperFunc.JoinNameParts(person.surname, LM.Get("Family"));
                count++;
            }
        }
        return count;
    }

    // 国家记下的初始随机国名("Countrynames")清掉，读档整理国家时重起(见 KingdomExtension.GetInitialRandomKingdomName)
    private static int RepairKingdoms()
    {
        int count = 0;
        foreach (Realm kingdom in EmpireWorld.Current.LiveRealms)
        {
            if (kingdom?.Record == null || AncientWarfareCompatibility.Owns(kingdom)) continue;
            KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
            if (!IsRaw(data.initial_random_name)) continue;
            data.initial_random_name = "";
            count++;
        }
        return count;
    }

    private static Culture CultureOf(Clan clan)
    {
        Culture culture = clan.People.FirstOrDefault(actor => actor?.Culture != null)?.Culture;
        return culture ?? CultureService.GetNativeCultureObject(OverallHelperFunc.GetCultureFromSpecies(clan.SpeciesId));
    }

    // 模板才重套过，一般一次就好；万一还是字表名就再起几次，都不行返回 null(调用方保持原样)
    private static string Generate(Func<string> generate)
    {
        for (int attempt = 0; attempt < Retries; attempt++)
        {
            string name;
            try { name = generate(); }
            catch (Exception) { return null; }
            if (!string.IsNullOrWhiteSpace(name) && !IsRaw(name)) return name;
        }
        return null;
    }
}
}
