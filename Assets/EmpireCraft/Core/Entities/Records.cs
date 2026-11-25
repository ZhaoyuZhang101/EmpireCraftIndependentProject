using System.Collections.Generic;

namespace EmpireCraft.Core
{
    // Anything a ported system can hang state on or hold a key to: the four core
    // entities and every record object (empire, de jure title ...).
    public abstract class Entity
    {
        public abstract long Key { get; }
        public abstract bool Alive { get; }
    }

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
        // Called by the store when the object leaves the world.
        public virtual void Dispose() { _removed = true; }
    }

    // Holds the record objects of one kind for a world: creation with fresh keys,
    // lookup by key, removal. Subclasses add their own bookkeeping.
    public class RecordStore<TObject, TData> where TObject : RecordObject<TData>, new() where TData : RecordData, new()
    {
        public readonly List<TObject> list = new List<TObject>();
        readonly Dictionary<long, TObject> _byKey = new Dictionary<long, TObject>();
        long _nextKey = 1;
        public int Count => list.Count;
        public long NextKey => _nextKey;

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
            list.Remove(item); item.Dispose();
        }

        public virtual void Clear()
        {
            foreach (var item in list) item.Dispose();
            list.Clear(); _byKey.Clear(); _nextKey = 1;
        }

        public virtual void Update(float elapsed) { }
    }
}
