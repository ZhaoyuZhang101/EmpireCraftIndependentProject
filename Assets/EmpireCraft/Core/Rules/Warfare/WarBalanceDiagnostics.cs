// Generated from the EmpireCraft mod: Scripts/GeneralSystems/Warfare/WarBalanceDiagnostics.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace EmpireCraft.Core
{

// 游牧与农耕的战力平衡诊断(用户 2026-10-20 选"积弊动真格""游牧来得快去得快"，先打日志再调数)：只记一方是游牧的战事，写进 Player.log——
//   · [游牧平衡·交战]：一座城一年至多一行，全图每月至多 MaxBattleLines 行；攻守双方的兵力、实效(空饷、武备、组织度)、
//     火力、统帅、补给、雄主，守方的守势加成、困守、团练、关城，地形与胜负比；
//   · [游牧平衡·兵源]：打仗的游牧国家、与游牧交战的国家，一国一年至多一行，全图每月至多 MaxRealmLines 行；
//     背景人口、兵源、军费系数、征兵比例、积弊、现有兵力。
// 阿拉伯政体同样记一份(先看行省的军费系数是否卡住兵额，再调央地分成)：一方是阿拉伯政体(帝国按核心)、又不是游牧战事的，
// 记成[阿拉伯平衡·交战]、[阿拉伯平衡·兵源]，另计行数(限频同上)；兵源行另记国家类别、央地分成、年经常收入与国库。
// 罗马、华夏两路(用户 2026-10-20 定"补上罗马、华夏两路，进游戏四系对比")：不是游牧、阿拉伯战事的，一方(帝国按核心)的文化线是
// 罗马线(西方各文化默认走罗马线)记[罗马平衡]，否则一方是华夏线记[华夏平衡]；格式、限频同阿拉伯，兵源行另记政体。
public static class WarBalanceDiagnostics
{
    private const int MaxBattleLines = 6, MaxRealmLines = 8, CooldownMonths = 12;
    private const double SecondsPerMonth = 1d; // world time counts months here
    // 四路诊断：0 游牧、1 阿拉伯、2 罗马、3 华夏
    private const int NomadChannel = 0, ArabicChannel = 1, RomaChannel = 2, HuaxiaChannel = 3, Channels = 4;
    private static readonly string[] Tags = { "游牧平衡", "阿拉伯平衡", "罗马平衡", "华夏平衡" };
    private static readonly string[] Marks = { "·游牧", "·阿拉伯", "·罗马", "·华夏" };

    private static readonly Dictionary<long, double>[] BattleLogged = { new(), new(), new(), new() },
        RealmLogged = { new(), new(), new(), new() };
    private static double _monthAt = -1d;
    private static readonly int[] BattleLines = new int[Channels], RealmLines = new int[Channels];

    private static bool Budget(bool battle, int channel)
    {
        double now = GameClock.Now;
        if (_monthAt < 0d || now < _monthAt || now - _monthAt >= SecondsPerMonth)
        {
            _monthAt = now;
            for (int i = 0; i < Channels; i++)
            {
                BattleLines[i] = RealmLines[i] = 0;
                if (BattleLogged[i].Count > 4096) BattleLogged[i].Clear();
                if (RealmLogged[i].Count > 4096) RealmLogged[i].Clear();
            }
        }
        return battle ? BattleLines[channel] < MaxBattleLines : RealmLines[channel] < MaxRealmLines;
    }

    private static bool Cooling(Dictionary<long, double> logged, long id) =>
        logged.TryGetValue(id, out double at) && GameClock.Now >= at && GameClock.MonthsSince(at) < CooldownMonths;

    private static bool Nomad(Realm kingdom) => FrontierRelations.IsNomadRealm(FrontSystem.ModRealm(kingdom));

    // 阿拉伯政体：本国或所在帝国的核心
    private static bool Arabic(Realm kingdom) =>
        kingdom?.Record != null && !kingdom.IsGone() &&
        (kingdom.GetRegime()?.type == RegimeType.Arabic || FrontSystem.ModRealm(kingdom)?.GetRegime()?.type == RegimeType.Arabic);

    // 文化线(帝国按核心)：罗马线(西方各文化默认走罗马线)、华夏线
    private static string Line(Realm kingdom)
    {
        Realm realm = FrontSystem.ModRealm(kingdom);
        return realm?.Record == null || realm.IsGone() ? "" : InstitutionSystem.GetCultureLine(CultureService.GetRealmCulture(realm));
    }

    private static bool Roman(Realm kingdom) => kingdom?.Record != null && !kingdom.IsGone() && Line(kingdom) == "Roma";

    private static bool Huaxia(Realm kingdom) => kingdom?.Record != null && !kingdom.IsGone() && Line(kingdom) == "Huaxia";

    // 这一方属于哪一路(游牧、阿拉伯、罗马、华夏依次)；都不是返回 -1
    private static int ChannelOf(Realm kingdom) =>
        Nomad(kingdom) ? NomadChannel : Arabic(kingdom) ? ArabicChannel : Roman(kingdom) ? RomaChannel :
        Huaxia(kingdom) ? HuaxiaChannel : -1;

    // 本国(连同所在帝国的核心)在跟某类国家打仗，或自己就是
    private static bool AtWarWith(Realm kingdom, global::System.Func<Realm, bool> watched)
    {
        if (watched(kingdom)) return true;
        foreach (Realm side in new[] { kingdom, FrontSystem.ModRealm(kingdom) })
        {
            if (side?.Record == null || side.IsGone()) continue;
            foreach (War war in side.GetWars())
            {
                if (war?.Record == null || war.HasEnded()) continue;
                Realm foe = war.IsAttacker(side) ? war.MainDefender : war.MainAttacker;
                if (foe?.Record != null && !foe.IsGone() && watched(foe)) return true;
            }
        }
        return false;
    }

    // 记哪一路：游牧优先，其次阿拉伯、罗马、华夏；都不是返回 -1
    private static int RealmChannel(Realm kingdom) =>
        AtWarWith(kingdom, Nomad) ? NomadChannel : AtWarWith(kingdom, Arabic) ? ArabicChannel :
        AtWarWith(kingdom, Roman) ? RomaChannel : AtWarWith(kingdom, Huaxia) ? HuaxiaChannel : -1;

    private static string Name(Realm kingdom) => kingdom?.Record == null ? "?" : kingdom.GetKingdomName();

    // 每月征召时(CorpsSystem.Mobilize，打仗时)
    public static void ModRealm(Realm kingdom, float background, float pool, float budget, float levyShare)
    {
        try
        {
            if (kingdom?.Record == null || EmpireWorld.Current == null) return;
            int channel = RealmChannel(kingdom);
            if (channel < 0 || !Budget(false, channel) || Cooling(RealmLogged[channel], kingdom.Key)) return;
            RealmLogged[channel][kingdom.Key] = GameClock.Now;
            RealmLines[channel]++;
            Empire empire = kingdom.GetEmpire();
            List<CorpsData> corps = CorpsSystem.Of(kingdom);
            float strength = corps.Sum(entry => Mathf.Max(0f, entry.strength)), full = corps.Sum(entry => Mathf.Max(0f, entry.full));
            string mark = ChannelOf(kingdom) == channel ? Marks[channel] : "";
            var text = new StringBuilder();
            text.Append($"[EmpireCraft][{Tags[channel]}·兵源] {Name(kingdom)}({CultureService.GetRealmCulture(kingdom)}" +
                        $"{mark}{(empire != null ? "·" + empire.GetEmpireName() : "")}) " +
                        $"背景人口{background:0} 征兵比例{levyShare:0.000} 武备兵源×{MaladySystem.LevyFactor(kingdom):0.00} " +
                        $"兵源{pool:0} 军费系数{budget:0.00} 实征{pool * budget:0} 现兵{strength:0}/{full:0} " +
                        $"积弊均{MaladySystem.Average(empire):0}/武备{MaladySystem.Get(empire, MaladySystem.Military):0} " +
                        $"正统{(empire != null ? empire.Legitimacy.ToString() : "—")} ");
            if (channel == NomadChannel)
                // 税率是实际生效的(游牧政体已打折，见 KingdomExtension.NomadTaxFactorOf)，看军费系数低是不是税收少
                text.Append($"思归×{NomadCycleSystem.HomesickLevy(kingdom):0.00} 自立门槛{NomadCycleSystem.SecessionGate(kingdom)} " +
                            $"税率{kingdom.GetTaxRate() * 100d:0.#}%");
            else
            {
                // 行省、埃米尔国(罗马、华夏同)的钱袋：央地分成(城/本国/中央)、年经常收入、国库，看军费系数是不是卡在钱上
                var (cityShare, localShare, centralShare) = TreasurySystem.TaxShares(kingdom);
                text.Append($"类别{kingdom.GetKingdomType()} 分成{cityShare}/{localShare}/{centralShare} " +
                            $"年入{StateDebtSystem.AnnualStableIncome(kingdom)} 国库{kingdom.GetMoney()}");
                if (channel != ArabicChannel) text.Append($" 政体{FrontSystem.ModRealm(kingdom)?.GetRegime()?.type}");
            }
            LogService.LogInfo(text.ToString());
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][战力平衡] 兵源诊断失败: {exception.Message}");
        }
    }

    // 每月交战结算后(FrontSystem.ResolveCity)
    public static void Battle(City city, Realm attacker, Realm owner, List<(Realm kingdom, CorpsData corps)> attackers,
        List<(Realm kingdom, CorpsData corps)> holders, FrontTerrain terrain, float ground, bool landing, bool breach,
        float remnant, float militia, float fort, float attack, float defend, float ratio)
    {
        try
        {
            if (city?.Record == null || EmpireWorld.Current == null) return;
            // 两边各属哪一路，取排在前面的那一路(游牧、阿拉伯、罗马、华夏)
            int attackerChannel = ChannelOf(attacker), ownerChannel = ChannelOf(owner);
            int channel = attackerChannel < 0 ? ownerChannel : ownerChannel < 0 ? attackerChannel : Mathf.Min(attackerChannel, ownerChannel);
            if (channel < 0 || !Budget(true, channel) || Cooling(BattleLogged[channel], city.Key)) return;
            BattleLogged[channel][city.Key] = GameClock.Now;
            BattleLines[channel]++;
            string attackerMark = attackerChannel == channel ? Marks[channel] : "";
            string ownerMark = ownerChannel == channel ? Marks[channel] : "";
            var text = new StringBuilder();
            text.Append($"[EmpireCraft][{Tags[channel]}·交战] {city.GetCityName()} 攻:{Name(attacker)}{attackerMark} ");
            Side(text, attackers);
            text.Append($" → 攻势{attack:0} | 守:{Name(owner)}{ownerMark} ");
            Side(text, holders);
            float entrench = Weighted(holders, corps => corps.entrench);
            text.Append($" 守势加成{(breach ? 1.1f + entrench * 0.5f : 1.25f + entrench):0.00} 困守×{remnant:0.00} 团练{militia:0} " +
                        $"关城×{fort:0.00} → 守势{defend:0} | 地形{TerrainSystem.Name(terrain)}×{ground:0.00}" +
                        $"{(landing ? " 登陆×0.70" : "")}{(breach ? " 重炮破城" : "")} 胜负比{ratio:0.00}");
            LogService.LogInfo(text.ToString());
        }
        catch (global::System.Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][战力平衡] 交战诊断失败: {exception.Message}");
        }
    }

    private static void Side(StringBuilder text, List<(Realm kingdom, CorpsData corps)> side)
    {
        float men = side.Sum(entry => entry.corps.strength);
        float effective = side.Sum(entry => FrontSystem.Effective(entry.corps));
        text.Append($"兵{men:0} 实效{effective:0}(空饷×{Weighted(side, PalaceArmySystem.RealShare):0.00} " +
                    $"武备×{Weighted(side, MaladySystem.MilitaryFactor):0.00} 组织度{Weighted(side, corps => corps.org):0} " +
                    $"义军×{Weighted(side, CorpsSystem.InsurgentFactor):0.00}) " +
                    $"火力×{Weighted(side, entry => FrontSystem.Firepower(entry.kingdom, entry.corps)):0.00} " +
                    $"统帅×{Weighted(side, entry => WarCommandSystem.Factor(entry.kingdom)):0.00} " +
                    $"补给×{Weighted(side, LogisticsSystem.AttackFactor):0.00} " +
                    $"雄主×{Weighted(side, entry => RulerTraitSystem.FounderCombat(entry.kingdom)):0.00} " +
                    $"八旗×{Weighted(side, entry => BannerSystem.CombatFactor(entry.kingdom, entry.corps)):0.00}");
    }

    private static float Weighted(List<(Realm kingdom, CorpsData corps)> side, global::System.Func<CorpsData, float> value) =>
        Weighted(side, entry => value(entry.corps));

    private static float Weighted(List<(Realm kingdom, CorpsData corps)> side,
        global::System.Func<(Realm kingdom, CorpsData corps), float> value)
    {
        float men = 0f, sum = 0f;
        foreach (var entry in side)
        {
            float weight = Mathf.Max(1f, entry.corps.strength);
            men += weight;
            sum += value(entry) * weight;
        }
        return men <= 0f ? 0f : sum / men;
    }
}
}
