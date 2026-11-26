using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{
    // Faiths and cultures as the ported systems hold them: a record with a key and a
    // name, kept in a store per world. What a faith teaches or a culture is like is up
    // to the systems that hang their own state on it; nothing here decides who
    // converts or how a culture spreads.
    //
    // The population is counted, not tracked, so a faith's or a culture's following
    // is the headcount of the cities that hold it.

    // The saved part. Field names follow what the ported systems read.
    public sealed class ReligionRecord : RecordData
    {
        public long creator_id = -1L;           // the founder
        public long creator_kingdom_id = -1L;   // the realm it was founded in
    }

    public sealed class CultureRecord : RecordData
    {
        public long creator_id = -1L;
        public long creator_kingdom_id = -1L;
    }

    public sealed class Religion : RecordObject<ReligionRecord>
    {
        public EmpireWorld World;
        public override EntityKind KindOfEntity => EntityKind.Religion;
        public int Age() => data == null || World == null ? 0 : (int)Math.Floor((World.Now() - data.created_time) / 12d);
        // The living cities and realms that hold this faith.
        public List<City> Cities { get { var found = new List<City>(); if (World != null) foreach (var city in World.Cities) if (city.Alive && city.Religion == this) found.Add(city); return found; } }
        public List<Realm> Realms { get { var found = new List<Realm>(); if (World != null) foreach (var realm in World.LiveRealms) if (realm.Religion == this) found.Add(realm); return found; } }
        public long Followers() { double total = 0; if (World != null) foreach (var city in World.Cities) if (city.Alive && city.Religion == this) total += city.Population; return (long)total; }
    }

    public sealed class Culture : RecordObject<CultureRecord>
    {
        public EmpireWorld World;
        public override EntityKind KindOfEntity => EntityKind.Culture;
        // The family of culture rules it follows (a key of the culture rules: "Huaxia" ...); none until a system says.
        public string Family;
        public NameParts GetOnomasticData(EntityKind kind) => null;
        public int Age() => data == null || World == null ? 0 : (int)Math.Floor((World.Now() - data.created_time) / 12d);
        public List<City> Cities { get { var found = new List<City>(); if (World != null) foreach (var city in World.Cities) if (city.Alive && city.Culture == this) found.Add(city); return found; } }
        public List<Realm> Realms { get { var found = new List<Realm>(); if (World != null) foreach (var realm in World.LiveRealms) if (realm.Culture == this) found.Add(realm); return found; } }
        public long Followers() { double total = 0; if (World != null) foreach (var city in World.Cities) if (city.Alive && city.Culture == this) total += city.Population; return (long)total; }

        // Traits are plain ids, as on a person; what one means is up to the system that gives it.
        HashSet<string> _traits;
        public IEnumerable<string> Traits => _traits ?? (IEnumerable<string>)Array.Empty<string>();
        public int TraitCount => _traits == null ? 0 : _traits.Count;
        public bool HasTrait(string id) => id != null && _traits != null && _traits.Contains(id);
        public bool AddTrait(string id) { if (string.IsNullOrEmpty(id)) return false; if (_traits == null) _traits = new HashSet<string>(StringComparer.Ordinal); return _traits.Add(id); }
        public bool RemoveTrait(string id) => id != null && _traits != null && _traits.Remove(id);
    }

    public sealed class ReligionStore : RecordStore<Religion, ReligionRecord>
    {
        readonly EmpireWorld _world;
        public ReligionStore(EmpireWorld world) { _world = world; }
        public override Religion NewObjectFromKey(long key)
        {
            var made = base.NewObjectFromKey(key);
            made.World = _world; made.data.created_time = _world.Now();
            return made;
        }
        // A new faith on record, nameless until the caller names it. Nobody follows it yet,
        // the founder included: whoever founds it says who converts.
        public Religion NewReligion(Person founder, bool withDefaults = false)
        {
            var made = NewObject();
            if (founder != null) { made.data.creator_id = founder.Key; made.data.creator_kingdom_id = founder.Realm == null ? -1L : founder.Realm.Key; }
            return made;
        }
    }

    public sealed class CultureStore : RecordStore<Culture, CultureRecord>
    {
        readonly EmpireWorld _world;
        public CultureStore(EmpireWorld world) { _world = world; }
        // The map's culture colours are redrawn by the client; nothing to mark here.
        public void SetDirtyCities() { }
        public override Culture NewObjectFromKey(long key)
        {
            var made = base.NewObjectFromKey(key);
            made.World = _world; made.data.created_time = _world.Now();
            return made;
        }
        public Culture NewCulture(Person founder, bool withDefaults = false)
        {
            var made = NewObject();
            if (founder != null) { made.data.creator_id = founder.Key; made.data.creator_kingdom_id = founder.Realm == null ? -1L : founder.Realm.Key; }
            return made;
        }
    }
}
