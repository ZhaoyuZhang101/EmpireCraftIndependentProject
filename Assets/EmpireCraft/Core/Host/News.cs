using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{
    // World news: what the ported systems announce ("so-and-so took the throne").
    // A message is a kind plus up to three texts; the kind's template in the language
    // tables says where the texts go. Posting puts the finished line on the world's
    // news log, where the client reads it. Nothing here draws anything.
    //
    // How loud a message is: Minor is routine, History is worth a line in a realm's
    // own history, Major is heard everywhere.
    public enum NewsLevel : byte { None, Minor, History, Major }

    public delegate void NewsTextReplacer(News message, ref string text);

    // A colour set as the ported systems hold one for a realm or a title: a main
    // colour, and the colour its name is written in. Colours are hints for the client.
    public sealed class Tint
    {
        public readonly string Id;          // "#RRGGBB"
        public readonly EcColor Main;
        public EcColor ColorText => Main;
        public string HexText => Id;
        Tint(string id, EcColor main) { Id = id; Main = main; }
        public EcColor GetColorText() => Main;
        public EcColor GetColorMain() => Main;
        public void InitColor() { }
        public static Tint FromRgb(uint rgb) => new Tint("#" + (rgb & 0xFFFFFF).ToString("X6"), EcColor.FromRgb(rgb));
        // Null for text that is not a colour.
        public static Tint FromHex(string hex) => EcColorUtility.TryParseHtmlString(hex, out EcColor parsed) ? FromRgb(parsed.Rgb) : null;
        // The colours of good and of worrying news.
        public static readonly EcColor Good = EcColor.FromRgb(0x8CE08C), Warning = EcColor.FromRgb(0xF0A050);
    }

    // One kind of message. The table of kinds is NewsKinds (generated from the mod's list).
    public sealed class NewsKind
    {
        public readonly string Id, Group;
        public readonly bool Major;                 // heard everywhere, whoever it is about
        public readonly EcColor Color = Tint.Good;
        public readonly NewsTextReplacer TextReplacer;
        readonly KeyValuePair<string, int>[] _slots; // placeholder in the template -> which of the three texts fills it
        public int Variants => 0;                    // kinds here have one wording each

        // Slots are written "1:$empire$": the first text goes where the template says $empire$.
        internal NewsKind(string id, string group, bool major, string[] slots)
        {
            Id = id; Group = group; Major = major;
            _slots = new KeyValuePair<string, int>[slots == null ? 0 : slots.Length];
            for (int i = 0; i < _slots.Length; i++)
            {
                int colon = slots[i].IndexOf(':');
                _slots[i] = new KeyValuePair<string, int>(slots[i].Substring(colon + 1), int.Parse(slots[i].Substring(0, colon)));
            }
            TextReplacer = Fill;
        }

        void Fill(News message, ref string text)
        {
            if (string.IsNullOrEmpty(text) || message == null) return;
            foreach (var slot in _slots) text = text.Replace(slot.Key, message.Special(slot.Value) ?? "");
        }

        public string LocaleId(int variant = 0) => Id;
        // The finished line in the loaded language; empty when the language has no template for this kind.
        public string Text(News message)
        {
            string text = LM.Has(Id) ? LM.Get(Id) : "";
            Fill(message, ref text);
            return text;
        }
    }

    // A message being put together by a system. Post it with Add().
    public sealed class News
    {
        public readonly NewsKind Kind;
        public string Special1, Special2, Special3;
        public EcColor Color1, Color2, Color3;      // colours for the three texts
        public Realm Realm;                          // whom it is about, if anyone
        public City City;
        public Person Person;
        public EcVector2 Location;
        public int Stamp;

        public News(NewsKind kind, string special1 = null, string special2 = null, string special3 = null)
        {
            Kind = kind; Special1 = special1; Special2 = special2; Special3 = special3;
        }

        public string KindId => Kind == null ? null : Kind.Id;
        public NewsKind GetKind() => Kind;
        public string Special(int index) => index == 1 ? Special1 : index == 2 ? Special2 : index == 3 ? Special3 : null;
        public string Text() => Kind == null ? "" : Kind.Text(this);
        public void Add() => WorldNews.Post(this);
    }

    // A posted message as the log keeps it.
    public sealed class NewsItem
    {
        public int Month;
        public string KindId, Group, Text;
        public NewsLevel Level;
        public int RealmId = -1;
        public bool Public;      // other realms hear of it too
        public bool OnScreen;    // big enough to flash on screen
    }

    // The recent news of one world, oldest first. It is a feed, not a record: only the
    // last Cap lines are kept and it is not saved.
    public sealed class NewsLog
    {
        public const int Cap = 512;
        public readonly List<NewsItem> Items = new List<NewsItem>();
        public long Posted;      // lines ever posted, dropped ones included
        internal void Push(NewsItem item)
        {
            Items.Add(item); Posted++;
            if (Items.Count > Cap) Items.RemoveRange(0, Items.Count - Cap);
        }
    }

    public static class WorldNews
    {
        public const string GenericKind = "empirecraft_event_log";   // free text: the level comes from Pending
        public const int MajorCities = 8;
        // While a system posts free text, how loud it is and whom it is about.
        public static NewsLevel Pending = NewsLevel.None;
        public static Realm PendingRealm;
        // A realm big enough for its own history to be news. The ported code may widen this (empires and their members).
        public static Func<Realm, bool> MajorRealm = realm => realm != null && realm.Alive && realm.Cities.Count >= MajorCities;
        // Raised after a line is on the log.
        public static event Action<News, NewsItem> Posted;

        static bool Generic(News message) => message != null && message.Kind != null && message.Kind.Id == GenericKind;

        public static NewsLevel LevelOf(News message)
        {
            if (message == null || message.Kind == null) return NewsLevel.None;
            if (Generic(message)) return Pending;
            return message.Kind.Major ? NewsLevel.Major : NewsLevel.History;
        }

        // News other realms hear too: the major kinds, and free text posted as major.
        public static bool IsPublic(News message) => LevelOf(message) == NewsLevel.Major;

        // Big events only: major news, and the history of an empire-sized realm.
        public static bool OnScreen(News message)
        {
            NewsLevel level = LevelOf(message);
            if (level == NewsLevel.Major) return true;
            if (level != NewsLevel.History || !Generic(message)) return false;
            return MajorRealm != null && MajorRealm(PendingRealm ?? message.Realm);
        }

        public static void Post(News message)
        {
            var world = EmpireWorld.Current;
            if (world == null || message == null || message.Kind == null) return;
            string text = message.Text();
            if (string.IsNullOrWhiteSpace(text)) return;
            Realm about = Generic(message) ? PendingRealm ?? message.Realm : message.Realm;
            var item = new NewsItem
            {
                Month = world.Month, KindId = message.Kind.Id, Group = message.Kind.Group, Text = text, Level = LevelOf(message),
                RealmId = about == null ? -1 : about.Id, Public = IsPublic(message), OnScreen = OnScreen(message)
            };
            world.News.Push(item);
            Posted?.Invoke(message, item);
        }
    }
}
