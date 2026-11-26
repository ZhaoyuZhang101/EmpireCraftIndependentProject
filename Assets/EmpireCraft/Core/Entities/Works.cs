using System.Collections.Generic;

namespace EmpireCraft.Core
{
    // Books and plots as the ported systems hold them: records with a key, kept in a
    // store per world. Nothing writes books or hatches plots yet; the stores are there
    // so the systems that read them find them empty rather than missing.

    public sealed class BookRecord : RecordData
    {
        public long author_id = -1L;
        public long city_id = -1L;
    }

    // A written work kept in a city. What it says is the business of the system that had it written.
    public sealed class Book : RecordObject<BookRecord>
    {
        public EmpireWorld World;
        public Person Author => World == null || data == null ? null : World.PersonByKey(data.author_id);
        public City City => World == null || data == null ? null : World.CityByKey(data.city_id);
    }

    public sealed class BookStore : RecordStore<Book, BookRecord>
    {
        readonly EmpireWorld _world;
        public BookStore(EmpireWorld world) { _world = world; }
        public override Book NewObjectFromKey(long key)
        {
            var made = base.NewObjectFromKey(key);
            made.World = _world; made.data.created_time = _world.Now();
            return made;
        }
        // A new book by this author, kept in the author's home city. None is written where the city has nowhere to keep books.
        public Book GenerateNewBook(Person author)
        {
            if (author == null || !author.Alive || author.City == null || !author.City.HasBookSlots()) return null;
            var made = NewObject();
            made.data.author_id = author.Key; made.data.city_id = author.City.Key;
            return made;
        }
    }

    // A household. None is ever on record: kinship is kept on the persons and their houses.
    public sealed class Family : Entity
    {
        public long Id = -1L;
        public string Name;
        public override long Key => Id;
        public override bool Alive => false;
        public override EntityKind KindOfEntity => EntityKind.Family;
        public Family Record => this;
        public CustomDataContainer<bool> CustomDataBool;
    }

    // A kind of plot: what is being plotted (a war, a union ...).
    public sealed class PlotKind
    {
        public string Id;
    }

    public sealed class PlotRecord : RecordData
    {
        public string kind;
        public long author_id = -1L;
        public long target_realm_id = -1L;
    }

    // Something a person is working towards against or with a realm.
    public sealed class Plot : RecordObject<PlotRecord>
    {
        public EmpireWorld World;
        public PlotKind Kind;
        public bool Finished;
        public bool IsActive() => Alive && !Finished;
        public PlotKind GetKind() => Kind;
        public Person GetAuthor() => World == null || data == null ? null : World.PersonByKey(data.author_id);
        public Realm TargetRealm => World == null || data == null ? null : World.RealmByKey(data.target_realm_id);
    }

    public sealed class PlotStore : RecordStore<Plot, PlotRecord>
    {
        readonly EmpireWorld _world;
        public PlotStore(EmpireWorld world) { _world = world; }
        public override Plot NewObjectFromKey(long key)
        {
            var made = base.NewObjectFromKey(key);
            made.World = _world; made.data.created_time = _world.Now();
            return made;
        }
    }
}
