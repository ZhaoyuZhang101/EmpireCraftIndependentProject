using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Orbis.Flat
{
    /// <summary>Native Earth shoreline samples shared by picking and navigation. Coordinates are WGS84 degrees.</summary>
    public sealed class FlatGeographicSurface
    {
        public FlatStraitNavigation Regional { get; }
        readonly byte[] directory, banks, lake, lakeDetail, polar;
        readonly int lakeWidth, polarSide;
        readonly bool[] lakePages;
        readonly ConditionalWeakTable<WorldData, Coverage> coverage = new ConditionalWeakTable<WorldData, Coverage>();
        const int Columns = 1440, Rows = 720, LakeColumns = 8192;
        const double EarthKm = 40030.2;
        sealed class Coverage
        {
            internal int Revision;
            internal byte[] Cells;
            internal byte[][] GroundEdges;
            internal bool South = true, North = true;
        }

        sealed class RawField
        {
            internal byte[] Directory, Banks, Lake, LakeDetail, Polar;
            internal int LakeWidth;
        }
        FlatGeographicSurface(FlatStraitNavigation regional, RawField raw)
        {
            Regional = regional; directory = raw.Directory; banks = raw.Banks; lake = raw.Lake;
            lakeDetail = raw.LakeDetail; lakeWidth = raw.LakeWidth; polar = raw.Polar;
            polarSide = polar == null ? 0 : (int)Math.Sqrt(polar.Length / 2);
            if(lake!=null)
            {
                // Conservative directory for land routing. Include the shoreline
                // interpolation footprint on both sides of each geographic page.
                var touched=new bool[Columns*Rows];lakePages=new bool[touched.Length];
                for(int y=0;y<LakeColumns/2;y++)for(int x=0;x<LakeColumns;x++)
                    if(lake[y*LakeColumns+x]>=104)touched[y*Rows/(LakeColumns/2)*Columns+x*Columns/LakeColumns]=true;
                for(int i=0;i<touched.Length;i++)if(touched[i])
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                    {
                        int row=i/Columns+dy;
                        if(row>=0 && row<Rows)lakePages[row*Columns+Mod(i%Columns+dx,Columns)]=true;
                    }
            }
        }
        internal bool HasGlobal => directory != null;
        static byte[] Inflate(byte[] data, int offset, int length, int expected)
        {
            using var input = new MemoryStream(data, offset, length, false);
            using var stream = new DeflateStream(input, CompressionMode.Decompress);
            var pixels = new byte[expected];
            for (int i = 0; i < expected;)
            {
                int n = stream.Read(pixels, i, expected - i);
                if (n == 0) throw new InvalidDataException("Truncated geographic surface.");
                i += n;
            }
            if (stream.ReadByte() != -1) throw new InvalidDataException("Excess geographic surface pixels.");
            return pixels;
        }
        public static FlatGeographicSurface Read(byte[] regional, byte[] ocean = null, byte[] lakes = null,
            byte[] lakeBanks = null, byte[] polarCoast = null)
        {
            var raw = new RawField();
            if (ocean != null)
            {
                using var r = new BinaryReader(new MemoryStream(ocean, false));
                if (r.ReadUInt32() != 0x3153434f) throw new InvalidDataException("Invalid global ocean shoreline.");
                int count = r.ReadInt32(), dir = r.ReadInt32(), pages = r.ReadInt32();
                if (count < 1 || count > 65535 || Math.Min(dir, pages) <= 0 || 16L + dir + pages != ocean.Length)
                    throw new InvalidDataException("Invalid ocean page lengths.");
                raw.Directory = Inflate(ocean, 16, dir, Columns * Rows * 4);
                raw.Banks = Inflate(ocean, 16 + dir, pages, 4096 * ((count + 511) / 512) * 32 * 4);
                for (int i = 0; i < raw.Directory.Length; i += 4)
                    if ((raw.Directory[i] | raw.Directory[i + 1] << 8) > count) throw new InvalidDataException("Invalid ocean page ID.");
            }
            if (lakes != null)
            {
                using var r = new BinaryReader(new MemoryStream(lakes, false));
                if (r.ReadUInt32() != 0x314b4c4f || r.ReadInt32() != LakeColumns || r.ReadInt32() != LakeColumns / 2)
                    throw new InvalidDataException("Invalid geographic lake chart.");
                raw.Lake = new byte[LakeColumns * (LakeColumns / 2)];
                int at = 0;
                while (r.BaseStream.Position < r.BaseStream.Length)
                {
                    byte value = r.ReadByte(); int run = r.ReadUInt16();
                    if (value == 0 || run == 0 || run > raw.Lake.Length - at) throw new InvalidDataException("Invalid lake run.");
                    Array.Fill(raw.Lake, value, at, run); at += run;
                }
                if (at != raw.Lake.Length) throw new InvalidDataException("Incomplete lake chart.");
            }
            if (lakes != null && lakeBanks != null)
            {
                using var r = new BinaryReader(new MemoryStream(lakeBanks, false));
                if (r.ReadUInt32() != 0x31444c4f) throw new InvalidDataException("Invalid geographic lake banks.");
                int w = r.ReadInt32(), h = r.ReadInt32();
                if ((w != 8192 && w != 4096) || h <= 1048576 / w || h > 8192 || (h - 1048576 / w) % 66 != 0)
                    throw new InvalidDataException("Invalid lake bank dimensions.");
                raw.LakeWidth = w; raw.LakeDetail = Inflate(lakeBanks,12,lakeBanks.Length-12,w*h);
                int slots = w / 66 * ((h - 1048576 / w) / 66);
                for (int i=0;i<1048576;i+=2)
                    if ((raw.LakeDetail[i] | raw.LakeDetail[i+1]<<8)>slots) throw new InvalidDataException("Invalid lake bank page ID.");
            }
            if (polarCoast != null)
            {
                int side = (int)Math.Sqrt(polarCoast.Length / 2);
                if (side < 3 || 2L * side * side != polarCoast.Length) throw new InvalidDataException("Invalid polar coast.");
                raw.Polar = polarCoast;
            }
            return new FlatGeographicSurface(regional == null ? null : FlatStraitNavigation.Read(regional), raw);
        }

        public void Attach(WorldData world)
        {
            if (world == null || world.Preset != TerrainPreset.Earth || world.WaterReference == null)
                throw new ArgumentException("Attach the Earth water reference before the geographic surface.");
            MaskFor(world);
            world.GeographicSurface = this;
        }
        public bool Prepared(WorldData world) => coverage.TryGetValue(world,out var m) && m.Revision==world.TerrainRevision;
        Coverage MaskFor(WorldData w)
        {
            if (coverage.TryGetValue(w, out var current) && current.Revision == w.TerrainRevision) return current;
            lock (coverage)
            {
                if (coverage.TryGetValue(w,out current) && current.Revision == w.TerrainRevision) return current;
                var m = new Coverage { Cells = new byte[w.Count], GroundEdges = new byte[(2*w.Count+4095)/4096][], Revision = w.TerrainRevision };
                for (int i = 0; i < w.Count; i++)
                    m.Cells[i] = (byte)(1 | (w.Water[i] != WaterKind.Lake && w.CoastDistance[i] <= 2 &&
                        w.WaterReference.ContainsMarineShoreOrSea(i) ? 2 : 0));
                for (int i = 0; i < w.Count; i++)
                {
                    if (w.WaterReference.MatchesOriginalBed(i,w.Elevation[i])) continue;
                    int x = i % w.Width, y = i / w.Width;
                    if (y <= 1) m.South = false;
                    if (y >= w.Height - 2) m.North = false;
                    for (int dy=-1;dy<=1;dy++)
                    {
                        int row = y+dy, col = x;
                        if (row<0 || row>=w.Height) { row=y; col+=w.Width/2; }
                        for (int dx=-1;dx<=1;dx++) m.Cells[w.Index(col+dx,row)] &= 2;
                    }
                }
                coverage.Remove(w); coverage.Add(w,m); return m;
            }
        }
        static double Clamp(double x,double lo,double hi) => Math.Max(lo,Math.Min(hi,x));
        internal static double Wrap(double x) => x-360*Math.Floor((x+180)/360);
        static int Mod(int x,int width) => (x % width + width) % width;
        static double Lerp(double x,double y,double t) => x+(y-x)*t;
        static double Smooth(double a,double b,double v)
        { double t=Clamp((v-a)/(b-a),0,1); return t*t*(3-2*t); }
        static double Quad(double a,double b,double c,double d,double x,double y)
            => Lerp(Lerp(a,b,x),Lerp(c,d,x),y);
        static double Page(byte[] data,int start,int stride,int channels,double x,double y)
        {
            int i=(int)x,j=(int)y,p=start+(j*stride+i)*channels;
            return Quad(data[p],data[p+channels],data[p+stride*channels],data[p+(stride+1)*channels],x-i,y-j);
        }
        static double Grid(byte[] data,int width,int height,double x,double y,int channels,int component)
        {
            int ix=(int)Math.Floor(x),iy=(int)Math.Floor(y);
            int x0=Mod(ix,width),x1=Mod(ix+1,width),y0=Math.Max(0,Math.Min(height-1,iy))*width,y1=Math.Max(0,Math.Min(height-1,iy+1))*width;
            return Quad(data[(y0+x0)*channels+component],data[(y0+x1)*channels+component],
                data[(y1+x0)*channels+component],data[(y1+x1)*channels+component],x-ix,y-iy);
        }
        static int Tile(WorldData w,FlatSeaPoint point) => w.Index((int)((Wrap(point.Longitude)+180)*w.Width/360),
            (int)Clamp((point.Latitude+90)*w.Height/180,0,w.Height-1));
        internal static FlatSeaPoint Center(WorldData w,int tile) => new FlatSeaPoint(
            (tile%w.Width+.5)*360/w.Width-180,(tile/w.Width+.5)*180/w.Height-90);
        static int DirectoryIndex(FlatSeaPoint p,out double x,out double y)
        {
            x=(Wrap(p.Longitude)+180)*4; y=Clamp((p.Latitude+90)*4,0,Rows-1e-8);
            int ix=(int)x,iy=(int)y;x-=ix;y-=iy;return (iy*Columns+ix)*4;
        }
        internal bool NativePage(int chart) => directory!=null && (directory[4*chart]|directory[4*chart+1])!=0;
        internal bool HasNativeBank(FlatSeaPoint p)
        {
            int chart=DirectoryIndex(p,out _,out _)/4;
            if(NativePage(chart) || Regional!=null && Regional.RegionAt(p)!=0)return true;
            return lakePages!=null && lakePages[chart];
        }
        internal static double NextBoundary(double start,double delta,double scale,double t)
        {
            if(delta==0)return double.PositiveInfinity;
            double grid=(start+delta*t)*scale;
            double boundary=delta>0?Math.Floor(grid+1e-8)+1:Math.Ceiling(grid-1e-8)-1;
            return (boundary/scale-start)/delta;
        }
        // Ground agents still use their own habitat, bridge and river rules. Only
        // the area-water footprint can veto a previously legal edge.
        public static bool GroundEdge(WorldData w,int from,int to)
        {
            if(w==null || from<0 || from>=w.Count || to<0 || to>=w.Count)return false;
            if(from==to)return IsGroundAt(w,Center(w,from)) || w.Buildings[from]==Structure.Bridge;
            if(w.SphereNeighbor(from,0)!=to && w.SphereNeighbor(from,1)!=to &&
               w.SphereNeighbor(from,2)!=to && w.SphereNeighbor(from,3)!=to)return false;
            var a=Center(w,from);var b=Center(w,to);
            bool bridge=w.Buildings[from]==Structure.Bridge || w.Buildings[to]==Structure.Bridge;
            var atlas=w.GeographicSurface;
            if(atlas==null)
            {
                if(w.Facts==null)return true;
                // Along an ordinary face this is a monotone cubic between the
                // two centres, plus at most .045 cells of procedural shore detail.
                if(w.Facts.ShoreDistance[from]<-.05 && w.Facts.ShoreDistance[to]<-.05 &&
                   !(from/w.Width==to/w.Width && Math.Abs(from-to)==w.Width/2))return true;
                return FlatModelSeaNavigation.GroundEdge(w,from,to,bridge);
            }
            if(from/w.Width==to/w.Width && Math.Abs(from-to)==w.Width/2)
            {
                double pole=a.Latitude<0?-90:90;
                return atlas.GroundSegment(w,a,new FlatSeaPoint(a.Longitude,pole),bridge,false) &&
                    atlas.GroundSegment(w,new FlatSeaPoint(b.Longitude,pole),b,bridge,false);
            }
            // Bridge construction is a tile revision independent of the terrain revision.
            if(bridge)return atlas.GroundSegment(w,a,b,true,false);
            int key=from/w.Width==to/w.Width ? 2*(w.SphereNeighbor(from,1)==to?from:to) : 2*Math.Min(from,to)+1;
            var mask=atlas.MaskFor(w);var pages=mask.GroundEdges;
            int page=key>>12;var edges=Volatile.Read(ref pages[page]);
            if(edges==null) { edges=new byte[4096];edges=Interlocked.CompareExchange(ref pages[page],edges,null)??edges; }
            int at=key&4095;byte known=Volatile.Read(ref edges[at]);
            if(known!=0)return known==1;
            // The monotone cubic model along adjacent cell centres cannot cross
            // zero when both endpoints are below the maximum procedural shore noise.
            bool modelDry=w.Facts!=null && w.Facts.ShoreDistance[from]<-.05 && w.Facts.ShoreDistance[to]<-.05 &&
                (Math.Max(Math.Abs(a.Latitude),Math.Abs(b.Latitude))<45 || ((mask.Cells[from]|mask.Cells[to])&2)==0);
            bool clear=atlas.GroundSegment(w,a,b,false,modelDry);
            Volatile.Write(ref edges[at],(byte)(clear?1:2));return clear;
        }
        bool GroundSegment(WorldData w,FlatSeaPoint a,FlatSeaPoint b,bool bridge,bool modelDry)
        {
            double dx=Wrap(b.Longitude-a.Longitude),dy=b.Latitude-a.Latitude,extent=Math.Max(Math.Abs(dx),Math.Abs(dy));
            FlatSeaPoint Point(double t)=>new FlatSeaPoint(Wrap(a.Longitude+dx*t),a.Latitude+dy*t);
            bool Ground(double t){var kind=At(w,Point(t),out _,out _);return kind==WaterKind.Land || kind==WaterKind.River || bridge && kind==WaterKind.Lake;}
            for(double t=0;t<1;)
            {
                double end=Math.Min(1,Math.Min(NextBoundary(a.Longitude+180,dx,4,t),NextBoundary(a.Latitude+90,dy,4,t)));
                if(end<=t)return false;
                var mid=Point((t+end)*.5);
                int chart=DirectoryIndex(mid,out _,out _)/4;
                if(modelDry && HasGlobal && !NativePage(chart) && directory[4*chart+3]<128 &&
                   (Regional==null||Regional.RegionAt(mid)==0) && (lakePages==null||!lakePages[chart]))
                { t=end;continue; }
                if(!Ground((t+end)*.5))return false;
                int mesh=HasNativeBank(mid)?512:Math.Max(12,w.Width/45);
                int steps=Math.Max(1,(int)Math.Ceiling((end-t)*extent*mesh*2));
                for(int i=0;i<=steps;i++)if(!Ground(Lerp(t,end,(double)i/steps)))return false;
                t=end;
            }
            return true;
        }
        double Ocean(FlatSeaPoint p,double cellKm,out double weight)
        {
            int i=DirectoryIndex(p,out var x,out var y),slot=(directory[i]|directory[i+1]<<8)-1;
            double fine=(directory[i+3]>=128?127:-127)/32.0;
            if(slot>=0)
            {
                int tile=slot/4, start=4*(tile/128*32*4096+tile%128*32)+slot%4;
                fine=(Page(banks,start,4096,4,.5+x*30,.5+y*30)-128)/32;
            }
            double gx=(Wrap(p.Longitude)+180)*4-.5,gy=(p.Latitude+90)*4-.5;
            double broad=(Grid(directory,Columns,Rows,gx,gy,4,2)+256*Grid(directory,Columns,Rows,gx,gy,4,3)-32768)/32;
            double km=Math.Sign(fine)*Lerp(Math.Abs(fine),Math.Max(Math.Abs(fine),Math.Sign(fine)*broad),Smooth(1.5,3.96,Math.Abs(fine)));
            weight=1-Smooth(1.5*cellKm+20,3*cellKm+45,Math.Abs(km));return km;
        }
        double Lake(double u,double v)
        {
            double distance=(Grid(lake,LakeColumns,LakeColumns/2,u*LakeColumns-.5,v*LakeColumns/2-.5,1,0)-128)*.25;
            if(lakeDetail==null || Math.Abs(distance)>=3.75) return distance;
            double x=u*1024,y=Math.Min(v*512,512-1e-8);
            int ix=(int)x,iy=(int)y,entry=2*(iy*1024+ix),slot=(lakeDetail[entry]|lakeDetail[entry+1]<<8)-1;
            if(slot<0)return distance;
            int stride=lakeWidth/66, first=(1048576/lakeWidth+slot/stride*66)*lakeWidth+slot%stride*66;
            double fine=(Page(lakeDetail,first,lakeWidth,1,.5+(x-ix)*64,.5+(y-iy)*64)-128)/32;
            return Lerp(distance,fine,1-Smooth(2.5,3.75,Math.Abs(distance)));
        }
        static bool Marine(WaterKind kind) => kind>=WaterKind.DeepOcean && kind<=WaterKind.ShallowOcean;
        static void MaskAt(Coverage mask,WorldData w,double x,double y,out double eligible,out double marine)
        {
            x-=.5;y-=.5;int ix=(int)Math.Floor(x),iy=(int)Math.Floor(y);
            int i0=Mod(ix,w.Width),i1=Mod(ix+1,w.Width),y0=Math.Max(0,Math.Min(w.Height-1,iy))*w.Width,y1=Math.Max(0,Math.Min(w.Height-1,iy+1))*w.Width;
            int a=mask.Cells[y0+i0],b=mask.Cells[y0+i1],c=mask.Cells[y1+i0],d=mask.Cells[y1+i1];
            eligible=Quad(a&1,b&1,c&1,d&1,x-ix,y-iy); marine=Quad(a>>1,b>>1,c>>1,d>>1,x-ix,y-iy);
        }

        public static WaterKind WaterAt(WorldData world,FlatSeaPoint point)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (!(point.Longitude>=-180 && point.Longitude<=180 && point.Latitude>=-90 && point.Latitude<=90))
                throw new ArgumentOutOfRangeException(nameof(point));
            return world.GeographicSurface?.At(world,point,out _,out _) ?? ModelAt(world,point,out _);
        }
        public static bool IsGroundAt(WorldData world,FlatSeaPoint point)
        { var kind=WaterAt(world,point);return kind==WaterKind.Land || kind==WaterKind.River; }

        internal static WaterKind ModelAt(WorldData w,FlatSeaPoint p,out double shoreCells)
        {
            int tile=Tile(w,p);var kind=w.Water[tile];
            if(w.Facts==null)
            {shoreCells=Marine(kind)||kind==WaterKind.Lake?.5:-.5;return kind;}
            double x=(Wrap(p.Longitude)+180)*w.Width/360,y=(p.Latitude+90)*w.Height/180;
            // More than two cells from a bank the bounded reconstruction cannot
            // change sign. Near shore sample the same metre-independent field as the GPU.
            shoreCells=w.Facts.ShoreDistance[tile];
            if(Math.Abs(shoreCells)<2)shoreCells=FlatSurfaceSampling.SampleShore(w,(float)x,(float)y);
            if(shoreCells<=0)return kind==WaterKind.River?WaterKind.River:WaterKind.Land;
            if(Marine(kind)||kind==WaterKind.Lake)return kind;
            return NearbyLake(w,x,y)?WaterKind.Lake:WaterKind.ShallowOcean;
        }

        internal WaterKind At(WorldData w,FlatSeaPoint p,out double signedKm,out bool covered)
        {
            double u=(Wrap(p.Longitude)+180)/360,v=(p.Latitude+90)/180,x=u*w.Width,y=v*w.Height,cellKm=EarthKm/w.Width;
            int tile=Tile(w,p);var kind=w.Water[tile];
            double strait=0,km=0;
            if(w.UsesSurveyedStraits && kind!=WaterKind.Lake)
            {
                if(Regional!=null)km=Regional.Sample(p,out strait);
                if(w.UsesGlobalStraits && directory!=null && strait<.99999)
                {
                    double global=Ocean(p,cellKm,out double strength),fill=(1-strait)*strength;
                    km=(km*strait+global*fill)/Math.Max(.00001,strait+fill);strait+=fill;
                }
            }
            double lakeCode=lake==null?-32:Lake(u,v);
            var mask=MaskFor(w);MaskAt(mask,w,x,y,out double eligible,out double marine);
            strait*=eligible;
            double polarWeight=0,polarKm=0;
            if(polar!=null && kind!=WaterKind.Lake && strait<.99999 && marine>0 && Math.Abs(p.Latitude)>45)
            {
                const double span=.828427124746;
                double lat=p.Latitude*Math.PI/180,lon=p.Longitude*Math.PI/180,s=Math.Sin(lat),den=1+Math.Abs(s);
                double scale=Math.Cos(lat)/(den*span),sx=Clamp(.5+Math.Sin(lon)*scale,2.0/polarSide,1-2.0/polarSide);
                double sy=Clamp(.5+Math.Cos(lon)*scale,2.0/polarSide,1-2.0/polarSide);
                double d=(Grid(polar,polarSide*2,polarSide,((s>=0?1:0)+sx)*polarSide-.5,sy*polarSide-.5,1,0)-128)*.25;
                double pole=(s<0?mask.South:mask.North)?1:0;
                polarWeight=Smooth(.70710678,.8660254,Math.Abs(s))*Lerp(pole,eligible,FlatSurfaceSampling.PolarRingWeight(v,w.Height))*
                    Smooth(0,.35,marine)*(1-Smooth(24,31,Math.Abs(d)));
                polarKm=d*span*den*EarthKm/(polarSide*2*Math.PI);
            }
            covered=strait>0 || polarWeight>0 || lakeCode>-31 && eligible>0;
            signedKm=Marine(kind)||kind==WaterKind.Lake?cellKm:-cellKm;
            if(!covered)
            {
                var model=ModelAt(w,p,out double cells);signedKm=cells*cellKm;return model;
            }
            double grid=0;
            if(strait<.99999 || lakeCode>-31 || kind==WaterKind.Land || kind==WaterKind.River)
                grid=w.Facts==null?(Marine(kind)||kind==WaterKind.Lake?.5:-.5)*cellKm:
                    FlatSurfaceSampling.SampleShore(w,(float)x,(float)y)*cellKm;
            signedKm=Lerp(Lerp(grid,polarKm,polarWeight),km,strait);
            bool inland=kind==WaterKind.Lake;
            if(strait>0 && signedKm<0 && grid>Math.Max(signedKm,-.28*cellKm) && (kind==WaterKind.Land || kind==WaterKind.River) &&
                NearbyLake(w,x,y))
            {signedKm=grid;strait=0;inland=true;}
            if(lakeCode>-31)
            {
                double marineSupport=Math.Max(marine,Marine(kind)?1:0);
                marineSupport=Math.Max(marineSupport,strait*Smooth(-.5,0,signedKm));
                double weight=eligible*Smooth(-31,-20,lakeCode)*Lerp(1,Smooth(-1,.25,lakeCode),marineSupport);
                double lakeKm=lakeCode*EarthKm/LakeColumns;
                signedKm=Lerp(signedKm,Lerp(lakeKm,Math.Max(signedKm,lakeKm),strait),weight);
                if(weight>0 && lakeCode>0)inland=true;
            }
            if(signedKm<=0)return kind==WaterKind.River?WaterKind.River:WaterKind.Land;
            if(inland)return WaterKind.Lake;
            return Marine(kind)?kind:WaterKind.ShallowOcean;
        }
        static bool NearbyLake(WorldData w,double x,double y)
        {
            x-=.5;y-=.5;int cx=(int)Math.Floor(x),cy=(int)Math.Floor(y);
            double sx=Smooth(0,1,x-cx),sy=Smooth(0,1,y-cy),wet=0,lakes=0;
            for(int j=0;j<2;j++)for(int i=0;i<2;i++)
            {
                int t=w.Index(cx+i,Math.Max(0,Math.Min(w.Height-1,cy+j)));var c=w.Water[t];
                if(!Marine(c)&&c!=WaterKind.Lake)continue;
                double weight=(i==0?1-sx:sx)*(j==0?1-sy:sy);wet+=weight;if(c==WaterKind.Lake)lakes+=weight;
            }
            return wet>1e-6 && 2*lakes>wet;
        }
    }
}
