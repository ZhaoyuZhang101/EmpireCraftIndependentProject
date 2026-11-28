using System;
using System.IO;
using System.Linq;

namespace EmpireCraft.Core
{
    // The rule data read before a world opens, from GameData.Folder, in the order the mod read its own as it started
    // (ModClass.OnLoad): the text table of the game's language, the tables of ranks, honorary offices, merit and peerage
    // names, the peoples' word lists (names, era names, temple and posthumous names, party names, book titles), the
    // institution trees, the regime templates and their titles, the peoples' rules, the factions, the laws (regimes before
    // the peoples' rules here, so a people may name a variant regime).
    // What a system reads the first time it needs it (technology, claims, formations ...) is left to that system.
    //
    // Everything is read from files, so what the regime editor changes in Data is what the game runs on; folders are
    // listed rather than named, so a people or a word list the editor adds is read too. Safe to call again.
    public static class RuleData
    {
        public static void Load()
        {
            string data = GameData.Folder;
            if (string.IsNullOrEmpty(data) || !Directory.Exists(data)) return;
            CultureNames.Reset();
            string locales = Path.Combine(data, "Locales");
            string texts = Path.Combine(locales, LM.Language + ".json");
            if (File.Exists(texts)) LM.LoadFile(texts);
            foreach (string table in new[] { "PeeragesLevelNames.csv", "HonoraryOfficial.csv", "MeritLevel.csv", "OfficialRanks.csv" })
                LM.LoadLocales(Path.Combine(locales, table));
            LoadWordLists(Path.Combine(locales, "Cultures"));
            InstitutionDefinitionRegistry.Load();
            // Regimes before the culture rules (the mod read them after): a culture may name a variant regime a regime
            // file adds (RegimeVariants), and the rules are read with that known.
            if (Newtonsoft.Json.JsonConvert.DefaultSettings == null)
                Newtonsoft.Json.JsonConvert.DefaultSettings = () => new Newtonsoft.Json.JsonSerializerSettings { Converters = { new RegimeVariants.Reader() } };
            RegimeManager.init();
            OnomasticsRule.ReadSetting();
            FactionManager.init();
            EmpireLawSystem.init();
        }

        // The mod's LoadCultureNameTemplate: every CSV under each people's folder (Culture_*), then the era names,
        // temple and posthumous names, and the shared party names and book titles. A file whose first line is not a
        // header of columns is skipped. Settlement, wilderness and street names are read by their own systems.
        static void LoadWordLists(string cultures)
        {
            if (!Directory.Exists(cultures)) return;
            foreach (string folder in Directory.EnumerateDirectories(cultures, "Culture_*").OrderBy(f => f, StringComparer.Ordinal))
                foreach (string file in Directory.EnumerateFiles(folder, "*.csv", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                {
                    string header = File.ReadLines(file).FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(header) || !header.Contains(',')) continue;
                    LM.LoadLocales(file);
                }
            foreach (string table in new[] { "YearName1.csv", "YearName2.csv", "MiaoHaoPrefixes.csv", "MiaoHaoSuffixes.csv", "ShiHao.csv" })
                LM.LoadLocales(Path.Combine(cultures, table));
            foreach (string shared in new[] { "PartyNames", "Books" })
            {
                string folder = Path.Combine(cultures, shared);
                if (!Directory.Exists(folder)) continue;
                foreach (string file in Directory.EnumerateFiles(folder, "*.csv").OrderBy(f => f, StringComparer.Ordinal)) LM.LoadLocales(file);
            }
        }
    }
}
