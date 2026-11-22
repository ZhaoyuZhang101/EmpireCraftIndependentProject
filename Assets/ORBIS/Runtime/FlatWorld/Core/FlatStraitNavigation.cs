using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace Orbis.Flat
{
    /// <summary>WGS84 longitude and latitude in degrees.</summary>
    public readonly struct FlatSeaPoint
    {
        public readonly double Longitude, Latitude;
        public FlatSeaPoint(double longitude, double latitude)
        {
            Longitude = longitude;
            Latitude = latitude;
        }
    }

    /// <summary>The geographic shoreline used by the surface shaders. Safe to share between route queries.</summary>
    public sealed class FlatStraitNavigation
    {
        const int Columns = 1440, Rows = 720, Page = 128, Resolution = 512;
        readonly int CoarseWidth, FineWidth, headerRows, regionalColumns, fineColumns, estuaryColumns;
        readonly byte[] coarse, fine;
        readonly int[] regions;
        readonly bool[] fullSurvey;
        const double KmPerDegree = 111.19508;
        // Half the equatorial diagonal of a native texel plus its distance-code rounding.
        const double SailingClearanceKm = .20;

        FlatStraitNavigation(byte[] coarse, byte[] fine, int cw, int fw)
        {
            this.coarse = coarse;
            this.fine = fine;
            CoarseWidth = cw; FineWidth = fw;
            headerRows = (Columns * Rows + cw - 1) / cw;
            regionalColumns = cw / 18; fineColumns = fw / 130; estuaryColumns = cw / 66;
            int slots = Math.Min(65535,(coarse.Length / (2 * cw) - headerRows) / 19 * regionalColumns);
            regions = new int[slots + 1];
            fullSurvey = new bool[slots + 1];
            var used = new bool[slots + 1];
            int lastSlot = 0, lastEstuary = 0;
            for (int i = 0; i < Columns * Rows; i++)
            {
                int s = Index(i);
                if (s == 0)
                    continue;
                if (s > slots || used[s])
                    throw new InvalidDataException("Invalid regional shoreline directory.");
                used[s] = true;
                lastSlot = Math.Max(lastSlot, s);
                int p = Regional(s), f = coarse[p] | coarse[p + 1] << 8;
                int m = coarse[p + 2] | coarse[p + 3] << 8;
                if (f > fine.Length / FineWidth / 130 * fineColumns)
                    throw new InvalidDataException("Invalid fine shoreline page.");
                if (f != 0 && m != 0) throw new InvalidDataException("Ambiguous shoreline detail.");
                lastEstuary = Math.Max(lastEstuary, m);
                bool full = true;
                for (int y = 1; y <= 18 && full; y++)
                    for (int x = 0; x < 18; x++)
                        if (coarse[p + 2 * (y * CoarseWidth + x) + 1] != 255)
                        {
                            full = false;
                            break;
                        }
                fullSurvey[s] = full;
            }
            int rows = coarse.Length/(2*CoarseWidth);
            if (headerRows + ((lastSlot+regionalColumns-1)/regionalColumns)*19 +
                ((lastEstuary+2*estuaryColumns-1)/(2*estuaryColumns))*66 > rows)
                throw new InvalidDataException("Overlapping regional shoreline pages.");
            var pending = new Queue<int>();
            int next = 0;
            for (int i = 0; i < Columns * Rows; i++)
            {
                int s = Index(i);
                if (s == 0 || regions[s] != 0)
                    continue;
                regions[s] = ++next;
                pending.Enqueue(i);
                while (pending.Count != 0)
                {
                    int t = pending.Dequeue(), x = t % Columns, y = t / Columns;
                    Visit(y * Columns + (x + Columns - 1) % Columns);
                    Visit(y * Columns + (x + 1) % Columns);
                    if (y > 0)
                        Visit(t - Columns);
                    if (y + 1 < Rows)
                        Visit(t + Columns);
                }
            }
            void Visit(int i)
            {
                int s = Index(i);
                if (s == 0 || regions[s] != 0)
                    return;
                regions[s] = next;
                pending.Enqueue(i);
            }
        }

        public static FlatStraitNavigation Read(byte[] bytes)
        {
            if (bytes == null || bytes.Length <= 20)
                throw new InvalidDataException("Missing strait shoreline.");
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream);
            uint format = reader.ReadUInt32();
            int cw = reader.ReadInt32();
            int h = reader.ReadInt32(), fw = reader.ReadInt32(), fh = reader.ReadInt32();
            bool legacy = format == 0x3148534fu;
            if (legacy ? cw != 2048 || fw != 4096 :
                format != 0x3248534fu || (cw != 2048 && cw != 4096 && cw != 8192) || (fw != 4096 && fw != 8192))
                throw new InvalidDataException("Invalid strait atlas format or width.");
            int maxSize = legacy ? 4096 : 8192, header = (Columns*Rows+cw-1)/cw;
            if (h <= header || h > maxSize || (h-header)%19 != 0 || fh < 130 || fh > maxSize || fh%130 != 0)
                throw new InvalidDataException("Invalid strait atlas dimensions.");
            using var inflater = new DeflateStream(stream, CompressionMode.Decompress);
            byte[] ReadPageBytes(int length)
            {
                var data = new byte[length];
                for (int n = 0; n < length;)
                {
                    int read = inflater.Read(data, n, length - n);
                    if (read == 0)
                        throw new InvalidDataException("Truncated OSH1 shoreline.");
                    n += read;
                }
                return data;
            }
            var regional = ReadPageBytes(2 * cw * h);
            var detail = ReadPageBytes(fw * fh);
            if (inflater.ReadByte() != -1)
                throw new InvalidDataException("Trailing OSH1 shoreline pixels.");
            return new FlatStraitNavigation(regional, detail, cw, fw);
        }

        static bool Valid(FlatSeaPoint p) => p.Longitude >= -180 && p.Longitude <= 180 && p.Latitude >= -90 && p.Latitude <= 90;
        static double Wrap(double lon) => lon - 360 * Math.Floor((lon + 180) / 360);
        int Index(int tile) => coarse[2 * tile] | coarse[2 * tile + 1] << 8;
        int Regional(int s) => 2 * ((headerRows + (s - 1) / regionalColumns * 19) * CoarseWidth + (s - 1) % regionalColumns * 18);
        int Fine(int s) => (s - 1) / fineColumns * 130 * FineWidth + (s - 1) % fineColumns * 130;
        static double Blend(double a, double b, double t) => a + (b - a) * t;
        static double Smooth(double a, double b, double v)
        {
            double t = Math.Max(0, Math.Min(1, (v - a) / (b - a)));
            return t * t * (3 - 2 * t);
        }
        static double Lookup(byte[] data, int origin, int stride, int channels, double x, double y)
        {
            int i = (int)x, j = (int)y, p = origin + (j * stride + i) * channels;
            double u = x - i, v = y - j;
            return Blend(Blend(data[p], data[p + channels], u), Blend(data[p + stride * channels], data[p + (stride + 1) * channels], u), v);
        }
        static int Chart(FlatSeaPoint p) => Math.Min(Rows - 1, (int)((p.Latitude + 90) * 4)) * Columns + (int)((Wrap(p.Longitude) + 180) * 4);
        public int RegionAt(FlatSeaPoint point) => Valid(point) ? regions[Index(Chart(point))] : 0;

        double Sample(FlatSeaPoint p, out double strength)
        {
            double x = (Wrap(p.Longitude) + 180) * 4, y = Math.Min(Rows - 1e-8, (p.Latitude + 90) * 4);
            int ix = (int)x, iy = (int)y, s = Index(iy * Columns + ix);
            if (s == 0)
            {
                strength = 0;
                return 0;
            }
            x -= ix;
            y -= iy;
            int header = Regional(s), basePixel = header + 2 * CoarseWidth;
            strength = fullSurvey[s] ? 1 : Lookup(coarse, basePixel + 1, CoarseWidth, 2, .5 + x * 16, .5 + y * 16) / 255;
            double km = (Lookup(coarse, basePixel, CoarseWidth, 2, .5 + x * 16, .5 + y * 16) - 128) * .5;
            int f = coarse[header] | coarse[header + 1] << 8;
            if (strength <= .975 || Math.Abs(km) >= 6)
                return km;
            double code;
            if (f != 0)
                code = Lookup(fine, Fine(f), FineWidth, 1, .5 + x * 128, .5 + y * 128);
            else
            {
                int m = (coarse[header+2] | coarse[header+3] << 8) - 1;
                if (m < 0) return km;
                int row = coarse.Length/(2*CoarseWidth) - (m/(2*estuaryColumns)+1)*66;
                int column = m/2 % estuaryColumns * 66;
                int origin = 2*(row*CoarseWidth+column) + m%2;
                code = Lookup(coarse, origin, CoarseWidth, 2, .5+x*64, .5+y*64);
            }
            double detailed = (code - 128) / 16;
            return Blend(km, detailed, Smooth(.975, .999, strength) * (1 - Smooth(2, 6, Math.Abs(km))));
        }

        static int Tile(WorldData w, FlatSeaPoint p) => w.Index((int)((Wrap(p.Longitude) + 180) * w.Width / 360),
            Math.Min(w.Height - 1, (int)((p.Latitude + 90) * w.Height / 180)));
        static bool Enabled(WorldData w) => w != null && w.Preset == TerrainPreset.Earth && w.UsesSurveyedStraits && w.WaterReference != null;
        const int Marine = 1, OriginalBed = 2, Survey = 4;
        static int Flags(WorldData w, int tile, Dictionary<int, int> memo)
        {
            if (memo != null && memo.TryGetValue(tile, out int known))
                return known;
            int flags = FlatSeaNavigation.IsMarineCell(w, tile) ? Marine : 0;
            if (w.Water[tile] != WaterKind.Lake && w.WaterReference.MatchesOriginalBed(tile, w.Elevation[tile]))
            {
                flags |= OriginalBed;
                bool pristine = true;
                int x = tile % w.Width, y = tile / w.Width;
                for (int dy = -1; dy <= 1 && pristine; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int n = w.Index(x + dx, y + dy);
                        if (n >= 0 && !w.WaterReference.MatchesOriginalBed(n, w.Elevation[n]))
                        {
                            pristine = false;
                            break;
                        }
                    }
                if (pristine)
                    flags |= Survey;
            }
            if (memo != null)
                memo[tile] = flags;
            return flags;
        }
        bool Water(WorldData w, FlatSeaPoint p, Dictionary<int, int> memo, out bool covered, double clearanceKm = 0)
        {
            int tile = Tile(w, p);
            covered = Enabled(w) && Index(Chart(p)) != 0;
            if (!covered)
                return FlatSeaNavigation.IsMarineCell(w, tile);
            int flags = Flags(w, tile, memo);
            if ((flags & OriginalBed) == 0)
                return (flags & Marine) != 0;
            double km = Sample(p, out double strength);
            if (strength <= 0)
                return (flags & Marine) != 0;
            if (km <= clearanceKm)
                return false;
            // The presentation fades to the simulated shore around a brush. An
            // untouched dry bank in that halo cannot become a passage past a dam.
            if ((flags & Survey) == 0)
                return (flags & Marine) != 0;
            if (strength >= .99999 || (flags & Marine) != 0)
                return true;
            // In the geographic feather, a sub-cell marine arm must still be
            // visible after the same signed field blend as the player surface.
            double model = w.Facts == null ? -.5 : FlatSurfaceSampling.SampleShore(w,
                (float)((Wrap(p.Longitude) + 180) * w.Width / 360), (float)((p.Latitude + 90) * w.Height / 180));
            return Blend(model, km * w.Width / 40030.2, strength) > 0;
        }
        public bool TryWater(WorldData world, FlatSeaPoint point, out bool wet)
        {
            wet = false;
            if (!Enabled(world) || !Valid(point))
                return false;
            wet = Water(world, point, null, out bool covered);
            return covered;
        }
        public bool TryAnchor(WorldData world, int tile, out FlatSeaPoint point)
        {
            point = default;
            if (world == null || !FlatSeaNavigation.IsMarineCell(world, tile))
                return false;
            double step = 360.0 / world.Width, west = tile % world.Width * step - 180, south = tile / world.Width * step - 90;
            point = new FlatSeaPoint(west + .5 * step, south + .5 * step);
            if (Water(world, point, null, out _))
                return true;
            int limit = (int)Math.Ceiling(step * Resolution * 2);
            var memo = new Dictionary<int, int>();
            for (int div = 4; div <= Math.Max(4, 2 * limit); div *= 2)
            {
                double best = double.MaxValue;
                for (int y = 0; y < div; y++)
                    for (int x = 0; x < div; x++)
                    {
                        double u = (x + .5) / div, v = (y + .5) / div, d = (u - .5) * (u - .5) + (v - .5) * (v - .5);
                        if (d >= best)
                            continue;
                        var q = new FlatSeaPoint(west + step * u, south + step * v);
                        if (Water(world, q, memo, out _))
                        {
                            point = q;
                            best = d;
                        }
                    }
                if (best < double.MaxValue)
                    return true;
            }
            return false;
        }
        static FlatSeaPoint Interpolate(FlatSeaPoint a, double dx, double dy, double t) =>
            new FlatSeaPoint(Wrap(a.Longitude + dx * t), a.Latitude + dy * t);
        public bool SegmentIsWater(WorldData world, FlatSeaPoint a, FlatSeaPoint b) =>
            world != null && Valid(a) && Valid(b) && Segment(world, a, b, null);

        // Split first at exact simulation faces and survey page boundaries,
        // including sub-pixel crossings near a corner. Inside a surveyed page
        // verify the signed shoreline at intervals of at most half a native texel.
        bool Segment(WorldData w, FlatSeaPoint a, FlatSeaPoint b, Dictionary<int, int> memo)
        {
            if (!Water(w, a, memo, out _) || !Water(w, b, memo, out _))
                return false;
            double dx = Wrap(b.Longitude - a.Longitude), dy = b.Latitude - a.Latitude;
            if (dx == 0 && dy == 0)
                return true;
            double dxGround = dx * Math.Cos((a.Latitude + b.Latitude) * Math.PI / 360);
            double lengthKm = Math.Sqrt(dxGround * dxGround + dy * dy) * KmPerDegree;
            bool nearStart = !Water(w, a, memo, out _, SailingClearanceKm), nearEnd = !Water(w, b, memo, out _, SailingClearanceKm);
            // Keep exact player picks in shallow water. Only their short approach
            // may enter the shoreline raster's error band.
            bool Navigable(double t, out bool covered)
            {
                double limit = SailingClearanceKm;
                if (nearStart)
                    limit = Math.Min(limit, t * lengthKm * .5);
                if (nearEnd)
                    limit = Math.Min(limit, (1 - t) * lengthKm * .5);
                return Water(w, Interpolate(a, dx, dy, t), memo, out covered, limit);
            }
            double Next(double start, double delta, double scale, double t)
            {
                if (delta == 0)
                    return double.PositiveInfinity;
                double grid = (start + delta * t) * scale;
                double boundary = delta > 0 ? Math.Floor(grid + 1e-8) + 1 : Math.Ceiling(grid - 1e-8) - 1;
                return (boundary / scale - start) / delta;
            }
            double gs = w.Width / 360.0, extent = Math.Max(Math.Abs(dx), Math.Abs(dy));
            for (double t = 0; t < 1;)
            {
                double col = Next(a.Longitude + 180, dx, gs, t), row = Next(a.Latitude + 90, dy, gs, t);
                double end = Math.Min(1, Math.Min(Math.Min(col, row),
                    Math.Min(Next(a.Longitude + 180, dx, 4, t), Next(a.Latitude + 90, dy, 4, t))));
                if (end <= t)
                    return false;
                if (!Navigable((t + end) * .5, out bool regional))
                    return false;
                if (regional)
                {
                    int n = Math.Max(1, (int)Math.Ceiling((end - t) * extent * Resolution * 2));
                    for (int i = 0; i <= n; i++)
                        if (!Navigable(Blend(t, end, (double)i / n), out _))
                            return false;
                }
                // A diagonal meeting of two dry faces is not a shipping passage.
                if (end < 1 && Math.Abs(col - row) < 1e-10 && Math.Abs(end - col) < 1e-10)
                {
                    var q = Interpolate(a, dx, dy, end);
                    double nudge = dx > 0 ? 1e-8 : -1e-8, vertical = dy > 0 ? 1e-8 : -1e-8;
                    if (!Water(w, new FlatSeaPoint(Wrap(q.Longitude + nudge), q.Latitude - vertical), memo, out _) &&
                       !Water(w, new FlatSeaPoint(Wrap(q.Longitude - nudge), q.Latitude + vertical), memo, out _))
                        return false;
                }
                t = end;
            }
            return true;
        }

        public bool TryRoute(WorldData world, FlatSeaPoint start, FlatSeaPoint end, out FlatSeaPoint[] path,
            CancellationToken cancel = default)
        {
            path = Array.Empty<FlatSeaPoint>();
            cancel.ThrowIfCancellationRequested();
            if (!Enabled(world) || !Valid(start) || !Valid(end))
                return false;
            int a = RegionAt(start), b = RegionAt(end);
            if ((a == 0 && b == 0) || (a != 0 && b != 0 && a != b))
                return false;
            if (!Water(world, start, null, out _) || !Water(world, end, null, out _))
                return false;
            if (start.Longitude == end.Longitude && start.Latitude == end.Latitude)
            {
                path = new[] { start };
                return true;
            }
            var search = new LocalSearch(this, world, a != 0 ? a : b, start, end, cancel);
            return search.Find(out path);
        }

        sealed class LocalSearch
        {
            readonly FlatStraitNavigation data; readonly WorldData world; readonly int region;
            readonly FlatSeaPoint start, end; readonly CancellationToken cancel;
            readonly Dictionary<int, int> flags = new Dictionary<int, int>();
            readonly Dictionary<int, int[]> pages = new Dictionary<int, int[]>();
            readonly List<Node> nodes = new List<Node> { null };
            static readonly int[] Empty = Array.Empty<int>();
            sealed class Node
            {
                internal int X, Y, Size, Parent; internal double Cost = double.PositiveInfinity;
                internal FlatSeaPoint Portal;
                internal FlatSeaPoint Center => Point(X + Size * .5, Y + Size * .5);
            }
            internal LocalSearch(FlatStraitNavigation data, WorldData world, int region, FlatSeaPoint start, FlatSeaPoint end, CancellationToken cancel)
            {
                this.data = data;
                this.world = world;
                this.region = region;
                this.start = start;
                this.end = end;
                this.cancel = cancel;
            }
            static FlatSeaPoint Point(double x, double y) => new FlatSeaPoint(x / Resolution - 180, y / Resolution - 90);
            static int X(FlatSeaPoint p) => (int)Math.Floor((Wrap(p.Longitude) + 180) * Resolution);
            static int Y(FlatSeaPoint p) => (int)Math.Floor((p.Latitude + 90) * Resolution);
            static double Distance(FlatSeaPoint a, FlatSeaPoint b)
            {
                double x = Wrap(a.Longitude - b.Longitude) * Math.Cos((a.Latitude + b.Latitude) * Math.PI / 360), y = a.Latitude - b.Latitude;
                return Math.Sqrt(x * x + y * y) * KmPerDegree;
            }
            bool Wet(FlatSeaPoint p) => data.Water(world, p, flags, out _, SailingClearanceKm);
            bool Clear(FlatSeaPoint a, FlatSeaPoint b) => data.Segment(world, a, b, flags);
            int Leaf(int x, int y)
            {
                if (y < 0 || y >= Rows * Page)
                    return 0;
                x = (x % (Columns * Page) + Columns * Page) % (Columns * Page);
                int px = x / Page, py = y / Page, key = py * Columns + px;
                if (!pages.TryGetValue(key, out var map))
                {
                    int slot = data.Index(key);
                    bool By(FlatSeaPoint p)
                    {
                        int dx = Math.Abs(px - X(p)/Page), limit = 1 + (int)Math.Ceiling(1440.0/world.Width);
                        return data.RegionAt(p) == 0 && Math.Min(dx,Columns-dx) <= limit && Math.Abs(py-Y(p)/Page) <= limit;
                    }
                    map = Empty;
                    if (data.regions[slot] == region || (slot == 0 && (By(start) || By(end))))
                    {
                        cancel.ThrowIfCancellationRequested();
                        map = new int[Page * Page];
                        Subdivide(px * Page, py * Page, Page, slot, map);
                    }
                    pages.Add(key, map);
                }
                return map.Length == 0 ? 0 : map[(y % Page) * Page + x % Page];
            }
            void Subdivide(int x, int y, int size, int slot, int[] map)
            {
                var c = Point(x + size * .5, y + size * .5);
                int first = Marine | OriginalBed | Survey, any = 0;
                int x0 = (int)((long)x * world.Width / (Columns * Page)), x1 = (int)Math.Floor((x + size - 1e-8) * world.Width / (Columns * Page));
                int y0 = (int)((long)y * world.Height / (Rows * Page)), y1 = (int)Math.Floor((y + size - 1e-8) * world.Height / (Rows * Page));
                for (int row = y0; row <= y1; row++)
                    for (int col = x0; col <= x1; col++)
                    {
                        int f = Flags(world, row * world.Width + col, flags);
                        first &= f;
                        any |= f;
                    }
                bool wet = false, settled = false;
                if (slot == 0 || (any & OriginalBed) == 0)
                {
                    if ((first & Marine) != 0 || (any & Marine) == 0)
                    {
                        wet = (first & Marine) != 0;
                        settled = true;
                    }
                }
                else
                {
                    double km = data.Sample(c, out _);
                    // The centre-to-corner ground radius plus interpolation /
                    // quantisation error bounds an untouched uniform square.
                    double radius = size * .154 + (Math.Abs(km) < 2 && data.fullSurvey[slot] ? .25 : 1.8);
                    if (km < -radius && ((first & OriginalBed) != 0 || (any & Marine) == 0))
                        settled = true;
                    if (km > radius + SailingClearanceKm && ((first & Marine) != 0 || ((first & Survey) != 0 && data.fullSurvey[slot])))
                        wet = settled = true;
                }
                if (!settled && size == 1)
                {
                    // At the native texel scale an oblique channel may only cross
                    // part of a square. Test each shared wet face when linking it.
                    wet = Wet(c);
                    settled = true;
                }
                if (!settled)
                {
                    int h = size / 2;
                    Subdivide(x, y, h, slot, map);
                    Subdivide(x + h, y, h, slot, map);
                    Subdivide(x, y + h, h, slot, map);
                    Subdivide(x + h, y + h, h, slot, map);
                    return;
                }
                if (!wet)
                    return;
                int id = nodes.Count;
                nodes.Add(new Node { X = x, Y = y, Size = size });
                for (int j = 0; j < size; j++)
                    Array.Fill(map, id, (y % Page + j) * Page + x % Page, size);
            }
            int Attach(FlatSeaPoint p)
            {
                int x = X(p), y = Y(p);
                for (int r = 0; r <= 4; r++)
                    for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                                continue;
                            int id = Leaf(x + dx, y + dy);
                            if (id != 0 && Clear(p, nodes[id].Center))
                                return id;
                        }
                return 0;
            }
            internal bool Find(out FlatSeaPoint[] path)
            {
                path = Array.Empty<FlatSeaPoint>();
                if (Clear(start, end))
                {
                    path = new[] { start, end };
                    return true;
                }
                int first = Attach(start), last = Attach(end);
                if (first == 0 || last == 0)
                    return false;
                var heap = new FloodHeap();
                nodes[first].Cost = 0;
                heap.Push(first, 0);
                while (heap.Count != 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    int id = heap.Pop(out long priority);
                    var n = nodes[id];
                    if (id == last)
                        break;
                    if (id != first && priority > Rank(n.Cost, n.Center))
                        continue;
                    for (int side = 0; side < 4; side++)
                    {
                        bool horizontal = side < 2;
                        int edge = horizontal ? n.X + (side == 1 ? n.Size : 0) : n.Y + (side == 3 ? n.Size : 0);
                        int outside = edge - (side == 0 || side == 2 ? 1 : 0);
                        int lo = horizontal ? n.Y : n.X, limit = lo + n.Size;
                        for (int walk = lo; walk < limit;)
                        {
                            int to = horizontal ? Leaf(outside, walk) : Leaf(walk, outside);
                            if (to == 0)
                            {
                                walk++;
                                continue;
                            }
                            var next = nodes[to];
                            int stop = Math.Min(limit, (horizontal ? next.Y : next.X) + next.Size);
                            double middle = (walk + stop) * .5;
                            walk = stop;
                            var portal = horizontal ? Point(edge, middle) : Point(middle, edge);
                            double cost = n.Cost + Distance(n.Center, portal) + Distance(portal, next.Center);
                            if (cost >= next.Cost)
                                continue;
                            if ((n.Size == 1 || next.Size == 1) && (!Wet(portal) || !Clear(n.Center, portal) || !Clear(portal, next.Center)))
                                continue;
                            next.Parent = id;
                            next.Portal = portal;
                            next.Cost = cost;
                            heap.Push(to, Rank(cost, next.Center));
                        }
                    }
                }
                if (last != first && nodes[last].Parent == 0)
                    return false;
                var raw = new List<FlatSeaPoint> { end, nodes[last].Center };
                for (int i = last; i != first; i = nodes[i].Parent)
                {
                    raw.Add(nodes[i].Portal);
                    raw.Add(nodes[nodes[i].Parent].Center);
                }
                raw.Add(start);
                raw.Reverse();
                var result = new List<FlatSeaPoint> { start };
                for (int at = 0; at + 1 < raw.Count;)
                {
                    cancel.ThrowIfCancellationRequested();
                    int best = at + 1;
                    if (!Clear(raw[at], raw[best]))
                        return false;
                    // Extend the visible line until a real shore intervenes.
                    for (int i = best + 1; i < raw.Count; i++)
                    {
                        if (!Clear(raw[at], raw[i]))
                            break;
                        best = i;
                    }
                    result.Add(raw[best]);
                    at = best;
                }
                path = result.ToArray();
                return true;
            }
            long Rank(double cost, FlatSeaPoint p) => (long)Math.Round((cost + Distance(p, end)) * 10000);
        }
    }
}
