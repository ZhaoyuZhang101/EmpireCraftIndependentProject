using System;
using System.Collections.Generic;
using System.Linq;
using Orbis.Flat;

namespace EmpireCraft.Core
{
    // Which people each realm belongs to when a world begins. This game's own rule, not the
    // mod's (the mod read it off the species of a realm's founder; here there is one people
    // for the map and many for the cultures).
    //
    // On the Earth map a realm takes the people whose homeland holds its capital. Anywhere
    // else - and on Earth where no homeland matches - realms are grouped into regions on their
    // landmass, a region is described by its land (climate mix, latitude, height, coast, side
    // of the continent), and each people's homeland is described the same way; regions take
    // the people that fit them best, each people once while some are still unused.
    //
    // The peoples that take part are those whose rule file describes a homeland (the historical peoples; see LoadHomelands).
    // The modern starts, the story and fantasy cultures and the very small ones have none: scenarios place them.
    public static class CultureGeography
    {
        sealed class Homeland
        {
            public string Family;
            public Koppen[] Climates;
            public float Latitude, Spread;          // absolute latitude in degrees, and how far from it still fits
            public float Height = -1f;              // metres; a highland people when set
            public int Side;                        // -1 west of its landmass, +1 east, 0 either
            public bool Coast, Interior, Island, South, River;
            public float West = 999f, East = 999f, North = 999f, SouthEdge = 999f;   // Earth homeland box (longitude, latitude)
        }

        // Each people's homeland, from its rule file (Locales/Cultures/Culture_<people>/CultureRule.jsonc, "homeland"), for the
        // peoples whose rules are loaded; a people without one is not placed at the start (scenarios place it). Read when a world
        // opens, so what the regime editor changes or adds there is what the next world uses. In the order of earth_priority
        // (where homeland boxes on Earth overlap, the lower number is taken), then by name.
        static List<Homeland> LoadHomelands()
        {
            var found = new List<(int priority, Homeland home)>();
            string root = string.IsNullOrEmpty(GameData.Folder) ? null : System.IO.Path.Combine(GameData.Folder, "Locales", "Cultures");
            if (root == null || !System.IO.Directory.Exists(root)) return new List<Homeland>();
            foreach (string folder in System.IO.Directory.EnumerateDirectories(root, "Culture_*").OrderBy(f => f, StringComparer.Ordinal))
            {
                string file = System.IO.Path.Combine(folder, "CultureRule.jsonc");
                string family = System.IO.Path.GetFileName(folder).Substring("Culture_".Length);
                if (!System.IO.File.Exists(file) || !CultureService.IsValidCulture(family)) continue;
                Newtonsoft.Json.Linq.JObject spec;
                try { spec = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(file))["homeland"] as Newtonsoft.Json.Linq.JObject; }
                catch (Exception e) { LogService.LogWarning("[EmpireCraft][故土] " + family + " 的故土读不出来: " + e.Message); continue; }
                if (spec == null) continue;
                var home = new Homeland
                {
                    Family = family,
                    Latitude = (float?)spec["latitude"] ?? 0f,
                    Spread = Math.Max(1f, (float?)spec["spread"] ?? 8f),
                    Height = (float?)spec["height"] ?? -1f,
                    Side = ((string)spec["side"]) == "east" ? 1 : ((string)spec["side"]) == "west" ? -1 : 0,
                    Coast = (bool?)spec["coast"] ?? false,
                    Interior = (bool?)spec["interior"] ?? false,
                    Island = (bool?)spec["island"] ?? false,
                    South = (bool?)spec["southern"] ?? false,
                    River = (bool?)spec["river"] ?? false,
                    Climates = (spec["climates"] as Newtonsoft.Json.Linq.JArray)?.Select(c => Enum.TryParse((string)c, out Koppen k) ? k : Koppen.Unknown)
                        .Where(k => k != Koppen.Unknown).ToArray() ?? new Koppen[0]
                };
                if (spec["earth"] is Newtonsoft.Json.Linq.JArray box && box.Count == 4)
                {
                    home.West = (float)box[0]; home.East = (float)box[1]; home.SouthEdge = (float)box[2]; home.North = (float)box[3];
                }
                found.Add(((int?)spec["earth_priority"] ?? int.MaxValue, home));
            }
            return found.OrderBy(f => f.priority).ThenBy(f => f.home.Family, StringComparer.Ordinal).Select(f => f.home).ToList();
        }

        // The families that can be placed: those of the homelands whose rules are loaded.
        public static IEnumerable<string> Families => LoadHomelands().Select(h => h.Family);

        // Gives every realm, its cities and its people a culture, and each realm the regime and institutions of its people.
        // Does nothing when the culture rules or the regime templates are not loaded. Returns the family of each realm.
        public static Dictionary<Realm, string> Assign(EmpireWorld w)
        {
            var chosen = new Dictionary<Realm, string>();
            if (w == null || OnomasticsRule.ALL_CULTURE_RULE.Count == 0 || RegimeManager.regimes == null) return chosen;
            var pool = LoadHomelands();
            if (pool.Count == 0) return chosen;
            var realms = w.LiveRealms.Where(r => r.Capital != null).OrderBy(r => r.Id).ToList();
            // Each landmass's size, and its centre east-west (as an angle: the map wraps), in one pass over the map.
            var landSize = new Dictionary<int, int>(); var sums = new Dictionary<int, (double sin, double cos)>();
            for (int t = 0; t < w.TileCity.Length; t++)
            {
                int mass = w.LandmassOf(t); if (mass < 0) continue;
                landSize.TryGetValue(mass, out int n); landSize[mass] = n + 1;
                double a = 2 * Math.PI * (t % w.Width) / w.Width; sums.TryGetValue(mass, out var s); sums[mass] = (s.sin + Math.Sin(a), s.cos + Math.Cos(a));
            }
            var centres = sums.ToDictionary(p => p.Key, p => Math.Atan2(p.Value.sin, p.Value.cos));
            int largest = landSize.Count == 0 ? 1 : landSize.Values.Max();

            var open = new List<Realm>();
            if (w.Map.Preset == TerrainPreset.Earth)
            {
                foreach (var realm in realms)
                {
                    var home = OnEarth(w, realm.Capital.Tile, pool);
                    if (home != null) chosen[realm] = home.Family; else open.Add(realm);
                }
            }
            else open.AddRange(realms);

            // The rest by region: a few realms to a region, regions on one landmass.
            var regions = new List<List<Realm>>();
            foreach (var group in open.GroupBy(r => w.LandmassOf(r.Capital.Tile)).OrderBy(g => g.Key))
                regions.AddRange(Split(w, group.OrderBy(r => r.Id).ToList()));
            var scores = new List<(int region, Homeland home, double score)>();
            for (int i = 0; i < regions.Count; i++)
            {
                var land = Describe(w, regions[i], landSize, largest, centres);
                foreach (var home in pool) scores.Add((i, home, Fit(home, land)));
            }
            var used = new HashSet<string>();
            var given = new Dictionary<int, string>();
            foreach (var (region, home, _) in scores.OrderByDescending(s => s.score).ThenBy(s => s.region).ThenBy(s => s.home.Family, StringComparer.Ordinal))
            {
                if (given.ContainsKey(region)) continue;
                if (used.Contains(home.Family) && used.Count < pool.Count) continue;
                given[region] = home.Family; used.Add(home.Family);
            }
            for (int i = 0; i < regions.Count; i++) foreach (var realm in regions[i]) chosen[realm] = given[i];

            foreach (var realm in realms) Apply(w, realm, chosen[realm]);
            return chosen;
        }

        static void Apply(EmpireWorld w, Realm realm, string family)
        {
            CultureService.SetRealmCulture(realm, family, updatePoliticalSystem: true, allowLegacyInstitutions: false);
            var culture = realm.Culture;
            if (culture == null) return;
            foreach (var city in realm.Cities) city.SetCulture(culture);
            foreach (var person in w.Living) if (person.Realm == realm && !person.HasCulture()) person.SetCulture(culture);
        }

        // Keeps newcomers in step: a city without a culture takes its realm's, a realm without one takes its seat's,
        // a person without one takes the home city's. For realms and cities founded after the world began.
        public static void Upkeep(EmpireWorld w)
        {
            if (w == null || OnomasticsRule.ALL_CULTURE_RULE.Count == 0 || RegimeManager.regimes == null) return;
            foreach (var realm in w.LiveRealms.ToArray())
            {
                if (realm.HasCulture() || realm.Capital == null) continue;
                var seat = realm.Capital.Culture;
                if (seat != null && seat.Alive && seat.Family != null) Apply(w, realm, seat.Family);
            }
            foreach (var city in w.Cities) if (city.Alive && !city.HasCulture() && city.Realm != null && city.Realm.HasCulture()) city.SetCulture(city.Realm.Culture);
            foreach (var person in w.Living) if (!person.HasCulture() && person.City != null && person.City.HasCulture()) person.SetCulture(person.City.Culture);
        }

        static Homeland OnEarth(EmpireWorld w, int tile, List<Homeland> pool)
        {
            float lon = (float)((tile % w.Width + 0.5) * 360.0 / w.Width - 180.0);
            float lat = w.Map.Latitude(tile);
            foreach (var home in pool)
                if (lon >= home.West && lon <= home.East && lat >= home.SouthEdge && lat <= home.North) return home;
            return null;
        }

        // How many realms share a people at the start, on average: a people is a block of several states.
        public const double RealmsPerRegion = 4.5;

        // Splits the realms of one landmass into regions around seeds spread as far apart as possible.
        static List<List<Realm>> Split(EmpireWorld w, List<Realm> realms)
        {
            int count = Math.Max(1, (int)Math.Round(realms.Count / RealmsPerRegion));
            var seeds = new List<Realm> { realms[0] };
            while (seeds.Count < count)
            {
                Realm far = null; float best = -1f;
                foreach (var realm in realms)
                {
                    float near = seeds.Min(s => w.Distance(s.Capital.Tile, realm.Capital.Tile));
                    if (near > best) { best = near; far = realm; }
                }
                if (far == null || seeds.Contains(far)) break;
                seeds.Add(far);
            }
            var regions = seeds.Select(_ => new List<Realm>()).ToList();
            foreach (var realm in realms)
            {
                int at = 0; float best = float.MaxValue;
                for (int i = 0; i < seeds.Count; i++) { float d = w.Distance(seeds[i].Capital.Tile, realm.Capital.Tile); if (d < best) { best = d; at = i; } }
                regions[at].Add(realm);
            }
            regions = regions.Where(r => r.Count > 0).ToList();
            // A seed far out on its own makes a region of one: fold it into the region of its nearest neighbour.
            foreach (var lone in regions.Where(r => r.Count == 1).ToList())
            {
                if (regions.Count == 1) break;
                var realm = lone[0];
                var into = regions.Where(r => r != lone).OrderBy(r => r.Min(o => w.Distance(o.Capital.Tile, realm.Capital.Tile))).First();
                into.Add(realm); regions.Remove(lone);
            }
            return regions;
        }

        sealed class Land
        {
            public Dictionary<Koppen, float> Climate = new Dictionary<Koppen, float>();
            public float Latitude, AbsLatitude, Height, Coast, Side, River;
            public bool Island;
        }

        static Land Describe(EmpireWorld w, List<Realm> region, Dictionary<int, int> landSize, int largest, Dictionary<int, double> centres)
        {
            var land = new Land(); var m = w.Map; int cells = 0; double lat = 0, abs = 0, height = 0, coast = 0, river = 0, x = 0;
            int mass = w.LandmassOf(region[0].Capital.Tile);
            foreach (var realm in region)
                foreach (var city in realm.Cities)
                    foreach (int t in city.Tiles)
                    {
                        cells++;
                        land.Climate.TryGetValue(m.Climates[t], out float n); land.Climate[m.Climates[t]] = n + 1;
                        float l = m.Latitude(t); lat += l; abs += Math.Abs(l);
                        height += m.Elevation[t];
                        if (m.CoastDistance != null && m.CoastDistance[t] <= 2) coast++;
                        if (m.Water[t] == WaterKind.River) river++;
                    }
            if (cells == 0) return land;
            foreach (var key in land.Climate.Keys.ToList()) land.Climate[key] /= cells;
            land.Latitude = (float)(lat / cells); land.AbsLatitude = (float)(abs / cells); land.Height = (float)(height / cells);
            land.Coast = (float)(coast / cells); land.River = (float)(river / cells);
            land.Island = landSize.TryGetValue(mass, out int size) && size * 4 < largest;
            // Which side of its landmass the region lies on: the mean of its capitals against the landmass's, the short way round.
            double sy = 0, cy = 0;
            foreach (var realm in region) { double a = 2 * Math.PI * (realm.Capital.Tile % w.Width) / w.Width; sy += Math.Sin(a); cy += Math.Cos(a); }
            if (centres.TryGetValue(mass, out double centre))
            {
                double here = Math.Atan2(sy, cy), d = here - centre;
                while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI;
                land.Side = (float)Math.Max(-1, Math.Min(1, d / 0.35));   // a landmass spanning ~40 degrees reads as -1..1 across
            }
            return land;
        }

        static double Fit(Homeland home, Land land)
        {
            double climate = 0; foreach (var k in home.Climates) if (land.Climate.TryGetValue(k, out float share)) climate += share;
            double lat = Math.Exp(-Math.Pow((land.AbsLatitude - home.Latitude) / home.Spread, 2));
            double score = climate * 4 + lat * 2;
            if (home.Height > 0) score += land.Height >= home.Height ? 1.5 : -1.0 + land.Height / home.Height;
            if (home.Side != 0) score += 0.75 * home.Side * land.Side;
            if (home.Coast) score += land.Coast;
            if (home.Interior) score += 0.5 - land.Coast;
            if (home.Island) score += land.Island ? 1.0 : -0.5;
            if (home.South) score += land.Latitude < 0 ? 0.5 : 0;
            if (home.River) score += Math.Min(1.0, land.River * 5);
            return score;
        }
    }
}
