// Generated from the EmpireCraft mod: Scripts/GeneralSystems/AdministrationReorganizationSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 改划仅操作本帝国城市。每帧最多处理一个城市，所有易主操作留在主线程。
public static class AdministrationReorganizationSystem
{
    private sealed class Region
    {
        public KingdomTitle Title;
        public List<City> Cities;
        public Realm Target;
        public int Cursor;
        // 法理里隔着长城另设的那片(见 FrontierWalls)：设封国时不带法理
        public bool Detached;
    }
    private sealed class Work
    {
        public Empire Empire;
        public Queue<Region> Regions;
        public HashSet<long> Protected;
        public Realm Core;
        public City Capital;
        public KingdomTitle MainTitle, CapitalTitle;
    }
    private static readonly Queue<Work> Pending = new();
    private static readonly HashSet<long> Active = new();
    public static bool IsRunning(Empire empire) => empire != null && Active.Contains(empire.Key);
    public static void ResetWorldState() { Pending.Clear(); Active.Clear(); }

    public static bool CanPropose(Person actor) => actor != null && !actor.IsGone() && actor.IsRuler() &&
        actor.Realm?.GetEmpire() is Empire empire && empire.Emperor == actor && actor.Realm == empire.CoreKingdom &&
        CanContinue(empire) && !empire.data.administrative_reorganization_pending;

    public static bool ShouldPropose(Person actor) => CanPropose(actor) &&
        // 历史剧本不每五年自动把各道、省改成通用法理分区；玩家仍可通过 CanPropose 明确发动重组。
        !WorldMapWilderness.FixedLayout &&
        !WorldMapWilderness.HasPreservedAdministration(actor.Realm.GetEmpire()) &&
        (actor.Realm.GetEmpire().data.last_administrative_reorganization_timestamp < 0 ||
         GameClock.YearsSince(actor.Realm.GetEmpire().data.last_administrative_reorganization_timestamp) >= 5);

    public static bool CanContinue(Empire empire) => empire?.data != null && !empire.IsGone() &&
        !empire.IsArchived() && empire.CoreKingdom != null && !empire.CoreKingdom.IsGone() &&
        empire.Legitimacy > 80 && !empire.HasTerritorialWar() && !ZhouEraSystem.RoyalReclaimBlocked(empire) &&
        !AncientWarfareCompatibility.Owns(empire.CoreKingdom);

#if EC_PENDING
    public static bool Begin(Person actor)
    {
        if (!CanPropose(actor)) return false;
        Empire empire = actor.Realm.GetEmpire();
        empire.data.administrative_reorganization_pending = true;
        empire.data.administrative_reorganization_explicit = true;
        Resume(empire);
        if (!Active.Contains(empire.Key)) { empire.data.administrative_reorganization_pending = false; return false; }
        empire.data.last_administrative_reorganization_timestamp = GameClock.Now;
        return true;
    }

    public static void Resume(Empire empire)
    {
        if (empire?.data?.administrative_reorganization_pending != true || Active.Contains(empire.Key) ||
            Pending.Count >= 8 || !CanContinue(empire)) return;
        if (!Requested(empire))
        {
            empire.data.administrative_reorganization_pending = false;
            return;
        }
        // 长城作行政区的天然界线(见 FrontierWalls)：一个法理里隔着长城的城分成几片，各设各的行政区(共用这个法理)，法理首府那片在前
        var groups = empire.kingdoms_list.Where(kingdom => Eligible(empire, kingdom))
            .SelectMany(kingdom => kingdom.Cities ?? new List<City>())
            .Where(city => city != null && !city.IsGone() && city.hasTitle() && city.GetTitle()?.IsGone() == false)
            .Distinct().GroupBy(city => city.GetTitle()).OrderBy(group => group.Key.Key)
            .SelectMany(group => FrontierWalls.Split(group.OrderBy(city => city == group.Key.title_capital ? 0 : 1).ThenBy(city => city.Key))
                .Select((cities, part) => new Region { Title = group.Key, Cities = cities, Detached = part > 0 }));
        var work = new Work { Empire = empire, Regions = new Queue<Region>(groups) };
        RefreshProtected(work);
        Pending.Enqueue(work);
        Active.Add(empire.Key);
    }
#endif

    private static bool Requested(Empire empire) =>
        !WorldMapWilderness.FixedLayout || empire.data.administrative_reorganization_explicit;

    // 剧本的地方实力派(晋绥、桂系……)自成一块，不拿来改划(见 ScenarioModernPolitics.MakeWarlordMember)
    private static bool Eligible(Empire empire, Realm kingdom) => kingdom != null && !kingdom.IsGone() &&
        kingdom.GetEmpire() == empire && kingdom.Cities?.Count > 0 && !AncientWarfareCompatibility.Owns(kingdom) &&
        !kingdom.GetOrCreate().scenario_autonomy;

    private static void RefreshProtected(Work work)
    {
        Realm core = work.Empire.CoreKingdom;
        City capital = core.Capital;
        KingdomTitle mainTitle = core.GetMainTitle(), capitalTitle = capital?.GetTitle();
        if (work.Protected != null && work.Core == core && work.Capital == capital &&
            work.MainTitle == mainTitle && work.CapitalTitle == capitalTitle) return;
        work.Core = core; work.Capital = capital;
        work.MainTitle = mainTitle; work.CapitalTitle = capitalTitle;
        work.Protected = work.Empire.GetProtectedDirectCityIds();
    }

#if EC_PENDING
    public static void Tick()
    {
        if (Pending.Count == 0 || !SimulationFrameBudget.HasTime) return;
        Work work = Pending.Dequeue();
        Empire empire = work.Empire;
        if (!CanContinue(empire))
        {
            // 战争和正统不足时暂停；存档中的标记在条件恢复后重新规划。
            Active.Remove(empire.Key);
            return;
        }
        RefreshProtected(work);
        if (work.Regions.Count == 0)
        {
            empire.data.administrative_reorganization_pending = false;
            empire.data.administrative_reorganization_explicit = false;
            Active.Remove(empire.Key);
            empire.RecordHistory(directContent: LM.Get("administration_reorganization_completed"));
            return;
        }
        Region region = work.Regions.Peek();
        if (region.Title == null || region.Title.IsGone() || region.Cursor >= region.Cities.Count)
        {
            if (Eligible(empire, region.Target))
            {
                if (region.Target.IsAdministrativeKingdomType()) region.Target.SetAdministrativeTitle(region.Title);
                empire.SynchronizeLandedLegalTitles(region.Target);
                EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(region.Target);
            }
            region.Title?.RefreshAdministrativeDivisionNames();
            work.Regions.Dequeue();
            Pending.Enqueue(work);
            return;
        }
        City city = region.Cities[region.Cursor++];
        if (city == null || city.IsGone() || city.GetTitle() != region.Title || !Eligible(empire, city.Realm))
        { Pending.Enqueue(work); return; }

        // 王畿、开国法理和现首都法理归核心直辖，核心首都始终不会被转出。
        // Revalidate capital/title changes while the work is spread over frames.
        // Keep saved title-list protections even if old city/title pointers disagree.
        City capital = empire.CoreKingdom.Capital;
        // 现代政体：直辖市只留首都一城，王畿/开国法理等原直辖区照法理划成正常行政区；
        // 古代照旧：王畿、开国法理和现首都法理归核心直辖
        bool modern = empire.CoreKingdom.GetRegime()?.type == RegimeType.Modern;
        bool protectedCity = modern
            ? city == capital
            : work.Protected.Contains(city.Key) || city == capital || region.Title == empire.CoreKingdom.GetMainTitle() ||
              region.Title == capital?.GetTitle();
        if (protectedCity) region.Target = empire.CoreKingdom;
        else
        {
            if (region.Target == empire.CoreKingdom) region.Target = null;
            // 治所在长城那边的行政区不归(这一片里的城本就互不隔墙，治所可能是别片的城)
            if (Eligible(empire, region.Target) && FrontierWalls.Separates(city, region.Target.Capital)) region.Target = null;
            if (!Eligible(empire, region.Target))
            {
                region.Target = empire.kingdoms_list.Where(kingdom => Eligible(empire, kingdom) &&
                    kingdom != empire.CoreKingdom && kingdom.Capital?.Realm == kingdom &&
                    kingdom.Capital.GetTitle() == region.Title && !FrontierWalls.Separates(city, kingdom.Capital))
                    .OrderBy(kingdom => kingdom.GetMainTitle() == region.Title ? 0 : 1).ThenBy(kingdom => kingdom.Key)
                    .FirstOrDefault();
                if (region.Target == null)
                {
                    if (city.Realm != empire.CoreKingdom) city.JoinRealm(empire.CoreKingdom);
                    // 行政区要有人主政：无小人模式下城里多半只有背景人口，先补城主(同 WorldMapScenario.Divide)——以前设不成，
                    // 唐的河西诸州重组时从陇右道划出来就一直留在京畿道直辖
                    CityPopulationSystem.EnsureLeader(city);
                    region.Target = empire.EstablishTerritorialDivision(new List<City> { city }, region.Title, region.Detached);
                    // 尚无合格长官时留下待办，下一轮战略检查可重新尝试。
                    if (region.Target == null) { Active.Remove(empire.Key); return; }
                }
            }
        }
        if (city.Realm != region.Target && city != empire.CoreKingdom.Capital && !WartimeCustodySystem.InCustody(city))
            city.JoinRealm(region.Target);
        Pending.Enqueue(work);
    }
#endif
}
}
