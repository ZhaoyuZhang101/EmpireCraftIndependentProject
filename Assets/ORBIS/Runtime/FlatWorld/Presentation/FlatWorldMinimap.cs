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
        readonly Action<PlanetLocation> recenter;
        readonly List<Vector2> segments = new List<Vector2>(440);
        FlatScrollNavigator navigation;
        FlatMapRenderer terrain;
        Texture2D basemap;
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
                    if (bounds.Contains(point))
                    {
                        recenter(PlanetCoordinateSystem.FromMapUV((point.x - bounds.x) / bounds.width,
                            1 - (point.y - bounds.y) / bounds.height));
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
            if (!basemap || basemap.width != world.Width || basemap.height != world.Height)
            {
                if (basemap) UnityEngine.Object.Destroy(basemap);
                basemap = new Texture2D(world.Width, world.Height, TextureFormat.RGBA32, true)
                {
                    name = "Minimap terrain", filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp
                };
                map.style.backgroundImage = new StyleBackground(basemap);
            }
            // The terrain shader stores water flags in alpha and extends land colour into
            // coastal texels. The HUD needs opaque land and the world's actual shoreline.
            var source = terrain.TerrainTexture.GetPixelData<Color32>(0);
            var target = basemap.GetPixelData<Color32>(0);
            for (int i = 0; i < target.Length; i++)
            {
                var kind = world.Water[i];
                var color = source[i];
                if (kind == WaterKind.River)
                    color = Color32.Lerp(color, FlatPalette.Water(kind), 2 * world.Facts.HalfWidth[i]);
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
            return new Vector2(rect.x + uv.x * rect.width, rect.y + (1 - uv.y) * rect.height);
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
                const float pad = 1.6f;
                pen.MoveTo(new Vector2(rect.xMin + pad, rect.yMin + pad));
                pen.LineTo(new Vector2(rect.xMax - pad, rect.yMin + pad));
                pen.LineTo(new Vector2(rect.xMax - pad, rect.yMax - pad));
                pen.LineTo(new Vector2(rect.xMin + pad, rect.yMax - pad));
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
            if (c.x - rect.xMin < 7) Reticle(pen, c + Vector2.right * rect.width);
            if (rect.xMax - c.x < 7) Reticle(pen, c - Vector2.right * rect.width);
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
