using System.Collections.Generic;

namespace EmpireCraft.Core
{
    // Anything a ported system can hang state on or hold a key to: the four core
    // entities and every record object (empire, de jure title ...).
    public abstract class Entity
    {
        public abstract long Key { get; }
        public abstract bool Alive { get; }
        public virtual EntityKind KindOfEntity => EntityKind.None;
    }

    // Bases of the mod's decision routines (its AI folder). Their per-tick entry points were
    // driven by WorldBox's behaviour trees and stay suspended; the helpers on them are used widely.
    public abstract class RealmTask { protected static EmpireWorld world => EmpireWorld.Current; public virtual BehResult Execute(Realm realm) => BehResult.Continue; }
    public abstract class CityTask { protected static EmpireWorld world => EmpireWorld.Current; public virtual BehResult Execute(City city) => BehResult.Continue; }
    public abstract class PersonTask { protected static EmpireWorld world => EmpireWorld.Current; public virtual BehResult Execute(Person person) => BehResult.Continue; }

    // The saved part of a record object. Field names follow the data classes of the
    // ported systems that derive from it.
    public abstract class RecordData
    {
        public long id = -1L;
        public string name;
        public double created_time;
    }

    // An object the ported systems define themselves (an empire, a de jure title):
    // identity and saved state live in its data record, behaviour in the subclass.
    public abstract class RecordObject<TData> : Entity where TData : RecordData, new()
    {
        public TData data;
        bool _removed;
        public override long Key => data == null ? -1L : data.id;
        public override bool Alive => !_removed && data != null;
        public string Name { get { return data == null ? null : data.name; } set { if (data != null) data.name = value; } }
        public virtual void LoadData(TData record) { data = record; }
        public bool IsAlive() => Alive;
        public virtual void ClearListUnits() { }
        public virtual void GenerateNewMetaObject() { }
        // The colours of this object, for those that have any (an empire, a title).
        public virtual Tint GetColor() => null;
        public virtual void UpdateColor(Tint tint) { }
        // What belongs to this object, for those that hold cities or persons; and its own bookkeeping before a save.
        public virtual IEnumerable<City> GetCities() => System.Linq.Enumerable.Empty<City>();
        public virtual IEnumerable<Person> GetUnits() => System.Linq.Enumerable.Empty<Person>();
        public virtual int CountUnits() => 0;
        public virtual void Save() { }
        // Called by the store when the object leaves the world, for the object's own tidying up.
        // The ported classes override this without calling the base, so the store marks the object itself.
        public virtual void Dispose() { _removed = true; }
        internal void MarkRemoved() { _removed = true; }
    }

    // Holds the record objects of one kind for a world: creation with fresh keys,
    // lookup by key, removal. Subclasses add their own bookkeeping.
    public class RecordStore<TObject, TData> : IEnumerable<TObject> where TObject : RecordObject<TData>, new() where TData : RecordData, new()
    {
        public IEnumerator<TObject> GetEnumerator() => list.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => list.GetEnumerator();
        public readonly List<TObject> list = new List<TObject>();
        readonly Dictionary<long, TObject> _byKey = new Dictionary<long, TObject>();
        long _nextKey = 1;
        public int Count => list.Count;
        public long NextKey => _nextKey;
        // A loaded store goes on numbering where the saved one stopped, so a removed object's key is not handed out again.
        public void RestoreNextKey(long key) { if (key > _nextKey) _nextKey = key; }

        public TObject Get(long key) => _byKey.TryGetValue(key, out TObject found) ? found : null;
        public bool Has(long key) => _byKey.ContainsKey(key);

        public virtual TObject NewObject() => NewObjectFromKey(_nextKey);

        public virtual TObject NewObjectFromKey(long key)
        {
            var created = new TObject();
            created.LoadData(new TData { id = key, created_time = GameClock.Now });
            AddObject(created);
            return created;
        }

        public virtual void AddObject(TObject item)
        {
            if (item == null || item.data == null || _byKey.ContainsKey(item.Key)) return;
            list.Add(item); _byKey[item.Key] = item;
            if (item.Key >= _nextKey) _nextKey = item.Key + 1;
        }

        public virtual void RemoveObject(TObject item)
        {
            if (item == null || !_byKey.Remove(item.Key)) return;
            list.Remove(item); item.MarkRemoved(); item.Dispose();
        }

        public virtual void Clear()
        {
            foreach (var item in list) { item.MarkRemoved(); item.Dispose(); }
            list.Clear(); _byKey.Clear(); _nextKey = 1;
        }

        public virtual void Update(float elapsed) { }
        public virtual void UpdateDirtyUnits() { }
        public virtual void StartCollectHistoryData() { }
    }
}
