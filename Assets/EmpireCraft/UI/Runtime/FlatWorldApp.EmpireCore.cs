using System;
using System.Collections.Generic;
using EmpireCraft.Core;
using EmpireCraft.Presentation;
using EmpireCraft.UI;
using UnityEngine;
using UnityEngine.UIElements;
using Action = System.Action;

namespace Orbis.Flat
{
    // The civilization HUD on the EmpireCraft core: the political map shows the core's
    // realms (each city's tiles in its realm's colour), clicking a tile follows its realm,
    // and the core's big news joins the chronicle strip. The archive windows that read the
    // core are in FlatWorldApp.CoreWindows.cs. ORBIS's own seeded nations are no longer
    // shown once the core runs.
    //
    // Values are read through Safe(): the ported systems are still being wired up, and a
    // figure that throws must leave a dash, not break the window.
    public sealed partial class FlatWorldApp
    {
        Realm activeRealm;
        int coreSelectedTile = -1;
        Texture2D coreOwners; Color32[] coreOwnerPixels;
        EmpireWorld coreOwnersWorld; int coreOwnersRevision = -1, coreOwnersYear = -1;
        long coreNewsSeen; EmpireWorld coreNewsWorld;

        static EmpireWorld CoreWorld => EmpireCoreDriver.Instance ? EmpireCoreDriver.Instance.World : null;

        // The core is on for this session: ORBIS's own nation tools give way to it.
        static bool CoreEnabled => EmpireCoreDriver.Enabled;

        // The core world that belongs to the map on screen (it lags a frame behind a new world).
        EmpireWorld CoreHere()
        {
            var w = CoreWorld;
            return w != null && World != null && ReferenceEquals(w.Map, World) ? w : null;
        }

        static string Safe(Func<string> read, string fallback = "—")
        {
            try { string s = read(); return string.IsNullOrEmpty(s) ? fallback : s; }
            catch (Exception) { return fallback; }
        }

        static T SafeValue<T>(Func<T> read, T fallback)
        {
            try { return read(); } catch (Exception) { return fallback; }
        }

        static readonly string[] MonthNames = { "正月", "二月", "三月", "四月", "五月", "六月", "七月", "八月", "九月", "十月", "冬月", "腊月" };
        static int YearOf(EmpireWorld w, int month) => w.StartYear + (int)Math.Floor(month / 12.0);
        // A year for display: 前 N 年 before the first year (there is no year 0).
        static string YearText(EmpireWorld w, int month) { int y = YearOf(w, month); return y >= 1 ? y.ToString() : "前" + (1 - y); }
        static string MonthName(int month) => MonthNames[((month % 12) + 12) % 12];

        // ---------- political map ----------
        // The owner texture keeps ORBIS's encoding: RGB is the realm's colour, alpha a realm
        // number (1..255, 0 = unclaimed) the shader compares to draw borders.
        void SyncCoreTerritory()
        {
            var w = CoreHere();
            if (w == null || !map || map.Material == null) return;
            int size = w.Width * w.Height;
            if (coreOwners == null || coreOwners.width != w.Width || coreOwners.height != w.Height)
            {
                if (coreOwners) Destroy(coreOwners);
                coreOwners = new Texture2D(w.Width, w.Height, TextureFormat.RGBA32, false, false)
                { name = "EmpireCraft realm colours", filterMode = FilterMode.Point, wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, anisoLevel = 0 };
                coreOwnerPixels = new Color32[size];
                coreOwnersRevision = -1;
            }
            // Realm colours can change too (a new dynasty, a renamed realm): repaint once a year as well.
            if (!ReferenceEquals(w, coreOwnersWorld) || w.TerritoryRevision != coreOwnersRevision || w.Year != coreOwnersYear)
            {
                coreOwnersWorld = w; coreOwnersRevision = w.TerritoryRevision; coreOwnersYear = w.Year;
                var tileCity = w.TileCity; var cities = w.Cities;
                for (int t = 0; t < size && t < tileCity.Length; t++)
                {
                    int c = tileCity[t];
                    var realm = c < 0 || c >= cities.Count ? null : cities[c].Realm;
                    coreOwnerPixels[t] = realm == null || !realm.Alive ? default
                        : new Color32((byte)(realm.Color >> 16), (byte)(realm.Color >> 8), (byte)realm.Color, (byte)(realm.Id % 255 + 1));
                }
                coreOwners.SetPixels32(coreOwnerPixels);
                coreOwners.Apply(false, false);
            }
            // ORBIS sets its own texture only when a material is made; the scroll surface and
            // atlas copy the map material every frame, the globe binds its own slot.
            if (map.Material.GetTexture("_OwnerTex") != coreOwners) map.Material.SetTexture("_OwnerTex", coreOwners);
            var globeSurface = globe && globe.View ? globe.View.SurfaceMaterial : null;
            if (globeSurface && globeSurface.GetTexture("_FlatOwners") != coreOwners) globeSurface.SetTexture("_FlatOwners", coreOwners);
        }

        // Clicking a tile on the map makes its realm the one the windows show.
        void FollowCoreSelection()
        {
            if (selected == coreSelectedTile) return;
            coreSelectedTile = selected;
            var w = CoreHere();
            if (w == null || selected < 0 || selected >= w.TileCity.Length) return;
            var realm = SafeValue(() => w.RealmAt(selected), null);
            if (realm != null && realm.Alive && realm != activeRealm) { activeRealm = realm; hudWindow?.Refresh(); }
        }

        // What the info panel says about the selected tile: its city and realm.
        string CoreTileNote(int tile)
        {
            var w = CoreHere();
            if (w == null || tile < 0 || tile >= w.TileCity.Length) return null;
            var city = w.CityAt(tile);
            if (city == null || !city.Alive) return "无主之地";
            return city.Realm != null && city.Realm.Alive ? city.Realm.Name + " · " + city.Name : city.Name;
        }

        static long CoreTotalPopulation(EmpireWorld w)
        {
            double total = 0;
            foreach (var city in w.Cities) if (city.Alive) total += city.Population;
            return (long)total;
        }

        // Camera to the realm's seat; the windows follow the realm.
        void FocusCoreRealm(Realm realm)
        {
            if (realm == null) return;
            activeRealm = realm;
            if (realm.Capital != null) FocusCoreTile(realm.Capital.Tile);
            hudWindow?.Refresh();
        }

        void FocusCoreTile(int tile)
        {
            if (World == null || tile < 0 || tile >= World.Count || !navigation || Busy) return;
            selected = tile; coreSelectedTile = tile; map.Select(tile);
            navigation.Focus(tile, CalendarDay);
            UpdateHUD();
        }

        // ---------- names ----------
        static string CultureOf(Realm realm) => Safe(() => realm.GetEmpireCraftCulture(true));
        static string CultureId(Realm realm) => SafeValue(() => CultureService.GetRealmCulture(realm), null);
        static string CultureName(string id) => Safe(() => id.GetCultureTranslate(), id ?? "—");
        static string RegimeOf(Realm realm)
        {
            return Safe(() =>
            {
                var regime = realm.GetRegime();
                return regime == null ? null : CompositeEmpireService.GetRegimeName(regime.type);
            });
        }

        // Big core news (the kinds that would flash on screen) joins the chronicle strip.
        // Returns whether anything was added.
        bool FeedCoreNews()
        {
            var w = CoreHere();
            if (w == null) return false;
            var items = w.News.Items;
            long posted = w.News.Posted;
            if (!ReferenceEquals(w, coreNewsWorld)) { coreNewsWorld = w; coreNewsSeen = posted; return false; }   // a new world: start from now
            long fresh = Math.Min(posted - coreNewsSeen, items.Count);
            coreNewsSeen = posted;
            bool added = false;
            for (int i = items.Count - (int)fresh; i < items.Count; i++)
            {
                var item = items[i];
                if (!item.OnScreen || string.IsNullOrWhiteSpace(item.Text)) continue;
                hudFeed.Insert(0, ("第 " + YearOf(w, item.Month) + " 年 " + (item.Month % 12 + 1) + " 月", item.Text.Trim()));
                added = true;
            }
            while (hudFeed.Count > HudFeedRows) hudFeed.RemoveAt(hudFeed.Count - 1);
            return added;
        }
    }
}
