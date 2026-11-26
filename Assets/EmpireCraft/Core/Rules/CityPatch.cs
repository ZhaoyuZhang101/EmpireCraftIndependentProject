// Generated from the EmpireCraft mod: Scripts/GamePatches/CityPatch.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using Time = EmpireCraft.Core.FrameClock;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EmpireCraft.Core.CityExtension;

namespace EmpireCraft.Core
{

public class CityPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    // ============================================================
    // 低兵力城市：帝国军入城即降
    // ============================================================

    // 整个守方 Realm 只剩 3 名及以下存活 Warrior 时触发。
    private const int ImperialArrivalSurrenderWarriorThreshold = 3;

    // 每帧只扫描一次 EmpireWorld.Current.units，避免每座城市都全量扫描单位。
    private static int _imperialPresenceCacheFrame = -1;

    // 同一帧内缓存“整个 Realm 的存活 Warrior 总数”。
    // 统计包含在外征战、驻扎其他城市、正在移动的所有本国 Warrior，
    // 而不是只统计当前城市里的守军。
    private static int _kingdomWarriorCountCacheFrame = -1;
    private static readonly Dictionary<Realm, int> _kingdomLivingWarriorCounts = new();

    // key = 当前帝国士兵实际所在的城市
    // value = 第一个检测到的、与该城市所属国家交战的帝国士兵所属 Realm
    private static readonly Dictionary<City, Realm> _imperialAttackerByCity = new();

    // 防止 finishCapture -> 城市状态变化 -> update 重入导致重复归降。
    private static readonly HashSet<City> _imperialArrivalSurrenderGuard = new();
    // 入城即降没能让城市易主时（接收国解析失败等），该城在冷却期内不再重试，
    // 否则每帧都会重新触发一次投降结算并刷屏日志。键为城市，值为失败时的世界时间。
    private static readonly Dictionary<City, double> _imperialArrivalSurrenderCooldown = new();
    private const int ImperialArrivalSurrenderRetryMonths = 6;

#if EC_PENDING
    public void Initialize()
    {

        new Harmony(nameof(destroy_city)).Patch(
            AccessTools.Method(typeof(City), nameof(City.destroyCity)),
            prefix: new HarmonyMethod(GetType(), nameof(destroy_city))
        );

        new Harmony(nameof(HasReachedWorldLawLimit)).Patch(
            AccessTools.Method(typeof(City), nameof(City.hasReachedWorldLawLimit)),
            prefix: new HarmonyMethod(GetType(), nameof(HasReachedWorldLawLimit))
        );
        
        new Harmony(nameof(getPopulationMaximum)).Patch(
            AccessTools.Method(typeof(City), nameof(City.GetPopulationMaximum)),
            prefix: new HarmonyMethod(GetType(), nameof(getPopulationMaximum))
        );
        
        new Harmony(nameof(isArmyOverLimit)).Patch(
            AccessTools.Method(typeof(City), nameof(City.isArmyOverLimit)),
            prefix: new HarmonyMethod(GetType(), nameof(isArmyOverLimit))
        );

        // 国库亏空时只有京师养得起兵(见 EmpireBankruptcySystem)。原版招兵看 status.warrior_slots，不看 getMaxWarriors，两处都要改
        new Harmony(nameof(unpaid_max_warriors)).Patch(
            AccessTools.Method(typeof(City), nameof(City.getMaxWarriors)),
            postfix: new HarmonyMethod(GetType(), nameof(unpaid_max_warriors)) { priority = Priority.Last }
        );
        new Harmony(nameof(unpaid_warrior_slots)).Patch(
            AccessTools.Method(typeof(City), "updateCityStatus"),
            postfix: new HarmonyMethod(GetType(), nameof(unpaid_warrior_slots)) { priority = Priority.Last }
        );

        new Harmony(nameof(removeData)).Patch(
            AccessTools.Method(typeof(City), nameof(City.Dispose)),
            prefix: new HarmonyMethod(GetType(), nameof(removeData))
        );

        new Harmony(nameof(setKingdom)).Patch(
            AccessTools.Method(typeof(City), nameof(City.SetKingdom)),
            prefix: new HarmonyMethod(GetType(), nameof(setKingdom)),
            postfix: new HarmonyMethod(GetType(), nameof(setKingdom_Postfix))
        );

        new Harmony(nameof(zone_steal)).Patch(
            AccessTools.Method(typeof(CityBehBorderSteal), nameof(CityBehBorderSteal.tryStealZone)),
            prefix: new HarmonyMethod(GetType(), nameof(zone_steal))
        );

        new Harmony(nameof(removeObject)).Patch(
            AccessTools.Method(typeof(CityManager), nameof(CityManager.removeObject)),
            prefix: new HarmonyMethod(GetType(), nameof(removeObject))
        );

        new Harmony(nameof(city_update)).Patch(
            AccessTools.Method(typeof(City), nameof(City.update)),
            prefix: new HarmonyMethod(GetType(), nameof(city_update))
        );

        new Harmony(nameof(joinAnotherKingdom)).Patch(
            AccessTools.Method(typeof(City), nameof(City.JoinRealm)),
            prefix: new HarmonyMethod(GetType(), nameof(joinAnotherKingdom))
        );

        new Harmony(nameof(makeOwnKingdom)).Patch(
            AccessTools.Method(typeof(City), nameof(City.MakeOwnRealm)),
            prefix: new HarmonyMethod(GetType(), nameof(makeOwnKingdom))
        );

        // 原版激励掉落(神力)让城当场自立再开战：算神力，不许自立的闸放行(见 SecessionGate)
        new Harmony(nameof(BeforeInspire)).Patch(
            AccessTools.Method(typeof(City), nameof(City.useInspire)),
            prefix: new HarmonyMethod(GetType(), nameof(BeforeInspire)),
            finalizer: new HarmonyMethod(GetType(), nameof(AfterInspire))
        );

        new Harmony(nameof(addZone)).Patch(
            AccessTools.Method(typeof(City), nameof(City.AddZone)),
            prefix: new HarmonyMethod(GetType(), nameof(addZone))
        );

        new Harmony(nameof(removeZone)).Patch(
            AccessTools.Method(typeof(City), nameof(City.RemoveZone)),
            prefix: new HarmonyMethod(GetType(), nameof(removeZone))
        );

        new Harmony(nameof(setLeader)).Patch(
            AccessTools.Method(typeof(City), nameof(City.SetLeader)),
            prefix: new HarmonyMethod(GetType(), nameof(setLeader))
        );

        new Harmony(nameof(GetHouseLimit)).Patch(
            AccessTools.Method(typeof(City), nameof(City.getHouseLimit)),
            prefix: new HarmonyMethod(GetType(), nameof(GetHouseLimit))
        );

        new Harmony(nameof(removeLeader)).Patch(
            AccessTools.Method(typeof(City), nameof(City.RemoveLeader)),
            prefix: new HarmonyMethod(GetType(), nameof(removeLeader))
        );

        new Harmony(nameof(TryToMakeWarrior)).Patch(
            AccessTools.Method(typeof(City), nameof(City.tryToMakeWarrior)),
            prefix: new HarmonyMethod(GetType(), nameof(TryToMakeWarrior))
        );

        new Harmony(nameof(newCity)).Patch(
            AccessTools.Method(typeof(City), nameof(City.newCityEvent)),
            prefix: new HarmonyMethod(GetType(), nameof(newCity))
        );

        new Harmony(nameof(CityBuilt)).Patch(
            AccessTools.Method(typeof(CityManager), nameof(CityManager.buildNewCity)),
            postfix: new HarmonyMethod(GetType(), nameof(CityBuilt))
        );

        new Harmony(nameof(FinishedCapture)).Patch(
            AccessTools.Method(typeof(City), nameof(City.FinishCapture)),
            prefix: new HarmonyMethod(GetType(), nameof(FinishedCapture))
        );

        new Harmony(nameof(RecalculateMaxHouses)).Patch(
            AccessTools.Method(typeof(City), nameof(City.recalculateMaxHouses)),
            prefix: new HarmonyMethod(GetType(), nameof(RecalculateMaxHouses))
        );

        new Harmony(nameof(AddCapturePoints)).Patch(
            original: AccessTools.Method(
                typeof(City),
                nameof(City.addCapturePoints),
                new Type[] { typeof(Realm), typeof(int) }
            ),
            prefix: new HarmonyMethod(GetType(), nameof(AddCapturePoints))
        );
        
        new Harmony(nameof(countWarriors)).Patch(
            AccessTools.Method(typeof(City), nameof(City.Warriors)),
            prefix: new HarmonyMethod(GetType(), nameof(countWarriors))
        );
        new Harmony(nameof(getPopulationPeople)).Patch(
            AccessTools.Method(typeof(City), nameof(City.PopulationCount)),
            prefix: new HarmonyMethod(GetType(), nameof(getPopulationPeople))
        );
        new Harmony(nameof(getMainSubspecies)).Patch(
            AccessTools.Method(typeof(City), nameof(City.getMainSubspecies)),
            prefix: new HarmonyMethod(GetType(), nameof(getMainSubspecies))
        );
        new Harmony(nameof(CanUseBuildAsset)).Patch(
            AccessTools.Method(typeof(CityBehBuild), nameof(CityBehBuild.canUseBuildAsset)),
            prefix: new HarmonyMethod(GetType(), nameof(CanUseBuildAsset))
        );
    }
#endif
    // 游牧扩张攻下都城整国并入的上限(城数)，再大的改为称臣纳贡(用户 2026-10-20 选"游牧来得快去得快"：三城改两城)
    private const int NomadAnnexMaxCities = 2;

#if EC_PENDING
    public static bool CanUseBuildAsset(CityBehBuild __instance, BuildOrder pBuildAsset, City pCity, ref bool __result)
    {
        using var timing = FrameProfiler.Measure("建造钩子·能否建造");
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(pCity)) return true;
        BuildingKind buildingAsset = CityBuildSafetyPatch.ResolveOrderBuilding(pCity, pBuildAsset?.id);
        // 没有资源定义的订单直接略过；其余仍按本文化的科技解锁建造或升级。
        if (buildingAsset?.cost == null || pCity?.Status == null || !TechnologyPatch.CanUseBuildOrder(pBuildAsset, pCity))
        {
            __result = false;
            return false;
        }
        // 纯净地图：矿场、伐木场、牧场、屠宰场改为城外村镇的生计(见 LivelihoodSystem)，不再建、不再升
        if (EmpireCraft.Core.LivelihoodSystem.Replaces(buildingAsset))
        {
            __result = false;
            return false;
        }
        if (pBuildAsset.min_zones != 0 && pCity.Zones.Count < pBuildAsset.min_zones)
        {
            __result = false;
            return false;
        }
        int num = pCity.CountBuildingsType(buildingAsset.Type, false);
        if (pBuildAsset.check_house_limit)
        {
            if (pCity.Status.HousingFree > 10)
            {
                __result = false;
                return false;
            }
            int houseLimit = pCity.getHouseLimit();
            if (num >= houseLimit)
            {
                __result = false;
                return false;
            }
        }
        int limitOfBuildingsType = pCity.getLimitOfBuildingsType(pBuildAsset);
        if (!pCity.HasBeenCombined())
        {
            if (pCity.Status.Population < pBuildAsset.required_pop ||
                CityLedger.Buildings(pCity) < pBuildAsset.required_buildings)
            {
                __result = false;
                return false;
            }
        }
        if (limitOfBuildingsType != 0 && num >= limitOfBuildingsType ||
            pBuildAsset.check_full_village && pCity.Status.HousingFree != 0 ||
            !HasRequiredBuildings(pBuildAsset, pCity) ||
            !CityBehBuild.haveRequiredBuildingTypes(pBuildAsset.requirements_types, pCity))
        {
            __result = false;
            return false;
        }
        if (pBuildAsset.upgrade)
        {
            // 记在建筑台账里的也算(见 CityLedger；countBuildingsOfID 已把台账的数加上)
            if (pCity.countBuildingsOfID(buildingAsset.Id) == 0)
            {
                __result = false;
                return false;
            }
        }
        else if (buildingAsset.docks && CityBehBuild.getDockTile(pCity) == null)
        {
            __result = false;
            return false;
        }
        __result = true;
        return false;
    }
#endif

    // 原版允许用升级后的建筑满足前置条件，逐级检查时也可能碰到缺失的模组建筑。
#if EC_PENDING
    private static bool HasRequiredBuildings(BuildOrder order, City city)
    {
        if (order.requirements_orders == null) return true;
        foreach (string id in order.requirements_orders)
        {
            BuildingKind asset = CityBuildSafetyPatch.ResolveOrderBuilding(city, string.IsNullOrEmpty(id) ? order.id : id);
            if (asset == null) return false;
            if (asset.Id == asset.UpgradeTo) continue;
            int remaining = BuildingKinds.Table.dict.Count;
            while (city.countBuildingsOfID(asset.Id) == 0)
            {
                if (!asset.can_be_upgraded || remaining-- == 0) return false;
                asset = CityLedger.Asset(asset.UpgradeTo);
                if (asset == null) return false;
            }
        }
        return true;
    }
#endif

    public static bool GetHouseLimit(City __instance, ref int __result)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        if (__instance.Buildings.Any(b => b.Asset.Id.Contains("city_")))
        {
            __result = 1;
            return false;
        }
        return true;
    } 
#if EC_PENDING
    private static bool RecalculateMaxHouses(City __instance)
    {
        using var timing = FrameProfiler.Measure("建造钩子·房屋上限");
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        if (__instance.Buildings.Any(b => b.Asset.Id.Contains("city_")))
        {
            __instance.Status.HousesMax = 0;
            return false;
        }
        return true;
    }
    public static bool AddCapturePoints(City __instance, Realm pKingdom, int pValue)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        if (WorldLaws.SwitchOccupyMode.IsEnabled()) return false;
        if (__instance.TryTriggerOccupationCaptureEvent(pKingdom))
        {
            return false;
        }
        int num;
        __instance._capturing_units.TryGetValue(pKingdom, out num);
        //正统值加成
        var mandateAddition = 0;
        //原帝国
        var originEmpirePoint = (__instance.Realm?.GetEmpire()?.Mandate??0);
        //新帝国
        var captureEmpirePoint = (pKingdom.GetEmpire()?.Mandate??0);
        mandateAddition += Mathf.Clamp(captureEmpirePoint-originEmpirePoint, 0, 30);
        //同文化加成
        var sameCultureFactor = (__instance.Realm?.GetEmpireCraftCulture()==pKingdom.GetEmpireCraftCulture())?2:0;
        mandateAddition *= sameCultureFactor;
        var kingDeathAddition = 0;
        if (__instance.Realm == null || !__instance.Realm.HasRuler() || __instance.Realm.Ruler == null || __instance.Realm.Ruler.IsGone() || !__instance.Realm.Ruler.Alive)
        {
            kingDeathAddition = 20;
        }
        __instance._capturing_units[pKingdom] = num + pValue + mandateAddition + kingDeathAddition;
        return false;
    }
#endif
    public static bool getMainSubspecies(City __instance, ref Subspecies __result)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        // 无小人模式下人口计入虚拟人口，城里可能一个实体都没有：原版会去取 units[0] 越界
        if (__instance.CountLivingPopulation() == 0 ||
            !__instance.HasLeader() && (__instance.People == null || __instance.People.Count == 0))
        {
            __result = null;
            return false;
        }
        return true;
    }

    public static bool TryToMakeWarrior(City __instance, Person pActor)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        return false;
    }
    public static bool FinishedCapture(City __instance, Realm pNewKingdom)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        if (__instance == null || __instance.Realm == null || pNewKingdom == null || pNewKingdom.IsGone())
            return false;

        Realm oldKingdom = __instance.Realm;
        bool isEmpireCapital = oldKingdom.IsEmpire() && oldKingdom.Capital == __instance;
        Empire targetEmpire = oldKingdom.GetEmpire();
        if (false)
            return false;
        // 分封制下的小乱攻不下原主都城、至多两座城(见 PettyRebellion)
        if (EmpireCraft.Core.PettyRebellion.Blocks(pNewKingdom, __instance))
        {
            __instance.ClearCapture();
            return false;
        }
        // 诸侯私战只争清单里的边城，都城与别的城打不下(见 HolyRomanSystem.Feuds)
        if (EmpireCraft.Core.HolyRomanSystem.BlocksFeudCity(pNewKingdom, __instance))
        {
            __instance.ClearCapture();
            return false;
        }
        // 游牧争草场只争清单里的边城(见 SteppeOrderSystem)
        if (EmpireCraft.Core.SteppeOrderSystem.BlocksPastureCity(pNewKingdom, __instance))
        {
            __instance.ClearCapture();
            return false;
        }
        // 部落之间破城不易主：部落联合以前赔款求和，以后奉胜者为共主，占的地全数退还(见 TribalWarSystem)
        if (EmpireCraft.Core.TribalWarSystem.TrySubmit(__instance, pNewKingdom))
        {
            __instance.ClearCapture();
            return false;
        }

        using (ListPool<War> pWars = new ListPool<War>(pNewKingdom.GetWars()))
        {
            Realm joinAfterCapture = __instance.FindKingdomToJoinAfterCapture(pNewKingdom, pWars);
            if (joinAfterCapture == null || joinAfterCapture.IsGone())
            {
                LogService.LogWarning(
                    $"城市占领结算找不到有效接收国：城市={__instance.Name}, 进攻方={pNewKingdom.Name}");
                return false;
            }

            // 原版的接收国解析在帝国战争里可能返回守城国自己（进攻方并非战争的直接参与者时），
            // 这样"占领"不会让城市易主，只会每次重复结算。此时改由实际进攻方接收；
            // 同一帝国的成员之间(藩王之乱、地方叛乱、军阀混战)只要确实在交战，同样由进攻方接收——
            // 以前同帝国一律放弃结算，内战里城市占满进度就被清零、永远打不下来。
            // 进攻方就是守城国、或双方并未交战时才放弃本次结算并清空占领进度。
            if (joinAfterCapture == oldKingdom)
            {
                if (pNewKingdom != oldKingdom &&
                    (!pNewKingdom.IsInSameEmpire(oldKingdom) || pNewKingdom.IsAtWarWith(oldKingdom)))
                {
                    joinAfterCapture = pNewKingdom;
                }
                else
                {
                    LogService.LogWarning(
                        $"城市占领结算的接收国就是守城国自身，放弃本次结算：城市={__instance.Name}, 进攻方={pNewKingdom.Name}");
                    ClearResolvedCaptureProgress(__instance);
                    return false;
                }
            }

            // 帝国成员在中央也参战的对外战争里打下的城：战时交中央暂管，战后再分配(见 WartimeCustodySystem)
            Realm actualCapturer = joinAfterCapture;
            Realm custodian = WartimeCustodySystem.CustodianFor(joinAfterCapture, oldKingdom);
            if (custodian != null) joinAfterCapture = custodian;

            LogService.LogInfo($"{__instance.Realm}的城市{__instance.Name}即将被{joinAfterCapture.Name}捕获");
            War war = null;
            War expendWar = null;
            War chaoGongWar = null;
            War coronationWar = null;
            War religionWar = null;
            War empireRoyalAcquireEmpireWar = null;
            bool attackerInEmpire = joinAfterCapture.IsAttacker() && joinAfterCapture.IsInEmpire();
            foreach (War w in pWars)
            {
                // pNewKingdom 可能同时参加多场战争。这里只能读取与当前守城国
                // 真正处于敌对阵营的那一场，否则别处的劫掠/朝贡战争会让本城
                // 提前结束占领结算，造成进度清空但城市不易主。
                if (!IsWarRelevantToCapture(w, pNewKingdom, joinAfterCapture, oldKingdom))
                    continue;

                LogService.LogInfo($"战争名称{w.Name}类型：{w.GetEmpireWarType().ToString()}");
                EmpireWarType warType = w.GetEmpireWarType();
                bool capturingSideIsAttacker =
                    IsKingdomRepresentedOnWarSide(w, pNewKingdom, true) ||
                    IsKingdomRepresentedOnWarSide(w, joinAfterCapture, true);
                bool eligibleImperialAttacker = attackerInEmpire && capturingSideIsAttacker;

                // 劫掠之战：攻方一边(联盟各部、独立的国都算，不必是帝国)破城即洗劫(见 NomadRaidSystem)
                if (war == null && capturingSideIsAttacker && warType == EmpireWarType.劫掠)
                    war = w;
                if (expendWar == null && eligibleImperialAttacker && warType == EmpireWarType.游牧扩张)
                    expendWar = w;
                if (chaoGongWar == null && eligibleImperialAttacker && warType == EmpireWarType.迫使朝贡)
                    chaoGongWar = w;
                if (coronationWar == null && capturingSideIsAttacker && warType == EmpireWarType.逼冕)
                    coronationWar = w;
                if (religionWar == null && capturingSideIsAttacker && warType == EmpireWarType.神圣)
                    religionWar = w;
                if (empireRoyalAcquireEmpireWar == null && eligibleImperialAttacker && warType == EmpireWarType.藩王索取皇位)
                    empireRoyalAcquireEmpireWar = w;
            }
            //检测城市是否被劫掠如果是则不执行占领城市逻辑但是相应的城市金库会被劫走
            if (war!= null)
            {
                // 劫掠只抢不占：洗劫这座城(金帛、粮、人口按出兵分给各部)，遭劫的国五年内不再遭劫；
                // 抢够了(单国一城，联盟每部一城)立即判攻方胜、整场罢兵，而不是只让当前守城国退出(见 NomadRaidSystem)
                bool enough = NomadRaidSystem.OnSack(war, __instance, pNewKingdom);
                ClearResolvedCaptureProgress(__instance);
                if (enough && war.Alive && !war.HasEnded())
                    EmpireWorld.Current.EndWar(war, WarOutcome.Attackers);
                return false;
            }
            //检测是否为游牧扩张战争，如果是则返还法理土地，保留制度但是加入帝国
            // 只吞小国(用户 2026-10-17 选"攻下都城只吞小国")：两城以内整国并入(见 NomadAnnexMaxCities)，大国改为称臣纳贡(同迫使朝贡)
            // 南下打的是别的帝国的成员(或来救援的那个帝国)：城照常易手，不整国并入、不改称臣
            if (expendWar != null && __instance.Realm.GetEmpire() is Empire otherEmpire &&
                otherEmpire != joinAfterCapture.GetEmpire())
            {
                LogService.LogInfo($"游牧扩张战争：南下攻取{otherEmpire.GetEmpireName()}的城{__instance.Name}");
                expendWar = null;
            }
            if (expendWar != null)
            {
                LogService.LogInfo("游牧扩张战争");
                Empire empire = joinAfterCapture.GetEmpire();
                if (__instance.IsCapital() && __instance.Realm.CountCities() > NomadAnnexMaxCities && empire != null)
                {
                    // 收不了朝贡(还没施行驿传网络之类的朝贡制度)就改为赔款三成，不至于打下都城一无所得
                    if (EmpireCraft.Core.FeudalConquestService.HasTributeInstitution(empire))
                        __instance.Realm.JoinTakenAlliance(empire, pForce: true);
                    else EmpireCraft.Core.TreasurySystem.TransferWarSpoils(__instance, pNewKingdom, 0.3f);
                    ClearResolvedCaptureProgress(__instance);
                    expendWar.Lost(__instance.Realm);
                    return false;
                }
                if (__instance.IsCapital()&&__instance.GetTitle()?.title_capital==__instance)
                {
                    __instance.GetTitle().city_list.ForEach(c =>
                    {
                        if (c.Realm.IsInSameEmpire(joinAfterCapture) && !c.IsCapital())
                        {
                            c.JoinRealm(__instance.Realm);
                        }
                    });
                    empire.join(__instance.Realm, pForce:true);
                    ClearResolvedCaptureProgress(__instance);
                    expendWar.Lost(__instance.Realm);
                    return false;
                }
            }
            // 逼冕之战(见 RealmNamingSystem.Universal)：教廷都城或圣城到手即算得手，城不易主，教宗加冕、教廷认输
            if (coronationWar != null && joinAfterCapture.GetEmpire() is Empire coronationEmpire &&
                RealmNamingSystem.IsCoercionPrize(__instance, coronationEmpire))
            {
                RealmNamingSystem.CoerceVictory(coronationEmpire);
                ClearResolvedCaptureProgress(__instance);
                coronationWar.Lost(__instance.Realm);
                return false;
            }
            //检测是否为迫使朝贡战争，如果是则迫使该国家加入朝贡体系
            if (chaoGongWar != null)
            {
                LogService.LogInfo("迫使朝贡战争");
                Empire empire = joinAfterCapture.GetEmpire();
                if (__instance.IsCapital())
                {
                    __instance.Realm.JoinTakenAlliance(empire, pForce: true);
                    ClearResolvedCaptureProgress(__instance);
                    chaoGongWar.Lost(__instance.Realm);
                    return false;
                }
            }
            //检测是否为迫使朝贡战争，如果是则迫使该国家加入朝贡体系
            if (religionWar != null)
            {
                LogService.LogInfo("宗教圣战");
                // 圣战攻下的城：只当场改与攻方之教同天下的(或无教的)，外天下的城留给国教推行；奉犹太教这类有"会堂"教义的城不改
                // (用户 2026-11-06 "古典时代全世界都信基督"，见 ReligionTenets)
                Religion conqueror = joinAfterCapture.Religion;
                Religion held = __instance.HasReligion() ? __instance.Religion : null;
                if (conqueror != null && held != conqueror &&
                    (held == null || ReligionTenets.AtHome(conqueror, ReligionSphere.SphereOfCity(__instance)) && !ReligionTenets.CaptureImmune(held)))
                {
                    BackgroundReligionSystem.Convert(__instance, conqueror, held);
                    ReligionDiagnostics.Add(ReligionDiagnostics.HolyWar);
                    TranslateHelper.LogReligionWarTransfer(__instance, conqueror);
                }
                else if (conqueror != null && held != conqueror) ReligionDiagnostics.Add(ReligionDiagnostics.HolyWarSpared);
            }
            if (empireRoyalAcquireEmpireWar != null)
            {
                LogService.LogInfo("藩王之乱");
                if (__instance.IsCapital())
                {
                    var empire = joinAfterCapture.GetEmpire();
                    var newEmperor = joinAfterCapture.Ruler;
                    if (newEmperor != null&&SpecificClanManager.SameLineage(newEmperor.GetSpecificClan(), empire.EmpireSpecificClan))
                    {
                        TranslateHelper.LogRoyalKingBecomeEmperor(empire, joinAfterCapture.GetMainTitle()??joinAfterCapture.Capital.GetTitle(), newEmperor);
                        empire.CoreKingdom.GetOffice().meta_object = empire.CoreKingdom;
                        empire.CoreKingdom.GetOffice().SetActor(newEmperor);
                        // 选帝制下当选者带着自家领国成为中央(见 HolyRomanSystem)，此时不用再并城
                        if (joinAfterCapture != empire.CoreKingdom)
                            joinAfterCapture.Cities.ToList().ForEach(c=>c.JoinRealm(empire.CoreKingdom));
                        ClearResolvedCaptureProgress(__instance);
                        return false;
                    }
                    empireRoyalAcquireEmpireWar.Leave(joinAfterCapture);
                    joinAfterCapture.EndLocalRebelling();
                    joinAfterCapture.GetRegime().SetAllowArmy(false);
                    joinAfterCapture.GetRegime().SetAllowSupportCenterArmy(false);
                    joinAfterCapture.GetRegime().SetLeaderSelectMethod(LeaderSelectMethod.Exam);
                    ClearResolvedCaptureProgress(__instance);
                    return false;
                }
            }
            // 正统之争以实际占领计算胜负，不能让城邦并国或封建归附拦截占城。
            bool legitimacyWar = pWars.Any(w => IsWarRelevantToCapture(w, pNewKingdom, joinAfterCapture, oldKingdom) &&
                                                 w.GetEmpireWarType() == EmpireWarType.去帝号);
            // 城邦一城一国：攻下的城由本地贵族立为新城邦入盟，对方只剩一城则整国入盟
            if (!legitimacyWar && CityStateService.TryResolveCapture(__instance, joinAfterCapture, oldKingdom))
            {
                ClearResolvedCaptureProgress(__instance);
                return false;
            }
            // 分封制帝国攻下别国都城：对方称臣归附，已占的该国法理之地归还(西欧封建只还都城那一片)，城市不易主
            if (!legitimacyWar &&
                FeudalConquestService.TryResolveCapitalConquest(__instance, joinAfterCapture, oldKingdom))
            {
                ClearResolvedCaptureProgress(__instance);
                return false;
            }
            PrepareCompletedCityTransfer(__instance, pNewKingdom, oldKingdom, targetEmpire, isEmpireCapital);
            if (TryTriggerEmpirePressureSurrender(__instance, joinAfterCapture, oldKingdom))
            {
                return false;
            }
            if (!__instance.CheckRebelWar(joinAfterCapture, pWars))
                joinAfterCapture.Record.NewConquestAt = GameClock.Now;
            if (custodian != null && joinAfterCapture == custodian)
                WartimeCustodySystem.Record(__instance, actualCapturer, oldKingdom,
                    pWars.FirstOrDefault(w => IsWarRelevantToCapture(w, pNewKingdom, joinAfterCapture, oldKingdom)));
            else WartimeCustodySystem.Clear(__instance);
            __instance.RemoveSoldiers();
            AdministrativeControlSystem.MarkConquered(__instance, joinAfterCapture);
            WarChronicle.CityTaken(__instance, oldKingdom, joinAfterCapture);
            __instance.JoinRealm(joinAfterCapture, true);
        }

        return false;
    }

    private static void ClearResolvedCaptureProgress(City city)
    {
        if (city == null)
            return;

        city.ClearCapture();
        city.ClearOccupiedStatus();
        city.RecalculateNeighbourCities();
    }

    private static void PrepareCompletedCityTransfer(
        City city,
        Realm capturingKingdom,
        Realm oldKingdom,
        Empire targetEmpire,
        bool isEmpireCapital)
    {
        if (city == null || capturingKingdom == null || oldKingdom == null)
            return;

        if (oldKingdom.HasRuler() && oldKingdom.Ruler?.City == city)
            oldKingdom.KingFledCity();

        ClearResolvedCaptureProgress(city);
        capturingKingdom.IncreaseHappinessFromNewCityCapture();
        oldKingdom.DecreaseHappinessFromLostCityCapture(city);

        if (targetEmpire != null)
        {
            if (!oldKingdom.IsInSameEmpire(capturingKingdom))
            {
                Empire newEmpire = capturingKingdom.GetEmpire();
                if (targetEmpire.CoreKingdom?.HasSameEmpireCraftCulture(newEmpire?.CoreKingdom) ?? false)
                    targetEmpire.AddMandate(-20);
                else
                    targetEmpire.AddMandate(-10);
            }
            else if (oldKingdom.IsEmpire())
            {
                // 选帝制双重选举的争位之战、分裂进程的争位之战(同用对立国王这一类，见 SchismSystem)：
                // 打下中央都城只扣 10(见 HolyRomanSystem.Election)
                targetEmpire.AddMandate(EmpireCraft.Core.HolyRomanSystem.InContest(oldKingdom, capturingKingdom)
                    ? EmpireCraft.Core.HolyRomanSystem.ContestCapitalLegitimacy
                    : -40);
            }
        }

        // 以前这里还有一段：现代政体的国家打下任何帝国的都城，就算"革命胜利"，当场取缔本国主导派系以外的所有派系。
        // 那是没有政党、革命战争之前的旧写法——两个现代国家打一仗，赢的那国莫名其妙成了一党制。
        // 革命的胜负另有结算(见 RepublicSystem.ResolveRevolutionWar)，这段撤掉
    }

    private static bool IsWarRelevantToCapture(
        War war,
        Realm capturingKingdom,
        Realm resolvedCaptureKingdom,
        Realm defendingKingdom)
    {
        if (war == null ||
            !war.Alive ||
            war.HasEnded() ||
            defendingKingdom == null)
        {
            return false;
        }

        return AreKingdomsOpposingInWar(war, capturingKingdom, defendingKingdom) ||
               AreKingdomsOpposingInWar(war, resolvedCaptureKingdom, defendingKingdom);
    }

    private static bool AreKingdomsOpposingInWar(War war, Realm first, Realm second)
    {
        if (war == null || first == null || second == null || first == second)
            return false;

        bool firstAttacker = IsKingdomRepresentedOnWarSide(war, first, true);
        bool firstDefender = IsKingdomRepresentedOnWarSide(war, first, false);
        bool secondAttacker = IsKingdomRepresentedOnWarSide(war, second, true);
        bool secondDefender = IsKingdomRepresentedOnWarSide(war, second, false);

        return (firstAttacker && secondDefender) ||
               (firstDefender && secondAttacker);
    }

    private static bool IsKingdomRepresentedOnWarSide(War war, Realm kingdom, bool attackers)
    {
        if (war == null || kingdom == null)
            return false;

        if (attackers ? war.IsAttacker(kingdom) : war.IsDefender(kingdom))
            return true;

        Empire empire = kingdom.GetEmpire();
        Realm coreKingdom = empire?.CoreKingdom;
        if (coreKingdom == null && kingdom.IsEmpire())
            coreKingdom = kingdom;

        return coreKingdom != null && coreKingdom != kingdom &&
               (attackers ? war.IsAttacker(coreKingdom) : war.IsDefender(coreKingdom));
    }

    private static bool TryTriggerEmpirePressureSurrender(City capturedCity, Realm attackerKingdom, Realm defenderKingdom)
    {
        if (capturedCity == null || attackerKingdom == null || defenderKingdom == null) return false;
        if (capturedCity.IsGone() || attackerKingdom.IsGone() || defenderKingdom.IsGone()) return false;

        Empire attackerEmpire = attackerKingdom.GetEmpire();
        Empire defenderEmpire = defenderKingdom.GetEmpire();
        if (attackerEmpire == null || defenderEmpire == null || attackerEmpire == defenderEmpire) return false;
        if (defenderEmpire.CoreKingdom == null || defenderEmpire.CoreKingdom.IsGone()) return false;
        if (defenderEmpire.cities_list == null) return false;
        if (!attackerKingdom.IsAtWarWith(defenderEmpire.CoreKingdom) && !defenderEmpire.CoreKingdom.IsAtWarWith(attackerKingdom)) return false;

        bool hitEmpireCapital = defenderEmpire.CoreKingdom.Capital == capturedCity;
        bool hitKingdomCapital = defenderKingdom.Capital == capturedCity;
        int totalCities = defenderEmpire.cities_list?.Count(c => c != null && !c.IsGone()) ?? 0;
        if (totalCities <= 0) return false;

        int occupiedCities = 0;
        for (int i = 0; i < defenderEmpire.cities_list.Count; i++)
        {
            City city = defenderEmpire.cities_list[i];
            if (city == null || city.IsGone()) continue;
            if (city == capturedCity)
            {
                occupiedCities++;
                continue;
            }

            Realm cityKingdom = city.Realm;
            if (cityKingdom != null && !cityKingdom.IsGone() && cityKingdom.IsInSameEmpire(attackerKingdom))
            {
                occupiedCities++;
            }
        }

        bool reachedThird = occupiedCities * 3 >= totalCities;
        if (!hitEmpireCapital && !reachedThird) return false;

        int surrenderChance = CalculateEmpirePressureSurrenderChance(defenderEmpire, hitEmpireCapital, reachedThird);
        if (surrenderChance <= 0) return false;
        if (surrenderChance < 70 && EcRandom.Range(0, 100) >= surrenderChance) return false;

        string targetName;
        bool cityOnly = false;
        if (hitEmpireCapital)
        {
            ForceEmpirePressureSurrenderTo(defenderEmpire, attackerKingdom, capturedCity);
            targetName = defenderEmpire.CoreKingdom.GetKingdomName();
        }
        else if (hitKingdomCapital)
        {
            ForceSurrenderKingdomTo(defenderKingdom, attackerKingdom, capturedCity);
            targetName = defenderKingdom.GetKingdomName();
        }
        else
        {
            capturedCity.JoinRealm(attackerKingdom, true);
            targetName = capturedCity.GetCityName();
            cityOnly = true;
        }

        defenderEmpire.CoreKingdom.EndWarWith(attackerEmpire.CoreKingdom);
        defenderKingdom.EndWarWith(attackerEmpire.CoreKingdom);
        TranslateHelper.LogEmpirePressureSurrender(attackerEmpire, defenderEmpire, targetName, cityOnly);
        return true;
    }

    private static int CalculateEmpirePressureSurrenderChance(Empire defenderEmpire, bool hitEmpireCapital, bool reachedThird)
    {
        if (defenderEmpire == null || defenderEmpire.CoreKingdom == null) return 0;
        int mandate = Mathf.Clamp(defenderEmpire.Mandate, 0, 100);
        if (mandate > 70) return 0;

        int chance = Mathf.Clamp(70 - mandate, 0, 100);
        if (reachedThird) chance += 15;
        if (hitEmpireCapital) chance += 25;
        if (defenderEmpire.CoreKingdom.GetMoney() <= 0) chance += 35;
        chance = Mathf.Clamp(chance, 0, 100);
        return chance;
    }

    private static void ForceSurrenderKingdomTo(Realm defeated, Realm occupier, City capturedCity)
    {
        if (defeated == null || occupier == null || defeated.IsGone() || occupier.IsGone() || defeated == occupier)
        {
            return;
        }

        List<City> citySnapshot = new List<City>(defeated.Cities);
        if (capturedCity != null && !capturedCity.IsGone() && capturedCity.Realm == defeated)
        {
            capturedCity.JoinRealm(occupier, true);
        }

        for (int i = 0; i < citySnapshot.Count; i++)
        {
            City targetCity = citySnapshot[i];
            if (targetCity == null || targetCity.IsGone() || targetCity == capturedCity || targetCity.Realm != defeated)
            {
                continue;
            }

            targetCity.JoinRealm(occupier, true);
        }
    }

    private static void ForceEmpirePressureSurrenderTo(Empire defenderEmpire, Realm occupier, City capturedCity)
    {
        if (defenderEmpire == null || occupier == null || occupier.IsGone()) return;

        HashSet<Realm> surrenderedKingdoms = new HashSet<Realm>();
        if (defenderEmpire.CoreKingdom != null && !defenderEmpire.CoreKingdom.IsGone())
        {
            surrenderedKingdoms.Add(defenderEmpire.CoreKingdom);
        }

        for (int i = 0; i < defenderEmpire.kingdoms_list.Count; i++)
        {
            Realm kingdom = defenderEmpire.kingdoms_list[i];
            if (kingdom == null || kingdom.IsGone() || kingdom == defenderEmpire.CoreKingdom) continue;
            Regime regime = kingdom.GetRegime();
            if (regime != null && !regime.IsAllowDiplomacy())
            {
                surrenderedKingdoms.Add(kingdom);
            }
        }

        foreach (var kingdom in surrenderedKingdoms)
        {
            ForceSurrenderKingdomTo(kingdom, occupier, capturedCity);
            kingdom.EndWarWith(occupier);
        }
    }
    public static bool countWarriors(City __instance, ref int __result)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        __result = __instance.CountLivingWarriors();
        return false;
    }
    public static bool getPopulationPeople(City __instance, ref int __result)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        // 原版逻辑读人口时按户计(见 CityPopulationSystem.VanillaScale)，界面与统计读真实人数
        __result = CityPopulationSystem.VanillaScale && CityPopulationSystem.AbstractPopulationEnabled
            ? CityPopulationSystem.Households(__instance)
            : __instance.CountLivingPopulation();
        return false;
    }
    public static bool isArmyOverLimit(City __instance, ref bool __result)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        if (__instance.Realm.IsEmpire())
        {
            __result = false;
            return true;
        }
        if (__instance.Status.WarriorsCurrent > __instance.Status.WarriorSlots)
        {
            __result = true;
            return false;
        }
        __result = false;
        return false; 
    }
    public static void unpaid_max_warriors(City __instance, ref int __result)
    {
        __result = Math.Max(__result, CityStabilitySystem.FundedSlots(__instance));
        if (__result > 0 && EmpireBankruptcySystem.IsUnpaidGarrison(__instance)) __result = 0;
    }
    public static void unpaid_warrior_slots(City __instance)
    {
        if (__instance?.Status != null)
            __instance.Status.WarriorSlots = Math.Max(__instance.Status.WarriorSlots, CityStabilitySystem.FundedSlots(__instance));
        if (__instance?.Status != null && EmpireBankruptcySystem.IsUnpaidGarrison(__instance))
            __instance.Status.WarriorSlots = 0;
    }
#if EC_PENDING
    public static bool getPopulationMaximum(City __instance, ref int __result)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        if (__instance.GetMaxPopulationLimitStats())
        {
            // 玩家设的人口上限按"户"(和单位个数同一量级，见 CityExtension.HasReachedPlayerPopLimit)；无小人模式下换成人数，
            // 以前直接当人数返回，开了上限的城被锁在一百来人
            __result = __instance.GetMaxPopulation() *
                       (CityPopulationSystem.AbstractPopulationEnabled && !CityPopulationSystem.VanillaScale
                           ? CityPopulationSystem.PeoplePerSlot(__instance) : 1);
            return false;
        }
        // 先按住房量给一个基础上限
        int cap = __instance.Status.HousingTotal;

        // 依次套用 20/50/100 三个世界法令的限制（哪个开了就取 min）
        if (EmpireCraftWorldLawLibrary.world_law_civ_limit_population_20?.isEnabled() == true)
            cap = Math.Min(cap, 20);

        if (EmpireCraftWorldLawLibrary.world_law_civ_limit_population_50?.isEnabled() == true)
            cap = Math.Min(cap, 50);

        if (WorldLaws.CivLimitPopulation100?.isEnabled() == true)
            cap = Math.Min(cap, 100);

        // 无小人模式：一个住房位住一户(古代 100 人、现代 1000 人)；原版逻辑读上限时仍按住房位数。
        // 游牧营地只算四成(见 CityStyleSystem.CapacityFactor)；原版按住房位数读的不打折，免得原版以为住满了一直盖房
        if (CityPopulationSystem.VanillaScale) __result = cap;
        else if (!CityPopulationSystem.AbstractPopulationEnabled) __result = cap * CityPopulationSystem.PeoplePerSlot(__instance);
        else __result = Mathf.CeilToInt(cap * CityPopulationSystem.PeoplePerSlot(__instance) * CityStyleSystem.CapacityFactor(__instance) +
                                        CityPopulationSystem.BrushCapacity(__instance));
        return false;
    }
    public static bool HasReachedWorldLawLimit(City __instance, ref bool __result)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        if (WorldLaws.CivLimitPopulation100.isEnabled() && __instance.PopulationCount() >= 100)
        {
            __result = true;
            return false;
        }
        if (EmpireCraftWorldLawLibrary.world_law_civ_limit_population_50.isEnabled() && __instance.PopulationCount() >= 50)
        {
            __result = true;
            return false;
        }
        if (EmpireCraftWorldLawLibrary.world_law_civ_limit_population_20.isEnabled() && __instance.PopulationCount() >= 20)
        {
            __result = true;
            return false;
        }
        __result = false;
        return false;
    }
#endif
    public static void removeLeader(City __instance)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        if (__instance.Leader!=null)
        {
            if (__instance.Leader.HasOfficeIdentity())
            {
                OfficeIdentity office = __instance.Leader.GetIdentity();
                office.RemoveOffice();
            }
        }
    }

#if EC_PENDING
    public static bool setLeader(City __instance, Person pActor, bool pNew)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        if (pActor != null && __instance.Realm.Ruler != pActor)
        {
            __instance.Leader = pActor;
            __instance.Leader.setProfession(UnitProfession.Leader);
            CityData cityData = __instance.Record;
            long leaderID = (__instance.Record.last_leader_id = pActor.Key);
            cityData.leaderID = leaderID;
            if (pNew)
            {
                __instance.Record.total_leaders++;
                __instance.Leader.changeHappiness("become_leader");
                __instance.addRuler(pActor);
            }
        }
        pActor.CheckSpecificClan();
        return false;
    }
#endif
    public static bool joinAnotherKingdom(City __instance, Realm pNewSetKingdom, bool pCaptured = false, bool pRebellion = false)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        // 参数检查
        if (__instance == null || pNewSetKingdom == null)
        {
            return false;
        }
        // 分封制下的小乱：割让、倒戈投靠同样至多两座城、拿不到原主都城(见 PettyRebellion)
        if (EmpireCraft.Core.PettyRebellion.Blocks(pNewSetKingdom, __instance)) return false;
        // 诸侯私战进行中：两家之间只有争的城能易手(见 HolyRomanSystem.Feuds)
        if (EmpireCraft.Core.HolyRomanSystem.BlocksFeudCity(pNewSetKingdom, __instance)) return false;
        
        __instance.SetCorruptionRate(0.0f);
        string pHappinessEvent = null;
        if (pCaptured)
        {
            EmpireWorld.Current.game_stats.data.citiesConquered++;
            EmpireWorld.Current.map_stats.citiesConquered++;
            pHappinessEvent = "was_conquered";
        }

        if (pRebellion)
        {
            EmpireWorld.Current.game_stats.data.citiesRebelled++;
            EmpireWorld.Current.map_stats.citiesRebelled++;
            pHappinessEvent = "just_rebelled";
        }
        Realm pKingdom = __instance.Realm;
        // 近代国家丢最后一座城：先把元首与要员带出城去东道国，免得随城归了占领者(见 ExileGovernmentSystem)
        EmpireCraft.Core.ExileGovernmentSystem.BeforeCityLeaves(__instance, pKingdom, pNewSetKingdom, pCaptured);
        pKingdom?.InvalidateCityValueContext();
        pNewSetKingdom.InvalidateCityValueContext();
        __instance.InvalidateCityStrategicValue();
        __instance.RemoveFromCurrentKingdom();
        if (pNewSetKingdom.IsInEmpire())
        {
            var empire = pNewSetKingdom.GetEmpire();
            if (empire != null)
            {
                if (!empire.cities_list.Contains(__instance))
                {
                    empire.cities_list.Add(__instance);
                }
            }
        }
        if (pNewSetKingdom.IsInEmpire()&&pCaptured&&!pKingdom.IsEmpire())
        {
            Empire empire = pNewSetKingdom.GetEmpire();
            if (!empire.CoreKingdom.IsAtWarWith(pKingdom))
            {
                var dominate = empire.CoreKingdom.GetRegime().GetDominateFaction();
                if (dominate != null)
                {
                    if (dominate.Type != FactionType.自治 && dominate.Type != FactionType.诸侯)
                    {
                        // 如果新加入的王国是帝国的一部分，并且城市被占领，则将城市加入帝国
                        pNewSetKingdom = empire.CoreKingdom;
                    } 
                }
            }
        }
        
        if (pKingdom != null)
        {
            var empire = pKingdom.GetEmpire();
            empire?.cities_list.Remove(__instance);
        }
        __instance.SetKingdom(pNewSetKingdom);
        if (pCaptured)
            CultureService.ApplyForeignOccupationCulture(__instance, pNewSetKingdom);
        __instance.NewForceKingdomEvent(__instance.People, __instance.Boats, pNewSetKingdom, pHappinessEvent);
        __instance.SwitchedKingdom();
        pNewSetKingdom.CapturedFrom(pKingdom);
        __instance.ClearOccupiedStatus();
        __instance.InvalidateCityStrategicValue();
        pNewSetKingdom.InvalidateCityValueContext();
        return false;
    }
    public static bool removeZone(City __instance, Cell pZone)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        if (!CityTerritoryProtection.CanRemoveZone(__instance, pZone)) return false;
        OccupationReadIndex.Invalidate(__instance, pZone);
        return true;
    }
#if EC_PENDING
    public static bool addZone(City __instance, Cell pZone)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        if (!__instance.Zones.Contains(pZone))
        {
            if (!CityExpansionPatch.CanAddZone(__instance, pZone)) return false;
            OccupationReadIndex.Invalidate(__instance, pZone);
            if (pZone.City != null)
            {
                if (!CityTerritoryProtection.CanRemoveZone(pZone.City, pZone)) return false;
                pZone.City.RemoveZone(pZone);
                // Another mod may veto removal. Never add a still-owned zone to a second city.
                if (pZone.City != null) return false;
            }
            __instance.Zones.Add(pZone);
            pZone.setCity(__instance);
            __instance.updateCityCenter();
            if (EmpireWorld.Current.city_zone_helper.city_place_finder.hasPossibleZones())
            {
                EmpireWorld.Current.city_zone_helper.city_place_finder.setDirty();
            }
            __instance.setStatusDirty();
        }
        return false;
    }
#endif
    // 存档里在世城市指向的王国不存在时(王国已被删除)，城市没有王国，随后加载单位时 City.isNeutral() 空引用、
    // 整个存档读不进来。加载单位之前(SaveManagerPatch 的 loadActors 前置)把这些城市归到上一个王国，找不到就划为中立。
    // 只修在世城市：统计库里已灭亡城市的历史记录本来就没有王国，不能碰。
#if EC_PENDING
    public static void RepairLiveCityKingdoms()
    {
        if (EmpireWorld.Current?.Cities == null) return;
        foreach (City city in EmpireWorld.Current.Cities.ToList())
        {
            if (city?.Record == null || city.Realm != null) continue;
            Realm previous = city.Record.last_kingdom_id > 0 ? EmpireWorld.Current.RealmByKey(city.Record.last_kingdom_id) : null;
            Realm fallback = previous != null && !previous.IsGone() && previous.asset != null
                ? previous
                : WildKingdomsManager.neutral;
            city.SetKingdom(fallback, pFromLoad: true);
            LogService.LogWarning($"[EmpireCraft] 城市 {city.Name}({city.Key}) 指向的王国不存在，已归入 {fallback?.Name}");
        }
    }
#endif

    public static void BeforeInspire() => EmpireCraft.Core.SecessionGate.EnterGod();

    public static Exception AfterInspire(Exception __exception)
    {
        EmpireCraft.Core.SecessionGate.ExitGod();
        return __exception;
    }

#if EC_PENDING
    public static bool makeOwnKingdom(City __instance, Person pActor, bool pRebellion, bool pFellApart, ref Realm __result)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        if (__instance == null || pActor == null)
        {
            return false;
        }
        // 不许自立(见 SecessionGate)：开关开着时城不自立成新国，调用方本来就判空；受命立国、神力、剧本照常
        if (EmpireCraft.Core.SecessionGate.Forbids("立国",
                $"{__instance.Name}(属 {__instance.Realm?.Name})"))
        {
            __result = null;
            return false;
        }
        // 非文明单位不能当建国者：换城里一个文明居民，找不到就不建
        if (!pActor.CanFoundCivKingdom())
        {
            pActor = __instance.People?.FirstOrDefault(actor => actor.CanFoundCivKingdom() && actor.IsAdult());
            if (pActor == null) return false;
        }

        string pHappinessEvent = null;
        if (pRebellion)
        {
            EmpireWorld.Current.game_stats.data.citiesRebelled++;
            EmpireWorld.Current.map_stats.citiesRebelled++;
            pHappinessEvent = "just_rebelled";
        }
        if (pFellApart)
        {
            pHappinessEvent = "kingdom_fell_apart";
        }
        Realm pKingdom = __instance.Realm;
        CityValueSnapshot rebellionOriginValue = pRebellion ? __instance.GetCityStrategicValue() : default;
        pKingdom?.InvalidateCityValueContext();
        __instance.InvalidateCityStrategicValue();
        Empire rebellionOrigin = pRebellion ? pKingdom?.GetEmpire() : null;
        __instance.RemoveFromCurrentKingdom();
        __instance.RemoveLeader();
        pActor.ClearDisposedKingdom();
        Realm kingdom = EmpireWorld.Current.LiveRealms.makeNewCivKingdom(pActor);
        kingdom.copyMetasFromOtherKingdom(pKingdom);
        kingdom.generateColor();
        __instance.NewForceKingdomEvent(__instance.People, __instance.Boats, kingdom, pHappinessEvent);
        __instance.SetKingdom(kingdom);
        __instance.SwitchedKingdom();
        kingdom.setCityMetas(__instance);
        // 叛乱自立的取当地之俗(见 CultureService.ApplyLocalCulture)
        if (pRebellion) EmpireCraft.Core.CultureService.ApplyLocalCulture(kingdom, __instance);
        kingdom.RememberRebellionOrigin(rebellionOrigin);
        // 立国出身：从共和国分出、共和派起事的一立国就是现代政体，从君主国割据的照旧君主(见 StateModernization)
        EmpireCraft.Core.StateModernization.MarkFounding(kingdom, pKingdom, pActor, pRebellion);
        if (pRebellion) kingdom.SetRebellionOriginCityValue(rebellionOriginValue);
        // 分封制的诸侯国里起事：一城一邑的小乱(见 PettyRebellion)
        if (pRebellion) EmpireCraft.Core.PettyRebellion.Mark(kingdom, pKingdom);
        // 起事按法理成片：同一法理里挨着的、人心也散了的城一并跟着走(见 SecessionSpread)
        if (pRebellion) EmpireCraft.Core.SecessionSpread.Spread(kingdom, __instance, pKingdom);
        kingdom.InvalidateCityValueContext();
        __instance.InvalidateCityStrategicValue();
        __result = kingdom;
        return false;
    }
#endif


    // 全世界的年度扫描：以前挂在每座城的更新里，第一座城一到期就七个一起跑，叠成大顿挫。
    // 现在每帧只轮到其中一个(各扫描自己仍按"一年一次"判断是否到期)，到期的扫描最多晚几帧执行
    // 名字与扫描写在一起(以前分两张表，加扫描漏加名字就错位越界)
#if EC_PENDING
    private static readonly (string label, Action scan)[] WorldYearlyScans =
    {
        ("年度扫描·宗族分支", ClanBranchSystem.TryYearlyScan),
        ("年度扫描·兼属", DeJureShareSystem.TryYearlyScan),
        ("年度扫描·荒漠冻土", BarrenLandSystem.TryMonthlyScan),
        ("年度扫描·理念传播", IdeologySpreadSystem.TryYearlyScan),
        ("年度扫描·科技", TechnologySystem.TryYearlyScan),
        ("年度扫描·制度漂移", InstitutionSystem.TryYearlyCultureDrift),
        ("年度扫描·苛政", HarshRuleSystem.TryYearlyKingdomScan),
        ("年度扫描·核威慑", EmpireCraft.Core.NuclearDoctrineSystem.TryYearlyScan),
        ("年度扫描·文化近代化", CultureModernizationSystem.TryYearlyRealmTransitionScan),
        ("年度扫描·失都改称", CapitalLossNamingSystem.TryYearlyScan),
        ("年度扫描·战后分配", WartimeCustodySystem.TryMonthlyScan),
        ("年度扫描·羁縻边防", JimiSystem.TryMonthlyScan),
        ("年度扫描·行政区划界", InternalBoundarySystem.TryYearlyScan),
        ("年度扫描·国债", StateDebtSystem.TryMonthlyScan),
        ("年度扫描·还于旧都", CoreCapitalRestorationSystem.TryMonthlyScan),
        ("年度扫描·战争赔款", WarReparationsSystem.TryMonthlyScan),
        ("年度扫描·海上商路", TradeRouteSystem.TryMonthlyScan),
        ("年度扫描·附庸描边", VassalColorService.TickReassert),
        ("年度扫描·封建贡赋", FeudalVassalService.TryYearlyTribute),
        ("年度扫描·名将名臣", FamousTalentSystem.TryYearlyScan),
        ("年度扫描·和亲互市", FrontierRelations.TryYearlyScan),
        ("年度扫描·宗教", ReligiousTension.TryYearlyScan),
        ("年度扫描·大国通好", GreatPowerDiplomacy.TryYearlyScan),
        ("年度扫描·世界格局", WorldOrderSystem.TryYearlyScan),
        ("年度扫描·制度接触", InstitutionSystem.TryYearlyWorldPass),
        ("年度扫描·制度·王国", InstitutionSystem.TryYearlyKingdomScan),
        ("年度扫描·教廷诊断", PapacySystem.TryYearlyDiagnose),
        ("年度扫描·旧制", LegacyInstitutionSystem.TryYearlyScan),
        ("年度扫描·游牧劫掠", NomadRaidSystem.TryMonthlyScan),
        ("年度扫描·远战清理", WarReach.TryMonthlyScan),
        ("年度扫描·国号文化", NameCultureRepair.TryYearlyScan)
    };
#endif
    private static int _worldScanFrame = -1;
    private const int SurrenderCheckEvery = 10;
    private const int SlowHookCitiesPerFrame = 3;

#if EC_PENDING
    private static void RunWorldYearlyScans()
    {
        int frame = Time.frameCount;
        if (frame == _worldScanFrame) return;
        _worldScanFrame = frame;
        int index = frame % WorldYearlyScans.Length;
        using (FrameProfiler.Measure(WorldYearlyScans[index].label)) WorldYearlyScans[index].scan();
    }

    public static void city_update(City __instance, float pElapsed)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return;

        // 守城军已经被打到 3 人及以下时：
        // 只要敌对帝国的 Warrior 实际进入本城市任意 Zone，
        // 该城立即归降。
        // 每座城每帧都跑：分段累计计时，每 10 秒在日志里报一次平均每帧耗时(见 FrameProfiler)
        // 这几项按年、按月更新就够，不用每座城每帧都查：
        //   入城归降每座城每 10 帧查一次；土地经济、占领扩散、城市就业每帧只轮到约 3 座城
        //   (100 多座城大约两秒轮一遍；游戏开得再快，这几项的总开销也不会跟着翻倍)
        int frame = Time.frameCount;
        long cityKey = __instance.Key;
        if ((cityKey + frame) % SurrenderCheckEvery == 0)
            using (FrameProfiler.Measure("城市钩子·入城归降")) TryImmediateSurrenderOnImperialArmyArrival(__instance);
        int cycle = Math.Max(1, (EmpireWorld.Current?.Cities?.Count ?? 1) / SlowHookCitiesPerFrame);
        if ((cityKey + frame) % cycle == 0)
        {
            using (FrameProfiler.Measure("城市钩子·土地经济")) LandEconomySystem.UpdateCity(__instance);
            using (FrameProfiler.Measure("城市钩子·占领扩散")) __instance.TryYearlyOccupationSpread();
            using (FrameProfiler.Measure("城市钩子·城市就业")) UrbanEmploymentSystem.UpdateCity(__instance);
        }
        // 无小人模式：城主一空缺立即从人口里补位
        using (FrameProfiler.Measure("城市钩子·城主补位")) CityPopulationSystem.EnsureLeader(__instance);
        using (FrameProfiler.Measure("城市钩子·年度扫描")) RunWorldYearlyScans();

        /*
        if (__instance.hasTitle())
        {
            if (__instance.GetTitle() != null)
            {
                __instance.GetTitle().isBeenControlled();
            }
        }
        */
    }
#endif

    /// <summary>
    /// 城市低兵力时，检测敌对帝国 Warrior 是否已经实际进入该城市。
    /// 满足条件后直接调用 finishCapture，而不是 setKingdom：
    /// 这样仍然会进入本 CityPatch 已有的 FinishedCapture 逻辑，
    /// 包括可占领战争、帝国压力投降、占领清理、日志等。
    /// 劫掠战争不转移城市，因此不能由这里触发自动归降。
    /// </summary>
    private const int ImperialArrivalSurrenderPeakWarriors = 10;

    // 存档后清理：去掉已消亡城市的重试冷却记录
    public static void PruneDeadCityCaches()
    {
        foreach (City city in _imperialArrivalSurrenderCooldown.Keys.Where(city => city == null || city.IsGone()).ToList())
            _imperialArrivalSurrenderCooldown.Remove(city);
    }

    private static void TryImmediateSurrenderOnImperialArmyArrival(City city)
    {
        if (city == null || city.IsGone())
            return;

        if (city.Realm == null || city.Realm.IsGone())
            return;

        // 避免重入。
        if (_imperialArrivalSurrenderGuard.Contains(city))
            return;
        if (_imperialArrivalSurrenderCooldown.TryGetValue(city, out double failedAt))
        {
            if (GameClock.MonthsSince(failedAt) < ImperialArrivalSurrenderRetryMonths) return;
            _imperialArrivalSurrenderCooldown.Remove(city);
        }

        // 不再只看 city.kingdom.hasEnemies()：帝国战争可能只登记在帝国核心国上，地方王国本身未必 hasEnemies()。
        // 本国与所在帝国的中央都没有敌国的城不查(以前每座城每十帧都把全国兵力、动员余量算一遍，性能日志里每帧 7 毫秒)
        Realm defenderKingdom = city.Realm;
        Realm realmCore = FrontSystem.ModRealm(defenderKingdom);
        if (!defenderKingdom.HasEnemies() && (realmCore == null || !realmCore.HasEnemies())) return;

        // 独立国按全国兵力；帝国成员按全帝国兵力，避免弱小诸侯在两支帝国军之间反复归附。
        Empire defendingEmpire = defenderKingdom.GetEmpire();
        int kingdomWarriors = defendingEmpire != null && !defendingEmpire.IsArchived() && !defendingEmpire.IsGone()
            ? GetEmpireLivingWarriorCount(defendingEmpire)
            : GetKingdomLivingWarriorCount(defenderKingdom);
        // 无小人模式：太平时只留少数将领，还能按战时兵额动员的不算"兵败"
        kingdomWarriors += CityPopulationSystem.MobilizableReserve(defenderKingdom);
        KingdomExtension.KingdomExtraData defenderData = defenderKingdom.GetOrCreate();
        if (kingdomWarriors > defenderData.peak_warriors) defenderData.peak_warriors = kingdomWarriors;

        // 守方整体只剩 0 / 1 / 2 / 3 个 Warrior 时，
        // 才允许帝国军入城触发该城立即归降。
        if (kingdomWarriors > ImperialArrivalSurrenderWarriorThreshold)
            return;
        // 而且必须是"曾经有过军队后崩溃"：新立国(比如刚起事的叛军)还没来得及征兵，不算崩溃
        if (defenderData.peak_warriors < ImperialArrivalSurrenderPeakWarriors)
            return;

        EnsureImperialPresenceCache();

        if (!_imperialAttackerByCity.TryGetValue(city, out Realm attackerKingdom))
            return;

        if (!IsValidImperialSurrenderAttacker(city, attackerKingdom))
            return;

        // 同帧各城共用动员统计；真正转移城市前再核对，避免本帧刚征兵或切换军制时误降。
        if (CityPopulationSystem.AbstractPopulationEnabled)
        {
            int fresh = (defendingEmpire != null && !defendingEmpire.IsArchived() && !defendingEmpire.IsGone()
                ? GetEmpireLivingWarriorCount(defendingEmpire) : GetKingdomLivingWarriorCount(defenderKingdom)) +
                CityPopulationSystem.MobilizableReserve(defenderKingdom, true);
            if (fresh > defenderData.peak_warriors) defenderData.peak_warriors = fresh;
            if (fresh > ImperialArrivalSurrenderWarriorThreshold) return;
            kingdomWarriors = fresh;
        }

        try
        {
            _imperialArrivalSurrenderGuard.Add(city);

            // 直接走原版/本模组“完成占领”的入口。
            //
            // 不直接 city.joinAnotherKingdom()，
            // 因为 finishCapture 已被本 CityPatch Patch，
            // 会继续执行游牧扩张、迫使朝贡、圣战、
            // 藩王索取皇位、帝国压力投降等现有规则。
            LogService.LogInfo(
                $"国家兵力崩溃，城市立即归降：{city.Name}，{defenderKingdom.Name}全国剩余士兵={kingdomWarriors}，归降于={attackerKingdom.Name}");

            city.FinishCapture(attackerKingdom);
            if (!city.IsGone() && city.Realm == defenderKingdom)
            {
                _imperialArrivalSurrenderCooldown[city] = GameClock.Now;
                LogService.LogWarning(
                    $"入城即降未能让城市易主，{ImperialArrivalSurrenderRetryMonths}个月内不再重试：城市={city.Name}, 攻方={attackerKingdom.Name}");
            }
        }
        catch (Exception e)
        {
            _imperialArrivalSurrenderCooldown[city] = GameClock.Now;
            LogService.LogWarning(
                $"帝国军入城即降失败：城市={city?.Name}, 攻方={attackerKingdom?.Name}, {e.Message}");
        }
        finally
        {
            _imperialArrivalSurrenderGuard.Remove(city);
        }
    }

    /// <summary>
    /// 返回整个 Realm 当前所有存活 Warrior 的总数。
    ///
    /// 注意：
    /// - 不按 City 统计；
    /// - 士兵人在国外、前线、船上或其他城市，只要 actor.kingdom == kingdom，
    ///   且仍然存活并且 isWarrior()，都算作这个国家的兵力。
    /// </summary>
    // 全帝国兵力：同一帧内各城共用(随王国兵力缓存一起按帧清空)
    private static readonly Dictionary<long, int> _empireLivingWarriorCounts = new();

    private static int GetEmpireLivingWarriorCount(Empire empire)
    {
        EnsureKingdomWarriorCountCache();
        if (_empireLivingWarriorCounts.TryGetValue(empire.Key, out int cached)) return cached;
        int total = 0;
        List<Realm> members = empire.kingdoms_list;
        if (members != null)
            for (int i = 0; i < members.Count; i++)
            {
                Realm member = members[i];
                if (member != null && !member.IsGone() && _kingdomLivingWarriorCounts.TryGetValue(member, out int count))
                    total += count;
            }
        _empireLivingWarriorCounts[empire.Key] = total;
        return total;
    }

    private static int GetKingdomLivingWarriorCount(Realm kingdom)
    {
        if (kingdom == null || kingdom.IsGone())
            return 0;

        EnsureKingdomWarriorCountCache();

        return _kingdomLivingWarriorCounts.TryGetValue(kingdom, out int count)
            ? count
            : 0;
    }

    private static void EnsureKingdomWarriorCountCache()
    {
        int frame = Time.frameCount;

        if (_kingdomWarriorCountCacheFrame == frame)
            return;

        _kingdomWarriorCountCacheFrame = frame;
        _kingdomLivingWarriorCounts.Clear();
        _empireLivingWarriorCounts.Clear();

        if (EmpireWorld.Current == null || EmpireWorld.Current.Living == null)
            return;

        foreach (Person actor in EmpireWorld.Current.Living)
        {
            if (actor == null ||
                !actor.Alive ||
                actor.IsGone() ||
                !actor.IsWarrior())
            {
                continue;
            }

            Realm kingdom = actor.Realm;

            if (kingdom == null || kingdom.IsGone())
                continue;

            _kingdomLivingWarriorCounts.TryGetValue(kingdom, out int count);
            _kingdomLivingWarriorCounts[kingdom] = count + 1;
        }
    }

    /// <summary>
    /// 每帧最多扫描一次所有 Person。
    ///
    /// 判断“到达该地”的标准：
    /// Warrior.current_tile.zone.city == city
    ///
    /// 即只要士兵脚下的 Zone 属于该城市，就算已经真正进入城市领土，
    /// 不要求必须抵达中心格。
    /// </summary>
    private static void EnsureImperialPresenceCache()
    {
        int frame = Time.frameCount;

        if (_imperialPresenceCacheFrame == frame)
            return;

        _imperialPresenceCacheFrame = frame;
        _imperialAttackerByCity.Clear();

        if (EmpireWorld.Current == null || EmpireWorld.Current.Living == null)
            return;

        foreach (Person actor in EmpireWorld.Current.Living)
        {
            if (actor == null || !actor.Alive || actor.IsGone())
                continue;

            if (!actor.IsWarrior())
                continue;

            Realm troopKingdom = actor.Realm;

            if (troopKingdom == null || troopKingdom.IsGone())
                continue;

            // 必须是真正的帝国体系军队：
            // 帝国核心国或帝国成员国均可。
            Empire attackerEmpire = troopKingdom.GetEmpire();
            if (attackerEmpire == null && !troopKingdom.IsEmpire())
                continue;

            Cell tile = actor.Cell;
            Cell zone = tile?.Zone;
            City enteredCity = zone?.City;

            if (enteredCity == null || enteredCity.IsGone())
                continue;

            Realm defenderKingdom = enteredCity.Realm;

            if (defenderKingdom == null ||
                defenderKingdom == troopKingdom ||
                defenderKingdom.IsGone())
            {
                continue;
            }

            // 同一帝国内部不触发投降。
            if (defenderKingdom.IsInSameEmpire(troopKingdom))
                continue;

            // 关键修正：
            // 不再要求 troopKingdom 自己必须直接和 defenderKingdom 开战。
            //
            // 帝国战争中常见情况：
            //   CoreKingdom A <-> CoreKingdom B 正在战争
            //   但实际进入城市的是 A 帝国下属 Realm C 的士兵。
            //
            // ResolveImperialCaptureKingdom 会把这种情况解析到真正的参战方。
            Realm captureKingdom =
                ResolveImperialCaptureKingdom(
                    troopKingdom,
                    defenderKingdom);

            if (captureKingdom == null)
                continue;

            // 如果多个帝国同时进入同一城市，
            // 当前帧保留第一个有效敌对帝国。
            if (!_imperialAttackerByCity.ContainsKey(enteredCity))
            {
                _imperialAttackerByCity[enteredCity] = captureKingdom;
            }
        }
    }

    private static bool IsValidImperialSurrenderAttacker(
        City city,
        Realm attackerKingdom)
    {
        if (city == null ||
            attackerKingdom == null ||
            attackerKingdom.IsGone())
        {
            return false;
        }

        Realm defenderKingdom = city.Realm;

        if (defenderKingdom == null ||
            defenderKingdom.IsGone() ||
            defenderKingdom == attackerKingdom)
        {
            return false;
        }

        if (!attackerKingdom.IsInEmpire() &&
            !attackerKingdom.IsEmpire())
        {
            return false;
        }

        if (defenderKingdom.IsInSameEmpire(attackerKingdom))
            return false;

        // attackerKingdom 已经由 ResolveImperialCaptureKingdom
        // 解析成真正可代表此次战争的一方。
        return AreEligibleImmediateSurrenderWarSides(
            attackerKingdom,
            defenderKingdom);
    }

    /// <summary>
    /// 从“实际进入城市的士兵所属 Realm”
    /// 解析出应该用于 finishCapture 的真正参战 Realm。
    ///
    /// 支持：
    /// 1. 成员国直接参战；
    /// 2. 帝国核心国参战、成员国士兵作战；
    /// 3. 双方都是帝国，战争登记在两个 CoreKingdom 上。
    /// </summary>
    private static Realm ResolveImperialCaptureKingdom(
        Realm troopKingdom,
        Realm defenderKingdom)
    {
        if (troopKingdom == null ||
            defenderKingdom == null)
        {
            return null;
        }

        Empire attackerEmpire =
            troopKingdom.GetEmpire();

        Realm attackerCore =
            attackerEmpire?.CoreKingdom;

        // 某些实现中核心国的 GetEmpire() 可能为空，
        // 但 IsEmpire() 为 true，此时本身就是战争核心。
        if (attackerCore == null &&
            troopKingdom.IsEmpire())
        {
            attackerCore = troopKingdom;
        }

        Empire defenderEmpire =
            defenderKingdom.GetEmpire();

        Realm defenderCore =
            defenderEmpire?.CoreKingdom;

        if (defenderCore == null &&
            defenderKingdom.IsEmpire())
        {
            defenderCore = defenderKingdom;
        }

        // 1. 实际部队所属国家自己就是直接参战方。
        if (AreDirectlyAtWarForImmediateSurrender(
                troopKingdom,
                defenderKingdom))
        {
            return troopKingdom;
        }

        // 2. 攻方核心国 vs 当前守城国。
        if (attackerCore != null &&
            AreDirectlyAtWarForImmediateSurrender(
                attackerCore,
                defenderKingdom))
        {
            return attackerCore;
        }

        // 3. 实际部队所属国 vs 守方帝国核心。
        if (defenderCore != null &&
            AreDirectlyAtWarForImmediateSurrender(
                troopKingdom,
                defenderCore))
        {
            return troopKingdom;
        }

        // 4. 最常见的帝国战争：
        //    攻方 CoreKingdom vs 守方 CoreKingdom。
        if (attackerCore != null &&
            defenderCore != null &&
            AreDirectlyAtWarForImmediateSurrender(
                attackerCore,
                defenderCore))
        {
            return attackerCore;
        }

        return null;
    }

    private static bool AreEligibleImmediateSurrenderWarSides(
        Realm attackerKingdom,
        Realm defenderKingdom)
    {
        if (attackerKingdom == null ||
            defenderKingdom == null)
        {
            return false;
        }

        if (AreDirectlyAtWarForImmediateSurrender(
                attackerKingdom,
                defenderKingdom))
        {
            return true;
        }

        Empire attackerEmpire =
            attackerKingdom.GetEmpire();

        Realm attackerCore =
            attackerEmpire?.CoreKingdom;

        if (attackerCore == null &&
            attackerKingdom.IsEmpire())
        {
            attackerCore = attackerKingdom;
        }

        Empire defenderEmpire =
            defenderKingdom.GetEmpire();

        Realm defenderCore =
            defenderEmpire?.CoreKingdom;

        if (defenderCore == null &&
            defenderKingdom.IsEmpire())
        {
            defenderCore = defenderKingdom;
        }

        if (attackerCore != null &&
            AreDirectlyAtWarForImmediateSurrender(
                attackerCore,
                defenderKingdom))
        {
            return true;
        }

        if (defenderCore != null &&
            AreDirectlyAtWarForImmediateSurrender(
                attackerKingdom,
                defenderCore))
        {
            return true;
        }

        if (attackerCore != null &&
            defenderCore != null &&
            AreDirectlyAtWarForImmediateSurrender(
                attackerCore,
                defenderCore))
        {
            return true;
        }

        return false;
    }

    private static bool AreDirectlyAtWarForImmediateSurrender(
        Realm a,
        Realm b)
    {
        if (a == null ||
            b == null ||
            a == b ||
            a.IsGone() ||
            b.IsGone())
        {
            return false;
        }

        try
        {
            foreach (War war in a.GetWars())
            {
                if (war == null ||
                    !war.Alive ||
                    war.HasEnded() ||
                    war.GetEmpireWarType() == EmpireWarType.劫掠)
                {
                    continue;
                }

                bool oppositeSides =
                    (war.IsAttacker(a) && war.IsDefender(b)) ||
                    (war.IsDefender(a) && war.IsAttacker(b));

                if (oppositeSides)
                    return true;
            }
        }
        catch
        {
            // 战争正在结束时，列表可能被原版同时修改；本帧不触发归降即可。
        }

        return false;
    }
#if EC_PENDING
    public static bool removeObject(CityManager __instance, City pObject)
    {
        if (CityTerritoryProtection.ShouldPreventDestruction(pObject))
        {
            FiscalDiagnostics.Count("保城拦截");
            return false;
        }
        // 保城开关开着、活城却仍被移除时记下原因(领土被蚕食光等)，便于追查"开关没生效"的路径；
        // 换地图时旧城先被标记死亡，不会触发这条
        if (EmpireCraft.Core.WorldLaws.PreventCityDestroy?.IsEnabled() == true &&
            pObject?.Record != null && pObject.Alive && !ModClass.IS_CLEAR &&
            !EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(pObject))
            LogService.LogWarning(
                $"[EmpireCraft][保城] 城市 {pObject.Name} 仍被移除：领土 {pObject.Zones?.Count ?? -1} 块，" +
                $"背景人口 {EmpireCraft.Core.CityPopulationSystem.GetBackgroundTotal(pObject):0}");
        return true;
    }
#endif

    public static void setKingdom(City __instance, Realm pKingdom)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        Regime regime = pKingdom.GetRegime();
        if (regime != null)
        {
            CityType cityType = EmpireCraftKingdomBehCheckKingdomType.CalcCityType(pKingdom);
            __instance.SetCityType(cityType);
            if (regime.bureau_config?.cities != null &&
                regime.bureau_config.cities.TryGetValue(cityType, out BureauSetting citySetting) && citySetting != null)
            {
                OfficeObject officeObject = __instance.GetOffice();
                if (officeObject != null) officeObject.InitialOffice(citySetting, isNew: false);
                else
                {
                    officeObject = new OfficeObject();
                    officeObject.InitialOffice(citySetting);
                    __instance.SetOffice(officeObject);
                }
                officeObject.regimeType = regime.type;
            }
        }

        if (pKingdom.IsInEmpire())
        {
            var empire = pKingdom.GetEmpire();
            if (empire != null)
            {
                empire.cities_list.Add(__instance);
                empire.cities_list = empire.cities_list.Distinct().ToList();
            }
        }
    }

    public static void setKingdom_Postfix(City __instance)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        if (__instance.Realm?.GetRegime() != null) __instance.RefreshCityDisplayName();
        OccupationReadIndex.Invalidate(__instance);
        if (__instance.hasTitle())
        {
            __instance.GetTitle().isBeenControlled();
        }
    }
    
    //创建新城市时触发
    // 城邦：开国之城定国名，之后新建的城独立为殖民城邦
    public static void CityBuilt(City __result, Person pActor)
    {
        // 预划县址：哪条路建的城落在没设县的县址里，就是这一县的县城(见 WildernessPlotSystem.OnCityBuilt)，所以放在各条路的提前返回之前
        WildernessPlotSystem.OnCityBuilt(__result);
        if (StateSettlementSystem.CreatingCity) return; // 国家移民安置完毕后再处理城邦独立。
        if (CityPlacementSystem.Placing) return; // 营建页「建城」：新城归所点地方的国家，不独立、不分部落
        if (TransmigrantSystem.Landing) return; // 穿越降临：落脚的城就是新国家的都城(见 TransmigrantSystem.Land)
        if (ColonySystem.Founding) return; // 殖民船建的城：编进殖民地(见 ColonySystem.Arrive)
        if (EmpireCraft.Core.WorldMapScenario.Building) return; // 世界地图开局摆城：按时代格局归国，不分部落、不分殖民城邦
        if (__result == null || EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__result)) return;
        CityStateService.OnCityBuilt(__result, pActor);
        // 部落、方国的族人新建的城分出去成为新部落(见 TribalFissionService)
        TribalFissionService.OnCityBuilt(__result, pActor);
    }

    public static void newCity(City __instance, Person pActor)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        CultureService.InitializeCityCulture(__instance);
        if (pActor != null && !pActor.HasCulture())
            CultureService.SyncActorToCityMainCulture(pActor, __instance);
    }

#if EC_PENDING
    public static bool zone_steal(CityBehBorderSteal __instance, City pCity)
    {
        // 原版侵蚀先移除邻城地块，再加给本城：即使尚未满额也会永久改变城市边界。
        // 模组战争使用临时占领与整城划归，在入口拦下，不能等 addZone 时才拒绝。
        return EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(pCity);
    }
#endif

    public static bool destroy_city(City __instance)
    {
        // 拦截必须发生在法理、宗教及帝国列表清理之前。
        if (CityTerritoryProtection.ShouldPreventDestruction(__instance))
        {
            FiscalDiagnostics.Count("保城拦截");
            return false;
        }
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return true;
        foreach (var religion in EmpireWorld.Current.Religions)
        {
            // 圣地毁了，迁到信众最多的城(以前取列表头一个)
            if (religion.GetCity() == __instance) ReligionSphere.RelocateHolyCity(religion, __instance);
        }

        if (__instance.HasRealm())
        {
            var kingdom = __instance.Realm;
            if (kingdom != null)
            {
                var empire = kingdom.GetEmpire();
                if (empire != null)
                {
                    empire.cities_list.Remove(__instance);
                }
            }
        }
        if (__instance.hasTitle())
        {
            __instance.GetTitle().removeCity(__instance);
        }
        EmpireCraft.Core.CityAssetSettlement.OnCityDestroyed(__instance);
        return true;
    }
    public static void removeData(City __instance)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        OccupationReadIndex.Invalidate(__instance);
        __instance.RemoveExtraData<City, CityExtraData>();
        _imperialArrivalSurrenderCooldown.Remove(__instance);
    }
}
}
