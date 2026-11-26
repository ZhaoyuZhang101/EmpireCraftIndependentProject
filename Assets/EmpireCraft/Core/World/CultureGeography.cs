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
    // Only the historical peoples take part. The modern starts, the story and fantasy cultures
    // and the very small ones are for scenarios to place.
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

        static Homeland H(string family, float latitude, float spread, params Koppen[] climates) =>
            new Homeland { Family = family, Latitude = latitude, Spread = spread, Climates = climates };

        static readonly Homeland[] Homelands =
        {
            Box(H("Huaxia", 32, 10, Koppen.Cwa, Koppen.Cfa, Koppen.Dwa, Koppen.Cwb), 100, 123, 20, 41, side: 1),
            Box(H("Japan", 37, 8, Koppen.Cfa, Koppen.Dfa, Koppen.Dfb, Koppen.Cfb), 129, 146, 30, 46, side: 1, island: true),
            Box(H("Manzhou", 47, 7, Koppen.Dwb, Koppen.Dwa, Koppen.Dfb, Koppen.Dwc, Koppen.Dfa), 118, 135, 41, 54, side: 1),
            Box(H("Xizang", 33, 8, Koppen.ET, Koppen.Dwc, Koppen.Cwc, Koppen.BWk), 78, 100, 27, 37, height: 3000),
            Box(H("Youmu", 46, 8, Koppen.BSk, Koppen.BWk, Koppen.Dwc, Koppen.Dfc), 50, 118, 40, 56, interior: true),
            Box(H("India", 20, 9, Koppen.Aw, Koppen.Am, Koppen.BSh, Koppen.Cwa), 68, 92, 6, 30),
            Box(H("Persepolis", 32, 6, Koppen.BSk, Koppen.BWh, Koppen.BWk, Koppen.Csa), 44, 64, 25, 40, height: 1000),
            Box(H("Egypt", 27, 5, Koppen.BWh), 24, 36, 20, 32, river: true),
            Box(H("Arab", 23, 8, Koppen.BWh, Koppen.BSh), 33, 60, 12, 37),
            Box(H("Swahili", 6, 8, Koppen.Aw, Koppen.Am, Koppen.Af), 34, 45, -12, 5, side: 1, coast: true, southern: true),
            Box(H("Bantu", 5, 9, Koppen.Af, Koppen.Aw, Koppen.Am), 8, 34, -15, 8, interior: true),
            Box(H("Zulu", 28, 7, Koppen.Cfa, Koppen.Cwa, Koppen.BSh, Koppen.Cwb), 15, 36, -35, -15, southern: true),
            Box(H("Roma", 40, 6, Koppen.Csa, Koppen.Csb), -10, 44, 34, 46, coast: true),
            Box(H("Frankish", 47, 5, Koppen.Cfb), -5, 8, 43, 51, side: -1),
            Box(H("Germanic", 52, 5, Koppen.Cfb, Koppen.Dfb), 5, 20, 46, 56),
            Box(H("Viking", 62, 7, Koppen.Dfc, Koppen.Cfc, Koppen.ET, Koppen.Dfb), 4, 32, 55, 72, coast: true),
            Box(H("Slavonic", 55, 7, Koppen.Dfb, Koppen.Dfa, Koppen.Dfc), 15, 60, 44, 62, interior: true),
            Box(H("Aztec", 20, 6, Koppen.Aw, Koppen.Cwb, Koppen.BSh), -118, -86, 14, 33, height: 1500),
            Box(H("Ojibwe", 47, 6, Koppen.Dfb, Koppen.Dfc, Koppen.Dfa), -100, -70, 40, 55),
        };

        // The homeland on Earth as a box of longitude (west, east) and latitude (bottom, top), and what kind of land it is.
        static Homeland Box(Homeland h, float west, float east, float bottom, float top, int side = 0, float height = -1f,
            bool coast = false, bool interior = false, bool island = false, bool southern = false, bool river = false)
        {
            h.West = west; h.East = east; h.SouthEdge = bottom; h.North = top;
            h.Side = side; h.Height = height; h.Coast = coast; h.Interior = interior; h.Island = island; h.South = southern; h.River = river;
            return h;
        }

        // The families that can be placed: those of the homelands whose rules are loaded.
        public static IEnumerable<string> Families => Homelands.Select(h => h.Family).Where(CultureService.IsValidCulture);

        // Gives every realm, its cities and its people a culture, and each realm the regime and institutions of its people.
        // Does nothing when the culture rules or the regime templates are not loaded. Returns the family of each realm.
        public static Dictionary<Realm, string> Assign(EmpireWorld w)
        {
            var chosen = new Dictionary<Realm, string>();
            if (w == null || OnomasticsRule.ALL_CULTURE_RULE.Count == 0 || RegimeManager.regimes == null) return chosen;
            var pool = Homelands.Where(h => CultureService.IsValidCulture(h.Family)).ToList();
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
