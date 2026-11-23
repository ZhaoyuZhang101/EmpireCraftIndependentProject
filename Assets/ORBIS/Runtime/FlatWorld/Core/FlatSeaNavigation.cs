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
        bool ModelSurface => survey==null && world.Facts!=null;

        public FlatSeaNavigation(WorldData world, FlatStraitNavigation survey = null)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            if (world.UsesSurveyedStraits)
            {
                if (world.WaterReference == null)
                    throw new InvalidOperationException("Restore the Earth's water reference before planning a surveyed sea route.");
                this.survey = survey ?? world.GeographicSurface?.Regional ?? throw new ArgumentNullException(nameof(survey));
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
            if(ModelSurface)return FlatTerrainAnalysis.Ocean(FlatGeographicSurface.WaterAt(world,p));
            return IsMarineCell(world, Tile(p));
        }

        /// <summary>Checks the whole displayed geographic leg, including both exact endpoints.</summary>
        public bool SegmentIsWater(FlatSeaPoint a,FlatSeaPoint b)
        {
            if(!Valid(a)||!Valid(b))return false;
            if(survey!=null)return survey.SegmentIsWater(world,a,b);
            return FlatModelSeaNavigation.Segment(world,a,b);
        }

        /// <summary>Returns ordered sailing waypoints, or an empty array if the endpoints cannot connect.</summary>
        public FlatSeaPoint[] Find(FlatSeaPoint start, FlatSeaPoint end, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            if (!IsWater(start) || !IsWater(end)) return Array.Empty<FlatSeaPoint>();
            if (Same(start, end)) return new[] { start };
            int region = survey?.RegionAt(start) ?? 0;
            bool global = survey!=null && survey.CanRouteOutsideCharts(world);
            double near = Math.Max(1,3*360.0/world.Width);
            bool localSpan = Math.Abs(FlatGeographicSurface.Wrap(end.Longitude-start.Longitude))<=near && Math.Abs(end.Latitude-start.Latitude)<=near;
            if ((region != 0 && region == survey.RegionAt(end) || global && localSpan) &&
                survey.TryRoute(world, start, end, out var local, cancel)) return local;
            if(ModelSurface && localSpan && FlatModelSeaNavigation.TryRoute(world,start,end,out var model,cancel))return model;

            int first = AccessCell(start, cancel), last = AccessCell(end, cancel);
            if (first < 0 || last < 0) return Array.Empty<FlatSeaPoint>();
            var blockedTiles=new HashSet<int>();
            var blockedFaces=new HashSet<long>();
            for (int attempt=0;attempt<12;attempt++)
            {
                var cells=MarineCells(first,last,cancel,blockedTiles,blockedFaces);
                if(cells.Length==0)return Array.Empty<FlatSeaPoint>();
                var track=TraceRoute(start,end,cells,region,global,cancel,out int blockedTile,out long blockedFace);
                if(track.Length>0)return track;
                if(!global && !ModelSurface)return track;
                // A model ocean tile may be an entire island, or have two
                // disconnected bays. Let the coarse search try another face.
                if(blockedTile>=0 && blockedTile!=first && blockedTile!=last && blockedTiles.Add(blockedTile))continue;
                if(blockedFace>=0 && blockedFaces.Add(blockedFace))continue;
                return track;
            }
            return Array.Empty<FlatSeaPoint>();
        }

        static long Face(int a,int b) => ((long)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);

        FlatSeaPoint[] TraceRoute(FlatSeaPoint start,FlatSeaPoint end,int[] cells,int region,bool global,
            CancellationToken cancel,out int blockedTile,out long blockedFace)
        {
            blockedTile=-1;blockedFace=-1;
            var result = new List<FlatSeaPoint> { start };
            FlatSeaPoint previous = start;
            int previousRegion = region;
            // A regional solve spans a continuous chart. Its tile centres
            // need not lie inside the navigable part of a narrow strait.
            for (int i = 0; i < cells.Length; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var point = Center(cells[i]);
                int chart = survey?.RegionAt(point) ?? 0;
                bool firstInChart = previousRegion != chart;
                bool lastInChart = i + 1 == cells.Length || chart == 0 ||
                    survey.RegionAt(Center(cells[i + 1])) != chart;
                if (chart != 0 && !firstInChart && !lastInChart) continue;
                if ((chart != 0 || global) && !survey.TryAnchor(world, cells[i], out point))
                { blockedTile=cells[i];return Array.Empty<FlatSeaPoint>(); }
                if (i>0)blockedFace=Face(cells[i-1],cells[i]);
                // Crossing a polar face follows both meridians to the pole.
                if (i > 0 && chart == 0 && previousRegion == 0 &&
                    Math.Abs(cells[i] - cells[i - 1]) == world.Width / 2 &&
                    cells[i] / world.Width == cells[i - 1] / world.Width)
                {
                    double pole = point.Latitude < 0 ? -90 : 90;
                    if(!Join(result,new FlatSeaPoint(previous.Longitude,pole),0,0,cancel) ||
                       !Join(result,new FlatSeaPoint(point.Longitude,pole),0,0,cancel))return Array.Empty<FlatSeaPoint>();
                }
                if (!Join(result, point, previousRegion, chart, cancel))return Array.Empty<FlatSeaPoint>();
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
            if(ModelSurface && !FlatModelSeaNavigation.Segment(world,start,end))
            {
                if(!FlatModelSeaNavigation.TryRoute(world,start,end,out var detour,cancel))return false;
                for(int i=1;i<detour.Length;i++)path.Add(detour[i]);
            }
            else if (survey!=null && (fromRegion!=0 || toRegion!=0 || survey.CanRouteOutsideCharts(world)) &&
                !survey.SegmentIsWater(world, start, end))
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
            bool worldwide=survey!=null && survey.CanRouteOutsideCharts(world);
            if(ModelSurface)
            {
                if(IsMarineCell(world,tile) && FlatModelSeaNavigation.Segment(world,point,Center(tile)))return tile;
                int x=tile%world.Width,y=tile/world.Width;
                for(int r=0;r<=3;r++)for(int j=-r;j<=r;j++)for(int i=-r;i<=r;i++)
                {
                    if(Math.Max(Math.Abs(i),Math.Abs(j))!=r)continue;
                    int candidate=world.Index(x+i,y+j);
                    if(IsMarineCell(world,candidate) && FlatModelSeaNavigation.TryRoute(world,point,Center(candidate),out _,cancel))return candidate;
                }
                return -1;
            }
            if (region == 0 && !worldwide) return IsMarineCell(world, tile) ? tile : -1;
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
                    if(!seen.Add(n))continue;
                    if(region!=0 ? survey.RegionAt(Center(n))==region :
                       Math.Abs(n/world.Width-tile/world.Width)<=3 &&
                       Math.Min(Math.Abs(n%world.Width-tile%world.Width),world.Width-Math.Abs(n%world.Width-tile%world.Width))<=3)
                        queue.Enqueue(n);
                }
            }
            return -1;
        }

        // A bidirectional face flood visits the smaller frontier first. The parent
        // direction and side occupy one byte per visited tile in lazily allocated pages.
        int[] MarineCells(int start,int end,CancellationToken cancel,HashSet<int> blockedTiles,HashSet<long> blockedFaces)
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
                        if (!IsMarineCell(world, n) || blockedTiles.Contains(n) || blockedFaces.Contains(Face(i,n))) continue;
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
