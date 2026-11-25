// Generated from the EmpireCraft mod: Scripts/GeneralSystems/EmpirePartitionSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using Color = EmpireCraft.Core.EcColor;
using Vector2 = EmpireCraft.Core.EcVector2;
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace EmpireCraft.Core
{

// 帝国分裂为两朝(东西或南北，按两朝都城的相对方位)：
//   ① 遗诏分治(东西罗马式)：疆域辽阔的大帝国，年过五十的皇帝有两个以上成年儿子时，可能诏分天下为二——
//      储君留守京城这一半，另一子在另一半的大城(受封的皇子优先用自己的封国)称帝。两朝同一国号加方位、同一宗室，
//      起初是兄弟之国：互不征伐，一方遭外敌攻打另一方出兵相助；四十年后(或一方被外姓篡夺)情谊渐疏，互争正统，
//      强的一方可以发动统一之战，胜者并吞另一朝；一方皇族绝嗣，群臣可以迎立另一朝的皇帝兼承大统，天下复归一统。
//   ② 权臣分立(东西魏式)：权臣挟制皇帝，远方另有手握重兵、不与权臣同派的强藩——或皇帝不堪挟制出奔强藩、
//      权臣在京城另立宗室；或强藩在自己的地盘拥立一位宗室。两朝同一国号加方位，各由权臣把持，互争正统、开战。
//   ③ 宗室另立(篡位后)：外姓篡位时，先朝宗室有一定可能(看原来的法统)在离篡位者都城最远的那一半起兵称帝，
//      延续旧国号加方位；已经叛离篡位者的宗室诸王归附。互争正统、开战。下个月起事。
//      先朝的王朝积弊、六弊按篡位前的减半承接，王朝年岁从头算(用户 2026-10-26 改朝换代三档，见 DynasticCycleSystem)；
//      ①② 两朝同一王朝，积弊、六弊、王朝年岁原样承接。
//      归大分裂冷却管(见 UpheavalGuard，用户 2026-10-21 选)：上次大分裂 15 年内、土崩瓦解收束期内不另立；
//      一次至多带走篡位帝国三分之一的城(见 Trim)。
// 划分：以两朝都城为两极，成员国按自己的都城离哪边近整国划走，京畿(核心王国)的城逐城划分。
// 玩家反馈(2026-10-16)："并立的概率调低点，下调大概一半""并立之后很容易分不清""分裂出来的那个就全靠他一个人，
//   按理来说会有一部分宗室追随他……很容易断代，然后直接被吞并"：三种分立的概率都减半；另一朝换一个和原朝相差最大的颜色；
//   宗室随新君而去(见 Followers)——新君的妻妾子孙全随行，兄弟一家各一半，留在原朝京城的宗室三成投奔，
//   在新朝疆域里的宗室本就归新朝。
// 玩家反馈："小皇帝跑路了就自己找出来一个皇叔……那个皇叔也是乖乖的把大权拱手相让""小皇帝跑到地方了以后也会立刻找一个
//   有能力的大臣让他当权臣""做成皇帝成年以后再出奔吧""出奔弄三种随机结果：成功跑路(并立)、反杀权臣(还政)、被废黜"：
//   · 皇帝年满二十才谋出奔；出奔三种结局(见 FleeOutcome)：成功出奔两朝并立、反杀权臣亲政、事泄被废(宗室无人可立就天下散伙)；
//   · 出奔成功，强藩起初只是辅政(控制三成)，庸弱昏聩之君才一来就被挟制；
//   · 权臣立的傀儡先挑京城里现成的宗室，再挑年幼的(五到十五岁)，不再随手从族谱里拉出一位成年皇叔。
public static class EmpirePartitionSystem
{
    public const string KindWill = "will", KindMinister = "minister", KindRestoration = "restoration";
    private const int AmityYears = 40, CooldownYears = 30, WillMinCities = 14, WillMinAge = 50, PendingMonths = 1;
    private const float WillBaseChance = 0.01f, MinisterChance = 0.06f, WillSpanShare = 0.3f, StrongmanDistanceShare = 0.3f,
        StrongmanMightShare = 0.15f;

    // 篡位后待起事的先朝宗室(帝国 id → 先朝宗族、旧国号、篡位时间、篡位前先朝的积弊家底)
    private static readonly Dictionary<long, (long clan, string name, double at, DynasticCycleSystem.Legacy legacy)> Restorations = new();

    #region 查询

    public static Empire Twin(Empire empire)
    {
        long id = empire?.data?.partition_twin_id ?? -1L;
        if (id <= 0 || ModClass.EMPIRE_MANAGER == null) return null;
        Empire twin = ModClass.EMPIRE_MANAGER.Get(id);
        return twin?.data == null || twin.IsGone() || twin.IsArchived() || twin.data.partition_twin_id != empire.data.id ? null : twin;
    }

    private static Empire RealmOf(Realm kingdom) =>
        kingdom == null ? null : kingdom.IsEmpire() || kingdom.IsInEmpire() ? kingdom.GetEmpire() : null;

    // 兄弟之国(遗诏分治后的四十年)：两朝之间不开战
    public static bool InAmity(Realm first, Realm second)
    {
        Empire a = RealmOf(first), b = RealmOf(second);
        return a != null && b != null && a != b && a.data.partition_amity && Twin(a) == b;
    }

    // 天命阶段的分裂倍数：治世 ×0.25、动荡 ×1、乱世(天下无主；都城不在任何天下里也记为乱世) ×1.5
    private static float PhaseFactor(Realm core) => TianxiaSystem.PhaseOf(core) switch
    {
        TianxiaSystem.Peace => 0.25f,
        TianxiaSystem.Chaos => 1.5f,
        _ => 1f
    };

#if EC_PENDING
    private static bool Eligible(Empire empire)
    {
        Realm core = empire?.CoreKingdom;
        // 元首制(古典共和施行元首制，元首家传)同君主制可分治(用户 2026-10-20 定"罗马元首制也会分裂")，倍数读古典共和配置(×1)
        if (core == null || core.IsGone() || core.Capital == null ||
            !SuccessionCouncilSystem.Applies(core) && !empire.IsPrincipate()) return false;
        // 穿越政权不分东西两朝(元老院一个，见 TransmigrantSystem.Governs)
        if (TransmigrantSystem.Governs(empire)) return false;
        // 开国气象(统一战争期间、统一后三十年)不分治、不分立(见 UnificationSystem)
        if (UnificationSystem.InGrace(core)) return false;
        if (Twin(empire) != null) return false;
        double since = empire.data.partition_time;
        return since < 0d || GameClock.YearsSince(since) >= CooldownYears;
    }
#endif

    private static float Span(IEnumerable<City> cities, out bool horizontal)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (City city in cities)
        {
            if (city?.Record == null) continue;
            Vector2 at = city.Center;
            minX = Mathf.Min(minX, at.x);
            maxX = Mathf.Max(maxX, at.x);
            minY = Mathf.Min(minY, at.y);
            maxY = Mathf.Max(maxY, at.y);
        }
        horizontal = maxX - minX >= maxY - minY;
        return minX > maxX ? 0f : Mathf.Max(maxX - minX, maxY - minY);
    }

    private static List<City> Cities(Empire empire) =>
        empire.kingdoms_list.Where(kingdom => kingdom != null && !kingdom.IsGone())
            .SelectMany(kingdom => kingdom.Cities ?? new List<City>())
            .Where(city => city?.Record != null && !city.IsGone()).Distinct().ToList();

    private static float Might(Realm kingdom) =>
        kingdom == null || kingdom.IsGone() ? 0f
            : kingdom.Warriors() + (kingdom.Cities ?? new List<City>()).Sum(city => CorpsSystem.EquivalentAt(city));

    // 两朝都城的相对方位：东西相距远就分东西，南北相距远就分南北
    private static string Side(Vector2 seat, Vector2 capital)
    {
        float dx = seat.x - capital.x, dy = seat.y - capital.y;
        if (Mathf.Abs(dx) >= Mathf.Abs(dy)) return dx > 0f ? "Eastern" : "Western";
        return dy > 0f ? "Northern" : "Southern";
    }

    private static string Opposite(string side) => side switch
    {
        "Eastern" => "Western",
        "Western" => "Eastern",
        "Northern" => "Southern",
        _ => "Northern"
    };

    #endregion

    #region 划分

    private sealed class Plan
    {
        public List<Realm> Members = new();
        public List<City> CoreCities = new();
        public int Moved, Total;
        public float Share => Total <= 0 ? 0f : (float)Moved / Total;
    }

    // 以京城、新都为两极：离新都近的成员国整国划走，京畿的城逐城划分
    private static Plan Divide(Empire source, City capital, City seat)
    {
        var plan = new Plan();
        Realm core = source.CoreKingdom;
        Vector2 a = capital.Center, b = seat.Center;
        bool Nearer(City city) => city != null && (city == seat ||
                                                   (city.Center - b).sqrMagnitude < (city.Center - a).sqrMagnitude);
        foreach (Realm kingdom in source.kingdoms_list.ToList())
        {
            if (kingdom == null || kingdom.IsGone() || kingdom == core) continue;
            int count = kingdom.Cities?.Count ?? 0;
            plan.Total += count;
            City anchor = kingdom.Capital ?? kingdom.Cities?.FirstOrDefault();
            if (anchor == null || !Nearer(anchor)) continue;
            plan.Members.Add(kingdom);
            plan.Moved += count;
        }
        foreach (City city in core.Cities?.ToList() ?? new List<City>())
        {
            if (city?.Record == null || city.IsGone()) continue;
            plan.Total++;
            if (city == capital || !Nearer(city)) continue;
            plan.CoreCities.Add(city);
            plan.Moved++;
        }
        if (seat.Realm != null && seat.Realm != core && !plan.Members.Contains(seat.Realm)) plan.Moved++;
        return plan;
    }

    // 新都：离京城最远的那一部分城里最大的(地块最多)，且划分后新朝占的比例在给定范围内
    private static City FarSeat(Empire source, Vector2 from, float minShare, float maxShare, out Plan plan)
    {
        plan = null;
        City capital = source.CoreKingdom.Capital;
        List<City> cities = Cities(source).Where(city => city != capital).ToList();
        if (cities.Count < 3) return null;
        foreach (City seat in cities.OrderByDescending(city => (city.Center - from).sqrMagnitude)
                     .Take(Mathf.Max(3, cities.Count / 2))
                     .OrderByDescending(city => city.Zones?.Count ?? 0))
        {
            Plan candidate = Divide(source, capital, seat);
            if (candidate.Share < minShare || candidate.Share > maxShare) continue;
            plan = candidate;
            return seat;
        }
        return null;
    }

    #endregion

    #region 分立

    // 在 seat 立 ruler 为另一朝皇帝；ownKingdom 不为空时(受封皇子、宗室诸王)以他自己的王国为新朝核心
#if EC_PENDING
    private static Empire Split(Empire source, Person ruler, City seat, Plan plan, string kind, string note,
        string dynastyName, bool renameSource, Realm ownKingdom = null)
    {
        Realm sourceCore = source.CoreKingdom;
        City capital = sourceCore?.Capital;
        if (ruler?.Record == null || ruler.IsGone() || !ruler.Alive || seat?.Record == null || capital == null) return null;
        // 不许自立(见 SecessionGate)：分立先动了君主、城主，在这里先问
        if (SecessionGate.Forbids("东西分立", source.GetEmpireFullName())) return null;
        string side = Side(seat.Center, capital.Center);
        Realm newCore;
        if (ownKingdom != null)
        {
            newCore = ownKingdom;
            plan.Members.Remove(newCore);
            if (newCore.IsInEmpire()) newCore.GetEmpire()?.leave(newCore, isLeave: false);
        }
        else
        {
            // 原来是别国君主、城主的先卸下
            if (ruler.IsRuler() && ruler.Realm != null) ruler.Realm.RemoveRuler();
            if (ruler.IsCityLeader() && ruler.City != null) ruler.City.RemoveLeader();
            ruler.removeFromArmy();
            // 新都所在的成员国：新都那座城自立为新朝核心，其余的城仍按划分随成员国走
            plan.CoreCities.Remove(seat);
            newCore = seat.MakeOwnRealm(ruler);
            if (newCore == null) return null;
            newCore.SetCapital(seat);
            ruler.JoinCity(seat);
        }
        newCore.SetRegimeType(sourceCore.GetOrCreate().regimeType);
        newCore.LoadRegime();
        newCore.SetSuccessionLaw(sourceCore.GetSuccessionLaw());
        PrinceStruggleSystem.Note(newCore, ruler, note);
        Empire twin = ModClass.EMPIRE_MANAGER.NewEmpire(newCore, isSplit: true, allowCultureRival: true, forceNewCore: true,
            suppressFoundingLog: true);
        if (twin?.data == null)
        {
            LogService.LogWarning($"[EmpireCraft][分裂] {source.GetEmpireFullName()} 分立失败：{newCore.Name} 未能称帝");
            return null;
        }
        // 一片地一个帝国法理：分立的一朝另立的法理只是与原朝并立时的伪法理(见 RealmNamingSystem.MarkProvisionalCore)
        RealmNamingSystem.MarkProvisionalCore(twin, source);
        Inherit(source, twin, ruler);
        foreach (Realm kingdom in plan.Members)
            if (kingdom != null && !kingdom.IsGone() && kingdom.GetEmpire() == source)
                twin.join(kingdom, pRecalc: false, pForce: true, pLegitimacyTransfer: true);
        foreach (City city in plan.CoreCities)
            if (city?.Record != null && !city.IsGone() && city.Realm == sourceCore) city.JoinRealm(newCore);
        // 国号：同一国号，各加方位
        string name = string.IsNullOrWhiteSpace(dynastyName) ? source.data.core_name : dynastyName;
        twin.data.directPre = side;
        twin.SetEmpireName(name);
        if (renameSource)
        {
            source.data.directPre = Opposite(side);
            source.SetEmpireName(source.data.core_name);
        }
        double now = GameClock.Now;
        twin.data.partition_twin_id = source.data.id;
        source.data.partition_twin_id = twin.data.id;
        twin.data.partition_kind = source.data.partition_kind = kind;
        twin.data.partition_time = source.data.partition_time = now;
        twin.data.partition_claimant = true;
        source.data.partition_claimant = false;
        DistinctColor(source, twin);
        Followers(source, twin, ruler, seat);
        // 世家择主(见 GentrySystem)
        GentrySystem.ChooseSides(new[] { source, twin });
        twin.recalculate();
        source.recalculate();
        EmpireWorld.Current.Zones?.MarkDirty();
        return twin;
    }
#endif

    // 另一朝换一个和原朝相差最大的颜色(以前新朝多半沿用受封王国的颜色，跟原朝同一色系，地图上分不清)
#if EC_PENDING
    private static void DistinctColor(Empire source, Empire twin)
    {
        try
        {
            List<Tint> colors = AssetManager.kingdom_colors_library?.list;
            Tint own = source.GetColor();
            Realm core = twin.CoreKingdom;
            if (colors == null || colors.Count == 0 || own == null || core == null) return;
            Color main = own.GetColorMain();
            var taken = new HashSet<Tint>(ModClass.EMPIRE_MANAGER.Where(empire => empire?.data != null && empire != twin &&
                !empire.IsGone()).Select(empire => empire.GetColor()).Where(color => color != null));
            float Distance(Tint color)
            {
                Color other = color.GetColorMain();
                return Mathf.Abs(other.r - main.r) + Mathf.Abs(other.g - main.g) + Mathf.Abs(other.b - main.b);
            }
            Tint best = colors.Where(color => color != null && !taken.Contains(color)).OrderByDescending(Distance)
                .FirstOrDefault() ?? colors.Where(color => color != null).OrderByDescending(Distance).First();
            core.updateColor(best);
            twin.updateColor(best);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][分裂] 换色失败: {exception.Message}");
        }
    }
#endif

    private const float SiblingFollowShare = 0.5f, CourtFollowShare = 0.3f;

    // 宗室随新君而去：妻妾子孙全随行，兄弟一家各一半，留在原朝京城的宗室三成；在新朝疆域里的本就归新朝
#if EC_PENDING
    private static void Followers(Empire source, Empire twin, Person ruler, City seat)
    {
        PersonalClanIdentity self = ruler?.GetPersonalIdentity();
        SpecificClan clan = ruler?.GetSpecificClan() ?? source.EmpireSpecificClan;
        if (self == null || clan == null || seat?.Record == null) return;
        Realm newCore = twin.CoreKingdom;
        City court = source.CoreKingdom?.Capital;
        var family = new HashSet<long>();
        void AddLine(PersonalClanIdentity person, int depth)
        {
            if (person == null || depth > 3 || !family.Add(person.id)) return;
            foreach (long child in person.children ?? new List<long>()) AddLine(SpecificClanManager.getPerson(child), depth + 1);
        }
        AddLine(self, 0);
        if (self.lover.identity > 0) family.Add(self.lover.identity);
        foreach ((long _, long concubine) in self.concubines ?? new List<(long, long)>()) family.Add(concubine);
        int moved = 0;
        foreach (PersonalClanIdentity person in clan.SnapshotPeople())
        {
            if (person == null || !person.is_alive || person.id == self.id) continue;
            Person actor = person.is_virtual ? null : person._actor;
            if (actor != null && (actor.IsRuler() || actor.IsEmperor() || actor.IsCityLeader() || actor.IsOnOffice())) continue;
            bool sibling = self.father > 0 && person.father == self.father || self.mother > 0 && person.mother == self.mother;
            long cityId = actor?.City?.Key ?? person.virtual_city_id;
            bool atCourt = court?.Record != null && cityId == court.Key;
            bool follows = family.Contains(person.id) ||
                           sibling && EcRandom.value < SiblingFollowShare ||
                           atCourt && EcRandom.value < CourtFollowShare;
            if (!follows) continue;
            if (sibling)
                foreach (long child in person.children ?? new List<long>()) family.Add(child);
            if (actor != null)
            {
                actor.JoinCity(seat);
                actor.SetRealm(newCore);
            }
            else person.virtual_city_id = seat.Key;
            moved++;
        }
        if (moved <= 0) return;
        string text = string.Format(LM.Get("partition_followers_history"), moved, ruler.FullName(), seat.GetCityName());
        twin.RecordHistory(directContent: text, actorId: ruler.Key);
        LogService.LogInfo("[EmpireCraft][分裂] " + text);
    }
#endif

    // 新朝承袭：史书、王号序数、制度、税率、国教、开国之君、法统
#if EC_PENDING
    private static void Inherit(Empire source, Empire twin, Person ruler)
    {
        twin.data.history.InsertRange(0, source.data.history ?? new List<EmpireCraftHistory>());
        twin.data.regnal_records.InsertRange(0, source.data.regnal_records ?? new List<RegnalRecord>());
        RegnalNameService.OnEmperorCrowned(twin, ruler, recompute: true);
        twin.data.dynasty_founder_actor_id = source.data.dynasty_founder_actor_id;
        twin.data.founder_actor_id = source.data.founder_actor_id;
        twin.data.founder_actor_name = source.data.founder_actor_name;
        twin.data.western_ordinal = source.data.western_ordinal;
        twin.data.TaxRate = source.data.TaxRate;
        twin.data.Religion = source.data.Religion;
        twin.data.pre_caliphate_faith = source.data.pre_caliphate_faith;
        twin.data.heir_type = source.data.heir_type;
        twin.data.long_posthumous = source.data.long_posthumous;
        twin.data.show_long_posthumous = source.data.show_long_posthumous;
        twin.data.ruling_culture = source.data.ruling_culture;
        twin.data.institutional_culture = source.data.institutional_culture;
        // 征服王朝分出来的新朝照旧是征服王朝(以前只抄两个文化，新朝融合阶段为"无"，复合帝国的一切都不生效)
        twin.data.conquest_founder_culture = source.data.conquest_founder_culture;
        twin.data.external_identity_culture = source.data.external_identity_culture;
        twin.data.military_tradition = source.data.military_tradition;
        twin.data.composite_integration = source.data.composite_integration;
        twin.data.composite_integration_stage = source.data.composite_integration_stage;
        twin.data.composite_route = source.data.composite_route;
        twin.data.nativist_revival_at = source.data.nativist_revival_at;
        twin.data.central_plains_legitimacy = source.data.central_plains_legitimacy;
        twin.data.ruling_tradition_legitimacy = source.data.ruling_tradition_legitimacy;
        twin.data.composite_identity_adopted_timestamp = source.data.composite_identity_adopted_timestamp;
        twin.data.Mandate = Mathf.Max(30, source.data.Mandate - 15);
        try
        {
            twin.data.institution_state = Clone(source.data.institution_state) ?? twin.data.institution_state;
            twin.data.constitutional_economy = Clone(source.data.constitutional_economy) ?? twin.data.constitutional_economy;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][分裂] 承袭制度失败: {exception.Message}");
        }
        // 王朝积弊随制度整份抄来(王朝年岁、满额照旧)；六弊以前不带，补上(改朝换代三档之"保留"，用户 2026-10-26)。
        // 宗室另立的一朝另按篡位前的家底减半(见 Restore)；新朝的换君、满额民变记录从头记
        DynasticCycleSystem.Inherit(twin, DynasticCycleSystem.Capture(source), DynasticCycleSystem.Transition.Keep);
    }
#endif

    private static T Clone<T>(T value) where T : class =>
        value == null ? null : JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));

#if EC_PENDING
    private static void Publish(Empire first, Empire second, string text, Person actor)
    {
        first?.RecordHistory(directContent: text, actorId: actor?.Key ?? -1L);
        second?.RecordHistory(directContent: text, actorId: actor?.Key ?? -1L);
        TranslateHelper.LogEventMessage(text, (first ?? second)?.CoreKingdom);
    }
#endif

    #endregion

    #region ④ 大汗东西分立、哈里发并立

    // 汗国大过汗庭管得到的(用户 2026-10-27 截图匈奴汗国横跨大半张图："游牧也太大了, 就没有什么法理控制上限吗"，
    // 问答选"换汗即东西分裂")：草原法理本身就大，汗国多半在自己的法理里，法理上限卡不住。新汗即位时(见 NomadCycleSystem.OnKhanSucceeded)，
    // 汗国的城(成员国按都城整国算，汗庭直辖的逐城算)有 KhanSplitShare 以上在汗庭(本国都城，不借成员都城)的控制范围之外，
    // 就按东西(或南北)分为两个汗国——像突厥东西分立、蒙古四大汗国：远方那一半由那边最大的兀鲁思之主称汗(没有就由宗室)，
    // 同一国号各加方位，兄弟之国四十年不互攻(同遗诏分治)。分出去的那一半占三成到六成；分治冷却、大分裂冷却里不分。
    // 哈里发并立(同日用户嫌"哈里发国也莫名其妙的大"，问答选"两样都做")：新哈里发即位(见 NomadCycleSystem.OnCaliphSucceeded)
    // 照同样的条件分，远方那一半由那边最大的埃米尔(苏丹)另立为哈里发——像后倭马亚、法蒂玛：以他自己的国号立国(宗室另立的才沿用原号加方位)，
    // 原朝不改名；两个哈里发互争正统，不是兄弟之国；开国气象里也照分
    private const float KhanSplitShare = 0.25f, KhanSplitMin = 0.3f, KhanSplitMax = 0.6f;
    private const int KhanSplitMinCities = 12, ScatterChunkCities = 4;

    // 汗国大过汗庭管得到的：草原碎成一地，不再分东西两个汗国(用户 2026-11-18 "理论上游牧是碎一地而不是东西南北分裂"
    // "现状是罗马没有游牧却有就很奇怪"，问答选"都改碎裂")。条件照旧(KhanSplitMinCities 城以上、KhanSplitShare 以上的城在汗庭
    // 控制范围之外、分立冷却与大分裂冷却)；汗庭管不到的兀鲁思各自为汗(同汗国崩解，见 NomadCycleSystem.Scatters)，
    // 汗庭直辖里管不到的城按连成一片交给当地首领各自立国；一次至多带走一半城(UpheavalGuard.HeroCapShare)，汗庭留着、不改名
#if EC_PENDING
    public static bool KhanateSplit(Empire empire) => NomadCycleSystem.IsKhanate(empire) && KhanateScatter(empire);

    private static bool KhanateScatter(Empire empire)
    {
        if (empire?.data == null || empire.IsGone() || empire.IsArchived() || EmpireWorld.Current == null) return false;
        Realm core = empire.CoreKingdom;
        City capital = core?.Capital;
        Person khan = empire.Emperor;
        if (capital == null || khan?.Record == null) return false;
        double since = empire.data.partition_time;
        if (since >= 0d && GameClock.YearsSince(since) < CooldownYears || UpheavalGuard.Blocked(empire, out _)) return false;
        int total = 0, far = 0;
        var remote = new List<Realm>();
        var farCore = new HashSet<City>();
        foreach (Realm kingdom in empire.kingdoms_list.ToList())
        {
            if (kingdom?.Record == null || kingdom.IsGone() || kingdom.Cities == null) continue;
            if (kingdom == core)
            {
                foreach (City city in core.Cities.ToList())
                {
                    total++;
                    if (city == capital || !AdministrativeControlSystem.BeyondCourt(core, city)) continue;
                    far++;
                    farCore.Add(city);
                }
                continue;
            }
            total += kingdom.Cities.Count;
            if (kingdom.Capital == null || !AdministrativeControlSystem.BeyondCourt(core, kingdom.Capital)) continue;
            far += kingdom.Cities.Count;
            remote.Add(kingdom);
        }
        if (total < KhanSplitMinCities || far < total * KhanSplitShare) return false;
        var budget = new UpheavalGuard.Budget(empire, UpheavalGuard.HeroCapShare);
        var gone = new List<Realm>();
        foreach (Realm member in remote.OrderByDescending(member => member.Cities.Count))
        {
            if (!NomadCycleSystem.Scatters(empire, member)) continue;
            int cities = member.Cities.Count;
            if (!budget.Fits(cities)) break;
            member.FinishedSelfPlot();
            empire.leave(member, true, false);
            if (member.IsInEmpire()) continue;
            EmpireCraft.Core.EmpireLawSystem.NoteSecession(member);
            EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(member);
            gone.Add(member);
            budget.Take(cities);
        }
        // 汗庭直辖里管不到的城：从离汗庭最远的起，挨着的至多 ScatterChunkCities 座一片，交给当地首领各自为汗
        // (以前整片连着的算一块，常比一次的上限还大，整片作罢，管不到的城一座没走)
        RegimeType? regime = core.GetRegime()?.type;
        Vector2 court = capital.Center;
        while (farCore.Count > 0 && !budget.Capped)
        {
            City seed = farCore.OrderByDescending(city => (city.Center - court).sqrMagnitude).First();
            var cluster = new List<City> { seed };
            farCore.Remove(seed);
            for (int i = 0; i < cluster.Count && cluster.Count < ScatterChunkCities; i++)
                foreach (City next in cluster[i].NeighbourCities?.ToList() ?? new List<City>())
                {
                    if (cluster.Count >= ScatterChunkCities) break;
                    if (next != null && farCore.Remove(next)) cluster.Add(next);
                }
            cluster = cluster.Where(city => city?.Record != null && !city.IsGone() && city.Realm == core).ToList();
            if (cluster.Count == 0) continue;
            if (!budget.Fits(cluster.Count)) continue;
            Realm local = AdministrativeControlSystem.ReleaseToLocal(core, cluster, _ => "independent", out _);
            if (local == null) continue;
            if (regime.HasValue && local.GetRegime()?.type != regime.Value)
            {
                local.SetRegimeType(regime.Value);
                local.LoadRegime();
            }
            EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(local);
            gone.Add(local);
            budget.Take(cluster.Count);
        }
        if (gone.Count == 0) return false;
        empire.data.partition_time = GameClock.Now;
        int remaining = UpheavalGuard.Cities(empire), taken = gone.Sum(kingdom => kingdom.Cities?.Count ?? 0);
        UpheavalGuard.Mark(empire, "汗国四分五裂", detail: $"走 {gone.Count} 部 {taken} 城，剩 {remaining}/{total} 城");
        string realm = empire.GetEmpireName();
        string names = string.Join(LM.Get("schism_name_separator"), gone.Take(4).Select(kingdom => kingdom.GetKingdomName()));
        if (gone.Count > 4) names = string.Format(LM.Get("khanate_collapse_more"), names, gone.Count);
        string content = string.Format(LM.Get("khanate_collapse_history"), realm, khan.FullName(), names, remaining);
        empire.RecordHistory(directContent: content);
        EmpireCraft.Core.TranslateHelper.LogMajor(content, core);
        LogService.LogInfo($"[EmpireCraft][汗国四分五裂] {realm} 新汗{khan.FullName()}即位，{far}/{total} 城在汗庭控制范围之外，" +
                           $"{string.Join("、", gone.Select(kingdom => $"{kingdom.GetKingdomName()}({kingdom.Cities?.Count ?? 0}城)"))}各自为汗，汗庭剩 {remaining} 城");
        return true;
    }

    public static bool CaliphateSplit(Empire empire) => EmirateSystem.Applies(empire) && CourtSplit(empire, rival: true);

    private static bool CourtSplit(Empire empire, bool rival)
    {
        if (empire?.data == null || empire.IsGone() || empire.IsArchived() || Twin(empire) != null) return false;
        Realm core = empire.CoreKingdom;
        City capital = core?.Capital;
        Person khan = empire.Emperor;
        if (capital == null || khan?.Record == null) return false;
        double since = empire.data.partition_time;
        if (since >= 0d && GameClock.YearsSince(since) < CooldownYears || UpheavalGuard.Blocked(empire, out _)) return false;
        int total = 0, far = 0;
        var remote = new List<Realm>();
        foreach (Realm kingdom in empire.kingdoms_list.ToList())
        {
            if (kingdom?.Record == null || kingdom.IsGone() || kingdom.Cities == null) continue;
            if (kingdom == core)
            {
                foreach (City city in core.Cities.ToList())
                {
                    total++;
                    if (city != capital && AdministrativeControlSystem.BeyondCourt(core, city)) far++;
                }
                continue;
            }
            total += kingdom.Cities.Count;
            if (kingdom.Capital == null || !AdministrativeControlSystem.BeyondCourt(core, kingdom.Capital)) continue;
            far += kingdom.Cities.Count;
            remote.Add(kingdom);
        }
        if (total < KhanSplitMinCities || far < total * KhanSplitShare) return false;
        Person ruler = null;
        City seat = null;
        Plan plan = null;
        Realm ownKingdom = null;
        // 远方最大的兀鲁思之主(埃米尔、苏丹)就地称汗(另立哈里发)；架空哈里发的苏丹本人不在此列
        Realm regentSeat = rival ? MilitaryRegency.Regent(empire)?.Realm : null;
        foreach (Realm member in remote.OrderByDescending(member => member.Cities.Count).ThenByDescending(Might))
        {
            Person lord = member.Ruler;
            if (lord?.Record == null || !lord.Alive || !lord.IsAdult() || member == regentSeat || member.IsFactionRebelling() ||
                member.IsLocalRebelling()) continue;
            Plan candidate = Divide(empire, capital, member.Capital);
            if (candidate.Share < KhanSplitMin || candidate.Share > KhanSplitMax) continue;
            (ruler, seat, plan, ownKingdom) = (lord, member.Capital, candidate, member);
            break;
        }
        if (ruler == null)
        {
            seat = FarSeat(empire, capital.Center, KhanSplitMin, KhanSplitMax, out plan);
            ruler = seat == null ? null : PickRoyal(empire, khan, preferYoung: false);
        }
        if (ruler == null || seat == null || plan == null) return false;
        string khanName = khan.FullName(), seatName = seat.GetCityName();
        // 另立的哈里发以自己的国号立国，原朝不改名
        string ownName = rival ? ownKingdom?.EnsureKingdomCoreName() : null;
        bool renamed = !string.IsNullOrWhiteSpace(ownName);
        int before = UpheavalGuard.Cities(empire);
        Empire twin = Split(empire, ruler, seat, plan, rival ? KindMinister : KindWill, PrinceStruggleSystem.NotePartition,
            renamed ? ownName : empire.data.core_name, renameSource: !rival, ownKingdom);
        if (twin == null) return false;
        // 另立哈里发记在自立的埃米尔身上(量刑天平的叛逆砝码)；汗国东西分立是兄弟之国，不记
        if (rival && ownKingdom != null) EmpireCraft.Core.EmpireLawSystem.NoteDefiance(ruler, true);
        if (renamed)
        {
            twin.data.directPre = "";
            twin.SetEmpireName(ownName);
        }
        int moved = before - UpheavalGuard.Cities(empire);
        if (rival)
        {
            empire.data.partition_amity = twin.data.partition_amity = false;
            ImperialLegitimacyChallengeService.DeclareDynasticRivals(twin, empire, war: false);
            UpheavalGuard.Mark(empire, "哈里发并立", detail: $"分走 {moved} 城");
        }
        else
        {
            empire.data.partition_amity = twin.data.partition_amity = true;
            UpheavalGuard.Mark(empire, "大汗东西分立", detail: $"分走 {moved} 城");
        }
        Publish(empire, twin, string.Format(LM.Get(rival ? DynastyNaming.CultureVariant(empire.CoreKingdom, "partition_caliphate_history") ?? "partition_caliphate_history" : "partition_khanate_history"), khanName,
            ruler.FullName(), seatName, empire.GetEmpireFullName(), twin.GetEmpireFullName()), khan);
        LogService.LogInfo($"[EmpireCraft][{(rival ? "哈里发并立" : "大汗分立")}] {empire.GetEmpireFullName()} 新君{khanName}即位，" +
                           $"{far}/{total} 城在都城控制范围之外，{ruler.FullName()} 据 {seatName} 另立为 {twin.GetEmpireFullName()}(分走 {moved} 城)");
        return true;
    }
#endif

    #endregion

    #region 每年、每月(RoyalHouseSystem.Tick)

#if EC_PENDING
    public static void Yearly(Empire empire)
    {
        if (empire?.data == null || empire.IsGone() || empire.IsArchived()) return;
        Empire twin = Twin(empire);
        if (twin == null)
        {
            if (empire.data.partition_twin_id > 0)
            {
                empire.data.partition_twin_id = -1L;
                empire.data.partition_amity = false;
            }
        }
        else if (empire.data.partition_amity) CheckAmity(empire, twin);
        TryWill(empire);
        TryMinister(empire);
    }

    public static void Monthly()
    {
        if (Restorations.Count == 0 || EmpireWorld.Current == null) return;
        foreach (var pair in Restorations.ToList())
        {
            if (GameClock.MonthsSince(pair.Value.at) < PendingMonths) continue;
            Restorations.Remove(pair.Key);
            try
            {
                Restore(ModClass.EMPIRE_MANAGER?.Get(pair.Key), pair.Value.clan, pair.Value.name, pair.Value.legacy);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][分裂] 宗室另立失败: {exception}");
            }
        }
    }
#endif

    #endregion

    #region ① 遗诏分治

#if EC_PENDING
    private static void TryWill(Empire empire)
    {
        if (!Eligible(empire)) return;
        Person emperor = empire.Emperor;
        Realm core = empire.CoreKingdom;
        if (emperor?.Record == null || !emperor.Alive || !emperor.IsAdult() || emperor.Age() < WillMinAge ||
            empire.data.is_been_controlled) return;
        List<City> cities = Cities(empire);
        if (cities.Count < WillMinCities) return;
        float span = Span(cities, out _);
        float mapSize = Mathf.Max(EmpireWorld.Current.Width, EmpireWorld.Current.Height);
        if (mapSize <= 0f || span < mapSize * WillSpanShare) return;
        bool allowFemale = PersonalUnionService.AllowsFemaleSuccession(core);
        bool Fit(Person child) => child?.Record != null && !child.IsGone() && child.Alive && child.IsAdult() &&
                                 (allowFemale || child.IsMale()) && !child.IsEmperor() && RealmOf(child.Realm) == empire;
        // 皇子连族谱里的虚拟族人一起数(纯净地图下皇子多不在地图上；以前只数地图上的儿子，几乎永远凑不够两个，
        // 罗马这样的大帝国从没遗诏分治过)；掷中了才把要用的落成实体
        PersonalClanIdentity emperorIdentity = emperor.GetPersonalIdentity();
        List<PersonalClanIdentity> virtualSons = emperorIdentity == null ? new List<PersonalClanIdentity>()
            : SpecificClanManager.getChildren(emperorIdentity).Select(pair => pair.Item2)
                .Where(person => person != null && person.is_alive && person.is_virtual &&
                                 (person._actor == null || person._actor.IsGone()) && person.age >= 18 &&
                                 (allowFemale || person.sex == Sex.Male))
                .Distinct().ToList();
        List<Person> sons = (emperor.GetChildren(false) ?? Enumerable.Empty<Person>()).Where(Fit).ToList();
        if (sons.Count + virtualSons.Count < 2) return;
        // 游牧不遗诏分治(用户 2026-11-18 "理论上游牧是碎一地而不是东西南北分裂")：汗国碎裂见 KhanateSplit
        if (NomadCycleSystem.IsKhanate(empire)) return;
        float chance = WillBaseChance;
        // 各政体的倍数读政体配置(partition_chance_factor)：律令 ×0.5、游牧 ×1.25(大汗死时已按兀鲁思分封，
        // 见 EnfeoffmentHelper.UlusPartition)、封建 ×1.5，其余 ×1
        chance *= RegimeManager.GetTemplate(core.GetRegime()?.type)?.partition_chance_factor ?? 1f;
        if (cities.Count >= 24) chance *= 1.5f;
        if (span >= mapSize * 0.45f) chance *= 1.5f;
        // 分裂集中在动荡、末世(用户 2026-10-17 选"以天命为准"：治世少分)
        chance *= PhaseFactor(core);
        // 积弊越重越容易分(六弊平均 50 时翻倍)；元首制没有权臣分立、绝嗣裂土那几条出路，分治再加一倍
        // (玩家反馈 2026-11-22 "罗马系国家太容易大一统"：罗马这样的大帝国几乎不分)
        chance *= 1f + MaladySystem.Average(empire) / 50f;
        if (empire.IsPrincipate()) chance *= 2f;
        chance *= RulerQualitySystem.Of(emperor) switch
        {
            RulerQualitySystem.Grade.Wise => 0.5f,
            RulerQualitySystem.Grade.Weak or RulerQualitySystem.Grade.Fatuous => 1.5f,
            _ => 1f
        };
        if (EcRandom.value >= chance) return;
        foreach (Person realized in VirtualGenealogySystem.RealizeAll(virtualSons, Fit))
            if (!sons.Contains(realized)) sons.Add(realized);
        if (sons.Count < 2) return;
        Person heir = core.HasHeir() ? core.GetHeir() : null;
        heir ??= sons.OrderByDescending(son => son.Age()).First();

        City capital = core.Capital;
        Person ruler = null;
        City seat = null;
        Plan plan = null;
        Realm ownKingdom = null;
        // 受封在远方的皇子以自己的封国称帝
        foreach (Person son in sons.Where(son => son != heir).OrderByDescending(son => son.Renown))
        {
            Realm fief = son.IsRuler() ? son.Realm : null;
            if (fief?.Capital == null || fief.GetEmpire() != empire || fief == core) continue;
            Plan candidate = Divide(empire, capital, fief.Capital);
            if (candidate.Share < 0.25f || candidate.Share > 0.6f) continue;
            (ruler, seat, plan, ownKingdom) = (son, fief.Capital, candidate, fief);
            break;
        }
        if (ruler == null)
        {
            ruler = sons.Where(son => son != heir && !son.IsRuler()).OrderByDescending(son => son.Renown).FirstOrDefault();
            seat = ruler == null ? null : FarSeat(empire, capital.Center, 0.25f, 0.6f, out plan);
        }
        if (ruler == null || seat == null || plan == null) return;
        string emperorName = emperor.FullName(), heirName = heir.FullName(), seatName = seat.GetCityName();
        Empire twin = Split(empire, ruler, seat, plan, KindWill, PrinceStruggleSystem.NotePartition, empire.data.core_name,
            renameSource: true, ownKingdom);
        if (twin == null) return;
        empire.data.partition_amity = twin.data.partition_amity = true;
        Publish(empire, twin, string.Format(LM.Get("partition_will_history"), emperorName, heirName, ruler.FullName(), seatName,
            empire.GetEmpireFullName(), twin.GetEmpireFullName()), emperor);
    }
#endif

    // 兄弟之国到期(四十年)，或一方已被外姓篡夺：情谊渐疏，互争正统
#if EC_PENDING
    private static void CheckAmity(Empire empire, Empire twin)
    {
        bool expired = empire.data.partition_time >= 0d && GameClock.YearsSince(empire.data.partition_time) >= AmityYears;
        bool sameHouse = SpecificClanManager.SameLineage(empire.EmpireSpecificClan, twin.EmpireSpecificClan);
        if (!expired && sameHouse) return;
        empire.data.partition_amity = twin.data.partition_amity = false;
        Empire claimant = empire.data.partition_claimant ? empire : twin;
        Empire incumbent = claimant == empire ? twin : empire;
        ImperialLegitimacyChallengeService.DeclareDynasticRivals(claimant, incumbent, war: false);
        Publish(empire, twin, string.Format(LM.Get("partition_amity_end_history"), empire.GetEmpireFullName(),
            twin.GetEmpireFullName()), null);
    }
#endif

    // 兄弟之国遭外敌攻打：另一朝出兵相助(WarPatch.new_war)
#if EC_PENDING
    public static void OnWarStarted(War war)
    {
        Realm defender = war?.MainDefender, attacker = war?.MainAttacker;
        Empire realm = RealmOf(defender);
        if (realm == null || !realm.data.partition_amity) return;
        Empire twin = Twin(realm);
        Realm ally = twin?.CoreKingdom;
        if (ally == null || ally.IsGone() || RealmOf(attacker) == twin || RealmOf(attacker) == realm ||
            war.HasRealm(ally) || ally.IsAtWarWith(attacker)) return;
        war.JoinDefenders(ally);
        string text = string.Format(LM.Get("partition_aid_history"), twin.GetEmpireFullName(), realm.GetEmpireFullName());
        twin.RecordHistory(directContent: text);
        TranslateHelper.LogEventOnly(text, ally);
    }
#endif

    #endregion

    #region ② 权臣分立

#if EC_PENDING
    private static void TryMinister(Empire empire)
    {
        if (!Eligible(empire)) return;
        Person emperor = empire.Emperor;
        Person minister = empire.GetPowerfulMinister();
        Realm core = empire.CoreKingdom;
        if (emperor?.Record == null || !emperor.Alive || minister?.Record == null || !minister.Alive ||
            !empire.data.is_been_controlled && empire.data.powerful_minister_progress < 50) return;
        List<City> cities = Cities(empire);
        if (cities.Count < 8) return;
        float span = Span(cities, out _);
        float total = empire.kingdoms_list.Sum(Might);
        City capital = core.Capital;
        FixedFaction ministerFaction = minister.GetFaction();
        // 强藩：远方、兵多(或带兵的统帅、节度使、都护、世袭的埃米尔)、不与权臣同派的成员国君主；
        // 苏丹挟制哈里发时，远方世袭的埃米尔也会拥立宗室另立一朝(见 EmirateSystem)
        Realm bastion = empire.kingdoms_list
            .Where(kingdom => kingdom != null && !kingdom.IsGone() && kingdom != core && kingdom.Capital != null &&
                              kingdom.Ruler?.Record != null && kingdom.Ruler.Alive && kingdom.Ruler.IsAdult() &&
                              kingdom.Ruler != minister && (ministerFaction == null || kingdom.Ruler.GetFaction() != ministerFaction) &&
                              Vector2.Distance(kingdom.Capital.Center, capital.Center) >= span * StrongmanDistanceShare &&
                              (Might(kingdom) >= total * StrongmanMightShare || CorpsSystem.IsGeneral(kingdom.Ruler) ||
                               kingdom.GetKingdomType() is KingdomType.LvLing_jiedushi or KingdomType.LvLing_duhufu ||
                               EmirateSystem.IsHereditaryEmir(empire, kingdom)))
            .OrderByDescending(Might).FirstOrDefault();
        if (bastion == null || EcRandom.value >= MinisterChance * PhaseFactor(core)) return;
        Plan plan = Divide(empire, capital, bastion.Capital);
        if (plan.Share < 0.15f || plan.Share > 0.7f) return;
        Person strongman = bastion.Ruler;
        City seat = bastion.Capital;
        bool flee = emperor.Age() >= FleeMinAge && EcRandom.value < 0.5f;
        if (flee)
        {
            switch (FleeOutcome(empire, emperor))
            {
                case Flee.Counter:
                    Counter(empire, emperor, minister);
                    return;
                case Flee.Deposed:
                    Depose(empire, emperor, minister);
                    return;
            }
            // 皇帝出奔：权臣先在京城另立宗室幼主，皇帝在强藩处另立朝廷
            Person puppet = PickRoyal(empire, emperor, preferYoung: true);
            if (puppet != null)
            {
                string emperorName = emperor.FullName();
                core.RemoveRuler();
                Install(empire, puppet, PrinceStruggleSystem.NoteEnthroned);
                Empire twin = Split(empire, emperor, seat, plan, KindMinister, PrinceStruggleSystem.NoteFled,
                    empire.data.core_name, renameSource: true);
                if (twin != null)
                {
                    AfterMinisterSplit(empire, twin, strongman, emperor,
                        RulerQualitySystem.Of(emperor) is RulerQualitySystem.Grade.Weak or RulerQualitySystem.Grade.Fatuous
                            ? ControlledProgress : HostProgress);
                    Publish(empire, twin, string.Format(LM.Get("partition_flee_history"), emperorName, minister.FullName(),
                        seat.GetCityName(), strongman.FullName(), puppet.FullName(), empire.GetEmpireFullName(),
                        twin.GetEmpireFullName()), emperor);
                }
                return;
            }
        }
        // 强藩拥立：在自己的地盘立一位宗室为帝
        Person royal = PickRoyal(empire, emperor, preferYoung: true);
        if (royal == null) return;
        Empire rival = Split(empire, royal, seat, plan, KindMinister, PrinceStruggleSystem.NoteRaised,
            empire.data.core_name, renameSource: true);
        if (rival == null) return;
        // 强藩拥立另立朝廷记在强藩之主身上(量刑天平的叛逆砝码)；迎皇帝出奔的不记
        EmpireCraft.Core.EmpireLawSystem.NoteDefiance(strongman, true);
        AfterMinisterSplit(empire, rival, strongman, royal, ControlledProgress);
        Publish(empire, rival, string.Format(LM.Get("partition_raise_history"), strongman.FullName(), seat.GetCityName(),
            royal.FullName(), minister.FullName(), empire.GetEmpireFullName(), rival.GetEmpireFullName()), royal);
    }
#endif

    // 强藩把持新朝(权臣)；两朝互争正统、开战
    private const int FleeMinAge = 20, ControlledProgress = 75, HostProgress = 30;

    private enum Flee { Escaped, Counter, Deposed }

    // 出奔的结局：成功出奔四成、反杀权臣三成、事泄被废三成；明君更能反杀，庸弱昏聩更易被废，权臣挟制已深的更易被废
    private static Flee FleeOutcome(Empire empire, Person emperor)
    {
        float escaped = 0.4f, counter = 0.3f, deposed = 0.3f;
        switch (RulerQualitySystem.Of(emperor))
        {
            case RulerQualitySystem.Grade.Wise:
                counter += 0.2f;
                break;
            case RulerQualitySystem.Grade.Weak:
            case RulerQualitySystem.Grade.Fatuous:
                deposed += 0.2f;
                counter = Mathf.Max(0.05f, counter - 0.15f);
                break;
        }
        if (empire.data.is_been_controlled) deposed += 0.1f;
        float roll = EcRandom.value * (escaped + counter + deposed);
        return roll < escaped ? Flee.Escaped : roll < escaped + counter ? Flee.Counter : Flee.Deposed;
    }

    // 反杀：皇帝与近臣谋诛权臣，抄没家产，亲政
#if EC_PENDING
    private static void Counter(Empire empire, Person emperor, Person minister)
    {
        Realm core = empire.CoreKingdom;
        string ministerName = minister.FullName();
        int fortune = Mathf.Max(0, minister.Money);
        if (fortune > 0)
        {
            minister.Money = 0;
            core.AddMoney(fortune, TreasuryCategory.RecoveredFunds);
        }
        empire.ResetPowerfulMinister();
        empire.AddMandate(5);
        string text = string.Format(LM.Get("partition_counter_history"), emperor.FullName(), ministerName);
        emperor.RecordPersonalHistory(text, relatedActorId: minister.Key);
        minister.RecordPersonalHistory(text, relatedActorId: emperor.Key);
        BiographySystem.Kill(minister, BiographySystem.Format("death_cause_executed_by", emperor.FullName()), emperor,
            executed: true, destroy: true, countDeath: false);
        empire.RecordHistory(directContent: text, actorId: emperor.Key);
        TranslateHelper.LogEventMessage(text, core);
    }
#endif

    // 事泄被废：权臣废帝，另立宗室幼主；宗室无人可立就天下散伙(见 InterregnumSystem)
#if EC_PENDING
    private static void Depose(Empire empire, Person emperor, Person minister)
    {
        Realm core = empire.CoreKingdom;
        string emperorName = emperor.FullName(), ministerName = minister.FullName();
        Person puppet = PickRoyal(empire, emperor, preferYoung: true);
        if (puppet == null)
        {
            InterregnumSystem.TryFracture(empire, emperor.GetPersonalIdentity(), minister,
                string.Format(LM.Get("interregnum_deposed_opening"), emperorName, ministerName));
            return;
        }
        core.RemoveRuler();
        Install(empire, puppet, PrinceStruggleSystem.NoteEnthroned);
        empire.data.powerful_minister_progress = Mathf.Max(empire.data.powerful_minister_progress, ControlledProgress);
        empire.data.powerful_minister_emperor_id = puppet.Key;
        empire.data.is_been_controlled = true;
        string text = string.Format(LM.Get("partition_deposed_history"), emperorName, ministerName, puppet.FullName());
        emperor.RecordPersonalHistory(text, relatedActorId: minister.Key);
        puppet.RecordPersonalHistory(text, relatedActorId: minister.Key);
        empire.RecordHistory(directContent: text, actorId: puppet.Key);
        TranslateHelper.LogEventMessage(text, core);
    }

    private static void AfterMinisterSplit(Empire source, Empire twin, Person strongman, Person ruler, int control)
    {
        if (strongman?.Record != null && strongman.Alive)
        {
            twin.data.powerful_minister_id = strongman.Key;
            twin.data.powerful_minister_progress = control;
            twin.data.powerful_minister_emperor_id = ruler?.Key ?? -1L;
            twin.data.last_powerful_minister_timestamp = GameClock.Now;
            twin.data.is_been_controlled = control >= ControlledProgress;
        }
        ImperialLegitimacyChallengeService.DeclareDynasticRivals(twin, source, war: true);
    }
#endif

    // 宗室里挑一位(权臣、强藩要立的多是年少的)，从族谱落成实体
#if EC_PENDING
    private static Person PickRoyal(Empire empire, Person emperor, bool preferYoung)
    {
        PersonalClanIdentity parent = emperor?.GetPersonalIdentity();
        if (parent == null) return null;
        var pool = EmpireCraftKingdomBehCheckHeir.CollateralPool(empire.CoreKingdom, parent);
        // 先挑京城里现成的宗室(实体)，再挑年幼的；不随手从族谱里拉出一位成年皇叔
        IEnumerable<PersonalClanIdentity> ordered = (preferYoung
                ? pool.OrderByDescending(item => !item.person.is_virtual && item.person._actor?.Record != null &&
                                                 item.person._actor.City == empire.CoreKingdom.Capital)
                    .ThenByDescending(item => item.person.age >= 5 && item.person.age < 16)
                    .ThenBy(item => item.kinship).ThenBy(item => Mathf.Abs(item.person.age - 10))
                : pool.OrderBy(item => item.kinship).ThenByDescending(item => item.person.age >= 18).ThenByDescending(item => item.person.age))
            .Select(item => item.person);
        return VirtualGenealogySystem.RealizeFirst(ordered,
            actor => actor?.Record != null && actor.Alive && actor != emperor && !actor.IsRuler());
    }

    private static void Install(Empire empire, Person successor, string note)
    {
        Realm core = empire.CoreKingdom;
        PrinceStruggleSystem.Note(core, successor, note);
        if (successor.IsCityLeader() && successor.City != null) successor.City.RemoveLeader();
        successor.removeFromArmy();
        OfficeObject office = core.GetOffice();
        if (office != null)
        {
            office.meta_object = core;
            office.SetActor(successor);
        }
        if (core.Ruler != successor) core.SetRuler(successor);
    }
#endif

    #endregion

    #region ③ 宗室另立(篡位后)

    // 外姓篡位时由 Empire.NewEmperor 调用：按原来的法统定先朝宗室起兵的可能，下个月起事
    public static void QueueRestoration(Empire empire, SpecificClan previousClan, string previousName, int mandate,
        bool ministerUsurpation)
    {
        if (empire?.data == null || previousClan == null || EmpireWorld.Current == null) return;
        if (TransmigrantSystem.Governs(empire)) return;
        // 权臣篡位减半。同日曾改为与外姓篡位同等(用户 2026-10-21 选"华夏有真乱世"：东晋、南明式的宗室另立)，
        // 玩家随即反馈"帝国容易突然碎一地"，用户选回调：恢复减半
        float chance = Mathf.Clamp01((mandate - 30f) / 60f) * 0.425f * (ministerUsurpation ? 0.5f : 1f);
        if (EcRandom.value >= chance) return;
        // 篡位前先朝的积弊家底(这时篡位者还没按改朝换代清掉)：另立的一朝按"减半"承接(用户 2026-10-26)
        Restorations[empire.data.id] = (previousClan.id, previousName ?? "", GameClock.Now,
            DynasticCycleSystem.Capture(empire));
    }

#if EC_PENDING
    private static void Restore(Empire usurper, long clanId, string oldName, DynasticCycleSystem.Legacy legacy)
    {
        if (usurper?.data == null || usurper.IsGone() || usurper.IsArchived() || Twin(usurper) != null) return;
        Realm core = usurper.CoreKingdom;
        if (UnificationSystem.InGrace(core)) return;
        // 大分裂冷却里、土崩瓦解收束期内不另立(见 UpheavalGuard，用户 2026-10-21 选)：这一回作罢，不再顺延
        if (UpheavalGuard.Blocked(usurper, out string why))
        {
            LogService.LogInfo($"[EmpireCraft][分裂] {usurper.GetEmpireFullName()} 先朝宗室本当另立，{why}，作罢");
            return;
        }
        City capital = core?.Capital;
        SpecificClan clan = SpecificClanManager.Get(clanId);
        if (capital == null || clan == null || SpecificClanManager.SameLineage(usurper.EmpireSpecificClan, clan)) return;
        bool Royal(Person actor) => actor?.Record != null && actor.Alive &&
                                   SpecificClanManager.SameLineage(actor.GetSpecificClan(), clan);
        // 已经叛离篡位者的宗室诸王：最强的一位以自己的王国称帝，其余归附
        List<Realm> princes = EmpireWorld.Current.LiveRealms
            .Where(kingdom => kingdom?.Record != null && !kingdom.IsGone() && kingdom.Capital != null && !kingdom.IsEmpire() &&
                              !kingdom.IsInEmpire() && Royal(kingdom.Ruler))
            .ToList();
        Realm leader = princes.OrderByDescending(Might).FirstOrDefault();
        Person claimant;
        City seat;
        Plan plan;
        if (leader != null)
        {
            claimant = leader.Ruler;
            seat = leader.Capital;
            plan = Divide(usurper, capital, seat);
            if (plan.Share > 0.5f) plan = new Plan();
        }
        else
        {
            seat = FarSeat(usurper, capital.Center, 0.15f, 0.5f, out plan);
            EmpireCraftHistory lastReign = usurper.data.history?.LastOrDefault(history =>
                history != null && history.id > 0 && history.royal_surname == clan.name);
            PersonalClanIdentity last = lastReign == null ? null
                : SpecificClanManager._actorToPersonLookup.TryGetValue(lastReign.id, out var identity) ? identity : null;
            IEnumerable<PersonalClanIdentity> pool = last != null
                ? EmpireCraftKingdomBehCheckHeir.CollateralPool(core, last).OrderBy(item => item.kinship)
                    .ThenByDescending(item => item.person.age >= 18).ThenByDescending(item => item.person.age)
                    .Select(item => item.person)
                : clan.SnapshotPeople().Where(person => person != null && person.is_alive)
                    .OrderByDescending(person => person.age >= 18).ThenByDescending(person => person.age);
            claimant = seat == null ? null : VirtualGenealogySystem.RealizeFirst(pool,
                actor => actor?.Record != null && actor.Alive && !actor.IsRuler() && actor.IsAdult());
        }
        if (claimant == null || seat == null || plan == null) return;
        // 一次至多带走篡位帝国总城数的三分之一(见 Trim)
        int before = UpheavalGuard.Cities(usurper);
        var budget = new UpheavalGuard.Budget(usurper, UpheavalGuard.CapShare);
        if (!Trim(plan, seat, leader, budget))
        {
            LogService.LogInfo($"[EmpireCraft][分裂] {usurper.GetEmpireFullName()} 先朝宗室另立：新都{seat.GetCityName()}已超出一次带走的上限" +
                               $"({budget.Cap}/{budget.Total} 城)，作罢");
            return;
        }
        string name = string.IsNullOrWhiteSpace(oldName) ? clan.name : oldName;
        Empire loyal = Split(usurper, claimant, seat, plan, KindRestoration, PrinceStruggleSystem.NoteRestoration, name,
            renameSource: false, ownKingdom: leader);
        if (loyal == null) return;
        // 宗室另立(东晋、南宋式)：先朝的王朝积弊、六弊减半，王朝年岁从头算(改朝换代三档之"减半"，用户 2026-10-26)。
        // 以前整份抄走篡位者的(外姓篡位时已清过、权臣篡位时原样)，六弊不带
        if (legacy != null) DynasticCycleSystem.Inherit(loyal, legacy, DynasticCycleSystem.Transition.Halve);
        // 篡位的帝国真丢了城才记为大分裂(宗室诸王都已在帝国外、只是自立称帝的不算)
        if (UpheavalGuard.Cities(usurper) < before) UpheavalGuard.Mark(usurper, "宗室另立", budget);
        foreach (Realm prince in princes)
            if (prince != leader && !prince.IsGone() && !prince.IsInEmpire())
                loyal.join(prince, pForce: true, pLegitimacyTransfer: true);
        ImperialLegitimacyChallengeService.DeclareDynasticRivals(loyal, usurper, war: true);
        Publish(usurper, loyal, string.Format(LM.Get("partition_restoration_history"), usurper.Emperor?.FullName() ?? "",
            claimant.FullName(), seat.GetCityName(), name, loyal.GetEmpireFullName()), claimant);
    }
#endif

    // 宗室另立一次带走的城不超过篡位帝国总城数(含成员)的三分之一(见 UpheavalGuard，用户 2026-10-21 选)：
    // 照划分的远近，离新都近的先走(成员国整国、京畿逐城)，下一处放进来就超过上限时就停。
    // 新都本是帝国的城(不是叛离在外的宗室诸王自带的国)时先算它一座；它都放不下就不另立(返回 false)
    private static bool Trim(Plan plan, City seat, Realm ownKingdom, UpheavalGuard.Budget budget)
    {
        Vector2 at = seat.Center;
        if (ownKingdom == null)
        {
            if (!budget.Fits(1)) return false;
            budget.Take(1);
        }
        var parts = new List<(float distance, Realm member, City city, int count)>();
        foreach (Realm member in plan.Members)
        {
            if (member == null || member.IsGone()) continue;
            City anchor = member.Capital ?? member.Cities?.FirstOrDefault();
            // 新都所在的成员国：新都那座上面已算过
            int count = (member.Cities?.Count ?? 0) - (ownKingdom == null && seat.Realm == member ? 1 : 0);
            parts.Add((anchor == null ? 0f : (anchor.Center - at).sqrMagnitude, member, null, Mathf.Max(0, count)));
        }
        foreach (City city in plan.CoreCities)
            if (city?.Record != null && city != seat) parts.Add(((city.Center - at).sqrMagnitude, null, city, 1));
        var members = new List<Realm>();
        var coreCities = new List<City>();
        foreach (var part in parts.OrderBy(item => item.distance))
        {
            if (!budget.Fits(part.count)) break;
            budget.Take(part.count);
            if (part.member != null) members.Add(part.member);
            else coreCities.Add(part.city);
        }
        plan.Members = members;
        plan.CoreCities = coreCities;
        return true;
    }

    #endregion

    #region 武家另立(幕府的南北朝)

    public const string KindShogunate = "shogunate";

    // 武士拥立皇族另一位、在离京城远的一头另立一朝(见 ShogunateDramaSystem)：万世一系的帝国不走遗诏分治、权臣分立
    // 那几条(见 Eligible)，这里单开一个口。两朝按都城的相对方位各加方位，立即交战。成了返回新的一朝
#if EC_PENDING
    public static Empire SplitRivalCourt(Empire source)
    {
        if (source?.data == null || source.IsGone() || source.IsArchived() || Twin(source) != null) return null;
        City capital = source.CoreKingdom?.Capital;
        Person emperor = source.Emperor;
        if (capital == null || emperor?.Record == null) return null;
        City seat = FarSeat(source, capital.Center, 0.25f, 0.6f, out Plan plan);
        Person royal = seat == null ? null : PickRoyal(source, emperor, preferYoung: true);
        if (royal == null) return null;
        Empire twin = Split(source, royal, seat, plan, KindShogunate, PrinceStruggleSystem.NoteRaised, source.data.core_name,
            renameSource: true);
        if (twin == null) return null;
        ImperialLegitimacyChallengeService.DeclareDynasticRivals(twin, source, war: true);
        return twin;
    }
#endif

    #endregion

    #region 复归一统

    // 一朝皇族绝嗣、群臣迎立另一朝的皇帝兼承大统(SuccessionCouncilSystem)：并入另一朝
    public static bool CanUnite(Empire vacant, out Empire heir)
    {
        heir = Twin(vacant);
        Person emperor = heir?.Emperor;
        return heir != null && emperor?.Record != null && emperor.Alive &&
               SpecificClanManager.SameLineage(heir.EmpireSpecificClan, vacant.EmpireSpecificClan);
    }

    // historyKey：记史的文案(参数同 partition_reunite_history：{0} 承统的君主、{1} 并入的一朝、{2} 合一后的国号)；
    // major：不论哪国都上屏(幕府说和、南北朝合一，见 ShogunateDramaSystem)
#if EC_PENDING
    public static bool Reunite(Empire vacant, Empire heir, string historyKey = "partition_reunite_history", bool major = false)
    {
        if (vacant?.data == null || heir?.data == null || heir.IsGone() || heir.IsArchived()) return false;
        string vacantName = vacant.GetEmpireFullName();
        Realm oldCore = vacant.CoreKingdom;
        foreach (Realm kingdom in vacant.kingdoms_list.ToList())
            if (kingdom != null && kingdom != oldCore && !kingdom.IsGone())
                heir.join(kingdom, pRecalc: false, pForce: true, pLegitimacyTransfer: true);
        List<City> cities = oldCore?.Cities?.ToList() ?? new List<City>();
        if (!vacant.IsArchived()) ModClass.EMPIRE_MANAGER.dissolveEmpire(vacant);
        foreach (City city in cities)
            if (city?.Record != null && !city.IsGone() && heir.CoreKingdom != null) city.JoinRealm(heir.CoreKingdom);
        heir.data.partition_twin_id = -1L;
        heir.data.partition_amity = false;
        heir.data.directPre = "";
        heir.SetEmpireName(heir.data.core_name);
        heir.recalculate();
        string text = string.Format(LM.Get(historyKey), heir.Emperor?.FullName() ?? "", vacantName,
            heir.GetEmpireFullName());
        heir.RecordHistory(directContent: text, actorId: heir.Emperor?.Key ?? -1L);
        if (major) TranslateHelper.LogMajor(text, heir.CoreKingdom);
        else TranslateHelper.LogEventOnly(text, heir.CoreKingdom);
        EmpireWorld.Current.Zones?.MarkDirty();
        return true;
    }
#endif

    #endregion
}
}
