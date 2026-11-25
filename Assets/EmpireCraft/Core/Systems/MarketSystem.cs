// Generated from the EmpireCraft mod: Scripts/GeneralSystems/MarketSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// 市场与交换(无小人模式，参考维多利亚 3)：原版靠小人一船一车运货，没有平民实体后贸易就停了。
// 这里按数据结算：
//   市场：同一国家(帝国则是整个帝国)的城市组成国内市场；有市场或码头的城还能和相邻、未交战的外国城市通商。
//   商品：粮食、木材、石料、金属。每城按户数算出应有储备(粮食一年口粮；木石按建设；金属按打造装备，交战时多备)。
//   交换：每月，储备不足的城向市场里有余量(超出储备一半以上)的城买进，按价格付钱
//        (粮食由民间存款付，木石金属由城市国库付，卖方的货是百姓的，见 PopulationEconomySystem.Savings；
//        百姓的货卖出去收到的是真钱，见 EnterpriseSystem.SaleCash)，
//        卖方收九成(一成是商人的利润，归卖方城里的背景商人；
//        卖粮的钱里租地收成那部分归地主，见 LandEconomySystem.AddBackgroundIncome)；每月能买卖多少受商贸能力限制(商人越多、有市场码头越多)。
//   价格：按市场总需求 ÷ 总供给在基础价的 0.25~4 倍之间浮动。
//   传播：买方受卖方主流理念影响；卖方的宗教(买方没有自建神庙时)、跨国时卖方的语言有小概率传入。
//   每次交换记作一次商队往来(见 ConstitutionalEconomySystem.RecordTrade)，城市就业的"近期航运"等都会计入。
// 通达层算好后(用户 2026-11-06 同意"先信息类、再贸易迁徙"，见 ConnectivitySystem)：
//   国内的卖家只取本城近邻表里(人员口径)成本不过 domestic_max_cost 的同一市场的城(以前全帝国的城一视同仁，西陲买东海的粮)；
//   外国邻城照旧要本城有市场或码头；买方付的价钱另加运费：市价 × domestic_freight × 成本(粮木石金属都又重又贱)，
//   运费是路上的花销，卖方照市价卖；到手价低的卖家先卖。商路传播(理念、宗教、语言)按两城之间的通达强度打折。
public static class MarketSystem
{
    public enum Good { Food, Wood, Stone, Metal }
    public static readonly Good[] Goods = { Good.Food, Good.Wood, Good.Stone, Good.Metal };

    private static readonly Dictionary<Good, float> BasePrice = new()
        { [Good.Food] = 1f, [Good.Wood] = 1f, [Good.Stone] = 1.5f, [Good.Metal] = 3f };

    private const float SurplusCushion = 1.5f;
    private const float SellerShare = 0.9f;
    private const float MinPriceFactor = 0.25f;
    private const float MaxPriceFactor = 4f;
    private const int BaseCapacity = 5;
    private const int CapacityPerMerchantHousehold = 2;
    private const int MarketCapacity = 20;
    private const int DocksCapacity = 20;
    private const float IdeologyInfluencePerTrade = 0.002f;
    private const float MaxReligionChance = 0.01f;
    private const float ReligionChancePerVolume = 0.0002f;

#if EC_PENDING
    private static List<ResourceAsset> _food;

    private static List<ResourceAsset> FoodAssets()
    {
        if (_food != null) return _food;
        _food = new List<ResourceAsset>();
        if (AssetManager.resources?.list != null)
            foreach (ResourceAsset asset in AssetManager.resources.list)
                if (asset != null && asset.type == ResType.Food) _food.Add(asset);
        return _food;
    }

    public static int Stock(City city, Good good)
    {
        switch (good)
        {
            case Good.Food:
                int food = 0;
                foreach (ResourceAsset asset in FoodAssets()) food += city.getResourcesAmount(asset.id);
                return food;
            case Good.Wood: return city.getResourcesAmount("wood");
            case Good.Stone: return city.getResourcesAmount("stone");
            default: return city.getResourcesAmount("common_metals");
        }
    }

    public static float Reserve(City city, Good good)
    {
        int households = CityPopulationSystem.Households(city);
        return good switch
        {
            Good.Food => households,
            Good.Wood => 15f + households * 0.2f,
            Good.Stone => 10f + households * 0.1f,
            _ => 5f + households * 0.05f + (CityPopulationSystem.IsAtWar(city) ? 10f : 0f)
        };
    }

    private static float Surplus(City city, Good good) => Mathf.Max(0f, Stock(city, good) - Reserve(city, good) * SurplusCushion);
    private static float Deficit(City city, Good good) => Mathf.Max(0f, Reserve(city, good) - Stock(city, good));

    public static int Capacity(City city)
    {
        int capacity = BaseCapacity;
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data?.groups != null)
        {
            float merchants = 0f;
            foreach (PopGroup group in data.groups)
                if (group.social_class == SocialClass.Merchant) merchants += group.Background;
            capacity += Mathf.RoundToInt(merchants / CityPopulationSystem.PeoplePerSlot(city)) * CapacityPerMerchantHousehold;
        }
        if (city.hasBuildingType("type_market")) capacity += MarketCapacity;
        if (city.hasBuildingType("type_docks")) capacity += DocksCapacity;
        // 港口、河口、路口商贸最旺(见 CityGeographySystem)
        return Mathf.RoundToInt(capacity * CityGeographySystem.Commerce(city));
    }
#endif

    // ---- 禁运与封锁 ----
    // 交战双方互不通商；双方的盟友也跟着禁运(本国盟友不和本国的敌人做生意，敌人的盟友也不和本国做生意)；
    // 正在被攻打、或一半以上区块被敌军占领的城被围封锁，买不进也卖不出
#if EC_PENDING
    public static bool Embargoed(Realm a, Realm b)
    {
        if (a == null || b == null || a == b) return false;
        if (a.IsEnemy(b)) return true;
        // 万国公会的制裁、冷战两个阵营之间的铁幕(见 WorldOrderSystem)
        if (WorldOrderSystem.TradeCut(a, b)) return true;
        // 关税战升到断绝通商(见 TradeWarSystem)
        if (TradeWarSystem.Cut(a, b)) return true;
        if (a.hasAlliance())
            foreach (Realm ally in a.getAlliance().kingdoms_list)
                if (ally != null && ally != a && !ally.IsGone() && ally.IsEnemy(b)) return true;
        if (b.hasAlliance())
            foreach (Realm ally in b.getAlliance().kingdoms_list)
                if (ally != null && ally != b && !ally.IsGone() && ally.IsEnemy(a)) return true;
        return false;
    }

    public static bool Blockaded(City city)
    {
        if (city == null || city.IsGone()) return true;
        if (city.isGettingCaptured()) return true;
        Dictionary<int, long> occupied = ScorchedEarthSystem.OccupiedZones(city);
        return occupied != null && city.Zones != null && city.Zones.Count > 0 && occupied.Count * 2 >= city.Zones.Count;
    }
#endif

    // ---- 围城断粮开城 ----
    // 被围封锁又闹饥荒，每月累计一次；满 SiegeFamineMonths 个月后每月有 (月数 - 2) × 10% 的概率守将开城投降，
    // 降给正在攻城的一方(没有就是占着本城区块最多的敌国)，按城破处理(伤亡、逃难、抢粮)
    private const int SiegeFamineMonths = 3;
    private const float SurrenderChancePerMonth = 0.1f;

#if EC_PENDING
    public static void CheckSiegeSurrender(City city, CityPopulationData data)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || city?.Realm == null || data == null) return;
        bool famine = data.last_food_shortage > 0f || CityPopulationSystem.GetGrowthFactors(city, data).Famine;
        if (!Blockaded(city) || !famine)
        {
            data.siege_famine_months = 0;
            return;
        }
        data.siege_famine_months++;
        if (data.siege_famine_months < SiegeFamineMonths) return;
        float chance = Mathf.Min(0.9f, SurrenderChancePerMonth * (data.siege_famine_months - 2));
        if (EcRandom.value >= chance) return;
        Realm besieger = city.being_captured_by;
        if (besieger == null || besieger.IsGone() || !besieger.IsEnemy(city.Realm))
        {
            besieger = null;
            Dictionary<int, long> occupied = ScorchedEarthSystem.OccupiedZones(city);
            if (occupied != null)
            {
                var counts = new Dictionary<long, int>();
                foreach (long id in occupied.Values) counts[id] = counts.TryGetValue(id, out int n) ? n + 1 : 1;
                int best = 0;
                foreach (KeyValuePair<long, int> pair in counts)
                {
                    Realm candidate = EmpireWorld.Current.RealmByKey(pair.Key);
                    if (candidate == null || candidate.IsGone() || !candidate.IsEnemy(city.Realm) || pair.Value <= best)
                        continue;
                    best = pair.Value;
                    besieger = candidate;
                }
            }
        }
        if (besieger == null) return;
        string cityName = city.GetCityName();
        Realm oldOwner = city.Realm;
        data.siege_famine_months = 0;
        city.JoinRealm(besieger, true);
        if (city.Realm == oldOwner) return;
        EmpireCraft.Core.TranslateHelper.LogEventOnly(
            string.Format(LM.Get("siege_famine_surrender"), cityName, besieger.GetKingdomName()),
            besieger);
    }
#endif

    // ---- 市场范围与价格 ----

    private static List<City> DomesticMarket(City city)
    {
        Empire empire = city.Realm?.GetEmpire();
        if (empire != null && !empire.IsGone() && !empire.IsArchived()) return empire.AllCities();
        return city.Realm?.Cities ?? new List<City>();
    }

    // 能做生意的卖家及买方到它的成本(通达层没算好时成本为 -1，不收运费)：见类头
    private static readonly List<ConnectivityNeighbour> NearBuffer = new();

#if EC_PENDING
    private static List<(City seller, float cost)> SellersByCost(City city)
    {
        var sellers = new List<(City, float)>();
        if (ConnectivitySystem.Ready && ConnectivitySystem.NearCities(city, NearBuffer, ConnectivitySystem.Config.domestic_max_cost) >= 0)
        {
            Empire empire = city.Realm?.GetEmpire();
            bool imperial = empire != null && !empire.IsGone() && !empire.IsArchived();
            var seen = new HashSet<City>();
            foreach (ConnectivityNeighbour near in NearBuffer)
            {
                City other = near.City;
                if (other == city || other.IsGone() || Blockaded(other) || Embargoed(city.Realm, other.Realm)) continue;
                bool domestic = other.Realm == city.Realm || imperial && other.Realm?.GetEmpire() == empire;
                if (!domestic || !seen.Add(other)) continue;
                sellers.Add((other, near.Cost));
            }
            if ((city.hasBuildingType("type_market") || city.hasBuildingType("type_docks")) && city.NeighbourCities != null)
                foreach (City other in city.NeighbourCities)
                {
                    if (other?.Realm == null || other.IsGone() || other.Realm.IsWild || seen.Contains(other)) continue;
                    if (other.Realm == city.Realm || Embargoed(city.Realm, other.Realm) || Blockaded(other)) continue;
                    float cost = ConnectivitySystem.NearCost(city, other, out _);
                    if (cost < 0f || float.IsPositiveInfinity(cost)) cost = ConnectivitySystem.Config.domestic_max_cost;
                    seen.Add(other);
                    sellers.Add((other, cost));
                }
            return sellers;
        }
        foreach (City other in Sellers(city)) sellers.Add((other, -1f));
        return sellers;
    }
#endif

    // 能做生意的卖家：国内市场，加上有市场或码头时相邻、未交战的外国城市
#if EC_PENDING
    private static List<City> Sellers(City city)
    {
        var sellers = new List<City>();
        foreach (City other in DomesticMarket(city))
            if (other != null && other != city && !other.IsGone() && !Blockaded(other) &&
                !Embargoed(city.Realm, other.Realm)) sellers.Add(other);
        if (!city.hasBuildingType("type_market") && !city.hasBuildingType("type_docks")) return sellers;
        if (city.NeighbourCities == null) return sellers;
        foreach (City other in city.NeighbourCities)
        {
            if (other?.Realm == null || other.IsGone() || sellers.Contains(other) || other.Realm.IsWild) continue;
            if (other.Realm == city.Realm || Embargoed(city.Realm, other.Realm) || Blockaded(other)) continue;
            sellers.Add(other);
        }
        return sellers;
    }
#endif

    private static readonly Dictionary<(object market, Good good), (double at, float price)> PriceCache = new();

    // 市场价格：国内市场总需求 ÷ 总供给(每个游戏月重算)
#if EC_PENDING
    public static float Price(City city, Good good)
    {
        List<City> market = DomesticMarket(city);
        object key = city.Realm?.GetEmpire() ?? (object)city.Realm;
        double now = EmpireWorld.Current?.Now() ?? 0d;
        if (key != null && PriceCache.TryGetValue((key, good), out var cached) && now >= cached.at &&
            GameClock.MonthsSince(cached.at) < 1) return cached.price;
        float demand = 0f, supply = 0f;
        foreach (City other in market)
        {
            if (other == null || other.IsGone()) continue;
            demand += Reserve(other, good);
            supply += Stock(other, good);
        }
        float factor = supply <= 0f ? MaxPriceFactor : Mathf.Clamp(demand / supply, MinPriceFactor, MaxPriceFactor);
        float price = BasePrice[good] * factor;
        if (key != null) PriceCache[(key, good)] = (now, price);
        return price;
    }
#endif

    // ---- 交换(每月随无小人模式的城市结算调用) ----

#if EC_PENDING
    public static void Settle(City buyer, CityPopulationData data)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || buyer?.Realm == null || buyer.Realm.IsWild || data == null)
            return;
        // 被围封锁：买不进
        if (Blockaded(buyer))
        {
            data.last_market_bought = 0f;
            return;
        }
        try
        {
            // 买卖额度是每月的：按距上次买卖过了几个月放大(每城的结算常常隔一年多才轮到一次，最多补 24 个月)
            double now = GameClock.Now;
            int months = data.last_market < 0d || now < data.last_market ? 1 : Mathf.Clamp(GameClock.MonthsSince(data.last_market), 1, 24);
            data.last_market = now;
            int capacity = Capacity(buyer) * months;
            float bought = 0f;
            // 本月已经用民间存款买粮花掉的钱
            float foodSpent = 0f;
            List<(City seller, float cost)> sellers = null;
            float freightPerCost = ConnectivitySystem.Config.domestic_freight;
            foreach (Good good in Goods)
            {
                if (capacity <= 0) break;
                float need = Deficit(buyer, good);
                if (need < 1f) continue;
                sellers ??= SellersByCost(buyer);
                float price = Price(buyer, good);
                // 到手价(市价加运费，见类头)低的先卖，同价的余量多的先卖(每种商品只算一次各卖家的余量)
                var offers = new List<(City seller, float surplus, float unit, float cost)>(sellers.Count);
                foreach ((City candidate, float cost) in sellers)
                {
                    float available = Surplus(candidate, good);
                    if (available >= 1f) offers.Add((candidate, available, cost > 0f ? price * (1f + freightPerCost * cost) : price, cost));
                }
                offers.Sort((a, b) => a.unit != b.unit ? a.unit.CompareTo(b.unit) : b.surplus.CompareTo(a.surplus));
                foreach ((City seller, float surplus, float unit, float cost) in offers)
                {
                    if (need < 1f || capacity <= 0) break;
                    // 粮食是百姓自己买，看民间存款；木石金属是公家买，看城市国库
                    float purse = good == Good.Food
                        ? Mathf.Max(0f, PopulationEconomySystem.Savings(buyer, data) - foodSpent)
                        : Mathf.Max(0, buyer.GetMoney());
                    int affordable = unit <= 0f ? 0 : Mathf.FloorToInt(purse / unit);
                    int amount = Mathf.Min(Mathf.FloorToInt(Mathf.Min(need, surplus)), Mathf.Min(capacity, affordable));
                    amount = Mathf.Min(amount, Capacity(seller) * months);
                    if (amount <= 0) continue;
                    int moved;
                    using (PopulationEconomySystem.PrivateUse()) moved = Move(seller, buyer, good, amount);
                    if (moved <= 0) continue;
                    // 买方付到手价(含运费)，卖方按市价卖；差额是路上的运费
                    int value = Mathf.CeilToInt(moved * unit);
                    int sale = Mathf.Min(value, Mathf.CeilToInt(moved * price));
                    // 买粮：钱换粮，民间家底不变；买木石金属：城市国库付钱，买来的算公家的
                    if (good != Good.Food)
                    {
                        buyer.SubMoney(value, TreasuryCategory.PublicTrade);
                        PopulationEconomySystem.AddPublicStock(buyer, MaterialId(good), moved);
                    }
                    else foodSpent += value;
                    // 卖方：先卖公家的存货，九成价钱归城市国库
                    int fromPublic = good == Good.Food ? 0 : PopulationEconomySystem.TakePublicStock(seller, MaterialId(good), moved);
                    int publicValue = Mathf.FloorToInt(sale * (float)fromPublic / moved);
                    if (publicValue > 0)
                        seller.AddMoney(Mathf.FloorToInt(publicValue * SellerShare *
                            ClaimPolicySystem.TradeIncomeFactor(seller.Realm)), TreasuryCategory.PublicTrade);
                    // 其余是百姓的货，九成价卖掉，家底少一成(商人的利润)；卖粮的钱里租地收成那部分是地主的
                    int privateValue = sale - publicValue;
                    int sellerGets = Mathf.FloorToInt(privateValue * SellerShare);
                    int rent = good == Good.Food
                        ? Mathf.FloorToInt(sellerGets * LandEconomySystem.BackgroundRentShare(seller)) : 0;
                    LandEconomySystem.AddBackgroundIncome(seller, privateValue - sellerGets, rent);
                    // 百姓的货卖出去收的是真钱(见 EnterpriseSystem.SaleCash)：公家买的由买方国库付的钱里出，百姓买的粮由买方百姓的现钱付
                    EnterpriseSystem.SaleCash(buyer, data, seller, sellerGets, good == Good.Food);
                    need -= moved;
                    capacity -= moved;
                    bought += value;
                    RecordSale(seller, sale);
                    bool foreign = seller.Realm != buyer.Realm &&
                                   (seller.Realm?.GetEmpire() == null || seller.Realm.GetEmpire() != buyer.Realm.GetEmpire());
                    ConstitutionalEconomySystem.RecordTrade(seller, buyer, foreign, sale);
                    Spread(seller, buyer, moved, foreign, cost);
                }
            }
            data.last_market_bought = bought;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][市场] 交换失败({buyer.Name}): {exception.Message}");
        }
    }
#endif

    private static readonly Dictionary<City, float> SoldThisMonth = new();

    private static void RecordSale(City seller, int value)
    {
        CityPopulationData data = CityPopulationSystem.Get(seller);
        if (data == null) return;
        double now = GameClock.Now;
        if (data.market_sold_since < 0d || GameClock.MonthsSince(data.market_sold_since) >= 1)
        {
            data.last_market_sold = data.market_sold_month;
            data.market_sold_month = 0f;
            data.market_sold_since = now;
        }
        data.market_sold_month += value;
    }

    private static string MaterialId(Good good) =>
        good == Good.Wood ? "wood" : good == Good.Stone ? "stone" : "common_metals";

    // 从卖方仓库搬到买方仓库，返回实际搬运的数量
#if EC_PENDING
    private static int Move(City seller, City buyer, Good good, int amount)
    {
        int moved = 0;
        if (good == Good.Food)
        {
            foreach (ResourceAsset asset in FoodAssets())
            {
                if (moved >= amount) break;
                moved += CityResourceTransferSystem.Move(seller, buyer, asset.id, amount - moved);
            }
            return moved;
        }
        return CityResourceTransferSystem.Move(seller, buyer, MaterialId(good), amount);
    }
#endif

    // 商路传播：理念、宗教、(跨国时)语言；通达层算好后按两城之间(人员口径)的通达强度打折(cost 为 -1 时不打折)
#if EC_PENDING
    private static void Spread(City seller, City buyer, int volume, bool foreign, float cost = -1f)
    {
        float reach = cost >= 0f ? ConnectivitySystem.StrengthOf(cost, seller) : 1f;
        if (reach < 0f) reach = 1f;
        if (reach <= 0f) return;
        PartyIdeology ideology = IdeologyPopulationSystem.GetDominant(seller);
        if (IdeologyPopulationSystem.GetDominant(buyer) != ideology)
            IdeologyPopulationSystem.InfluenceCity(buyer, ideology, Mathf.Min(0.05f, IdeologyInfluencePerTrade * volume) * reach);
        float chance = Mathf.Min(MaxReligionChance, ReligionChancePerVolume * volume) * reach;
        if (seller.religion != null && buyer.religion != seller.religion && !buyer.hasBuildingType("type_temple") &&
            EcRandom.value < chance)
            buyer.setReligion(seller.religion);
        if (foreign && seller.language != null && buyer.language != seller.language && EcRandom.value < chance)
            buyer.setLanguage(seller.language);
    }
#endif

    public static void ResetWorldState()
    {
        PriceCache.Clear();
        SoldThisMonth.Clear();
    }
}
}
