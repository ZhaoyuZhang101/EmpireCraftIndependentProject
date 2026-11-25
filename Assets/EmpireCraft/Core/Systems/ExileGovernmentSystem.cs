// Generated from the EmpireCraft mod: Scripts/GeneralSystems/ExileGovernmentSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 流亡政府(用户 2026-11-10 "流亡政府机制")：近代国家亡于外敌时不就此灭亡，元首带着政府流亡到盟国或同仇敌忾的国家，
// 等盟军打回来再还都。史例：大韩民国临时政府(上海→重庆)、海尔·塞拉西流亡英国、二战中波兰、荷兰、挪威、比利时、
// 希腊、南斯拉夫的政府流亡伦敦，菲律宾自治邦政府流亡华盛顿。
//   · 什么时候流亡：近代国家(现代政体、君主立宪，或科技进了电气时代，见 ExileRules.ModernEnough)在战争里丢掉最后一座城，
//     又找得到东道国(与占领者交战的、盟国、交情很好的，见 ExileRules.HostScore)。城易手之前(CityPatch.joinAnotherKingdom 的前置)
//     把元首、储君与要员带出城：原版城易手时城里的人全归新主，不先带出来元首就成了占领者的人；
//   · 流亡期间：没有城、没有兵，随员是无城之人(仍算本国的人，原版不把文明国家的人并进别的城；无小人模式的流民收拢也跳过无城之国)，
//     住在东道国都城外；外交关系、战争状态照旧；
//   · 待收复的旧城：最后丢的那座城，连同流亡时被占领者占着的本国法理之城(剧本开局流亡的，是认领地区里的城)；
//   · 还都：这些旧城落到东道国、盟国或正与占领者交战的国家手里(ExileRules.Liberator)，就交还给流亡政府；第一座交还的城作都城，
//     政府还都、随员入城；还都后 LiberationYears 年内盟军打回的旧城照样交还；
//   · 东道国亡了、与流亡政府交战、站到占领者一边：另投别国，找不到就解散(随员入籍东道国)；流亡满 ExpireYears 年还没还都也解散。
// 剧本可开局即流亡(大韩民国临时政府寄居中华民国，见 WorldMapPolity.exile 与 WorldMapScenario.BuildExiles)。
public static class ExileGovernmentSystem
{
    // 自己往回交城时不再触发流亡判定
    private static bool _restoring;

    public static bool IsExiled(Realm kingdom) =>
        kingdom?.Record != null && !kingdom.IsGone() && kingdom.GetOrCreate().exile_since >= 0d;

#if EC_PENDING
    public static Realm Host(Realm exile)
    {
        if (exile?.Record == null) return null;
        long id = exile.GetOrCreate().exile_host_id;
        Realm host = id < 0 ? null : EmpireWorld.Current?.kingdoms?.get(id);
        return host?.Record == null || host.IsGone() ? null : host;
    }

    private static Realm Enemy(Realm exile)
    {
        long id = exile?.Record == null ? -1L : exile.GetOrCreate().exile_enemy_id;
        Realm enemy = id < 0 ? null : EmpireWorld.Current?.kingdoms?.get(id);
        return enemy?.Record == null || enemy.IsGone() ? null : enemy;
    }
#endif

    #region 关系判定

    // 一国所在的一方：帝国按中央；殖民地按宗主；傀儡按主国
    private static Realm ModRealm(Realm kingdom)
    {
        if (kingdom?.Record == null) return null;
        Realm root = PuppetStateSystem.Master(kingdom) ?? ColonySystem.MetropoleOf(kingdom) ?? kingdom;
        Empire empire = root.GetEmpire();
        return empire?.CoreKingdom != null && !empire.IsGone() ? empire.CoreKingdom : root;
    }

    private static bool AtWar(Realm a, Realm b)
    {
        if (a?.Record == null || b?.Record == null || a == b) return false;
        if (a.IsAtWarWith(b)) return true;
        Realm ra = ModRealm(a), rb = ModRealm(b);
        return ra != null && rb != null && ra != rb && ra.IsAtWarWith(rb);
    }

#if EC_PENDING
    private static bool Allied(Realm a, Realm b) =>
        a?.Record != null && b?.Record != null && a.hasAlliance() && b.hasAlliance() && a.getAlliance() == b.getAlliance();
#endif

    // 站在占领者一边：占领者本身、同一帝国、殖民地、傀儡、同盟
#if EC_PENDING
    private static bool SidesWith(Realm kingdom, Realm enemy)
    {
        if (kingdom?.Record == null || enemy?.Record == null) return false;
        if (kingdom == enemy || ModRealm(kingdom) == ModRealm(enemy)) return true;
        return Allied(kingdom, enemy) || Allied(ModRealm(kingdom), ModRealm(enemy));
    }

    private static int Opinion(Realm a, Realm b) => EmpireWorld.Current?.diplomacy?.getOpinion(a, b)?.total ?? 0;

    private static int HostScore(Realm candidate, Realm exile, Realm enemy) =>
        ExileRules.HostScore(enemy != null && AtWar(candidate, enemy), Allied(candidate, exile), Opinion(candidate, exile),
            AtWar(candidate, exile), enemy != null && SidesWith(candidate, enemy));
#endif

    private static bool CanHost(Realm candidate, Realm exile)
    {
        if (candidate?.Record == null || candidate.IsGone() || candidate.IsWild || !candidate.IsCivilized() || candidate == exile) return false;
        if (candidate.Capital == null || candidate.Capital.IsGone() || (candidate.Cities?.Count ?? 0) == 0) return false;
        // 东道国得自己做主：帝国中央或独立国；流亡政府、傀儡、殖民地不收留
        if (candidate.IsInEmpire() && !candidate.IsEmpire()) return false;
        return !IsExiled(candidate) && !PuppetStateSystem.IsPuppet(candidate) && !ColonySystem.IsColony(candidate);
    }

#if EC_PENDING
    internal static Realm PickHost(Realm exile, Realm enemy, Realm except = null)
    {
        Realm best = null;
        int bestScore = int.MinValue;
        double bestPower = -1d;
        foreach (Realm candidate in EmpireWorld.Current.LiveRealms)
        {
            if (candidate == except || !CanHost(candidate, exile)) continue;
            int score = HostScore(candidate, exile, enemy);
            if (!ExileRules.AcceptableHost(score)) continue;
            double power = candidate.GetNationalPower();
            if (score < bestScore || score == bestScore && power <= bestPower) continue;
            best = candidate;
            bestScore = score;
            bestPower = power;
        }
        return best;
    }
#endif

    #endregion

    #region 流亡


    // CityPatch.joinAnotherKingdom 的前置里、城真正易手之前调：这座城一走，本国就一座城都没有了
#if EC_PENDING
    public static void BeforeCityLeaves(City city, Realm loser, Realm taker, bool captured)
    {
        if (_restoring || city?.Record == null || loser?.Record == null || taker?.Record == null || loser == taker) return;
        try
        {
            if (loser.IsWild || !loser.IsCivilized() || loser.Cities == null || loser.Cities.Count != 1 || loser.Cities[0] != city) return;
            if (IsExiled(loser) || PuppetStateSystem.IsPuppet(loser) || ColonySystem.IsColony(loser) || !loser.HasRuler()) return;
            // 战争里丢的：攻占，或与接手的一方正在交战
            if (!captured && !AtWar(loser, taker)) return;
            // 帝国的成员、行政区不流亡(城归了谁是帝国里的事)；帝国中央连同成员一座城都不剩才算
            if (loser.IsInEmpire() && !loser.IsEmpire()) return;
            Empire empire = loser.GetEmpire();
            if (empire?.kingdoms_list != null && empire.kingdoms_list.Any(member => member != null && member != loser && !member.IsGone() &&
                                                                                    (member.Cities?.Count ?? 0) > 0)) return;
            // 造反的、流寇不流亡
            if (loser.IsFactionRebelling() || loser.IsLocalRebelling() || ScenarioEventSystem.Roving(loser)) return;
            if (!StateModernization.IsModernPower(loser)) return;
            Realm enemy = ModRealm(taker);
            Realm host = PickHost(loser, enemy);
            if (host == null) return;
            var lost = new List<City> { city };
            // 待收复：流亡时被占领者一边占着的本国法理之城
            HashSet<long> titles = new HashSet<long>((loser.GetControlledTitle() ?? new List<KingdomTitle>())
                .Concat(new[] { loser.GetMainTitle(), city.GetTitle() }).Where(title => title?.data != null).Select(title => title.data.id));
            foreach (City other in EmpireWorld.Current.Cities)
            {
                if (other?.Record == null || other.IsGone() || other == city || other.Realm == null) continue;
                KingdomTitle title = other.GetTitle();
                if (title?.data != null && titles.Contains(title.data.id) && SidesWith(other.Realm, enemy)) lost.Add(other);
            }
            Begin(loser, host, enemy, lost.Select(entry => entry.Key), city);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][流亡政府] {loser?.data?.name} 流亡失败: {exception.Message}");
        }
    }
#endif

    // 开始流亡：记下东道国、占领者、待收复的旧城，随员出城去东道国都城。leaving 是正在丢的城(剧本开局流亡的为空，quiet 不记史书)
#if EC_PENDING
    public static void Begin(Realm exile, Realm host, Realm enemy, IEnumerable<long> claims, City leaving, bool quiet = false)
    {
        KingdomExtension.KingdomExtraData data = exile.GetOrCreate();
        data.exile_host_id = host.Key;
        data.exile_since = GameClock.Now;
        data.exile_enemy_id = enemy?.data?.id ?? -1L;
        data.exile_returned_at = -1d;
        data.exile_city_ids = (data.exile_city_ids ?? new List<long>()).Concat(claims ?? Enumerable.Empty<long>()).Distinct().ToList();
        Evacuate(exile, leaving, host);
        if (quiet) return;
        string text = string.Format(LM.Get("exile_begin_history"), exile.GetKingdomName(), host.GetKingdomName(),
            host.Capital?.GetCityName() ?? host.GetKingdomName());
        EventRecorder.Record(exile, text, exile.Ruler);
        TranslateHelper.LogEventMessage(text, host);
        LogService.LogInfo($"[EmpireCraft][流亡政府] {exile.GetKingdomName()} 流亡 {host.GetKingdomName()}，待收复 {data.exile_city_ids.Count} 城");
    }
#endif

    // 随员：元首、储君、元首的家人，再按声望取本国的要人(官员、名将名臣……)，至多 MaxEntourage 人
#if EC_PENDING
    private static List<Person> Entourage(Realm exile)
    {
        Person king = exile.Ruler, heir = exile.GetHeir();
        return exile.units.ToList()
            .Where(actor => actor?.data != null && !actor.IsGone() && actor.Alive && actor.asset?.civ == true && !actor.asset.is_boat &&
                            actor.kingdom == exile)
            .Select(actor => (actor, rank: actor == king ? 0 : actor == heir ? 1 :
                king?.Clan != null && actor.clan == king.Clan ? 2 : actor == king?.lover ? 2 :
                CityPopulationSystem.IsNotable(actor) ? 3 : 9))
            .Where(entry => entry.rank < 9)
            .OrderBy(entry => entry.rank).ThenByDescending(entry => entry.actor.renown)
            .Take(ExileRules.MaxEntourage).Select(entry => entry.actor).ToList();
    }

    private static void Evacuate(Realm exile, City leaving, Realm host)
    {
        WorldTile at = host?.capital?.getTile();
        BringRoyalKin(exile, leaving);
        foreach (Person actor in Entourage(exile))
        {
            try
            {
                // 离城不离国：无城之人仍算本国的人(原版 joinCity(null) 不改国家)
                if (actor.City != null) actor.joinCity(null);
                if (at != null) actor.setCurrentTilePosition(at);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][流亡政府] {actor.FullName()} 随行失败: {exception.Message}");
            }
        }
    }
#endif

    // 元首的子女、兄弟随行：纯净地图下多是虚拟族人，以前留在沦陷的城里(族谱记着那座城)，元首死了由外人接任。
    // 落在本国或刚丢的那座城的成年近亲落成，编进流亡政府，至多 RoyalKinFollow 人
    private const int RoyalKinFollow = 4;

#if EC_PENDING
    private static void BringRoyalKin(Realm exile, City leaving)
    {
        PersonalClanIdentity ruler = exile?.king?.GetPersonalIdentity();
        if (ruler == null) return;
        var kin = KinRoster.Children(ruler).Concat(KinRoster.Siblings(ruler))
            .Where(person => KinRoster.Virtual(person) && person.age >= KinRoster.AdultAge && !person.is_concubine &&
                             (KinRoster.KingdomOf(person) == exile || leaving != null && KinRoster.CityOf(person) == leaving))
            .OrderBy(person => person.rank).Take(RoyalKinFollow).ToList();
        foreach (Person actor in KinRoster.Realize(kin, actor => actor.Alive && !actor.IsRuler()))
        {
            try
            {
                if (actor.Realm != exile) actor.setKingdom(exile);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][流亡政府] {actor.FullName()} 随行失败: {exception.Message}");
            }
        }
    }
#endif

    #endregion

    #region 每月

    private static object _world;
    private static double _lastMonth = -1d;

    public static void ResetWorldState()
    {
        _world = null;
        _lastMonth = -1d;
    }

#if EC_PENDING
    public static void Tick()
    {
        EmpireWorld world = EmpireWorld.Current;
        if (world?.LiveRealms == null || ModClass.IS_CLEAR) return;
        if (!ReferenceEquals(_world, world))
        {
            _world = world;
            _lastMonth = -1d;
        }
        double now = world.Now();
        if (_lastMonth >= 0d && now >= _lastMonth && GameClock.MonthsSince(_lastMonth) < 1) return;
        _lastMonth = now;
        foreach (Realm kingdom in world.LiveRealms.ToList())
        {
            if (kingdom?.Record == null || kingdom.IsGone()) continue;
            KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
            try
            {
                if (data.exile_since >= 0d) Upkeep(kingdom, data);
                else if (data.exile_returned_at >= 0d)
                {
                    // 还都后的收复期：盟军打回来的旧城照样交还；期满不再记
                    if (ExileRules.InLiberationWindow(GameClock.YearsSince(data.exile_returned_at))) Liberate(kingdom, data);
                    else
                    {
                        data.exile_returned_at = -1d;
                        data.exile_city_ids = null;
                    }
                }
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][流亡政府] {kingdom.Name} 月结失败: {exception.Message}");
            }
        }
    }

    private static void Upkeep(Realm exile, KingdomExtension.KingdomExtraData data)
    {
        // 别的途径拿回了城(剧情献城、自己打回来)：就此还都
        if ((exile.Cities?.Count ?? 0) > 0)
        {
            Return(exile, data, exile.Capital ?? exile.Cities[0]);
            return;
        }
        if (!exile.HasRuler() && !Succeed(exile))
        {
            Dissolve(exile, data, "exile_dissolved_leaderless_history");
            return;
        }
        if (ExileRules.Expired(GameClock.YearsSince(data.exile_since)))
        {
            Dissolve(exile, data, "exile_dissolved_expired_history");
            return;
        }
        Realm host = Host(exile), enemy = Enemy(exile);
        if (!CanHost(host, exile) || ExileRules.HostScore(false, false, 0, AtWar(host, exile), enemy != null && SidesWith(host, enemy)) == int.MinValue)
        {
            Realm next = PickHost(exile, enemy, host);
            if (next == null)
            {
                Dissolve(exile, data, "exile_dissolved_homeless_history");
                return;
            }
            data.exile_host_id = next.Key;
            Evacuate(exile, null, next);
            string text = string.Format(LM.Get("exile_move_history"), exile.GetKingdomName(), next.GetKingdomName(),
                next.Capital?.GetCityName() ?? next.GetKingdomName());
            EventRecorder.Record(exile, text, exile.Ruler);
            TranslateHelper.LogEventOnly(text, next);
        }
        Liberate(exile, data);
    }
#endif

    // 元首死了：随员里的储君、年长的要人接任
#if EC_PENDING
    private static bool Succeed(Realm exile)
    {
        SpecificClan royal = SpecificClanManager.getPerson(exile.GetOrCreate().last_ruler_identity_id)?._specificClan;
        Person heir = exile.GetHeir();
        Person next = heir != null && heir.Alive && heir.Realm == exile && heir.IsAdult() ? heir
            : exile.units.Where(actor => actor?.data != null && !actor.IsGone() && actor.Alive && actor.isAdult() &&
                                         actor.asset?.civ == true && !actor.asset.is_boat && actor.kingdom == exile)
                // 宗室在先(随行的近亲，见 BringRoyalKin)，以前只按声望，元首一死常由外人接任
                .OrderByDescending(actor => royal != null && SpecificClanManager.SameLineage(actor.GetSpecificClan(), royal))
                .ThenByDescending(actor => actor.renown).FirstOrDefault();
        if (next == null) return false;
        exile.SetRuler(next);
        return exile.HasRuler();
    }
#endif

    // 待收复的旧城落在东道国、盟国、与占领者交战的国家手里：交还
#if EC_PENDING
    private static void Liberate(Realm exile, KingdomExtension.KingdomExtraData data)
    {
        if (data.exile_city_ids == null || data.exile_city_ids.Count == 0) return;
        Realm host = Host(exile), enemy = Enemy(exile);
        var returned = new List<City>();
        foreach (long id in data.exile_city_ids.ToList())
        {
            City city = EmpireWorld.Current.CityByKey(id);
            if (city?.Record == null || city.IsGone()) continue;
            Realm holder = city.Realm;
            if (holder == null || holder == exile || holder.IsWild) continue;
            bool liberator = ExileRules.Liberator(holder == host || ModRealm(holder) == host, Allied(holder, exile),
                enemy != null && AtWar(holder, enemy), enemy != null && SidesWith(holder, enemy), AtWar(holder, exile));
            if (!liberator) continue;
            // 不夺人家的都城
            if (holder.Capital == city && (holder.Cities?.Count ?? 0) > 1) continue;
            returned.Add(city);
        }
        if (returned.Count == 0) return;
        _restoring = true;
        try
        {
            foreach (City city in returned)
            {
                Realm from = city.Realm;
                city.JoinRealm(exile);
                if (city.Realm == exile)
                    LogService.LogInfo($"[EmpireCraft][流亡政府] {from?.GetKingdomName()} 把 {city.GetCityName()} 交还 {exile.GetKingdomName()}");
            }
        }
        finally
        {
            _restoring = false;
        }
        City seat = returned.FirstOrDefault(city => city.Realm == exile);
        if (seat != null && IsExiled(exile)) Return(exile, data, seat);
    }
#endif

    // 还都：第一座交还的城作都城，随员入城
#if EC_PENDING
    private static void Return(Realm exile, KingdomExtension.KingdomExtraData data, City seat)
    {
        data.exile_since = -1d;
        data.exile_host_id = -1L;
        data.exile_returned_at = GameClock.Now;
        if (seat != null && seat.Realm == exile)
        {
            exile.SetCapital(seat);
            foreach (Person actor in exile.units.ToList())
                if (actor?.Record != null && !actor.IsGone() && actor.Alive && actor.City == null && actor.Realm == exile &&
                    actor.asset?.civ == true && !actor.asset.is_boat)
                    actor.joinCity(seat);
        }
        string text = string.Format(LM.Get("exile_return_history"), exile.GetKingdomName(), seat?.GetCityName() ?? "");
        EventRecorder.Record(exile, text, exile.Ruler);
        TranslateHelper.LogEventMessage(text, exile);
    }
#endif

    // 解散：随员入籍东道国(进东道国都城)，本国随之无人而亡
#if EC_PENDING
    private static void Dissolve(Realm exile, KingdomExtension.KingdomExtraData data, string historyKey)
    {
        Realm host = Host(exile);
        string text = string.Format(LM.Get(historyKey), exile.GetKingdomName(), host?.GetKingdomName() ?? "");
        EventRecorder.Record(exile, text, exile.Ruler);
        TranslateHelper.LogEventOnly(text, exile);
        data.exile_since = -1d;
        data.exile_host_id = -1L;
        data.exile_city_ids = null;
        City refuge = host?.Capital;
        if (refuge == null || refuge.IsGone()) return;
        foreach (Person actor in exile.units.ToList())
            if (actor?.Record != null && !actor.IsGone() && actor.Alive && actor.City == null && actor.asset?.civ == true && !actor.asset.is_boat)
                actor.joinCity(refuge);
    }
#endif

    #endregion
}
}
