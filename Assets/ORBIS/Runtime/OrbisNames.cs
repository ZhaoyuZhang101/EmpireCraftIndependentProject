using System;
using System.Collections.Generic;

namespace Orbis
{
    public sealed class NamedPlace
    {
        public int tile, priority;
        public string name;
    }
    public static class ChineseText
    {
        public static string Zone(ZoneKind kind)
        {
            return new[] { "遠洋", "海岸", "湖泊", "河谷", "高地", "冰原", "荒漠", "草原", "森林" }[(int)kind];
        }
        public static string OnOff(bool on) => on ? "開啟" : "關閉";
        public static string Era(float tech) => tech < 2 ? "農耕時代" : tech < 4 ? "航海時代" : tech < 7 ? "城邦時代" : "繁盛時代";
        public static string Name(string key, string suffix)
        {
            string[] a = { "青", "蒼", "霜", "碧", "赤", "星", "雲", "嵐", "金", "松", "遠", "黎", "晴", "寒", "月", "澄" };
            string[] b = { "川", "嶺", "岫", "岬", "原", "澤", "峰", "潮", "汐", "陵", "林", "光", "石", "風", "泉", "溪" };
            uint hash = RandomStream.Hash(key); return a[(int)(hash % (uint)a.Length)] + b[(int)((hash >> 8) % (uint)b.Length)] + suffix;
        }
    }
    public sealed partial class OrbisWorld
    {
        public List<NamedPlace> namedPlaces = new List<NamedPlace>();
        readonly Dictionary<int, string> riverNames = new Dictionary<int, string>();
        public string WorldName => ChineseText.Name(seed + "planet", "星");
        public string PlaceName(int tile)
        {
            if (tile < 0 || tile >= tiles.Length) return "未知地區";
            int zone = tiles[tile].zoneId;
            return zone >= 0 && zone < geography.zones.Length ? geography.zones[zone].name : "未命名地區";
        }
        public string RiverName(int tile)
        {
            int basin = tiles[tile].basin; return riverNames.TryGetValue(basin, out string name) ? name : "無主要河流";
        }
        public void RefreshNames()
        {
            namedPlaces.Clear(); riverNames.Clear();
            var massSize = new Dictionary<int, int>();
            foreach (var c in geography.chunks) { if (!massSize.ContainsKey(c.landmassId)) massSize[c.landmassId] = 0; massSize[c.landmassId] += c.tileIds.Length; }
            var namedMasses = new HashSet<int>();
            foreach (var c in geography.chunks)
            {
                string suffix = c.ocean ? "海" : massSize[c.landmassId] < 100 ? "島" : "洲";
                string mass = ChineseText.Name(seed + "mass" + c.landmassId, suffix);
                c.name = mass + "・" + ChineseText.Name(seed + "chunk" + c.tileIds[0], "地區");
                if (namedMasses.Add(c.landmassId)) namedPlaces.Add(new NamedPlace { tile = c.tileIds[0], name = mass, priority = massSize[c.landmassId] });
            }
            foreach (var z in geography.zones)
                z.name = ChineseText.Name(seed + "zone" + z.tileIds[0] + (int)z.kind, ChineseText.Zone(z.kind));
            var largest = new Dictionary<int, int>();
            for (int i = 0; i < tiles.Length; i++)
            {
                Tile t = tiles[i]; if (t.h <= 0 || t.basin < 0 || t.RiverStrength < 4) continue;
                if (!largest.TryGetValue(t.basin, out int previous) || t.RiverStrength > tiles[previous].RiverStrength) largest[t.basin] = i;
            }
            foreach (var pair in largest)
            {
                string name = ChineseText.Name(seed + "river" + pair.Key, "河"); riverNames[pair.Key] = name;
                if (tiles[pair.Value].RiverStrength > 12) namedPlaces.Add(new NamedPlace { tile = pair.Value, name = name, priority = (int)tiles[pair.Value].RiverStrength });
            }
            namedPlaces.Sort((a, b) => b.priority.CompareTo(a.priority));
        }
        public int MainRiver()
        {
            int best = -1; float flow = 0;
            for (int i = 0; i < tiles.Length; i++) if (tiles[i].h > 0 && tiles[i].RiverStrength > flow) { flow = tiles[i].RiverStrength; best = i; }
            return best;
        }
    }
}
