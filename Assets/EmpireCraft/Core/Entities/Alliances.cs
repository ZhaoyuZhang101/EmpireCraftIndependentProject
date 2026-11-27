using System.Collections.Generic;

namespace EmpireCraft.Core
{
    // An alliance: realms that stand together. Like a war, it only records who is in it;
    // what allies owe each other is up to the systems. Mutate through WorldOps.
    public sealed class Alliance : Entity
    {
        public int Id;
        public string Name;
        public Realm Founder;
        // Forced on its members by hand (the base game's god power): no alliance is, here, yet.
        public bool IsForced() => false;
        public int FoundedMonth;
        public int EndedMonth = -1;
        public readonly List<Realm> Members = new List<Realm>();       // in the order they joined
        public readonly List<Realm> PastMembers = new List<Realm>();   // those who left, and all of them once it ends
        public override long Key => Id;
        public override bool Alive => EndedMonth < 0;
        public override EntityKind KindOfEntity => EntityKind.Alliance;
        public Alliance Record => this;
        public HashSet<Realm> MemberSet => new HashSet<Realm>(Members);
        public bool Has(Realm realm) => realm != null && Members.Contains(realm);
        public void SetName(string name) { if (!string.IsNullOrWhiteSpace(name)) Name = name; }
        // The troops of all its members.
        public int CountWarriors() { long total = 0; foreach (var realm in Members) if (realm.Alive) total += realm.Warriors(); return (int)System.Math.Min(int.MaxValue, total); }
        public static bool IsSame(Alliance a, Alliance b) => a != null && b != null && a == b;
    }

    // What two realms have between them beyond wars and alliances. For now: when they last made peace.
    public sealed class RealmRelation
    {
        public readonly Realm A, B;
        public double LastWarEnded = -1d;      // GameClock stamp; -1 when they never fought to an end
        internal RealmRelation(Realm a, Realm b) { A = a; B = b; }
        public RealmRelation Record => this;
    }

    // A reason one realm thinks well or ill of another.
    public sealed class OpinionKind
    {
        public readonly string Id;
        public OpinionKind(string id) { Id = id; }
        public static readonly OpinionKind AtWar = new OpinionKind("at_war"), Allied = new OpinionKind("allied");
    }

    // What one realm thinks of another: the reasons and their sum. Above or at zero is good will.
    public sealed class RealmOpinion
    {
        public int Total;
        public readonly Dictionary<OpinionKind, int> Results = new Dictionary<OpinionKind, int>();
    }

    // Relations and opinions between the realms of one world. Relations follow from the wars on
    // record, so they are worked out again on load and not saved. Opinions are reckoned on demand
    // from what this layer knows for a fact - a war between the two, a shared alliance - and are
    // where the ported opinion rules will add theirs.
    public sealed class Diplomacy
    {
        public const int WarOpinion = -100, AllyOpinion = 50;
        readonly EmpireWorld _world;
        readonly Dictionary<(int, int), RealmRelation> _relations = new Dictionary<(int, int), RealmRelation>();
        internal Diplomacy(EmpireWorld world) { _world = world; }

        public RealmRelation GetRelation(Realm a, Realm b)
        {
            if (a == null || b == null || a == b) return null;
            var key = a.Id < b.Id ? (a.Id, b.Id) : (b.Id, a.Id);
            if (!_relations.TryGetValue(key, out RealmRelation relation)) _relations[key] = relation = a.Id < b.Id ? new RealmRelation(a, b) : new RealmRelation(b, a);
            return relation;
        }

        public RealmOpinion GetOpinion(Realm from, Realm to)
        {
            if (from == null || to == null || from == to) return null;
            var opinion = new RealmOpinion();
            if (from.IsAtWarWith(to)) { opinion.Results[OpinionKind.AtWar] = WarOpinion; opinion.Total += WarOpinion; }
            if (from.HasAlliance() && from.Alliance == to.Alliance) { opinion.Results[OpinionKind.Allied] = AllyOpinion; opinion.Total += AllyOpinion; }
            return opinion;
        }

        // Everyone on one side made peace with everyone on the other.
        internal void WarEnded(War war)
        {
            foreach (var attacker in Both(war.Attackers, war.PastAttackers))
                foreach (var defender in Both(war.Defenders, war.PastDefenders))
                {
                    var relation = GetRelation(attacker, defender);
                    if (relation != null && war.EndedMonth > relation.LastWarEnded) relation.LastWarEnded = war.EndedMonth;
                }
        }

        internal void Rebuild()
        {
            _relations.Clear();
            foreach (var war in _world.Wars) if (!war.Alive) WarEnded(war);
        }

        static IEnumerable<Realm> Both(List<Realm> now, List<Realm> past) { foreach (var r in now) yield return r; foreach (var r in past) yield return r; }
    }
}
