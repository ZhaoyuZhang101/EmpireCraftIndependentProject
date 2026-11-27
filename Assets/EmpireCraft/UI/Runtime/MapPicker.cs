using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace EmpireCraft.UI
{
    // 天下简图 (docs/design/ui-style.md §5.6): a small pixel map that is also a picker. The
    // world is sampled cell by cell into a point-filtered texture; each map key (a realm, a city,
    // a province…) gets the look its caller gives it, borders are drawn where keys change, and a
    // click hands the key back. Diplomacy, provinces and city actions all use it; callers only
    // say how each key looks and what a click does.
    //
    // The owner keeps the picker across page rebuilds (the view, zoom and texture live here) and
    // calls Redraw() and re-adds Root when its page is built again.
    public sealed class MapPicker
    {
        public struct Look
        {
            public Color32 Fill;
            public Edge Edge;
            public string Label;   // name written on the map (null: none)
            public bool Clickable;
        }

        public enum Edge : byte { Plain, Gold, White }

        public readonly VisualElement Root;
        public Texture2D Texture => texture;   // the drawn map (save thumbnails use it)
        public Func<int, int> KeyAt;           // tile → key (-1: nobody's)
        public Func<int, bool> IsWater;
        public Func<int, Look> LookOf;          // key → look
        public Action<int> Pick;                // a clickable key was clicked
        public Action<int> Hover;               // key under the pointer (-1 when off any key)

        readonly int worldW, worldH, texel, tw, th;
        readonly VisualElement image, labels;
        readonly Texture2D texture;
        readonly Color32[] pixels;
        readonly int[] keys;
        float x0, y0, rw, rh;                   // the view, in tiles (x wraps)
        bool pressed, dragged; Vector2 pressAt, lastAt;

        static readonly Color32 Sea = new Color32(29, 58, 92, 255), SeaLine = new Color32(34, 66, 102, 255),
            Wild = new Color32(74, 66, 54, 255), Ink = new Color32(12, 8, 7, 255),
            GoldEdge = new Color32(250, 224, 140, 255), WhiteEdge = new Color32(255, 255, 255, 255);

        public MapPicker(int worldWidth, int worldHeight, int width = 336, int height = 128, int texelSize = 4)
        {
            worldW = worldWidth; worldH = worldHeight; texel = Mathf.Max(1, texelSize);
            tw = Mathf.Max(8, width / texel); th = Mathf.Max(8, height / texel);
            texture = new Texture2D(tw, th, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "MapPicker" };
            pixels = new Color32[tw * th]; keys = new int[tw * th];
            Root = new VisualElement(); Root.AddToClassList("ec-mappick");
            Root.style.width = tw * texel + 4; Root.style.height = th * texel + 4;
            image = new VisualElement { pickingMode = PickingMode.Ignore }; image.AddToClassList("ec-mappick-img");
            image.style.width = tw * texel; image.style.height = th * texel;
            image.style.backgroundImage = new StyleBackground(texture);
            labels = new VisualElement { pickingMode = PickingMode.Ignore }; labels.AddToClassList("ec-mappick-labels");
            Root.Add(image); Root.Add(labels);
            ShowAll();
            Root.RegisterCallback<PointerDownEvent>(OnDown);
            Root.RegisterCallback<PointerMoveEvent>(OnMove);
            Root.RegisterCallback<PointerUpEvent>(OnUp);
            Root.RegisterCallback<PointerLeaveEvent>(_ => { pressed = false; Hover?.Invoke(-1); });
            Root.RegisterCallback<WheelEvent>(OnWheel);
        }

        // ---------- view ----------
        public void ShowAll() { x0 = 0; y0 = 0; rw = worldW; rh = worldW * (float)th / tw; if (rh > worldH) { rh = worldH; rw = worldH * (float)tw / th; } Center(worldW / 2f, worldH / 2f); }

        // Aim at a set of tiles (with a margin), keeping the picture's proportions.
        public void FocusOn(IEnumerable<int> tiles, float margin = .35f)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue; int n = 0;
            foreach (int t in tiles)
            {
                if (t < 0) continue;
                float x = t % worldW, y = t / worldW;
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); n++;
            }
            if (n == 0) { ShowAll(); return; }
            float w = (maxX - minX + 1) * (1 + 2 * margin), h = (maxY - minY + 1) * (1 + 2 * margin);
            float aspect = (float)tw / th;
            if (w / h < aspect) w = h * aspect; else h = w / aspect;
            w = Mathf.Clamp(w, tw * .5f, worldW); h = w / aspect;
            rw = w; rh = h;
            Center((minX + maxX) / 2f, (minY + maxY) / 2f);
        }

        void Center(float cx, float cy)
        {
            x0 = cx - rw / 2f;
            y0 = Mathf.Clamp(cy - rh / 2f, 0, Mathf.Max(0, worldH - rh));
        }

        int TileAt(int i, int j)
        {
            int x = Mathf.FloorToInt(x0 + (i + .5f) * rw / tw); x = ((x % worldW) + worldW) % worldW;
            int y = Mathf.Clamp(Mathf.FloorToInt(y0 + (j + .5f) * rh / th), 0, worldH - 1);
            return y * worldW + x;
        }

        // ---------- drawing ----------
        public void Redraw()
        {
            if (KeyAt == null || LookOf == null) return;
            var looks = new Dictionary<int, Look>();
            Look LookFor(int key) { if (!looks.TryGetValue(key, out var l)) looks[key] = l = LookOf(key); return l; }
            for (int j = 0; j < th; j++)
                for (int i = 0; i < tw; i++)
                {
                    int tile = TileAt(i, j), k = j * tw + i;
                    bool water = IsWater != null && IsWater(tile);
                    int key = water ? -2 : KeyAt(tile);
                    keys[k] = key;
                    pixels[k] = key == -2 ? (j % 3 == 0 ? SeaLine : Sea) : key < 0 ? Wild : LookFor(key).Fill;
                }
            // Borders: a texel next to another key takes its own key's edge colour.
            for (int j = 0; j < th; j++)
                for (int i = 0; i < tw; i++)
                {
                    int k = j * tw + i, key = keys[k];
                    if (key < 0) continue;
                    bool edge = (i + 1 < tw && keys[k + 1] != key) || (i > 0 && keys[k - 1] != key) ||
                                (j + 1 < th && keys[k + tw] != key) || (j > 0 && keys[k - tw] != key);
                    if (!edge) continue;
                    var e = LookFor(key).Edge;
                    pixels[k] = e == Edge.White ? WhiteEdge : e == Edge.Gold ? GoldEdge : Ink;
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            PlaceLabels(LookFor);
        }

        void PlaceLabels(Func<int, Look> lookFor)
        {
            labels.Clear();
            var sum = new Dictionary<int, (long x, long y, int n)>();
            for (int j = 0; j < th; j++)
                for (int i = 0; i < tw; i++)
                {
                    int key = keys[j * tw + i];
                    if (key < 0) continue;
                    sum.TryGetValue(key, out var s); sum[key] = (s.x + i, s.y + j, s.n + 1);
                }
            var order = new List<KeyValuePair<int, (long x, long y, int n)>>(sum);
            order.Sort((a, b) => b.Value.n.CompareTo(a.Value.n));
            int placed = 0;
            foreach (var kv in order)
            {
                if (placed >= 14 || kv.Value.n < 2) break;
                string text = lookFor(kv.Key).Label;
                if (string.IsNullOrEmpty(text)) continue;
                float cx = (kv.Value.x / (float)kv.Value.n + .5f) * texel, cy = (th - kv.Value.y / (float)kv.Value.n - .5f) * texel;
                var l = new Label(text) { pickingMode = PickingMode.Ignore };
                l.AddToClassList("ec-mappick-label");
                l.style.left = cx; l.style.top = cy;
                labels.Add(l);
                placed++;
            }
        }

        // ---------- input ----------
        int KeyUnder(Vector2 local)
        {
            int i = Mathf.FloorToInt((local.x - 2) / texel), j = th - 1 - Mathf.FloorToInt((local.y - 2) / texel);
            if (i < 0 || j < 0 || i >= tw || j >= th) return -1;
            return keys[j * tw + i];
        }

        void OnDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            pressed = true; dragged = false; pressAt = lastAt = e.localPosition;
            Root.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e)
        {
            if (pressed)
            {
                Vector2 p = e.localPosition;
                if (!dragged && (p - pressAt).sqrMagnitude > 16) dragged = true;
                if (dragged)
                {
                    Vector2 d = p - lastAt; lastAt = p;
                    x0 -= d.x / texel * rw / tw;
                    y0 = Mathf.Clamp(y0 + d.y / texel * rh / th, 0, Mathf.Max(0, worldH - rh));
                    Redraw();
                }
                return;
            }
            Hover?.Invoke(KeyUnder(e.localPosition));
        }

        void OnUp(PointerUpEvent e)
        {
            if (!pressed) return;
            pressed = false;
            if (Root.HasPointerCapture(e.pointerId)) Root.ReleasePointer(e.pointerId);
            if (dragged) return;
            int key = KeyUnder(e.localPosition);
            if (key >= 0 && LookOf != null && LookOf(key).Clickable) Pick?.Invoke(key);
            e.StopPropagation();
        }

        void OnWheel(WheelEvent e)
        {
            float f = e.delta.y > 0 ? 1.25f : .8f;
            int i = Mathf.FloorToInt((e.localMousePosition.x - 2) / texel), j = th - 1 - Mathf.FloorToInt((e.localMousePosition.y - 2) / texel);
            float cx = x0 + (i + .5f) * rw / tw, cy = y0 + (j + .5f) * rh / th;
            float nw = Mathf.Clamp(rw * f, tw * .5f, worldW), nh = nw * th / tw;
            if (nh > worldH) { nh = worldH; nw = nh * tw / th; }
            x0 = cx - (cx - x0) * nw / rw; y0 = Mathf.Clamp(cy - (cy - y0) * nh / rh, 0, Mathf.Max(0, worldH - nh));
            rw = nw; rh = nh;
            Redraw();
            e.StopPropagation();
        }
    }
}
