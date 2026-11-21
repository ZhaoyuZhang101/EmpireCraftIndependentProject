using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Orbis.Flat
{
    // Normalized WGS84 polyline parts, independent of the playable world's resolution.
    public sealed class FlatEarthRiverReference
    {
        internal readonly struct Part
        {
            internal readonly byte Rank;
            internal readonly int First, Count;
            internal Part(byte rank,int first,int count) { Rank=rank;First=first;Count=count; }
        }

        internal readonly Part[] Parts;
        readonly uint[] coordinates;
        readonly ushort[] widthsMetres;
        readonly float coordinateMax;

        FlatEarthRiverReference(Part[] parts,uint[] coordinates,ushort[] widthsMetres)
        {
            Parts=parts; this.coordinates=coordinates; this.widthsMetres=widthsMetres;
            coordinateMax=widthsMetres==null ? 65535f : 16777215f;
        }

        internal float X(int i,int width) => coordinates[2*i]*(width/coordinateMax);
        internal float Y(int i,int height) => coordinates[2*i+1]*(height/coordinateMax);
        internal float Radius(int i,int width,float legacyRadius) =>
            widthsMetres==null ? legacyRadius : widthsMetres[i]*(width/80060400f);
        internal ulong StationKey(int i) => ((ulong)coordinates[2*i]<<32)|coordinates[2*i+1];

        public static void Restore(WorldData world,byte[] bed,byte[] water,byte[] rivers,CancellationToken cancel)
        {
            if(world.Preset!=TerrainPreset.Earth)return;
            cancel.ThrowIfCancellationRequested();
            world.WaterReference=FlatWaterReference.Read(world.Width,bed,water,world.UsesSurveyedLakeBeds,world.UsesSurveyedStraits);
            world.RiverReference=Read(rivers);
            FlatEcology.ApplyNileCropland(world,world.Facts,cancel);
            world.Facts.RiverCurves=FlatRiverCurves.Build(world,world.Facts,cancel);
        }

        public static FlatEarthRiverReference Read(byte[] data)
        {
            if(data==null||data.Length<8)throw new InvalidDataException("Missing Earth river geometry.");
            using(var reader=new BinaryReader(new MemoryStream(data,false)))
            {
                uint signature=reader.ReadUInt32();
                bool hasWidths=signature==0x3256524f;
                if(!hasWidths && signature!=0x3156524f)throw new InvalidDataException("Invalid Earth river signature.");
                int stride=hasWidths ? 10 : 4;
                int count=reader.ReadInt32();
                if(count<1||count>(data.Length-8)/(3+2*stride))throw new InvalidDataException("Invalid Earth river part count.");
                int pointBytes=data.Length-8-3*count;
                if(pointBytes%stride!=0)throw new InvalidDataException("Incomplete Earth river coordinates.");
                int points=pointBytes/stride;
                var parts=new Part[count];
                var xy=new uint[2*points];
                var widths=hasWidths ? new ushort[points] : null;
                int cursor=0;
                for(int p=0;p<count;p++)
                {
                    byte rank=reader.ReadByte();int n=reader.ReadUInt16();
                    if(rank>10||n<2||n>points-cursor)throw new InvalidDataException("Invalid Earth river part.");
                    parts[p]=new Part(rank,cursor,n);
                    for(int i=cursor;i<cursor+n;i++)
                    {
                        if(hasWidths)
                        {
                            xy[2*i]=reader.ReadUInt32(); xy[2*i+1]=reader.ReadUInt32();
                            widths[i]=reader.ReadUInt16();
                            if(xy[2*i]>16777215 || xy[2*i+1]>16777215 || widths[i]==0)
                                throw new InvalidDataException("Invalid Earth river station.");
                        }
                        else { xy[2*i]=reader.ReadUInt16(); xy[2*i+1]=reader.ReadUInt16(); }
                    }
                    cursor+=n;
                }
                if(cursor!=points)throw new InvalidDataException("Trailing Earth river coordinates.");
                return new FlatEarthRiverReference(parts,xy,widths);
            }
        }
    }

    public sealed partial class FlatRiverCurves
    {
        static bool[] FindEditedTiles(WorldData world,CancellationToken cancel)
        {
            bool[] edited=null;
            for(int i=0;i<world.Count;i++)
            {
                if((i&4095)==0)cancel.ThrowIfCancellationRequested();
                if(world.WaterReference.MatchesOriginalBed(i,world.Elevation[i]))continue;
                if(edited==null)edited=new bool[world.Count];
                int x=i%world.Width,y=i/world.Width;
                for(int dy=-1;dy<=1;dy++)
                {
                    int row=y+dy,column=x;
                    if(row<0||row>=world.Height){row=y;column+=world.Width/2;}
                    for(int dx=-1;dx<=1;dx++)edited[row*world.Width+FMath.Wrap(column+dx,world.Width)]=true;
                }
            }
            return edited;
        }

        static void AddSurvey(WorldData world,List<Segment> built,CancellationToken cancel)
        {
            var data=world.RiverReference;
            var junctions=new HashSet<ulong>();
            foreach(var part in data.Parts)
            {
                junctions.Add(data.StationKey(part.First));
                junctions.Add(data.StationKey(part.First+part.Count-1));
            }
            foreach(var part in data.Parts)
            {
                cancel.ThrowIfCancellationRequested();
                float importance=1-part.Rank*.1f;
                // Legacy geometry carried only Natural Earth map prominence.
                float legacyRadius=(.65f+4.4f*importance*importance)*world.Width/40075f;
                float depth=1.2f+6*importance;
                int last=part.First+part.Count-1;
                int reach=~part.Rank;
                float x=data.X(part.First,world.Width), y=data.Y(part.First,world.Height);
                float dx=Wrapped(data.X(part.First+1,world.Width)-x,world.Width), dy=data.Y(part.First+1,world.Height)-y;
                var previous=SurveyVertex(x,y,data.Radius(part.First,world.Width,legacyRadius),depth,dx,dy);
                for(int i=part.First+1;i<=last;i++)
                {
                    x=data.X(i,world.Width); y=data.Y(i,world.Height);
                    float radius=data.Radius(i,world.Width,legacyRadius);
                    if(i==last)
                    {
                        AppendSurvey(built,ref previous,SurveyVertex(x,y,radius,depth,dx,dy),world.Width,reach);
                        break;
                    }
                    float sx=Wrapped(data.X(i+1,world.Width)-x,world.Width), sy=data.Y(i+1,world.Height)-y;
                    float east=Metric(y,world.Height);
                    float inLength=(float)Math.Sqrt(dx*dx*east*east+dy*dy);
                    float outLength=(float)Math.Sqrt(sx*sx*east*east+sy*sy);
                    float inX=dx/Math.Max(inLength,1e-8f), inY=dy/Math.Max(inLength,1e-8f);
                    float outX=sx/Math.Max(outLength,1e-8f), outY=sy/Math.Max(outLength,1e-8f);
                    float cosine=FMath.Clamp(inX*outX*east*east+inY*outY,-1,1);
                    float halfSin=(float)Math.Sqrt((1-cosine)*.5f);
                    float before=data.Radius(i-1,world.Width,legacyRadius), after=data.Radius(i+1,world.Width,legacyRadius);
                    // A quadratic fillet replaces only the corner inside the source
                    // channel. At most 40% of either leg can belong to one corner.
                    float allowance=.55f*Math.Min(radius,Math.Min(before,after));
                    float cut=Math.Min(.4f*Math.Min(inLength,outLength),2*allowance/Math.Max(halfSin,.001f));
                    float offset=.5f*cut*halfSin;
                    // A subpixel displacement or gentle existing bend has no
                    // discernible corner. Keep these as one indexed span.
                    const float tolerance=.002f; // 0.19 screen pixels at maximum zoom
                    if(offset<=.005f || cosine>.87f || cosine<-.995f || junctions.Contains(data.StationKey(i)))
                    {
                        float tx=inX+outX,ty=inY+outY;
                        if(cosine<-.99999f){tx=outX;ty=outY;}
                        AppendSurvey(built,ref previous,SurveyVertex(x,y,radius,depth,tx,ty),world.Width,reach);
                    }
                    else
                    {
                        float r0=FMath.Lerp(radius,before,cut/inLength), r1=FMath.Lerp(radius,after,cut/outLength);
                        int steps=Math.Min(12,(int)Math.Ceiling(Math.Sqrt(offset/tolerance)));
                        for(int j=0;j<=steps;j++)
                        {
                            float t=(float)j/steps,s=1-t;
                            float px=x+cut*(t*t*outX-s*s*inX), py=y+cut*(t*t*outY-s*s*inY);
                            float r=t<.5f?FMath.Lerp(r0,radius,2*t):FMath.Lerp(radius,r1,2*t-1);
                            AppendSurvey(built,ref previous,SurveyVertex(px,py,r,depth,s*inX+t*outX,s*inY+t*outY),world.Width,reach);
                        }
                    }
                    dx=sx;dy=sy;
                }
            }
        }

        static Vertex SurveyVertex(float x,float y,float radius,float depth,float dx,float dy)
        {
            Normalize(ref dx,ref dy);
            return new Vertex(x,y,radius,depth,dx,dy,0);
        }

        static void AppendSurvey(List<Segment> built,ref Vertex previous,Vertex next,int width,int reach)
        {
            var end=new Vertex(previous.X+Wrapped(next.X-previous.X,width),next.Y,next.Radius,next.Depth,next.FlowX,next.FlowY,0);
            built.Add(new Segment(previous,end,reach));
            previous=next;
        }
    }
}
