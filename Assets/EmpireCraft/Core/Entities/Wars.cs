using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{
    public enum WarOutcome : byte { Nobody, Attackers, Defenders, Peace, Merged }

    // What sort of war it is. The ported systems pick one by name when they declare war.
    public sealed class WarKind
    {
        public readonly string Id;
        public readonly bool TotalWar;      // fought to the end of one side rather than to a settlement
        public WarKind(string id, bool totalWar = false) { Id = id; TotalWar = totalWar; }
    }

    public static class WarKinds
    {
        public static readonly WarKind Normal = new WarKind("normal");
        public static readonly WarKind Rebellion = new WarKind("rebellion", true);
        public static readonly WarKind WhisperOfWar = new WarKind("whisper_of_war");
        public static readonly WarKind Inspire = new WarKind("inspire");
        static readonly Dictionary<string, WarKind> ById = new Dictionary<string, WarKind>(StringComparer.Ordinal)
        { [Normal.Id] = Normal, [Rebellion.Id] = Rebellion, [WhisperOfWar.Id] = WhisperOfWar, [Inspire.Id] = Inspire };
        public static WarKind Get(string id) => id != null && ById.TryGetValue(id, out WarKind kind) ? kind : Normal;
    }

    // The record of one war: two sides, a leader on each, when it began and how it ended.
    // Who moves where and who takes what is not decided here; WorldOps keeps the sides.
    public sealed class War : Entity
    {
        public int Id;
        public string Name;
        public WarKind Kind = WarKinds.Normal;
        public Realm MainAttacker, MainDefender;
        public readonly List<Realm> Attackers = new List<Realm>(), Defenders = new List<Realm>();           // still fighting
        public readonly List<Realm> PastAttackers = new List<Realm>(), PastDefenders = new List<Realm>();   // left or fell
        public int StartedMonth, EndedMonth = -1;
        public WarOutcome Outcome;

        public override long Key => Id;
        public override EntityKind KindOfEntity => EntityKind.War;
        public override bool Alive => EndedMonth < 0;
        public War Record => this;
        public double StartedStamp => StartedMonth;
        public bool HasEnded() => EndedMonth >= 0;
        // Two wars are the same quarrel when the same two realms lead them, whichever attacked.
        public bool IsSameAs(War other) => other != null && other != this &&
            ((MainAttacker == other.MainAttacker && MainDefender == other.MainDefender) || (MainAttacker == other.MainDefender && MainDefender == other.MainAttacker));
        public bool IsAttacker(Realm realm) => realm != null && Attackers.Contains(realm);
        public bool IsDefender(Realm realm) => realm != null && Defenders.Contains(realm);
        public bool HasRealm(Realm realm) => IsAttacker(realm) || IsDefender(realm);
        // The two fight each other in this war (on opposite sides).
        public bool IsInWarWith(Realm a, Realm b) => IsAttacker(a) && IsDefender(b) || IsDefender(a) && IsAttacker(b);
        // A realm gives up: the main attacker's or defender's surrender ends the war for the other side, anyone else leaves.
        public void Lost(Realm loser)
        {
            var world = EmpireWorld.Current;
            if (world == null || !Alive || loser == null || !HasRealm(loser)) return;
            if (loser == MainAttacker) WorldOps.EndWar(world, this, WarOutcome.Defenders);
            else if (loser == MainDefender) WorldOps.EndWar(world, this, WarOutcome.Attackers);
            else WorldOps.LeaveWar(world, this, loser);
        }
        public Realm GetMainAttacker() => MainAttacker;
        public Realm GetMainDefender() => MainDefender;
        public List<Realm> GetAttackers() => Attackers;
        public List<Realm> GetDefenders() => Defenders;
        public WarKind GetKind() => Kind;
        public bool IsTotalWar() => Kind != null && Kind.TotalWar;
        public int DurationMonths() => (int)((HasEnded() ? EndedMonth : GameClock.Now) - StartedMonth);
        public int AgeYears() => DurationMonths() / 12;
        // The side a realm fights on faces the other one.
        public List<Realm> EnemiesOf(Realm realm) => IsAttacker(realm) ? Defenders : IsDefender(realm) ? Attackers : null;
    }

    // What the ported systems ask a realm about its wars.
    public static class RealmWars
    {
        public static IEnumerable<War> GetWars(this Realm realm, bool random = false) => realm.Wars;
        public static bool HasEnemies(this Realm realm) => realm.Wars.Count > 0;
        public static bool IsAtWarWith(this Realm realm, Realm other)
        {
            if (other == null || other == realm) return false;
            foreach (var war in realm.Wars) { var enemies = war.EnemiesOf(realm); if (enemies != null && enemies.Contains(other)) return true; }
            return false;
        }
        public static bool IsEnemy(this Realm realm, Realm other) => realm.IsAtWarWith(other);
        public static bool IsOnSameSide(this Realm realm, Realm other)
        {
            if (other == null || other == realm) return false;
            foreach (var war in realm.Wars)
                if ((war.IsAttacker(realm) && war.IsAttacker(other)) || (war.IsDefender(realm) && war.IsDefender(other))) return true;
            return false;
        }
        public static List<Realm> GetEnemies(this Realm realm)
        {
            var found = new List<Realm>();
            foreach (var war in realm.Wars) { var enemies = war.EnemiesOf(realm); if (enemies != null) foreach (var enemy in enemies) if (!found.Contains(enemy)) found.Add(enemy); }
            return found;
        }
    }
}
