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
        readonly ushort[] coordinates;

        FlatEarthRiverReference(Part[] parts,ushort[] coordinates)
        { Parts=parts;this.coordinates=coordinates; }

        internal float X(int i,int width) => coordinates[2*i]*(width/65535f);
        internal float Y(int i,int height) => coordinates[2*i+1]*(height/65535f);

        public static void Restore(WorldData world,byte[] bed,byte[] water,byte[] rivers,CancellationToken cancel)
        {
            if(world.Preset!=TerrainPreset.Earth)return;
            cancel.ThrowIfCancellationRequested();
            world.WaterReference=FlatWaterReference.Read(world.Width,bed,water,world.UsesSurveyedLakeBeds,world.UsesSurveyedStraits);
            world.RiverReference=Read(rivers);
            world.Facts.RiverCurves=FlatRiverCurves.Build(world,world.Facts,cancel);
        }

        public static FlatEarthRiverReference Read(byte[] data)
        {
            if(data==null||data.Length<8)throw new InvalidDataException("Missing Earth river geometry.");
            using(var reader=new BinaryReader(new MemoryStream(data,false)))
            {
                if(reader.ReadUInt32()!=0x3156524f)throw new InvalidDataException("Invalid Earth river signature.");
                int count=reader.ReadInt32();
                if(count<1||count>(data.Length-8)/11)throw new InvalidDataException("Invalid Earth river part count.");
                int pointBytes=data.Length-8-3*count;
                if(pointBytes%4!=0)throw new InvalidDataException("Incomplete Earth river coordinates.");
                var parts=new Part[count];
                var xy=new ushort[pointBytes/2];
                int cursor=0;
                for(int p=0;p<count;p++)
                {
                    byte rank=reader.ReadByte();int n=reader.ReadUInt16();
                    if(rank>10||n<2||n>xy.Length/2-cursor)throw new InvalidDataException("Invalid Earth river part.");
                    parts[p]=new Part(rank,cursor,n);
                    for(int end=2*(cursor+n),i=2*cursor;i<end;i++)xy[i]=reader.ReadUInt16();
                    cursor+=n;
                }
                if(cursor!=xy.Length/2)throw new InvalidDataException("Trailing Earth river coordinates.");
                return new FlatEarthRiverReference(parts,xy);
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
            foreach(var part in data.Parts)
            {
                cancel.ThrowIfCancellationRequested();
                float importance=1-part.Rank*.1f;
                // Optical dimensions for a 1:10m map. Natural Earth supplies linework
                // and scale rank, not cross-sections, gauge flows or water levels.
                float radius=(.65f+4.4f*importance*importance)*world.Width/40075f;
                float depth=1.2f+6*importance;
                float ax=data.X(part.First,world.Width),ay=data.Y(part.First,world.Height);
                for(int i=part.First+1;i<part.First+part.Count;i++)
                {
                    float bx=data.X(i,world.Width),by=data.Y(i,world.Height);
                    float dx=Wrapped(bx-ax,world.Width),dy=by-ay;
                    float fx=dx,fy=dy;
                    Normalize(ref fx,ref fy);
                    var a=new Vertex(ax,ay,radius,depth,fx,fy,0);
                    var b=new Vertex(ax+dx,by,radius,depth,fx,fy,0);
                    // A negative reach encodes the source rank and has no modelled gauge.
                    built.Add(new Segment(a,b,~part.Rank));ax=bx;ay=by;
                }
            }
        }
    }
}
