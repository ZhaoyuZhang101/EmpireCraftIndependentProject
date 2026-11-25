// Generated from the EmpireCraft mod: Scripts/GeneralSystems/ShuiTuSystem.Outlets.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 水土的出路(用户 2026-10-20 问答定，AI 会走)：到第一档后每年按 OutletChance×档位(定居入草按 SettledOutletChance)的机会挑一条(按局势)，
// 走成了 OutletCooldownYears 年内不再走；打着仗不走。改了政体(不再是游牧/只宜农地)、回到旱地，计时自然清零。
//   游牧离草：
//     1. 从俗：复合帝国"采纳属地制度""从俗"的门槛按离草档位放宽、对一切定居文化都肯(见 CompositeEmpireService)，不在这里走；
//     2. 本族定居：本文化施行了"定居官制"(youmu_settled_bureaucracy)，改行定居官僚的律令制(先选)；
//     3. 北归(第二档起，有旱地上的本族旧地)：迁都旧地，农地为主的直辖成员各自脱离，本国的农地城交当地首领(朝贡、附庸)，
//        离草从头计；复合帝国的北归见 CompositeEmpireService.TryNorthernRetreat；
//     4. 封建化(第二档起)：改行封建，兀鲁思(同一制度线的成员)随之改为封建诸侯。旱地还占三成以上的先北归，否则先封建化；
//   定居入草：
//     1. 羁縻：施行了羁縻制度的帝国先把到档的旱地城(连成一片的至多六座)交当地首领设羁縻州；
//     2. 整国游牧化：直辖城旱地六成以上的先改行游牧(五到六成的别的路都走不了才改)；附近要有游牧(见 NomadNearby)；
//     3. 改从游牧：到档的旱地城连成一片、邻近有游牧文化，主流文化改成它、城外村镇改放牧(城池建筑照旧，不改建营地)，
//        当地首领立为游牧之国——在帝国里就留作游牧成员(自主外交、世袭，自成一体)，不是帝国就做附庸；
//        这种成员带着游牧易散的毛病：宗主更替逢积弊深重或正统低落，按汗位更替之乱的机会据地自立(见 OnSuzerainSucceeded)；
//     4. 交当地首领(第二档起)：按政体文化转为朝贡国、附庸(同"扩张上限"，见 AdministrativeControlSystem.ReleaseToLocal)；
//     5. 屯田只拖延：屯田实边把不服水土的旱地城的计时往回拨 DelayYears 年(不晚于现在)，免不掉(见 Delay)；本族为主、已过顶档的城不拨(拨了只推迟适应)。
//     整国游牧化、改从游牧都是学邻居的活法：附近没有游牧就不走(用户 2026-10-22 "整个地图都没有游牧制度他会变成游牧"，
//     以前改从游牧附近没有时找本文化制度线上的游牧文化，整国游牧化不看)；以前这样游牧化的，读档后头一趟改回(见 RevertUnfounded)。
//     以上档位都按逐步适应退过之后的算(用户 2026-10-21 选 A3，见 UnfitTierOf)：本族为主、占满 60 年的城逐年退档，
//     退到不罚的城不再算"到档"、不会被交出去，出路的机会、交当地首领要第二档也照退过的最高档。
// 出路改的政体记在 shuitu_regime 并当作手选(regime_manually_selected)，免得制度同步按文化又改回去；玩家手选的政体不动。
public static partial class ShuiTuSystem
{
    private const float OutletChance = 0.15f;
    // 定居入草的出路都是把旱地城交出去(羁縻、改从游牧、交当地首领)：15% → 5%×档位(用户 2026-10-21 选"水土旱地出走降频")
    private const float SettledOutletChance = 0.05f;
    private const int OutletCooldownYears = 10;
    public const int RetreatTier = 2;
    private const string SettledNode = "youmu_settled_bureaucracy";
    private const float RetreatFirstShare = 0.3f, NomadizeShare = 0.5f;
    private const int ClusterMax = 6, RetreatClusters = 4;
    private const int DelayYears = 5;
    // 改从游牧的成员：宗主更替之乱(同汗位更替之乱，见 NomadCycleSystem)；附庸逢宗主更替起兵独立的机会
    private const float CrisisMalady = 50f, BaseSplit = 0.15f, MaladySplit = 0.5f, VassalSplit = 0.3f;
    private const int CrisisLegitimacy = 50;
    public const string NomadizedReason = "rebellion_reason_nomadized_succession";

    #region 挑出路

#if EC_PENDING
    private static void TryOutlet(Realm unit, UnitInfo info, List<Realm> kingdoms, List<City> cities)
    {
        bool nomad = info.Fit == Fit.Nomad && info.OffGrass && info.OffTier > 0;
        bool settled = info.Fit == Fit.Farm && info.UnfitTop > 0;
        // 政权包含直辖成员；中央和平、直辖行省仍在打仗时也不能拆地改制。
        if (!nomad && !settled || unit.HasEnemies() || kingdoms.Any(member => member != unit && member.HasEnemies())) return;
        var extra = unit.GetOrCreate();
        double now = GameClock.Now;
        if (extra.shuitu_outlet_at >= 0d && now >= extra.shuitu_outlet_at && GameClock.YearsSince(extra.shuitu_outlet_at) < OutletCooldownYears)
            return;
        if (EcRandom.value >= (nomad ? OutletChance * info.OffTier : SettledOutletChance * info.UnfitTop)) return;
        string taken = nomad ? NomadOutlet(unit, info, kingdoms, cities) : SettledOutlet(unit, info, cities);
        if (taken == null) return;
        extra.shuitu_outlet_at = now;
        // 出路可能换了政体或领土，下一帧重算效果，不沿用旧档位再扣一年。
        info.At = -1d;
        LogService.LogInfo($"[EmpireCraft][水土] {unit.GetKingdomName()} 走出路：{taken}");
    }

    private static string NomadOutlet(Realm unit, UnitInfo info, List<Realm> kingdoms, List<City> cities)
    {
        Empire empire = unit.GetEmpire();
        // 复合帝国走从俗、北归(见 CompositeEmpireService)
        if (empire?.CoreKingdom == unit && CompositeEmpireService.IsComposite(empire)) return null;
        bool free = RegimeFree(unit);
        string culture = CultureService.GetRealmCulture(unit);
        if (free && InstitutionSystem.IsEnacted(culture, SettledNode) && Settle(unit)) return "本族定居";
        if (info.OffTier < RetreatTier) return null;
        City seat = HomelandSeat(unit, cities, culture);
        bool retreatFirst = seat != null && info.Dry >= info.Direct * RetreatFirstShare;
        if (retreatFirst && Retreat(unit, kingdoms, seat)) return "北归";
        if (free && Feudalize(unit)) return "封建化";
        if (seat != null && !retreatFirst && Retreat(unit, kingdoms, seat)) return "北归";
        return null;
    }

    private static string SettledOutlet(Realm unit, UnitInfo info, List<City> cities)
    {
        Empire empire = unit.GetEmpire();
        bool composite = empire?.CoreKingdom == unit && CompositeEmpireService.IsComposite(empire);
        bool free = !composite && RegimeFree(unit);
        float share = info.Direct > 0 ? info.Dry / (float)info.Direct : 0f;
        // 到档的旱地城(各国都城不动)
        List<City> unfit = cities.Where(city => UnfitTier(city) > 0 && city.Realm.Capital != city).ToList();
        if (empire != null && JimiSystem.Enacted(empire) && unfit.Count > 0 && Release(unit, unfit)) return "羁縻";
        bool nomadNear = NomadNearby(unit, cities);
        if (nomadNear && free && share >= SteppeShare && Nomadize(unit)) return "整国游牧化";
        if (nomadNear && unfit.Count > 0 && TurnNomad(unit, unfit)) return "改从游牧";
        if (nomadNear && free && share >= NomadizeShare && Nomadize(unit)) return "整国游牧化";
        if (info.UnfitTop >= 2 && unfit.Count > 0 && Release(unit, unfit)) return "交当地首领";
        return null;
    }
#endif

    #endregion

    #region 改政体

    // 出路能不能改这国的政体：共和锁定的、玩家手选的不改(出路自己改的不算手选)
    private static bool RegimeFree(Realm kingdom)
    {
        if (kingdom?.Record == null || RepublicSystem.IsRegimeLocked(kingdom)) return false;
        var extra = kingdom.GetOrCreate();
        return !extra.regime_manually_selected || extra.shuitu_regime >= 0 && extra.shuitu_regime == (int)extra.regimeType;
    }

    // 改政体：帝国核心连同同一制度线的成员一起改(同制度改革，见 InstitutionSystem.ChangeRegimeForTransition)；
    // 改了的各国记下(制度同步不再按文化改回去)。返回改了的成员数(不含本国)，没改成返回 -1
#if EC_PENDING
    private static int ChangeRegime(Realm unit, RegimeType target)
    {
        if (RegimeManager.regimes == null || !RegimeManager.regimes.ContainsKey(target) || unit.GetRegime()?.type == target)
            return -1;
        Empire empire = unit.GetEmpire();
        var before = new Dictionary<long, RegimeType?>();
        before[unit.Key] = unit.GetRegime()?.type;
        if (empire?.CoreKingdom == unit)
        {
            foreach (Realm member in empire.kingdoms_list ?? new List<Realm>())
                if (member?.Record != null && !member.IsGone()) before[member.Key] = member.GetRegime()?.type;
            InstitutionSystem.EnsureRealmState(InstitutionRealm.Of(empire));
            InstitutionSystem.ChangeRegimeForTransition(empire, target);
        }
        else
        {
            before[unit.Key] = unit.GetRegime()?.type;
            unit.SetRegimeType(target);
            if (unit.IsInEmpire()) unit.LoadRegimeKeepingRole();
            else unit.LoadRegime();
            unit.SystemChange();
        }
        if (unit.GetRegime()?.type != target) return -1;
        int members = 0;
        foreach (long id in before.Keys)
        {
            Realm kingdom = EmpireWorld.Current.RealmByKey(id);
            if (kingdom?.Record == null || kingdom.IsGone() || kingdom.GetRegime()?.type != target || before[id] == target) continue;
            var extra = kingdom.GetOrCreate();
            extra.regime_manually_selected = true;
            extra.shuitu_regime = (int)target;
            extra.shuitu_nomad_justified = target == RegimeType.YouMu;
            extra.last_kingdom_status_ts = -1d;
            EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
            if (kingdom != unit) members++;
        }
        return members;
    }
#endif

    // 本族定居：改行定居官僚的律令制
#if EC_PENDING
    private static bool Settle(Realm unit)
    {
        string name = unit.GetKingdomName();
        if (ChangeRegime(unit, RegimeType.LvLing) < 0) return false;
        ClearOffGrass(unit);
        EventRecorder.Record(unit, string.Format(LM.Get("shuitu_settle_history"), name,
            CompositeEmpireService.GetRegimeName(RegimeType.LvLing)));
        return true;
    }
#endif

    // 自行封建化保留本族汗庭与世袭领主；已采纳西方制度的才改行西方封建。
#if EC_PENDING
    private static bool Feudalize(Realm unit)
    {
        string name = unit.GetKingdomName();
        RegimeType target = NomadFeudalismSystem.FeudalizationTarget(unit);
        int members = ChangeRegime(unit, target);
        if (members < 0) return false;
        ClearOffGrass(unit);
        EventRecorder.Record(unit, string.Format(LM.Get("shuitu_feudalize_history"), name,
            CompositeEmpireService.GetRegimeName(target), members));
        return true;
    }
#endif

    // 整国游牧化：定居政权改行游牧
#if EC_PENDING
    private static bool Nomadize(Realm unit)
    {
        string name = unit.GetKingdomName();
        if (ChangeRegime(unit, RegimeType.YouMu) < 0) return false;
        EventRecorder.Record(unit, string.Format(LM.Get("shuitu_nomadize_history"), name,
            CompositeEmpireService.GetRegimeName(RegimeType.YouMu)));
        return true;
    }
#endif

    // 附近有游牧：本国直辖城或与之相邻的城里有游牧文化为主的城，或接壤、隔海相望的国家是游牧文化
#if EC_PENDING
    private static bool NomadNearby(Realm unit, List<City> cities)
    {
        foreach (City city in cities)
        {
            if (NomadicCity(city)) return true;
            foreach (City next in (IEnumerable<City>)city.NeighbourCities ?? Array.Empty<City>())
                if (NomadicCity(next)) return true;
        }
        foreach (Realm other in EmpireWorld.Current.LiveRealms)
            if (other != unit && Valid(other) && other.hasCities() &&
                CompositeEmpireService.IsNomadicCulture(CultureService.GetRealmCulture(other)) && StraitReach.Near(unit, other))
                return true;
        return false;
    }

    private static bool NomadicCity(City city) =>
        city?.Record != null && !city.IsGone() &&
        CompositeEmpireService.IsNomadicCulture(CultureService.GetMainCulture(city, initialize: false));
#endif

    // 读档后每个政权头一趟：以前附近没有游牧也"整国游牧化"了的定居文化国，附近确实没有游牧的改回本文化眼下应有的政体，
    // 国号、君主称号随政体恢复(用户 2026-10-22 选"读档时改回原政体")
    private static readonly HashSet<long> RevertChecked = new();

#if EC_PENDING
    private static void RevertUnfounded(Realm unit, List<Realm> kingdoms, List<City> cities)
    {
        if (!RevertChecked.Add(unit.Key)) return;
        var extra = unit.GetOrCreate();
        if (extra.shuitu_regime != (int)RegimeType.YouMu || extra.shuitu_nomad_justified ||
            unit.GetRegime()?.type != RegimeType.YouMu) return;
        if (CompositeEmpireService.IsNomadicCulture(CultureService.GetRealmCulture(unit)) || NomadNearby(unit, cities)) return;
        string institution = CultureService.GetInstitutionCulture(unit);
        RegimeType target = InstitutionSystem.TryResolveCultureRegime(institution, out RegimeType resolved)
            ? resolved
            : OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(institution, out Setting setting) ? setting.regime : RegimeType.Feudalism;
        target = InstitutionSystem.AdjustForMonarchyEmpire(unit, target);
        // 同制度同步的防护(见 InstitutionSystem.SyncForeignRegime)：本文化还在原始阶段的不改回部落，共和、现代另有专管
        if (KingdomExtension.IsNomadTaxRegime(target) || RegimeManager.IsPrimitive(target) ||
            target is RegimeType.Modern or RegimeType.Republic) return;
        string name = unit.GetKingdomName();
        if (ChangeRegime(unit, target) < 0) return;
        // 改回的是本文化自己的政体，不当手选，之后照常随制度同步(帝国的话连同间接成员，ChangeRegime 改的是全体成员)
        var realm = new List<Realm>(kingdoms);
        if (unit.GetEmpire()?.kingdoms_list is List<Realm> members) realm.AddRange(members);
        foreach (Realm kingdom in realm)
        {
            if (kingdom?.Record == null || kingdom.IsGone()) continue;
            var data = kingdom.GetOrCreate();
            if (data.shuitu_regime != (int)target) continue;
            data.shuitu_regime = -1;
            data.regime_manually_selected = false;
        }
        // 离草计时一并清零：改了政体，同一趟结算里不再另记一条"离草结束"
        ClearOffGrass(unit);
        EventRecorder.Record(unit, string.Format(LM.Get("shuitu_denomadize_history"), name,
            CompositeEmpireService.GetRegimeName(target)));
        LogService.LogInfo($"[EmpireCraft][水土] {name} 附近没有游牧，撤回整国游牧化，改回{CompositeEmpireService.GetRegimeName(target)}");
    }
#endif

    // 走了出路的离草清零(到过档的已记过史书，这里不再记"离草结束")
    private static void ClearOffGrass(Realm unit)
    {
        var extra = unit.GetOrCreate();
        extra.shuitu_offgrass_since = -1d;
        extra.shuitu_offgrass_tier = 0;
        if (Units.TryGetValue(unit.Key, out UnitInfo info)) info.At = -1d;
    }

    #endregion

    #region 北归

    // 旱地上的本族旧地：直辖城里主流文化是游牧文化的旱地城(本国文化优先，人口多的优先)；各成员国的都城不拿
#if EC_PENDING
    private static City HomelandSeat(Realm unit, List<City> cities, string culture)
    {
        return cities.Where(city => city.Realm == unit || city.Realm.Capital != city)
            .Where(IsDryCity)
            .Select(city => (city, main: CultureService.GetMainCulture(city, initialize: false)))
            .Where(pair => CompositeEmpireService.IsNomadicCulture(pair.main))
            .OrderByDescending(pair => string.Equals(pair.main, culture, StringComparison.Ordinal))
            .ThenByDescending(pair => pair.city.CountLivingPopulation())
            .Select(pair => pair.city).FirstOrDefault();
    }
#endif

    // 北归：迁都旱地旧地；农地为主的直辖成员各自脱离；本国的农地城一片片交当地首领(至多 RetreatClusters 片)。离草从头计
#if EC_PENDING
    private static bool Retreat(Realm unit, List<Realm> kingdoms, City seat)
    {
        if (seat.Realm != unit) seat.JoinRealm(unit);
        if (seat.Realm != unit) return false;
        string name = unit.GetKingdomName();
        if (unit.Capital != seat) unit.SetCapital(seat);
        Empire empire = unit.GetEmpire();
        int left = 0;
        if (empire?.CoreKingdom == unit)
            foreach (Realm member in kingdoms.Where(member => member != unit && member.GetEmpire() == empire).ToList())
            {
                List<City> own = member.Cities?.Where(Valid).ToList() ?? new List<City>();
                if (own.Count == 0 || own.Count(IsDryCity) * 2 >= own.Count) continue;
                // 不许自立(见 SecessionGate)：农地成员不脱离(不记叛离，Empire.leave 那道门拦不到)
                if (SecessionGate.Forbids("北归脱离", member.Name)) continue;
                empire.leave(member);
                left++;
            }
        // 帝国里自成一体的成员放出去的城自立，不再层层附庸
        bool subordinate = empire == null || empire.CoreKingdom == unit;
        Realm realm = empire?.CoreKingdom ?? unit;
        int released = 0;
        for (int clusters = 0; clusters < RetreatClusters; clusters++)
        {
            City seed = unit.Cities.Where(city => Valid(city) && city != unit.Capital && !IsDryCity(city))
                .OrderBy(AdministrativeControlSystem.Get).ThenBy(city => city.Key).FirstOrDefault();
            if (seed == null) break;
            List<City> cluster = Cluster(seed, city => city.Realm == unit && city != unit.Capital && !IsDryCity(city));
            Realm local = AdministrativeControlSystem.ReleaseToLocal(realm, cluster,
                subordinate ? null : (Func<Realm, string>)(_ => "independent"), out _);
            if (local == null) break;
            released += cluster.Count;
        }
        var extra = unit.GetOrCreate();
        extra.shuitu_offgrass_since = -1d;
        extra.shuitu_offgrass_tier = 0;
        if (Units.TryGetValue(unit.Key, out UnitInfo info)) info.At = -1d;
        EventRecorder.Record(unit, string.Format(LM.Get("shuitu_retreat_history"), name, seat.GetCityName(), released, left));
        return true;
    }
#endif

    // 复合帝国北归之后(见 CompositeEmpireService.TryNorthernRetreat)：离草从头计
    public static void OnNorthernRetreat(Realm core)
    {
        if (core?.Record == null) return;
        var extra = core.GetOrCreate();
        extra.shuitu_offgrass_since = -1d;
        extra.shuitu_offgrass_tier = 0;
        extra.shuitu_outlet_at = EmpireWorld.Current?.Now() ?? -1d;
        if (Units.TryGetValue(core.Key, out UnitInfo info)) info.At = -1d;
    }

    #endregion

    #region 定居入草

    // 连成一片的城(从 seed 起沿邻城走，至多 ClusterMax 座)
    private static List<City> Cluster(City seed, Func<City, bool> member)
    {
        var cluster = new List<City> { seed };
        var seen = new HashSet<City> { seed };
        for (int i = 0; i < cluster.Count && cluster.Count < ClusterMax; i++)
            foreach (City next in (IEnumerable<City>)cluster[i].NeighbourCities ?? Array.Empty<City>())
            {
                if (cluster.Count >= ClusterMax) break;
                if (next == null || !seen.Add(next) || !Valid(next) || !member(next)) continue;
                cluster.Add(next);
            }
        return cluster;
    }

#if EC_PENDING
    private static List<City> UnfitCluster(List<City> unfit)
    {
        City seed = unfit.OrderByDescending(UnfitTier).ThenBy(city => city.Key).First();
        var set = new HashSet<City>(unfit);
        return Cluster(seed, city => set.Contains(city) && city.Realm == seed.Realm);
    }
#endif

    // 羁縻、交当地首领：到档最高的那片旱地城按政体文化转为羁縻州、朝贡国、附庸(同扩张上限)
#if EC_PENDING
    private static bool Release(Realm unit, List<City> unfit)
    {
        List<City> cluster = UnfitCluster(unfit);
        Realm realm = unit.GetEmpire()?.CoreKingdom ?? unit;
        string name = realm.GetKingdomName();
        Realm local = AdministrativeControlSystem.ReleaseToLocal(realm, cluster, null, out string form);
        if (local == null) return false;
        EventRecorder.Record(unit, string.Format(LM.Get("shuitu_release_" + form + "_history"), name, local.GetKingdomName(),
            cluster.Count));
        return true;
    }
#endif

    // 改从游牧：那片旱地城改从邻近的游牧文化、改建游牧营地，当地首领立为游牧之国
#if EC_PENDING
    private static bool TurnNomad(Realm unit, List<City> unfit)
    {
        // 锁定官方文化的城不走改从异俗这条路，仍可羁縻、分封或整国改政体。
        List<City> candidates = unfit.Where(city => !CultureService.IsCityMainCultureLocked(city)).ToList();
        if (candidates.Count == 0) return false;
        List<City> cluster = UnfitCluster(candidates);
        string nomad = NomadCultureNear(cluster);
        if (nomad == null) return false;
        Realm realm = unit.GetEmpire()?.CoreKingdom ?? unit;
        string name = realm.GetKingdomName();
        Realm local = AdministrativeControlSystem.ReleaseToLocal(realm, cluster, created => PlaceNomads(created, unit, nomad),
            out string form);
        if (local == null) return false;
        // 自立成功后才改住民文化与生计；找不到首领或建国失败时保留原城。易主不改建筑(用户 2026-10-20)：城墙、宫城、城貌、关城、
        // 村镇样式照旧，不改建成营地、不降人口上限；城外村镇没在窗口里手动改过的改放牧，之后新添的村镇按游牧规矩(毡帐、只在旱地)
        foreach (City city in cluster)
        {
            CultureService.SetCityMainCulture(city, nomad);
            city.GetOrCreate().nomad_settlers = true;
            bool changed = false;
            foreach (RuralSettlement settlement in RuralSettlementSystem.InCity(city))
            {
                if (settlement.fort || settlement.trade_manual || settlement.trade == LivelihoodSystem.Pasture) continue;
                settlement.trade = LivelihoodSystem.Pasture;
                settlement.trade_rules = LivelihoodSystem.RulesVersion;
                changed = true;
            }
            if (changed) RuralSettlementSystem.TradeChanged(city);
        }
        EventRecorder.Record(unit, string.Format(LM.Get("shuitu_nomadized_" + form + "_history"), name, local.GetKingdomName(),
            cluster.Count, nomad.GetCultureTranslate()));
        return true;
    }
#endif

    // 新立的游牧之国：改游牧文化、游牧政体；宗主在帝国里就留作成员(自主外交、世袭)，否则做附庸
#if EC_PENDING
    private static string PlaceNomads(Realm local, Realm unit, string nomad)
    {
        var extra = local.GetOrCreate();
        extra.shuitu_nomadized_at = GameClock.Now;
        CultureService.SetRealmCulture(local, nomad);
        Empire empire = unit.GetEmpire();
        string form = "independent";
        if (empire != null && !empire.IsGone())
        {
            if (local.hasAlliance()) local.getAlliance().leave(local);
            empire.join(local, pForce: true);
            if (local.GetEmpire() == empire) form = "member";
        }
        else if (FeudalVassalService.ForceBind(unit, local)) form = "vassal";
        local.SetRegimeType(RegimeType.YouMu);
        if (local.IsInEmpire()) local.LoadRegimeKeepingRole();
        else local.LoadRegime();
        local.SystemChange();
        if (local.GetRegime() is Regime regime && regime.type == RegimeType.YouMu)
        {
            extra.regime_manually_selected = true;
            extra.shuitu_regime = (int)RegimeType.YouMu;
            if (form == "member")
            {
                regime.SetAllowDiplomacy(true);
                regime.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
            }
        }
        extra.last_kingdom_status_ts = -1d;
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(local);
        return form;
    }
#endif

    // 邻近的游牧文化：这片城与它们的邻城里的主流文化(这片城自己的算两份)；没有就找本文化制度线上的游牧文化
#if EC_PENDING
    private static string NomadCultureNear(List<City> cluster)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        void Count(City city, int weight)
        {
            string main = city?.Record == null || city.IsGone() ? "" : CultureService.GetMainCulture(city, initialize: false);
            if (!CompositeEmpireService.IsNomadicCulture(main)) return;
            counts.TryGetValue(main, out int count);
            counts[main] = count + weight;
        }
        foreach (City city in cluster)
        {
            Count(city, 2);
            foreach (City next in (IEnumerable<City>)city.NeighbourCities ?? Array.Empty<City>()) Count(next, 1);
        }
        // 附近没有游牧文化就不改从游牧(以前找本文化制度线上的游牧文化顶上)
        return counts.Count > 0
            ? counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).First().Key
            : null;
    }
#endif

    // 屯田实边(见 TempFac_屯田实边)：本国不服水土的旱地城计时往回拨 DelayYears 年(不晚于现在)，返回拨了几座
#if EC_PENDING
    public static int Delay(Realm kingdom)
    {
        Realm unit = UnitOf(kingdom);
        if (unit == null || EmpireWorld.Current == null) return 0;
        double now = GameClock.Now;
        var kingdoms = new List<Realm>();
        int delayed = 0;
        string ruling = CultureService.GetRealmCulture(unit);
        foreach (City city in DirectCities(unit, kingdoms))
        {
            var data = city.GetOrCreate();
            if (data.shuitu_owner != unit.Key || data.shuitu_since < 0d) continue;
            // 本族为主、计时已过顶档(40 年)的城不往回拨：此后计时是在往逐步适应走(见 UnfitTierOf)，
            // 拨回去只会推迟适应，满了 60 年的还会把退下的档又拨上来
            string culture = CultureService.GetMainCulture(city, initialize: false);
            if (TierOf(YearsSince(data.shuitu_since)) >= MaxTier && RulersCulture(culture, ruling)) continue;
            data.shuitu_since = Math.Min(now, data.shuitu_since + DelayYears * SecondsPerYear);
            delayed++;
            if (!CityInfos.TryGetValue(city.Key, out CityInfo info)) continue;
            info.Years = YearsSince(data.shuitu_since);
            info.Tier = UnfitTierOf(info.Years, culture, ruling);
            info.Culture = culture;
            CityInfos[city.Key] = info;
        }
        // 政权的最高档当即按拨过的重算
        if (delayed > 0 && Units.TryGetValue(unit.Key, out UnitInfo unitInfo))
        {
            unitInfo.UnfitTop = unitInfo.UnfitYears = unitInfo.Adapted = 0;
            Array.Clear(unitInfo.TierCounts, 0, unitInfo.TierCounts.Length);
            foreach (long id in unitInfo.CityIds)
            {
                if (!CityInfos.TryGetValue(id, out CityInfo info) || info.Unit != unit.Key || !info.Dry || unitInfo.Fit != Fit.Farm) continue;
                unitInfo.TierCounts[info.Tier]++;
                unitInfo.UnfitTop = Math.Max(unitInfo.UnfitTop, info.Tier);
                unitInfo.UnfitYears = Math.Max(unitInfo.UnfitYears, info.Years);
                if (info.Tier < TierOf(info.Years)) unitInfo.Adapted++;
            }
        }
        if (delayed > 0) LogService.LogInfo($"[EmpireCraft][水土] {unit.GetKingdomName()} 屯田实边，{delayed} 座旱地城的计时往回拨 {DelayYears} 年");
        return delayed;
    }
#endif

    #endregion

    #region 宗主更替

    // 定居帝国换了君主(KingdomPatch 的 setKing 补丁)：积弊深重或正统低落时，改从游牧的成员各按汗位更替之乱的机会据地自立。
    // 汗国自己有汗位更替之乱(游牧政体的成员都在内，见 NomadCycleSystem.OnKhanSucceeded)
#if EC_PENDING
    public static void OnSuzerainSucceeded(Empire empire)
    {
        try
        {
            if (empire?.data == null || empire.IsGone() || empire.IsArchived() || EmpireWorld.Current == null ||
                NomadCycleSystem.IsKhanate(empire)) return;
            float average = MaladySystem.Average(empire);
            int legitimacy = empire.Legitimacy;
            if (average < CrisisMalady && legitimacy >= CrisisLegitimacy) return;
            float chance = BaseSplit + MaladySplit * average / 100f;
            var split = new List<string>();
            foreach (Realm member in (empire.kingdoms_list ?? new List<Realm>()).ToList())
            {
                if (member?.Record == null || member.IsGone() || member == empire.CoreKingdom ||
                    member.GetOrCreate().shuitu_nomadized_at < 0d || member.GetRegime()?.type != RegimeType.YouMu ||
                    !member.HasRuler() || member.Capital == null ||
                    member.IsFactionRebelling() || member.IsLocalRebelling() || !RebellionSystem.CanAttempt(member) ||
                    EcRandom.value >= chance) continue;
                string name = member.GetKingdomName();
                if (NomadCycleSystem.DeclareIndependence(member, empire, NomadizedReason)) split.Add(name);
            }
            if (split.Count > 0)
                LogService.LogInfo($"[EmpireCraft][水土] {empire.GetEmpireName()} 换了君主(积弊均{average:0}，正统{legitimacy})，" +
                                   $"改从游牧的 {string.Join("、", split)} 据地自立(每国机会 {chance:0.00})");
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][水土] 宗主更替结算失败: {exception.Message}");
        }
    }
#endif

    // 不是帝国的宗主换了君主：改从游牧的附庸各有 VassalSplit 的机会起兵独立
#if EC_PENDING
    public static void OnOverlordSucceeded(Realm lord)
    {
        try
        {
            if (lord?.Record == null || lord.IsGone() || lord.IsEmpire() || EmpireWorld.Current == null) return;
            foreach (Realm vassal in FeudalVassalService.GetDirectVassals(lord).ToList())
            {
                if (!Valid(vassal) || vassal.GetOrCreate().shuitu_nomadized_at < 0d ||
                    vassal.GetRegime()?.type != RegimeType.YouMu || !vassal.HasRuler() || vassal.HasEnemies() ||
                    EcRandom.value >= VassalSplit) continue;
                War war = EmpireWorld.Current.StartWar(vassal, lord, WarKinds.Normal);
                if (war == null) continue;
                war.SetEmpireWarType(EmpireWarType.附庸独立);
                LogService.LogInfo($"[EmpireCraft][水土] {lord.GetKingdomName()} 换了君主，改从游牧的附庸 {vassal.GetKingdomName()} 起兵独立");
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][水土] 宗主更替结算失败: {exception.Message}");
        }
    }
#endif

    #endregion
}
}
