using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{
    // Books and plots as the ported systems hold them: records with a key, kept in a
    // store per world. Nothing writes books yet; plots are started and run by the
    // ported decision system (see PlotKind).

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
        public void IncreaseReadTimes() { }
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

    // A kind of plot: what is being plotted (a war, an empire, a union ...). The ported decision
    // system (DecisionSystem) runs them; each kind says who may start it, what it costs, how much
    // work it takes, and what happens when it is done. The field names are the ones the ported
    // definitions fill in (EmpireCraftPlotsAddition); the defaults are what a definition that leaves
    // a field out means.
    public sealed class PlotKind
    {
        public string Id;
        public string path_icon;
        public string group_id;
        // Started by persons on their own (otherwise only when another system names it).
        public bool is_basic_plot;
        public bool can_be_done_by_king, can_be_done_by_leader, can_be_done_by_clan_member;
        // Only while the world allows diplomacy, rebellions.
        public bool requires_diplomacy, requires_rebellion;
        // Standing (renown tiers) and talents the author needs, renown of author and realm.
        public int min_level, min_renown_actor, min_renown_kingdom;
        public int min_intelligence = 2, min_diplomacy = 2, min_warfare = 2, min_stewardship = 2;
        public int money_cost = 5;
        public float progress_needed = 60f;
        // Which targets must still be there for the plot to go on.
        public bool check_target_actor, check_target_city, check_target_kingdom, check_target_alliance, check_target_war;
        public Func<Person, bool> check_is_possible, check_can_be_forced, check_should_continue, action, post_action;
        public Func<Person, PlotKind, bool, bool> try_to_start_advanced;

        // The world's laws let it run (diplomacy, rebellions switched on where it needs them).
        public bool IsAllowedByWorldLaws() =>
            (!requires_diplomacy || WorldLaws.Diplomacy?.IsEnabled() != false) && (!requires_rebellion || WorldLaws.Rebellions?.IsEnabled() != false);

        // Whether this person holds one of the stations that may start it.
        public bool canBeDoneByRole(Person person) =>
            person != null && (can_be_done_by_king && person.IsRuler() || can_be_done_by_leader && person.IsCityLeader() ||
                               can_be_done_by_clan_member && person.HasClan());
    }

    // All kinds of plot, by id. The ported definitions add theirs at rule load.
    public sealed class PlotsLibrary
    {
        public readonly List<PlotKind> list = new List<PlotKind>();
        public readonly Dictionary<string, PlotKind> dict = new Dictionary<string, PlotKind>();
        public readonly List<PlotKind> basic_plots = new List<PlotKind>();

        // Kinds the base game of the mod had; this game has none of its own.
        public static PlotKind rebellion, new_war, alliance_create;

        public PlotKind add(PlotKind kind)
        {
            if (kind?.Id == null) return kind;
            if (dict.TryGetValue(kind.Id, out PlotKind old)) { list.Remove(old); basic_plots.Remove(old); }
            dict[kind.Id] = kind;
            list.Add(kind);
            if (kind.is_basic_plot) basic_plots.Add(kind);
            return kind;
        }

        public PlotKind get(string id) => id != null && dict.TryGetValue(id, out PlotKind kind) ? kind : null;
        public List<PlotKind> getList() => list;
        public void linkAssets() { }
        public void Clear() { list.Clear(); dict.Clear(); basic_plots.Clear(); }
    }

    public static class PlotKinds
    {
        public static readonly PlotsLibrary Library = new PlotsLibrary();
    }

    public enum PlotState
    {
        Active = 0,
        Finished = 1,
        Cancelled = 2
    }

    public sealed class PlotRecord : RecordData
    {
        public string kind;
        public long author_id = -1L;
        public long target_realm_id = -1L, target_city_id = -1L, target_person_id = -1L, target_alliance_id = -1L, target_war_id = -1L;
        public bool forced;
        public float progress_current;
        public PlotState state;
    }

    // Something a person is working towards against or with a realm. The decision system keeps
    // the work done; the plot is the badge that says so and that other systems look up.
    public sealed class Plot : RecordObject<PlotRecord>
    {
        public EmpireWorld World;
        // The progress last shown and when (for a progress bar).
        public float ProgressShown;
        public double LastProgressAt;

        public PlotKind Kind => data == null ? null : PlotKinds.Library.get(data.kind);
        public PlotState GetState() => data == null ? PlotState.Cancelled : data.state;
        public bool IsActive() => Alive && data.state == PlotState.Active;
        public PlotKind GetKind() => Kind;
        public float ProgressNeeded() => Kind?.progress_needed ?? 0f;
        public bool IsSameType(PlotKind kind) => kind != null && data != null && data.kind == kind.Id;
        public Person GetAuthor() => World == null || data == null ? null : World.PersonByKey(data.author_id);

        public Realm TargetRealm
        {
            get { return World == null || data == null ? null : World.RealmByKey(data.target_realm_id); }
            set { if (data != null) data.target_realm_id = value == null ? -1L : value.Key; }
        }
        public City TargetCity
        {
            get { return World == null || data == null ? null : World.CityByKey(data.target_city_id); }
            set { if (data != null) data.target_city_id = value == null ? -1L : value.Key; }
        }
        public Person TargetPerson
        {
            get { return World == null || data == null ? null : World.PersonByKey(data.target_person_id); }
            set { if (data != null) data.target_person_id = value == null ? -1L : value.Key; }
        }
        public Alliance TargetAlliance
        {
            get { return World == null || data == null ? null : World.AllianceByKey(data.target_alliance_id); }
            set { if (data != null) data.target_alliance_id = value == null ? -1L : value.Key; }
        }
        public War TargetWar
        {
            get { return World == null || data == null ? null : World.WarByKey(data.target_war_id); }
            set { if (data != null) data.target_war_id = value == null ? -1L : value.Key; }
        }

        // The author is still alive and still on this plot, and every target the kind cares about is still there.
        public bool TargetsStillThere()
        {
            Person author = GetAuthor();
            if (author == null || author.Plot != this) return false;
            PlotKind kind = Kind;
            if (kind == null) return true;
            if (kind.check_target_kingdom && data.target_realm_id >= 0 && TargetRealm == null) return false;
            if (kind.check_target_city && data.target_city_id >= 0 && TargetCity == null) return false;
            if (kind.check_target_actor && data.target_person_id >= 0 && TargetPerson == null) return false;
            if (kind.check_target_alliance && data.target_alliance_id >= 0 && TargetAlliance == null) return false;
            if (kind.check_target_war && data.target_war_id >= 0 && TargetWar == null) return false;
            return true;
        }

        // Closes the plot (done or given up): the author is free again and the badge leaves the record.
        public void Finish(PlotState state, Person author = null)
        {
            if (data == null || data.state != PlotState.Active) return;
            data.state = state == PlotState.Active ? PlotState.Cancelled : state;
            author ??= World?.People != null && data.author_id >= 0 && data.author_id < World.People.Count ? World.People[(int)data.author_id] : null;
            if (author != null && author.Plot == this) author.Plot = null;
            World?.Plots?.RemoveObject(this);
        }

        // Switched off from outside: the same as giving up.
        public void SetAlive(bool alive)
        {
            if (!alive) Finish(PlotState.Cancelled);
        }
    }

    public sealed class PlotStore : RecordStore<Plot, PlotRecord>
    {
        readonly EmpireWorld _world;
        public PlotStore(EmpireWorld world) { _world = world; }

        // Told of every plot started (the decision system takes it up). Set by whoever runs plots.
        public static Action<Plot, Person, PlotKind> Started;

        public override Plot NewObjectFromKey(long key)
        {
            var made = base.NewObjectFromKey(key);
            made.World = _world; made.data.created_time = _world.Now();
            return made;
        }

        // A person starts a plot of this kind. One plot at a time; the kind's own conditions are the
        // caller's to check (the decision system weighs them before it gets here).
        public Plot newPlot(Person author, PlotKind kind, bool forced = false)
        {
            if (author == null || !author.Alive || kind?.Id == null || author.Plot != null && author.Plot.IsActive()) return null;
            var made = NewObject();
            made.data.kind = kind.Id; made.data.name = kind.Id; made.data.author_id = author.Key; made.data.forced = forced;
            author.Plot = made;
            Started?.Invoke(made, author, kind);
            return made;
        }

        // Whether this person or anyone of the realm is already on a plot of this kind.
        public bool isPlotTypeAlreadyRunning(Person author, PlotKind kind)
        {
            if (kind == null) return false;
            Realm realm = author?.Realm;
            foreach (Plot plot in list)
            {
                if (!plot.IsActive() || plot.data.kind != kind.Id) continue;
                Person other = plot.GetAuthor();
                if (other == author || realm != null && other?.Realm == realm) return true;
            }
            return false;
        }

        // Plots whose author has died or moved on to another plot are given up.
        public int Sweep()
        {
            int closed = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Plot plot = list[i];
                Person author = plot.GetAuthor();
                if (plot.IsActive() && author != null && author.Plot == plot) continue;
                plot.Finish(PlotState.Cancelled, author);
                if (list.Count > i && list[i] == plot) RemoveObject(plot);
                closed++;
            }
            return closed;
        }
    }
}
