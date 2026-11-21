using System;
using System.Collections.Generic;
using Orbis.Planet;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.Flat
{
    internal sealed class FlatWorldMinimap
    {
        static readonly Vector2[] Corners = {
            new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1)
        };
        static readonly Color Ink = new Color(.045f, .08f, .09f, .96f);
        static readonly Color Ivory = new Color(1, .95f, .77f);
        static readonly Color Gold = new Color(1, .76f, .34f);
        static readonly Vector2[] Axes = { Vector2.right, Vector2.up, Vector2.left, Vector2.down };
        readonly VisualElement viewport, frame, map;
        readonly VisualElement scale, scaleLine;
        readonly Label scaleDistance;
        int lastScaleMetres = -1;
        readonly Action<PlanetLocation> recenter;
        readonly List<Vector2> segments = new List<Vector2>(440);
        FlatScrollNavigator navigation;
        FlatMapRenderer terrain;
        Texture2D basemap;
        int[] basemapTiles;
        int sourceWidth;
        Matrix4x4 previousMatrix;
        Vector2 previousOrigin, center;
        float previousSpin, previousMorph, nextRefresh;
        int previousRevision, basemapRevision, pointer = -1;
        bool previousAtlas, valid, wholeAtlas;

        internal FlatWorldMinimap(VisualElement viewport, Action<PlanetLocation> recenter)
        {
            this.viewport = viewport;
            this.recenter = recenter;
            frame = viewport.Q("minimapFrame");
            map = frame.Q("minimap");
            scale = viewport.Q("mapScale");
            scaleLine = scale.Q("mapScaleLine");
            scaleDistance = scale.Q<Label>("mapScaleDistance");
            scale.style.display = DisplayStyle.None;
            frame.style.display = DisplayStyle.None;
            viewport.RegisterCallback<GeometryChangedEvent>(e => Resize(e.newRect.size));
            map.generateVisualContent += Draw;
            frame.RegisterCallback<PointerDownEvent>(e => {
                if (e.button == 0 && pointer < 0)
                {
                    pointer = e.pointerId;
                    frame.CapturePointer(pointer);
                    viewport.Focus();
                }
                e.StopPropagation();
            });
            frame.RegisterCallback<PointerMoveEvent>(e => e.StopPropagation());
            frame.RegisterCallback<PointerUpEvent>(e => {
                if (pointer == e.pointerId && e.button == 0)
                {
                    frame.ReleasePointer(pointer);
                    pointer = -1;
                    var point = map.WorldToLocal(e.position);
                    var bounds = map.contentRect;
                    if (bounds.Contains(point) && TryMapLocation(point, bounds, out var location))
                    {
                        recenter(location);
                        nextRefresh = 0;
                    }
                }
                e.StopPropagation();
            });
            frame.RegisterCallback<PointerCaptureOutEvent>(e => { pointer = -1; e.StopPropagation(); });
            frame.RegisterCallback<WheelEvent>(e => e.StopPropagation());
        }

        void Resize(Vector2 size)
        {
            float width = Mathf.Max(0, Mathf.Min(240, Mathf.Min(size.x * .26f, size.y * .46f)));
            frame.style.width = width;
            frame.style.height = (width - 8) * .5f + 8;
            nextRefresh = 0;
            valid = false;
        }

        // Two symmetric screen samples measure the ground distance at the view centre,
        // including local projection, atlas distortion, date-line wrapping and zoom.
        void UpdateScale(FlatMinimapProjection projection)
        {
            float panelWidth = viewport.contentRect.width;
            if (panelWidth < 60) { scale.style.display = DisplayStyle.None; return; }
            float lineWidth = Mathf.Min(96, panelWidth * .16f);
            var offset = new Vector2(lineWidth / panelWidth, 0);
            var left = projection.Sample(-offset);
            var right = projection.Sample(offset);
            var a = PlanetCoordinateSystem.SurfaceNormal(PlanetCoordinateSystem.FromMapUV(left.x, left.y));
            var b = PlanetCoordinateSystem.SurfaceNormal(PlanetCoordinateSystem.FromMapUV(right.x, right.y));
            double metres = PlanetLocalProjection.Distance(a, b, 6371008.8);
            if (!(metres > 0)) { scale.style.display = DisplayStyle.None; return; }
            scale.style.display = DisplayStyle.Flex;
            scaleLine.style.width = lineWidth;
            int rounded = metres >= 1000 ? (int)Math.Round(metres / 1000) * 1000 : (int)Math.Round(metres);
            if (rounded == lastScaleMetres) return;
            lastScaleMetres = rounded;
            scaleDistance.text = rounded >= 1000 ? $"{rounded / 1000:N0} 公里" : $"{rounded} 米";
        }

        internal void Bind(FlatScrollNavigator navigator, FlatMapRenderer renderer)
        {
            navigation = navigator;
            terrain = renderer;
            basemapRevision = int.MinValue;
            frame.style.display = DisplayStyle.Flex;
            valid = false;
            nextRefresh = 0;
            Resize(viewport.contentRect.size);
        }

        void RefreshBasemap()
        {
            var world = terrain.World;
            if (!basemap || sourceWidth != world.Width)
            {
                if (basemap) UnityEngine.Object.Destroy(basemap);
                // The HUD is at most 240 pixels wide. Cache a supersampled inverse
                // lookup per map size, reused on terrain edits and while navigating.
                int width = Mathf.Clamp(world.Width, 480, 960);
                int height = width / 2;
                basemap = new Texture2D(width, height, TextureFormat.RGBA32, true)
                {
                    name = "Minimap terrain", filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp
                };
                basemapTiles = new int[width * height];
                sourceWidth = world.Width;
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int tile = -1;
                    if (HammerProjection.TryUnproject((x + .5) / width, (y + .5) / height, out var location))
                    {
                        int column = (int)(PlanetCoordinateSystem.MapU(location) * world.Width);
                        int row = Math.Min(world.Height - 1, (int)(PlanetCoordinateSystem.MapV(location) * world.Height));
                        tile = world.Index(column, row);
                    }
                    basemapTiles[y * width + x] = tile;
                }
                map.style.backgroundImage = new StyleBackground(basemap);
            }
            // The terrain shader stores water flags in alpha and extends land colour into
            // coastal texels. The HUD needs opaque land and the world's actual shoreline.
            var source = terrain.TerrainTexture.GetPixelData<Color32>(0);
            var target = basemap.GetPixelData<Color32>(0);
            for (int i = 0; i < target.Length; i++)
            {
                int tile = basemapTiles[i];
                if (tile < 0) { target[i] = default; continue; }
                var kind = world.Water[tile];
                var color = source[tile];
                if (kind == WaterKind.River)
                    color = Color32.Lerp(color, FlatPalette.Water(kind), 2 * world.Facts.HalfWidth[tile]);
                else if (kind != WaterKind.Land) color = FlatPalette.Water(kind);
                color.a = 255;
                target[i] = color;
            }
            basemap.Apply();
            if (map.panel != null) RuntimePanelUtils.SetTextureDirty(map.panel, basemap);
            basemapRevision = terrain.PublishedTerrainRevision;
        }

        internal void Dispose()
        {
            if (basemap) UnityEngine.Object.Destroy(basemap);
        }

        internal void Tick(Camera camera)
        {
            if (!navigation || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .05f;
            int revision = terrain.PublishedTerrainRevision;
            if (revision != basemapRevision) RefreshBasemap();
            var matrix = camera.projectionMatrix * camera.worldToCameraMatrix;
            var origin = navigation.MinimapOrigin;
            float spin = navigation.DisplaySpin, morph = navigation.MinimapMorph;
            bool atlas = navigation.IsAtlas;
            if (valid && matrix.Equals(previousMatrix) && previousSpin == spin && previousMorph == morph
                && origin == previousOrigin && atlas == previousAtlas && previousRevision == revision) return;
            previousMatrix = matrix;
            previousSpin = spin;
            previousMorph = morph;
            previousOrigin = origin;
            previousAtlas = atlas;
            previousRevision = revision;
            valid = true;

            var projection = navigation.MinimapProjection();
            UpdateScale(projection);
            center = projection.Sample(Vector2.zero);
            segments.Clear();
            int steps = projection.IsFlatAtlas ? 32 : morph <= 0 ? 20 : 8;
            for (int edge = 0; edge < Corners.Length; edge++)
            {
                var a = Corners[edge];
                var b = Corners[(edge + 1) % Corners.Length];
                var outward = (a + b) * .5f;
                var last = projection.Sample(a);
                for (int i = 1; i <= steps; i++)
                {
                    var position = Vector2.Lerp(a, b, (float)i / steps);
                    var current = projection.Sample(position);
                    var outside = Vector2.Lerp(a, b, (i - .5f) / steps) + outward * .002f;
                    if (!projection.IsFlatAtlas || !projection.CoveredByAtlasCopy(outside))
                        AddGeographicEdge(last, current);
                    last = current;
                }
            }
            wholeAtlas = projection.IsFlatAtlas && segments.Count == 0;
            map.MarkDirtyRepaint();
        }

        void AddGeographicEdge(Vector2 a, Vector2 b)
        {
            float dx = Mathf.Repeat(b.x - a.x + .5f, 1) - .5f;
            // A chart crossing a pole emerges half a world away. Both ends touch the
            // same pole, but drawing between their longitudes would mark unrelated land.
            if (Mathf.Abs(dx) > .38f && (Mathf.Min(a.y, b.y) > .90f || Mathf.Max(a.y, b.y) < .10f))
            {
                float pole = a.y > .5f ? 1 : 0;
                Add(a, new Vector2(a.x, pole));
                Add(new Vector2(b.x, pole), b);
                return;
            }
            if (Mathf.Abs(b.x - a.x) <= .5f) { Add(a, b); return; }
            float seam = dx > 0 ? 1 : 0;
            float latitude = Mathf.Abs(dx) < 1e-7f ? (a.y + b.y) * .5f : Mathf.Lerp(a.y, b.y, (seam - a.x) / dx);
            Add(a, new Vector2(seam, latitude));
            Add(new Vector2(1 - seam, latitude), b);
        }

        void Add(Vector2 from, Vector2 to)
        {
            segments.Add(from);
            segments.Add(to);
        }

        static Vector2 Pixel(Vector2 uv, Rect rect)
        {
            HammerProjection.Project(uv.x, uv.y, out double x, out double y);
            return new Vector2(rect.x + (float)x * rect.width, rect.y + (float)(1 - y) * rect.height);
        }

        static bool TryMapLocation(Vector2 point, Rect rect, out PlanetLocation location)
        {
            return HammerProjection.TryUnproject((point.x - rect.x) / (double)rect.width,
                1 - (point.y - rect.y) / (double)rect.height, out location);
        }

        void Draw(MeshGenerationContext context)
        {
            var rect = map.contentRect;
            if (!valid || rect.width < 1 || rect.height < 1) return;
            var pen = context.painter2D;
            pen.lineJoin = LineJoin.Round;
            pen.lineCap = LineCap.Round;
            pen.BeginPath();
            if (wholeAtlas)
            {
                var radius = rect.size * .5f - Vector2.one * 1.6f;
                for (int i = 0; i < 64; i++)
                {
                    float angle = i * (2 * Mathf.PI / 64);
                    var p = rect.center + Vector2.Scale(radius, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)));
                    if (i == 0) pen.MoveTo(p); else pen.LineTo(p);
                }
                pen.ClosePath();
            }
            else
            {
                for (int i = 0; i < segments.Count; i += 2)
                {
                    if (i == 0 || segments[i] != segments[i - 1]) pen.MoveTo(Pixel(segments[i], rect));
                    pen.LineTo(Pixel(segments[i + 1], rect));
                }
            }
            Stroke(pen, 3.5f, Ink);
            Stroke(pen, 1.5f, Ivory);
            var c = Pixel(center, rect);
            pen.BeginPath();
            Reticle(pen, c);
            float edge = center.x < .5f ? 0 : 1;
            var seam = Pixel(new Vector2(edge, center.y), rect);
            var other = Pixel(new Vector2(1 - edge, center.y), rect);
            if ((c - seam).sqrMagnitude < 49 && (other - seam).sqrMagnitude > 196)
                Reticle(pen, other + (c - seam));
            Stroke(pen, 3.5f, Ink);
            Stroke(pen, 1.7f, Gold);
        }

        static void Reticle(Painter2D pen, Vector2 center)
        {
            foreach (var axis in Axes)
            {
                pen.MoveTo(center + axis * 3);
                pen.LineTo(center + axis * 6);
            }
        }

        static void Stroke(Painter2D pen, float width, Color color)
        {
            pen.lineWidth = width;
            pen.strokeColor = color;
            pen.Stroke();
        }
    }
}
