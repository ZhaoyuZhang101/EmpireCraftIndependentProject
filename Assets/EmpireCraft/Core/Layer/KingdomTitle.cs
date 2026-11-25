// Generated from the EmpireCraft mod: Scripts/Layer/KingdomTitle.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Core
{
public class KingdomTitle : RecordObject<KingdomTitleData>
{
    public const string JurisdictionHolder = "holder";
    public const string JurisdictionAdministration = "administration";
#if EC_PENDING
    public BannerAsset BannerAsset;
#endif
    public HashSet<City> city_list_hash = new HashSet<City>();
    public List<City> city_list = new List<City>();
#if EC_PENDING
    public Vector3 last_center;
    public Vector3 title_center;
#endif
    private readonly List<Cell> _zoneScratch = new();
    private readonly List<City> _needRemoveCitiesBuffer = new();
    public City title_capital;
    public Realm control_kingdom;
    public Realm main_kingdom;

    public Person owner;
#if EC_PENDING
    public Tint kingdomColor => getColor();
    public override MetaType meta_type => MetaTypeExtension.KingdomTitle;

    public void SetOwner(Person pActor)
    {
        owner?.removeTitle(this);
        owner = pActor;
    }
#endif
    public int GetTitleBeenControlledYear()
    {
        if (this.data == null) return 0;
        if (this.data.timestamp_been_controlled <= 0) return 0;
        return GameClock.YearsSince(this.data.timestamp_been_controlled);
    }
#if EC_PENDING
    public void newKingdomTitle(City city)
    {
        if (city == null || city.IsGone() || city.Realm == null || city.Realm.IsGone()) return;
        // 国君空缺时开创者留空、物种取这个国家的开国物种
        Person king = city.Realm.Ruler != null && !city.Realm.Ruler.IsGone() ? city.Realm.Ruler : null;
        this.title_capital = city;
        this.data.founder_actor_id = king?.Key ?? -1L;
        this.data.founder_actor_name = king?.Name ?? "";
        this.data.created_time = GameClock.Now;
        this.data.banner_icon_id = city.Realm.Record.banner_icon_id;
        this.data.banner_background_id = city.Realm.Record.banner_background_id;
        this.data.founder_kingdom_id = city.Realm.Key;
        this.data.founder_kingdom_name = city.Realm.Name;
        this.data.timestamp_established_time = GameClock.Now;
        this.data.jurisdiction_history = new List<KingdomTitleJurisdictionRecord>();
        this.owner = null;
        string kingdomName = city.SelectKingdomName();
        data.province_name = title_capital.GetCityName();
        // 属地借国名的不带"某属"名号(见 ColonySystem.SovereignName)
        this.data.name = !string.IsNullOrEmpty(kingdomName) ? kingdomName : EmpireCraft.Core.ColonySystem.SovereignName(city.Realm);
        // 城里没有古国名、借了所属国的名字：那国是道、省这类行政区，或本来就叫城名的，这个法理名就是地名
        this.data.place_name = string.IsNullOrEmpty(kingdomName) && RealmNamingSystem.IsPlaceName(this.data.name);
        this.addCity(city);
        data.original_actor_asset = king?.asset?.id ?? city.Realm.Record.original_actor_asset;
        CultureService.InitializeTitleCulture(this);
        recalculate();
        generateColor();
        LogService.LogInfo("创建头衔成功");
        preserveAlive();
    }
#endif

    public bool HasOwner()
    {
        return owner != null && !owner.IsGone();
    }

    public void RecordJurisdiction(Realm kingdom, string relation)
    {
        if (data == null || kingdom == null || kingdom.IsGone() || string.IsNullOrWhiteSpace(relation)) return;
        data.jurisdiction_history ??= new List<KingdomTitleJurisdictionRecord>();
        double now = GameClock.Now;
        KingdomTitleJurisdictionRecord current = data.jurisdiction_history.LastOrDefault(record =>
            record != null && record.end_time < 0);
        if (current != null && current.kingdom_id == kingdom.Key && current.relation == relation)
        {
            current.kingdom_name = kingdom.Name ?? current.kingdom_name;
            return;
        }
        foreach (KingdomTitleJurisdictionRecord record in data.jurisdiction_history.Where(record =>
                     record != null && record.end_time < 0))
        {
            record.end_time = now;
        }
        data.jurisdiction_history.Add(new KingdomTitleJurisdictionRecord
        {
            kingdom_id = kingdom.Key,
            kingdom_name = kingdom.Name ?? "",
            relation = relation,
            start_time = now,
            end_time = -1L
        });
    }

    public void EndJurisdiction(Realm kingdom, string relation)
    {
        if (data?.jurisdiction_history == null || kingdom == null) return;
        double now = GameClock.Now;
        foreach (KingdomTitleJurisdictionRecord record in data.jurisdiction_history.Where(record =>
                     record != null && record.end_time < 0 && record.kingdom_id == kingdom.Key &&
                     record.relation == relation))
        {
            record.end_time = now;
        }
    }

#if EC_PENDING
    public override Tint GetColor()
    {
        // 王国法理用自己那套设色、接壤的不同色(见 DeJureMap)，不再与国家同一个色库
        Tint own = DeJureMap.TitleColor(this);
        if (own != null) return own;
        if (_cached_color == null)
        {
            _cached_color = getColorLibrary().list[data.color_id];
        }

        return _cached_color;
    }

    public Sprite getElementIcon()
    {
        ActorAsset actorAsset = getActorAsset();
        if (actorAsset == null) return null;
        try
        {
            return AssetManager.kingdom_banners_library.getSpriteIcon(data.banner_icon_id, actorAsset.banner_id);
        }
        catch (ArgumentOutOfRangeException)
        {
            data.banner_icon_id = 0;
            try
            {
                return AssetManager.kingdom_banners_library.getSpriteIcon(0, actorAsset.banner_id);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }
    }
    
    public Sprite getElementBackground()
    {
        ActorAsset actorAsset = getActorAsset();
        if (actorAsset == null) return null;
        try
        {
            return AssetManager.kingdom_banners_library.getSpriteBackground(data.banner_background_id, actorAsset.banner_id);
        }
        catch (ArgumentOutOfRangeException)
        {
            data.banner_background_id = 0;
            try
            {
                return AssetManager.kingdom_banners_library.getSpriteBackground(0, actorAsset.banner_id);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }
    }
#endif
    public int countPopulation()
    {
        int res = 0;
        foreach(City city in city_list_hash)
        {
            res += city.PopulationCount();
        }
        return res;
    }

#if EC_PENDING
    public int countWarriors()
    {
        int res = 0;
        foreach (City city in city_list_hash)
        {
            res += city.getMaxWarriors();
        }
        return res;
    }


    public override ActorAsset getActorAsset()
    {
        return getFounderSpecies();
    }

    public override int countUnits()
    {
        var num = 0;
        foreach (var city in city_list)
        {
            num+=city.getUnits().Count();
        }
        return num;
    }
    public override void generateColor()
    {
        ActorAsset actorAsset = getActorAsset();
        if (actorAsset == null)
        {
            data.setColorID(0);
            return;
        }
        int nextColorIndex = getColorLibrary().getNextColorIndex(actorAsset);
        data.setColorID(nextColorIndex);
    }

    public ActorAsset getFounderSpecies()
    {
        if (!string.IsNullOrWhiteSpace(data?.original_actor_asset))
        {
            ActorAsset storedAsset = AssetManager.actor_library.get(data.original_actor_asset);
            if (storedAsset != null) return storedAsset;
        }

        ActorAsset recoveredAsset = title_capital?.Realm?.Ruler?.asset ??
                                    city_list.FirstOrDefault(city => city?.Realm?.Ruler?.asset != null)?.kingdom.king.asset;
        if (recoveredAsset != null && data != null) data.original_actor_asset = recoveredAsset.id;
        return recoveredAsset;
    }
#endif


    public int countZones()
    {
        int tResult = 0;
        List<City> tCities = this.city_list;
        for (int i = 0; i < tCities.Count; i++)
        {
            City tCity = tCities[i];
            tResult += tCity.CountZones();
        }
        return tResult;
    }

#if EC_PENDING
    public override ColorLibrary getColorLibrary()
    {
        return AssetManager.kingdom_colors_library;
    }
    public Vector3 GetCenter()
    {
        if (!this._units_dirty)
            return this.last_center;

        if (this.countZones() <= 0)
        {
            this.title_center = Globals.POINT_IN_VOID_2;
            return this.title_center;
        }
        float num = 0f;
        float num2 = 0f;
        float num3 = float.MaxValue;
        Cell tileZone = null;
        var zones = this.allZones();
        for (int i = 0; i < zones.Count; i++)
        {
            Cell tileZone2 = zones[i];
            num += tileZone2.Center.posV3.x;
            num2 += tileZone2.Center.posV3.y;
        }
        this.title_center.x = num / (float)zones.Count;
        this.title_center.y = num2 / (float)zones.Count;
        for (int j = 0; j < zones.Count; j++)
        {
            Cell tileZone3 = zones[j];
            float num4 = Toolbox.SquaredDist((float)tileZone3.Center.X, (float)tileZone3.Center.Y, this.title_center.x, this.title_center.y);
            if (num4 < num3)
            {
                tileZone = tileZone3;
                num3 = num4;
            }
        }
        this.title_center.x = tileZone.Center.posV3.x;
        this.title_center.y = tileZone.Center.posV3.y + 2f;
        this.last_center = this.title_center;
        this._units_dirty = false;
        return this.last_center;
    }

    public bool isBeenControlled()
    {
        Realm kingdom = null;
        foreach (City c in city_list)
        {
            if (c.Alive&&!c.isNeutral())
            {
                if (kingdom == null)
                {
                    kingdom = c.Realm;
                }else
                {
                    if(c.Realm != kingdom)
                    {
                        this.data.timestamp_been_controlled = -1L;
                        return false;
                    }
                    else
                    {
                        kingdom = c.Realm;
                    }
                }
            }
        }
        if (control_kingdom!=kingdom)
        {
            data.timestamp_been_controlled = GameClock.Now;
            control_kingdom = kingdom;
        }
        return true;
    }

    public override void save()
    {
        if (this.data == null) return;
        if (this.city_list_hash == null || this.city_list_hash.Count == 0)
        {
            this.data.cities ??= new List<long>();
            return;
        }

        this.data.cities = new List<long>();
        foreach (City city in city_list_hash)
        {
            if (city == null || city.IsGone())
            {
                continue;
            }

            this.data.cities.Add(city.Key);
        }
        if (this.title_capital != null && !this.title_capital.IsGone())
        {
            this.data.title_capital = this.title_capital.Key;
        }
        try
        {
            this.data.owner = this.owner == null ? -1L : this.owner.Key;
        }
        catch
        {
            this.data.owner = -1L;
        }
        if (main_kingdom!=null)
        {
            if (main_kingdom.Record == null)
            {
                main_kingdom = null;
                this.data.main_kingdom = -1L;
            } else
            {
                this.data.main_kingdom = main_kingdom.Key;
            }

        }
        else
        {
            this.data.main_kingdom = -1L;
        }

    }
#endif

    public List<Cell> allZones()
    {
        List<Cell> zones= new List<Cell>();
        foreach(City city in  city_list_hash)
        {
            foreach (Cell tz in city.Zones) 
            {
                zones.Add(tz);
            }
        }
        return zones;
    }

#if EC_PENDING
    public void addCity(City city)
    {
        if (city == null) return;
        if (city.hasTitle())
        {
            KingdomTitle oldTitle = city.GetTitle();
            oldTitle.removeCity(city);
        }
        city.SetTitle(this);
        city_list_hash.Add(city);
        CultureService.InitializeCityCulture(city);
        // 法理都城没记核心(换过都城、读旧档)时按法理记录找，免得新并进来的城成了无主(见 EmpireCoreManager.ReconcileCityCores)
        EmpireCore core = this.title_capital?.GetEmpireCore();
        if (core == null || !EmpireCoreManager.ContainsTitle(core, this)) core = EmpireCoreManager.LawfulCoreOf(this);
        if (core != null)
        {
            city.SetEmpireCore(core);
        }
        recalculate();
    }
#endif

    // byPlayer：玩家用权能把城市从法理上摘下。冻结法理只防止城市毁灭、战乱让法理自然消失；
    // 玩家亲手摘掉最后一座城时照样解散，否则会留下没有城市的"幽灵法理"
#if EC_PENDING
    public void removeCity(City city, bool byPlayer = false)
    {
        EmpireCore core = this.title_capital?.GetEmpireCore();
        city.RemoveTitle();
        this.city_list_hash.Remove(city);
        if (core != null && city.GetEmpireCoreID() == core.id)
        {
            city.SetEmpireCore(null);
        }
        this.recalculate();
        if (this.city_list_hash.Count <= 0 && (byPlayer || !ModClass.KINGDOM_TITLE_FREEZE))
        {
            ModClass.KINGDOM_TITLE_MANAGER.dissolveTitle(this);
        }
    }
    public override bool isReadyForRemoval()
    {
        return false;
    }
#endif
    // Token: 0x06001124 RID: 4388 RVA: 0x000C7748 File Offset: 0x000C5948
#if EC_PENDING
    public bool checkActive()
    {
        bool tChanged = false;
        if (this.data == null)
        {
            return false;
        }
        this.city_list_hash ??= new HashSet<City>();
        this.city_list ??= new List<City>();
        bool hasNullCity = false;
        for (int i = 0; i < this.city_list.Count; i++)
        {
            if (this.city_list[i] == null)
            {
                hasNullCity = true;
                break;
            }
        }

        if (this.city_list.Count != this.city_list_hash.Count || hasNullCity)
        {
            this.recalculate();
        }
        List<City> cities = this.city_list;
        if (cities.Count <= 0)
        {
            return this.data.cities != null && this.data.cities.Count > 0;
        }
        _needRemoveCitiesBuffer.Clear();
        foreach (City city in cities) 
        {
            if (city == null || city.IsGone() || !city.Alive)
            {
                _needRemoveCitiesBuffer.Add(city);
                tChanged = true;
            }
        }
        foreach (City city in _needRemoveCitiesBuffer)
        {
            if (city != null && !city.IsGone())
            {
                city.RemoveTitle();
            }
            this.city_list_hash.Remove(city);
            this.city_list.Remove(city);
        }
        _needRemoveCitiesBuffer.Clear();
        if (city_list.Count > 0)
        {
            if (this.title_capital == null || this.title_capital.IsGone() || !this.city_list_hash.Contains(this.title_capital))
            {
                this.title_capital = city_list.First();
                tChanged = true;
            }
        }
        if (this.owner != null && this.owner.IsGone())
        {
            this.owner = null;
            tChanged = true;
        }
        if (this.main_kingdom != null && (this.main_kingdom.IsGone() || this.main_kingdom.GetMainTitle() != this))
        {
            this.main_kingdom = null;
            tChanged = true;
        }
        if (this.title_capital != null && !this.title_capital.IsGone())
        {
            if (string.IsNullOrWhiteSpace(this.data.province_name))
            {
                this.data.province_name = this.title_capital.GetCityName();
                tChanged = true;
            }

            if (string.IsNullOrWhiteSpace(this.data.name))
            {
                var titleName = this.title_capital.SelectKingdomName();
                if (string.IsNullOrWhiteSpace(titleName))
                {
                    titleName = EmpireCraft.Core.ColonySystem.SovereignName(this.title_capital.Realm);
                }
                if (string.IsNullOrWhiteSpace(titleName))
                {
                    titleName = this.title_capital.GetCityName();
                }
                this.data.name = titleName ?? "";
                this.data.place_name = !this.title_capital.HasKingdomName() && RealmNamingSystem.IsPlaceName(this.data.name);
                tChanged = true;
            }
        }
        if (tChanged)
        {
            this.recalculate();
        } 
        return this.city_list.Count >= 1 || (this.data.cities != null && this.data.cities.Count > 0);
    }
#endif
    public void recalculate()
    {
        this.city_list.Clear();
        this.city_list.AddRange(city_list_hash);
    }


#if EC_PENDING
    public override void Dispose()
    {
        clearListUnits();
        this.city_list.Clear();
        this.city_list_hash.Clear();
        this.title_capital = null;
        this.owner = null;
    }

    public void Dissolve()
    {
        foreach (var kingdom in EmpireWorld.Current.LiveRealms)
        {
            if (kingdom.GetMainTitle() == this)
            {
                kingdom.RemoveMainTitle();
            }
        }
        foreach (var unit in EmpireWorld.Current.Living)
        {
            unit.removeTitle(this);
        }
        foreach (City city in city_list_hash)
        {
            city.RemoveTitle();
        }
    }

    public override IEnumerable<City> getCities()
    {
        return city_list;
    }

    public override void LoadData(KingdomTitleData pData)
    {
        base.LoadData(pData);
        this.city_list_hash.Clear();
        this.city_list.Clear();
        pData.cities ??= new List<long>();
        foreach (long city_id in pData.cities)
        {
            City city = EmpireWorld.Current.CityByKey(city_id);
            if (city == null || city.IsGone())
            {
                continue;
            }

            this.city_list_hash.Add(city);
            city.SetTitle(this);
        }
        this.title_capital = EmpireWorld.Current.CityByKey(this.data.title_capital);
        if ((this.title_capital == null || this.title_capital.IsGone() || !this.city_list_hash.Contains(this.title_capital)) && this.city_list_hash.Count > 0)
        {
            this.title_capital = this.city_list_hash.First();
            this.data.title_capital = this.title_capital.Key;
        }
        this.city_list.AddRange(this.city_list_hash);
        this.owner = this.data.owner == -1L? null:EmpireWorld.Current.PersonByKey(this.data.owner);
        this.main_kingdom = this.data.main_kingdom == -1L ? null : EmpireWorld.Current.RealmByKey(this.data.main_kingdom);
        pData.jurisdiction_history ??= new List<KingdomTitleJurisdictionRecord>();
        if (string.IsNullOrWhiteSpace(this.data.original_actor_asset) && this.title_capital != null)
        {
            this.data.original_actor_asset = this.title_capital.Realm?.Ruler?.asset?.id ?? this.title_capital.Realm?.asset?.id ?? this.title_capital.getSpecies();
        }
        if(string.IsNullOrEmpty(pData.province_name) && title_capital != null)
        {
            pData.province_name = title_capital.GetCityName();
        } 
    }
#endif
}
}
