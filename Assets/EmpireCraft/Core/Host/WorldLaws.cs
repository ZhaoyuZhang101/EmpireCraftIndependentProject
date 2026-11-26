using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{
    // A switch over how a world plays ("world law"): a named on/off the systems ask about.
    // The switches are static for now, as the mod's other settings are; they move onto
    // the world (and into its save) when the client gets a place to change them.
    public sealed class WorldLaw
    {
        public readonly string Id;
        public readonly bool Default;
        public bool Enabled;
        internal WorldLaw(string id, bool on) { Id = id; Default = on; Enabled = on; }
        public bool IsEnabled() => Enabled;
        public void Toggle(bool on) { Enabled = on; }
    }

    public static class WorldLaws
    {
        public static readonly List<WorldLaw> All = new List<WorldLaw>();
        static WorldLaw Law(string id, bool on) { var law = new WorldLaw(id, on); All.Add(law); return law; }

        // The mod's own laws with its defaults (EmpireCraftWorldLawLibrary @ 10d66ff6).
        public static readonly WorldLaw RealisticAge = Law("realistic_age", true);
        public static readonly WorldLaw TransmigrantArrival = Law("transmigrant_arrival", false);
        public static readonly WorldLaw PreventCityDestroy = Law("prevent_city_destroy", true);
        public static readonly WorldLaw BanEmpire = Law("ban_empire", false);
        public static readonly WorldLaw SimplifyNameplates = Law("simplify_nameplates", false);
        public static readonly WorldLaw PreventBuildingDestroy = Law("prevent_building_destroy", false);
        public static readonly WorldLaw AllowHarem = Law("allow_harem", true);
        public static readonly WorldLaw AllowSkeleton = Law("allow_skeleton", false);
        public static readonly WorldLaw SwitchOccupyMode = Law("switch_occupy_mode", true);
        public static readonly WorldLaw CorpsGeneral = Law("corps_general", true);
        public static readonly WorldLaw AllowSocial = Law("allow_social", false);
        public static readonly WorldLaw FixedDeJureCulture = Law("fixed_de_jure_culture", false);
        public static readonly WorldLaw BanVanillaAlliance = Law("ban_vanilla_alliance", true);
        public static readonly WorldLaw TechTree = Law("tech_tree", true);
        public static readonly WorldLaw Premodern = Law("premodern", false);
        public static readonly WorldLaw TechCalendar = Law("tech_calendar", true);
        public static readonly WorldLaw CombineHouses = Law("combine_houses", false);
        public static readonly WorldLaw NoCommoners = Law("no_commoners", true);
        public static readonly WorldLaw FixedSettlements = Law("fixed_settlements", false);
        public static readonly WorldLaw FixedCityBorders = Law("fixed_city_borders", false);
        public static readonly WorldLaw SurnameDynasty = Law("surname_dynasty", false);
        public static readonly WorldLaw DejureFirst = Law("dejure_first", true);
        public static readonly WorldLaw EnforceLaws = Law("enforce_laws", true);
        public static readonly WorldLaw BanRebellion = Law("ban_rebellion", false);
        public static readonly WorldLaw BanDiplomacy = Law("ban_diplomacy", false);
        public static readonly WorldLaw BanAiWar = Law("ban_ai_war", false);
        public static readonly WorldLaw BanAiAlliance = Law("ban_ai_alliance", false);
        public static readonly WorldLaw BanAiVassal = Law("ban_ai_vassal", false);
        public static readonly WorldLaw BanAiTribute = Law("ban_ai_tribute", false);
        public static readonly WorldLaw BanAiMarriage = Law("ban_ai_marriage", false);
        public static readonly WorldLaw BanAiPact = Law("ban_ai_pact", false);

        // Switches the mod read from the base game. Here they are this game's own, all on.
        public static readonly WorldLaw CivArmy = Law("civ_army", true);
        public static readonly WorldLaw Rebellions = Law("rebellions", true);
        public static readonly WorldLaw Diplomacy = Law("diplomacy", true);
        public static readonly WorldLaw KingdomExpansion = Law("kingdom_expansion", true);
        public static readonly WorldLaw OldAge = Law("old_age", true);

        public static WorldLaw Get(string id) { foreach (var law in All) if (string.Equals(law.Id, id, StringComparison.Ordinal)) return law; return null; }
        public static void Reset() { foreach (var law in All) law.Enabled = law.Default; }
    }

    // How a person died, as the ported systems record it. People here die of age or by a
    // system's hand; the rest are labels those systems may write into a biography.
    public enum DeathCause : byte { Other, Age, Weapon, Fire, Water, Starvation, Poison, Plague, Infection, Tumor, Explosion, Eaten }
}
