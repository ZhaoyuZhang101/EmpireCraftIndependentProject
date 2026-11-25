// Generated from the EmpireCraft mod: Scripts/GeneralSystems/AirPowerSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 制空权、战略轰炸与导弹(用户 2026-11-24 "现代之后就很无聊了"，五批里的第五批)。以前战斗机、轰炸机只是火力高一点的"军"。
// 每年把各场战争过一遍：
//   · 空中力量：一方各国的战斗机军兵力(轰炸机算一半、直升机三成)合计，研究出喷气发动机的 ×1.3；
//   · 制空权：一方的空中力量达到对方一倍半，夺得制空权——它的陆军在战线上火力 +15%，对方 −10%(见 FrontSystem.Firepower)；
//   · 战略轰炸：握有制空权又有轰炸机的一方每年轰炸对方工业最多的那座城——那座城的工厂一年里减产(轰炸机越多减得越多，至多四成)，
//     对方劳工、市民怨气上涨，有机会炸毁一座厂的一个档次；
//   · 导弹：研究出火箭技术的一方轰炸力 +30%，洲际导弹再 +30%；没有制空权但有火箭技术，照样能打，只是效果减半。
// 末代的可重复研究"军事精进"也在这里乘进火力(见 TechnologySystem.RepeatFactor)。只影响数值战线，不放实体飞机。
public static class AirPowerSystem
{
    public const string JetTech = "jet_engines", RocketTech = "rocketry", IcbmTech = "icbm";
    private const float SuperiorityRatio = 1.5f, MinAir = 200f, WinFire = 1.15f, LoseFire = 0.9f;
    private const float BombBase = 0.15f, BombPerIntensity = 0.25f, FullBombers = 2000f, RazeChance = 0.1f;

    private static readonly Dictionary<long, float> Fire = new();
    private static readonly Dictionary<long, (double until, float cut)> Bombed = new();

    public static void ResetWorldState()
    {
        Fire.Clear();
        Bombed.Clear();
    }

#if EC_PENDING
    private static bool Has(Realm kingdom, string tech) => TechnologySystem.HasTech(TechnologySystem.GetCultureOf(kingdom), tech);
#endif

    // 每年(WorldOrderSystem.Scan)
#if EC_PENDING
    internal static void Yearly(double now)
    {
        Fire.Clear();
        var fighters = new Dictionary<long, float>();
        var bombers = new Dictionary<long, float>();
        foreach ((Realm kingdom, CorpsData corps) in CorpsSystem.All())
        {
            if (kingdom?.Record == null || corps == null || corps.militia) continue;
            CorpsArmSystem.Arm arm = CorpsArmSystem.Of(corps);
            float air = arm switch
            {
                CorpsArmSystem.Arm.Fighter => corps.strength,
                CorpsArmSystem.Arm.Bomber => corps.strength * 0.5f,
                CorpsArmSystem.Arm.Helicopter => corps.strength * 0.3f,
                _ => 0f
            };
            if (air <= 0f) continue;
            if (Has(kingdom, JetTech)) air *= 1.3f;
            fighters[kingdom.Key] = (fighters.TryGetValue(kingdom.Key, out float sum) ? sum : 0f) + air;
            if (arm == CorpsArmSystem.Arm.Bomber)
                bombers[kingdom.Key] = (bombers.TryGetValue(kingdom.Key, out float load) ? load : 0f) + corps.strength;
        }
        foreach (War war in EmpireWorld.Current.LiveWars.ToList())
        {
            if (war == null || war.HasEnded()) continue;
            List<Realm> attackers = Alive(war.Attackers), defenders = Alive(war.Defenders);
            if (attackers.Count == 0 || defenders.Count == 0) continue;
            float Sum(List<Realm> side, Dictionary<long, float> table) =>
                side.Sum(kingdom => table.TryGetValue(kingdom.Key, out float value) ? value : 0f);
            float mine = Sum(attackers, fighters), theirs = Sum(defenders, fighters);
            int side = mine >= MinAir && mine >= theirs * SuperiorityRatio ? 1
                : theirs >= MinAir && theirs >= mine * SuperiorityRatio ? -1 : 0;
            if (side != 0)
            {
                List<Realm> winners = side > 0 ? attackers : defenders, losers = side > 0 ? defenders : attackers;
                foreach (Realm kingdom in winners) Scale(kingdom, WinFire);
                foreach (Realm kingdom in losers) Scale(kingdom, LoseFire);
                Note(war, 1, "air_superiority_history", winners[0], losers[0], null);
                Strike(war, winners, losers, Sum(winners, bombers), 1f, now);
                // 落了下风的一方有火箭技术，照样拿导弹还手
                if (losers.Any(kingdom => Has(kingdom, RocketTech))) Strike(war, losers, winners, 0f, 0.5f, now);
            }
            else
            {
                foreach ((List<Realm> from, List<Realm> to) in new[] { (attackers, defenders), (defenders, attackers) })
                    if (from.Any(kingdom => Has(kingdom, RocketTech))) Strike(war, from, to, 0f, 0.5f, now);
            }
        }
        foreach (long city in Bombed.Where(pair => pair.Value.until <= now).Select(pair => pair.Key).ToList()) Bombed.Remove(city);
    }
#endif

    private static List<Realm> Alive(IEnumerable<Realm> side) =>
        side?.Where(kingdom => kingdom?.Record != null && !kingdom.IsGone()).ToList() ?? new List<Realm>();

    private static void Scale(Realm kingdom, float factor) =>
        Fire[kingdom.Key] = Mathf.Clamp((Fire.TryGetValue(kingdom.Key, out float value) ? value : 1f) * factor, 0.8f, 1.3f);

    // 每场战争每样事只记一回
#if EC_PENDING
    private static void Note(War war, int bit, string key, Realm from, Realm to, City city)
    {
        var data = war.GetOrCreate();
        if ((data.air_noted & bit) != 0) return;
        data.air_noted |= bit;
        string text = string.Format(LM.Get(key), from.GetKingdomFullName(), to.GetKingdomFullName(), city?.GetCityName() ?? "");
        EventRecorder.Record(from, text);
        if (to.GetEmpire() is { } other) other.RecordHistory(directContent: text, kingdomId: other.CoreKingdom?.Key ?? -1L);
    }
#endif

    // 轰炸：打对方工业最多的那座城
#if EC_PENDING
    private static void Strike(War war, List<Realm> from, List<Realm> to, float bomberStrength, float effect, double now)
    {
        float power = bomberStrength;
        bool rockets = from.Any(kingdom => Has(kingdom, RocketTech));
        if (rockets) power = Mathf.Max(power, FullBombers * 0.3f) * 1.3f;
        if (from.Any(kingdom => Has(kingdom, IcbmTech))) power *= 1.3f;
        if (power <= 0f) return;
        City target = null;
        int best = -1;
        foreach (Realm kingdom in to)
            foreach (City city in kingdom.Cities)
            {
                if (city?.Record == null || city.IsGone()) continue;
                int units = FactorySystem.Units(city) + (city == kingdom.Capital ? 1 : 0);
                if (units <= best) continue;
                best = units;
                target = city;
            }
        if (target == null) return;
        float intensity = Mathf.Clamp01(power / FullBombers) * effect;
        float cut = BombBase * effect + BombPerIntensity * intensity;
        Bombed[target.Key] = (now + 12d * GameClock.MonthLength, Mathf.Max(cut, Bombed.TryGetValue(target.Key, out var old) ? old.cut : 0f));
        if (target.Realm?.GetEmpire() is { } victim)
        {
            ClaimPolicySystem.AddGrievance(victim, SocialClass.Labour, 3f * intensity);
            ClaimPolicySystem.AddGrievance(victim, SocialClass.Citizen, 4f * intensity);
        }
        if (EcRandom.value < RazeChance * intensity)
            foreach (FactorySite site in FactorySystem.InCity(target))
            {
                int index = site.units.FindIndex(unit => unit.level > 1);
                if (index < 0) continue;
                FactorySystem.SetLevel(site, index, site.units[index].level - 1);
                break;
            }
        Note(war, bomberStrength > 0f ? 2 : 4, bomberStrength > 0f ? "air_bombing_history" : "air_missile_history", from[0], target.Realm ?? to[0], target);
    }
#endif

    // 陆军火力(见 FrontSystem.Firepower)：制空权，再乘末代的"军事精进"
#if EC_PENDING
    public static float FirepowerFactor(Realm kingdom) => kingdom?.Record == null ? 1f
        : (Fire.TryGetValue(kingdom.Key, out float value) ? value : 1f) *
          TechnologySystem.RepeatFactor(TechnologySystem.GetCultureOf(kingdom), "military");
#endif

    // 挨了轰炸的城工厂减产(见 FactorySystem.Produce)
    public static float BombedFactor(City city) =>
        city != null && Bombed.TryGetValue(city.Key, out var hit) && GameClock.Now < hit.until ? 1f - hit.cut : 1f;
}
}
