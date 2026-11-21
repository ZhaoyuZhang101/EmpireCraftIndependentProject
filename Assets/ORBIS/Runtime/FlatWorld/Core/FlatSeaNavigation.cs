using System;
using System.Collections.Generic;
using System.Threading;

namespace Orbis.Flat
{
    /// <summary>Read-only marine routing on a published world. Waypoints use WGS84 degrees.</summary>
    public sealed class FlatSeaNavigation
    {
        readonly WorldData world;
        readonly FlatStraitNavigation survey;

        public FlatSeaNavigation(WorldData world, FlatStraitNavigation survey = null)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            if (world.UsesSurveyedStraits)
            {
                if (world.WaterReference == null)
                    throw new InvalidOperationException("Restore the Earth's water reference before planning a surveyed sea route.");
                this.survey = survey ?? throw new ArgumentNullException(nameof(survey));
            }
        }

        public static bool IsMarineCell(WorldData world, int tile)
        {
            return tile >= 0 && tile < world.Count && world.Elevation[tile] <= 0 &&
                FlatTerrainAnalysis.Ocean(world.Water[tile]);
        }

        static bool Valid(FlatSeaPoint p) => p.Longitude >= -180 && p.Longitude <= 180 &&
            p.Latitude >= -90 && p.Latitude <= 90;
        int Tile(FlatSeaPoint p) => world.Index((int)((p.Longitude + 180) * world.Width / 360),
            Math.Min(world.Height - 1, (int)((p.Latitude + 90) * world.Height / 180)));
        static bool Same(FlatSeaPoint a, FlatSeaPoint b) =>
            a.Longitude == b.Longitude && a.Latitude == b.Latitude;
        internal FlatSeaPoint Center(int tile) => new FlatSeaPoint(
            (tile % world.Width + .5) * 360 / world.Width - 180,
            (tile / world.Width + .5) * 180 / world.Height - 90);

        public bool IsWater(FlatSeaPoint p)
        {
            if (!Valid(p)) return false;
            if (survey != null && survey.TryWater(world, p, out bool wet)) return wet;
            return IsMarineCell(world, Tile(p));
        }

        /// <summary>Returns ordered sailing waypoints, or an empty array if the endpoints cannot connect.</summary>
        public FlatSeaPoint[] Find(FlatSeaPoint start, FlatSeaPoint end, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            if (!IsWater(start) || !IsWater(end)) return Array.Empty<FlatSeaPoint>();
            if (Same(start, end)) return new[] { start };
            int region = survey?.RegionAt(start) ?? 0;
            if (region != 0 && region == survey.RegionAt(end) &&
                survey.TryRoute(world, start, end, out var local, cancel)) return local;

            int first = AccessCell(start, cancel), last = AccessCell(end, cancel);
            if (first < 0 || last < 0) return Array.Empty<FlatSeaPoint>();
            var cells = MarineCells(first, last, cancel);
            if (cells.Length == 0) return Array.Empty<FlatSeaPoint>();
            var result = new List<FlatSeaPoint> { start };
            FlatSeaPoint previous = start;
            int previousRegion = region;
            // A single local solve spans each continuous survey area. Tile centres
            // inside a strait need not lie in the navigable part of that tile.
            for (int i = 0; i < cells.Length; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var point = Center(cells[i]);
                int chart = survey?.RegionAt(point) ?? 0;
                bool firstInChart = previousRegion != chart;
                bool lastInChart = i + 1 == cells.Length || chart == 0 ||
                    survey.RegionAt(Center(cells[i + 1])) != chart;
                if (chart != 0 && !firstInChart && !lastInChart) continue;
                if (chart != 0 && !survey.TryAnchor(world, cells[i], out point)) return Array.Empty<FlatSeaPoint>();
                // A polar face joins opposite meridians across the pole itself.
                if (i > 0 && chart == 0 && previousRegion == 0 &&
                    Math.Abs(cells[i] - cells[i - 1]) == world.Width / 2 &&
                    cells[i] / world.Width == cells[i - 1] / world.Width)
                {
                    double pole = point.Latitude < 0 ? -90 : 90;
                    result.Add(new FlatSeaPoint(previous.Longitude, pole));
                    result.Add(new FlatSeaPoint(point.Longitude, pole));
                }
                if (!Join(result, point, previousRegion, chart, cancel)) return Array.Empty<FlatSeaPoint>();
                previous = point;
                previousRegion = chart;
            }
            return Join(result, end, previousRegion, survey?.RegionAt(end) ?? 0, cancel)
                ? result.ToArray() : Array.Empty<FlatSeaPoint>();
        }

        bool Join(List<FlatSeaPoint> path, FlatSeaPoint end, int fromRegion, int toRegion, CancellationToken cancel)
        {
            var start = path[path.Count - 1];
            if (Same(start, end)) return true;
            if ((fromRegion != 0 || toRegion != 0) && !survey.SegmentIsWater(world, start, end))
            {
                if (!survey.TryRoute(world, start, end, out var leg, cancel)) return false;
                for (int i = 1; i < leg.Length; i++) path.Add(leg[i]);
            }
            else path.Add(end);
            return true;
        }

        int AccessCell(FlatSeaPoint point, CancellationToken cancel)
        {
            int tile = Tile(point), region = survey?.RegionAt(point) ?? 0;
            if (region == 0) return IsMarineCell(world, tile) ? tile : -1;
            // A plotted inlet may have a dry mean elevation. Attach the selected
            // water point only to an ocean cell reachable within its own chart.
            var seen = new HashSet<int> { tile };
            var queue = new Queue<int>();
            queue.Enqueue(tile);
            while (queue.Count != 0)
            {
                cancel.ThrowIfCancellationRequested();
                int i = queue.Dequeue();
                if (IsMarineCell(world, i) && survey.TryAnchor(world, i, out var anchor) &&
                    (survey.SegmentIsWater(world, point, anchor) ||
                     survey.TryRoute(world, point, anchor, out _, cancel))) return i;
                for (int d = 0; d < 4; d++)
                {
                    int n = world.SphereNeighbor(i, d);
                    if (seen.Add(n) && survey.RegionAt(Center(n)) == region) queue.Enqueue(n);
                }
            }
            return -1;
        }

        // A bidirectional face flood visits the smaller frontier first. The parent
        // direction and side occupy one byte per visited tile in lazily allocated pages.
        int[] MarineCells(int start, int end, CancellationToken cancel)
        {
            if (start == end) return new[] { start };
            const int shift = 12, pageSize = 1 << shift, reverse = 8, root = 5;
            var pages = new byte[(world.Count + pageSize - 1) / pageSize][];
            byte Mark(int i) => pages[i >> shift]?[i & (pageSize - 1)] ?? 0;
            void Set(int i, int code)
            {
                var page = pages[i >> shift] ?? (pages[i >> shift] = new byte[pageSize]);
                page[i & (pageSize - 1)] = (byte)code;
            }
            void Trace(List<int> path, int i)
            {
                for (;;)
                {
                    path.Add(i);
                    int mark = Mark(i) & 7;
                    if (mark == root) return;
                    i = world.SphereNeighbor(i, mark - 1);
                }
            }
            var left = new List<int> { start };
            var right = new List<int> { end };
            var next = new List<int>();
            Set(start, root); Set(end, reverse | root);
            while (left.Count != 0 && right.Count != 0)
            {
                bool forward = left.Count <= right.Count;
                var frontier = forward ? left : right;
                int side = forward ? 0 : reverse;
                for (int k = 0; k < frontier.Count; k++)
                {
                    if ((k & 1023) == 0) cancel.ThrowIfCancellationRequested();
                    int i = frontier[k];
                    for (int d = 0; d < 4; d++)
                    {
                        int n = world.SphereNeighbor(i, d);
                        if (!IsMarineCell(world, n)) continue;
                        int mark = Mark(n);
                        if (mark != 0)
                        {
                            if ((mark & reverse) == side) continue;
                            var path = new List<int>();
                            Trace(path, forward ? i : n);
                            path.Reverse();
                            Trace(path, forward ? n : i);
                            return path.ToArray();
                        }
                        int back = (d == 2 && i < world.Width) || (d == 3 && i >= world.Count - world.Width)
                            ? d : d ^ 1;
                        Set(n, side | (back + 1));
                        next.Add(n);
                    }
                }
                frontier.Clear();
                if (forward) left = next; else right = next;
                next = frontier;
            }
            return Array.Empty<int>();
        }
    }
}
