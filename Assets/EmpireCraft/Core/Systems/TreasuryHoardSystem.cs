// Ported from the EmpireCraft mod: Scripts/GeneralSystems/TreasuryHoardSystem.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// 府库积压：国库、城库的死钱有去处。
// 本地存档第 191 年扎希哈里发国国库六万一，是第二名的四倍多，还在每年净攒三千；可全帝国的经常收支其实在亏
// (外来收入 2461、对外支出 3117)：军饷、战争开支多由成员国自己付，哈里发只收中央那份税和各城余钱上缴，国库没有花钱的出口。
// 百余年的积蓄(城库留成远超城里开支、打下的富城与部落连城库一起归来)就这样汇到核心放着不用；维京城邦、印度部落也一样攒死钱。
//   · 中央养兵：帝国核心国库超出储备的余钱，先替听调遣的成员国发野战军饷与战争开支(见 CorpsSystem.PayUpkeep)，
//     成员付不起时中央照旧补；兵额仍按成员自己的财力定(见 CorpsSystem.BudgetFactor)，钱多不等于兵多；
//   · 积压损耗：每年一次，国库超出储备、城库超出两年开支(同城库余钱上缴的口径，见 CityStabilitySystem.SurplusReserve)的部分
//     折损 DecayRate——国库一半记宫廷开支、一半记贪墨，荒淫、昏聩之君多损、明君少损，积弊深的多损；城库记胥吏侵吞。
// 储备 = ReserveYears 年必要开支(军饷、战争、治理、驻军、福利、维护，见 TreasuryRules.EssentialExpense)，至少 MinReserve
public static class TreasuryHoardSystem
{
    public const float ReserveYears = 3f;
    public const long MinReserve = 1000L;
    private const float DecayRate = 0.2f;
    private const int PerFrame = 48;

    private static object _world;
    private static int _year = int.MinValue;
    private static readonly Queue<Realm> KingdomQueue = new();
    private static readonly Queue<City> CityQueue = new();

    // 本年度的损耗与中央代发军饷，结完记一行 [EmpireCraft][府库]
    private static long _kingdomLoss, _cityLoss, _subsidy;
    private static int _kingdomCount, _cityCount;
    private static (string name, long loss, long left, long reserve) _largest;
    private static readonly Dictionary<long, long> SubsidyByCore = new();

    public static long Reserve(Realm kingdom)
    {
        TreasuryData data = kingdom.GetOrCreate().treasury;
        if (data == null || data.started < 0d) return MinReserve;
        // 账目不满一年的按已有月数折成一年
        long essential = TreasuryRules.EssentialExpense(data, GameClock.Now, GameClock.MonthsSince);
        int months = Math.Min(12, Math.Max(1, GameClock.MonthsSince(data.started) + 1));
        return Math.Max(MinReserve, (long)(essential * 12d / months * ReserveYears));
    }

    // 国库超出储备的余钱(没有就是 0)
#if EC_PENDING
    public static long Surplus(Realm kingdom) =>
        kingdom?.Record == null || kingdom.IsGone() || !TreasurySystem.Enabled(kingdom)
            ? 0L
            : Math.Max(0L, kingdom.GetTreasuryBalance() - Reserve(kingdom));
#endif

    public static void RecordSubsidy(Realm core, int amount)
    {
        if (amount <= 0) return;
        _subsidy += amount;
        SubsidyByCore.TryGetValue(core.Key, out long sum);
        SubsidyByCore[core.Key] = sum + amount;
    }

#if EC_PENDING
    public static void Tick()
    {
        if (EmpireWorld.Current == null) return;
        int year = GameClock.Year;
        if (!ReferenceEquals(_world, EmpireWorld.Current))
        {
            // 读档、换图：当年不结，下一年起
            _world = EmpireWorld.Current;
            _year = year;
            KingdomQueue.Clear();
            CityQueue.Clear();
            ResetTally();
            return;
        }
        if (year != _year)
        {
            _year = year;
            foreach (Realm kingdom in EmpireWorld.Current.LiveRealms ?? new List<Realm>()) KingdomQueue.Enqueue(kingdom);
            foreach (City city in EmpireWorld.Current.Cities ?? new List<City>()) CityQueue.Enqueue(city);
        }
        if (KingdomQueue.Count == 0 && CityQueue.Count == 0) return;
        for (int i = 0; i < PerFrame && KingdomQueue.Count > 0; i++) DecayKingdom(KingdomQueue.Dequeue());
        for (int i = 0; i < PerFrame && CityQueue.Count > 0; i++) DecayCity(CityQueue.Dequeue());
        if (KingdomQueue.Count == 0 && CityQueue.Count == 0) LogYear();
    }

    private static void DecayKingdom(Realm kingdom)
    {
        if (kingdom?.Record == null || kingdom.IsGone() || !TreasurySystem.Enabled(kingdom)) return;
        long reserve = Reserve(kingdom);
        long excess = kingdom.GetTreasuryBalance() - reserve;
        if (excess <= 0L) return;
        int loss = (int)Math.Min(int.MaxValue, (long)Math.Round(excess * (double)(DecayRate * Waste(kingdom))));
        if (loss <= 0) return;
        int palace = loss / 2;
        if (palace > 0) kingdom.SubMoney(palace, TreasuryCategory.Palace);
        kingdom.SubMoney(loss - palace, TreasuryCategory.Corruption);
        _kingdomLoss += loss;
        _kingdomCount++;
        if (loss > _largest.loss) _largest = (kingdom.GetKingdomName(), loss, kingdom.GetTreasuryBalance(), reserve);
    }
#endif

    // 荒淫、昏聩之君多损，明君少损；积弊(0~100)深的再多损至五成
#if EC_PENDING
    private static float Waste(Realm kingdom)
    {
        float factor = RulerQualitySystem.Of(kingdom.Ruler) switch
        {
            RulerQualitySystem.Grade.Wise => 0.75f,
            RulerQualitySystem.Grade.Weak => 1.1f,
            RulerQualitySystem.Grade.Tyrant => 1.1f,
            RulerQualitySystem.Grade.Fatuous => 1.25f,
            RulerQualitySystem.Grade.Dissolute => 1.5f,
            _ => 1f
        };
        Empire empire = kingdom.GetEmpire();
        if (empire?.data != null) factor *= 1f + MaladySystem.Average(empire) / 200f;
        return factor;
    }

    private static void DecayCity(City city)
    {
        if (city?.Record == null || city.IsGone() || !TreasurySystem.Enabled(city)) return;
        long excess = city.GetTreasuryBalance() - CityStabilitySystem.SurplusReserve(city);
        if (excess <= 0L) return;
        int loss = (int)Math.Min(int.MaxValue, (long)Math.Round(excess * (double)DecayRate));
        if (loss <= 0) return;
        city.SubMoney(loss, TreasuryCategory.Corruption);
        _cityLoss += loss;
        _cityCount++;
    }

    private static void LogYear()
    {
        if (_kingdomCount > 0 || _cityCount > 0 || _subsidy > 0)
        {
            long topCore = -1L, topSubsidy = 0L;
            foreach (KeyValuePair<long, long> pair in SubsidyByCore)
                if (pair.Value > topSubsidy) (topCore, topSubsidy) = (pair.Key, pair.Value);
            string core = topCore < 0 ? "" : EmpireWorld.Current.RealmByKey(topCore)?.GetKingdomName() ?? "";
            LogService.LogInfo($"[EmpireCraft][府库] 积压损耗：国库 {_kingdomCount} 国共 {MoneyDisplay.Format(_kingdomLoss)}，" +
                               $"城库 {_cityCount} 城共 {MoneyDisplay.Format(_cityLoss)}；" +
                               (_largest.loss > 0
                                   ? $"损耗最多 {_largest.name} {MoneyDisplay.Format(_largest.loss)}(余 {MoneyDisplay.Format(_largest.left)}，" +
                                     $"储备 {MoneyDisplay.Format(_largest.reserve)})；"
                                   : "") +
                               $"中央代发成员军饷共 {MoneyDisplay.Format(_subsidy)}" +
                               (topSubsidy > 0 ? $"，最多 {core} {MoneyDisplay.Format(topSubsidy)}" : ""));
        }
        ResetTally();
    }
#endif

    private static void ResetTally()
    {
        _kingdomLoss = _cityLoss = _subsidy = 0L;
        _kingdomCount = _cityCount = 0;
        _largest = default;
        SubsidyByCore.Clear();
    }
}
}
