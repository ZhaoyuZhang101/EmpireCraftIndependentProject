// Generated from the EmpireCraft mod: Scripts/GeneralSystems/FrontierRelations.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 农耕与游牧之间(用户 2026-10-19 选"和亲互市与降部戍边")，每年一次：
//   · 和亲：定居的君主制帝国与接壤的游牧政权(有游牧特性的文化)，对方兵力不弱于本国八成、或近十年来劫掠过本国时，
//     有一定机会和亲——君主的女儿或宗室女嫁给可汗或其储君(找不到合适的人也照样议和)，此后每年送岁币
//     (见 KingdomExtension.JoinGivenAlliance，二十年；岁币两国互不开战，见 WarGate)，正打着的仗就此罢兵；
//   · 互市：游牧与定居的邻国相安(不在打仗、近五年没劫掠过)，两边每年各得一笔市易之利，按两国中较少的人口算；
//     本文化施行了商队贸易(特性 caravan_trade)的，与任何异族邻国都有陆路商路，所得多两成半
//     (以前多五成；用户 2026-10-21 选文明平衡 A2"阿拉伯财政降一档"：互市、人头税、早早到高税叠在一起，前中期最富)；
//     通达层算好后(用户 2026-11-06 同意"先信息类、再贸易迁徙"，见 ConnectivitySystem)：两国人员往来的通达(取好的一边)
//     不到 frontier_market_min 的不通市——接壤却隔着雪山大漠、商旅过不去的，互市开不起来；
//   · 降部戍边：游牧政权战败、要向施行了羁縻的帝国称臣时，改为内附——整部并入帝国为羁縻州，承担边防(见 WarGoalSystem、
//     JimiSystem 的边防义务)。
public static class FrontierRelations
{
    public const string CaravanFeature = "caravan_trade";
    private const float HeqinChance = 0.2f, HeqinPowerShare = 0.8f, CaravanBonus = 1.25f;
    private const int HeqinRaidYears = 10, MarketRaidYears = 5;
    private static double _lastScan = -1d;

    public static void ResetWorldState() => _lastScan = -1d;

#if EC_PENDING
    public static bool IsNomadRealm(Realm kingdom) =>
        kingdom?.Record != null && !kingdom.IsGone() && CultureService.GetRealmCulture(kingdom).IsNomadCulture();

    public static void TryYearlyScan()
    {
        if (EmpireWorld.Current?.LiveRealms == null || ModClass.EMPIRE_MANAGER == null || ModClass.IS_CLEAR) return;
        double now = GameClock.Now;
        if (_lastScan >= 0d && now >= _lastScan && GameClock.YearsSince(_lastScan) < 1) return;
        _lastScan = now;
        var settled = new HashSet<(long, long)>();
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            try
            {
                Scan(empire, settled);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][边地] {empire?.GetEmpireName()} 和亲互市结算失败: {exception.Message}");
            }
        }
    }

    private static void Scan(Empire empire, HashSet<(long, long)> settled)
    {
        Realm core = empire?.CoreKingdom;
        if (empire?.data == null || empire.IsGone() || empire.IsArchived() || core?.Record == null || core.IsGone()) return;
        bool nomad = IsNomadRealm(core);
        foreach (Realm neighbour in empire.GetKingdomNeighbours())
        {
            Realm realm = neighbour?.IsInEmpire() == true ? neighbour.GetEmpire().CoreKingdom : neighbour;
            if (realm?.Record == null || realm.IsGone() || realm.IsWild || realm == core) continue;
            (long, long) pair = core.Key < realm.Key ? (core.Key, realm.Key) : (realm.Key, core.Key);
            if (!settled.Add(pair)) continue;
            bool frontier = nomad != IsNomadRealm(realm);
            if (frontier && !nomad) TryHeqin(empire, realm);
            Market(core, realm, frontier);
        }
    }
#endif

    // 和亲：本国定居、对方游牧
#if EC_PENDING
    private static void TryHeqin(Empire empire, Realm khan)
    {
        Realm core = empire.CoreKingdom;
        if (!RegimeManager.IsMonarchy(core.GetRegime()?.type) || khan.HasGivenAlliance() || empire.given_Kingdoms.Contains(khan) ||
            RepublicSystem.IsRepublic(empire) || EcRandom.value >= HeqinChance) return;
        bool raided = RaidedWithin(core, HeqinRaidYears);
        if (!raided && WarSituation.Power(khan) < WarSituation.Power(empire) * HeqinPowerShare) return;
        Person bride = RoyalMarriageSystem.ArrangeHeqin(core, khan);
        khan.JoinGivenAlliance(empire);
        khan.EndWarWith(core);
        string text = bride != null
            ? string.Format(LM.Get("frontier_heqin_history"), empire.GetEmpireName(), bride.FullName(), khan.GetKingdomName())
            : string.Format(LM.Get("frontier_heqin_plain_history"), empire.GetEmpireName(), khan.GetKingdomName());
        empire.RecordHistory(directContent: text, kingdomId: core.Key);
        khan.GetEmpire()?.RecordHistory(directContent: text, kingdomId: khan.Key);
        TranslateHelper.LogEventMessage(text, core);
    }
#endif

    // 互市、商路：两国相安时各得市易之利
#if EC_PENDING
    private static void Market(Realm a, Realm b, bool frontier)
    {
        bool caravanA = HasCaravan(a), caravanB = HasCaravan(b);
        bool foreign = !CultureService.SameKin(CultureService.GetRealmCulture(a), CultureService.GetRealmCulture(b));
        if (!frontier && !(foreign && (caravanA || caravanB))) return;
        // 买和开的互市(见 NomadRaidSystem)：期内遭劫过也照常通市
        if (a.IsAtWarWith(b) || (RaidedWithin(a, MarketRaidYears) || RaidedWithin(b, MarketRaidYears)) &&
            !NomadRaidSystem.MarketTreaty(a, b)) return;
        if (ConnectivitySystem.Ready)
        {
            float reach = Math.Max(ConnectivitySystem.Strength(a, b, ConnectivityMode.People),
                ConnectivitySystem.Strength(b, a, ConnectivityMode.People));
            if (reach >= 0f && reach < ConnectivitySystem.Config.frontier_market_min) return;
        }
        int amount = Mathf.Min(Population(a), Population(b)) / 10;
        if (amount <= 0) return;
        Pay(a, caravanA ? Mathf.RoundToInt(amount * CaravanBonus) : amount);
        Pay(b, caravanB ? Mathf.RoundToInt(amount * CaravanBonus) : amount);
        // 游牧一方记下互市(断了两年算互市断绝，见 NomadRaidSystem 草原压力)
        if (frontier) NomadRaidSystem.NoteMarket(a, b);
    }
#endif

    private static void Pay(Realm kingdom, int amount)
    {
        if (amount > 0 && TreasurySystem.Enabled(kingdom)) kingdom.AddMoney(amount, TreasuryCategory.PublicTrade);
    }

#if EC_PENDING
    private static bool HasCaravan(Realm kingdom) =>
        InstitutionSystem.HasFeature(CultureService.GetInstitutionCulture(kingdom), CaravanFeature);

    private static int Population(Realm kingdom) =>
        CityPopulationSystem.AbstractPopulationEnabled ? CityPopulationSystem.Households(kingdom) : kingdom.PopulationCount() ?? 0;
#endif

    private static bool RaidedWithin(Realm kingdom, int years)
    {
        double at = kingdom.GetOrCreate().raided_at;
        return at >= 0d && GameClock.YearsSince(at) < years;
    }

    // 降部戍边：游牧政权要向施行了羁縻的帝国称臣时，改为整部内附为羁縻州；返回是否内附了
#if EC_PENDING
    public static bool SettleSurrenderedTribe(Realm lord, Realm tribe)
    {
        Empire empire = lord?.GetEmpire();
        if (empire?.CoreKingdom == null || tribe?.Record == null || tribe.IsGone() || tribe.IsEmpire() || tribe.IsInEmpire() ||
            !IsNomadRealm(tribe) || IsNomadRealm(empire.CoreKingdom) || !JimiSystem.Enacted(empire)) return false;
        if (tribe.HasTakenAlliance()) tribe.RemoveTakenAlliance();
        empire.join(tribe, true, true);
        if (tribe.GetEmpire() != empire) return false;
        EmpireCraft.Core.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(tribe);
        string text = string.Format(LM.Get("frontier_tribe_settled_history"), tribe.GetKingdomName(), empire.GetEmpireName());
        empire.RecordHistory(directContent: text, kingdomId: tribe.Key);
        TranslateHelper.LogEventMessage(text, lord);
        return true;
    }
#endif
}
}
