using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Collections.Generic;
namespace Orbis.Flat
{
    // Bounded binary payload + SHA-256. Separate path/schema from legacy V4 JSON.
    public static class FlatSave
    {
        public const int Version=3,MaxFileBytes=96*1024*1024;
        const string Magic="ORBIS-FLAT-01";
        const int CycloneStateBytes = sizeof(long) + 4 * (sizeof(byte) + 7 * sizeof(double));
        static void StringWrite(BinaryWriter b,string s,int max)
        {byte[] a=Encoding.UTF8.GetBytes(s??"");if(a.Length>max)throw new InvalidDataException("String too long.");b.Write(a.Length);b.Write(a);}
        static string StringRead(BinaryReader b,int max)
        {int n=b.ReadInt32();if(n<0||n>max)throw new InvalidDataException("Invalid string size.");byte[] a=b.ReadBytes(n);if(a.Length!=n)throw new EndOfStreamException();return Encoding.UTF8.GetString(a);}
        static void Check(bool ok,string message) { if(!ok)throw new InvalidDataException(message); }
        static bool Finite(double d) { return !double.IsNaN(d)&&!double.IsInfinity(d); }

        static void WriteCycloneTrack(BinaryWriter writer, FlatCycloneTrack track)
        {
            writer.Write(track.Tracked);
            writer.Write(track.U);
            writer.Write(track.V);
            writer.Write(track.PressureDeficit);
            writer.Write(track.WindSpeed);
            writer.Write(track.Phase);
            writer.Write(track.AgeDays);
            writer.Write(track.OceanCooling);
        }

        static FlatCycloneTrack ReadCycloneTrack(BinaryReader reader)
        {
            byte tracked = reader.ReadByte();
            Check(tracked <= 1, "Invalid cyclone tracking flag.");
            return new FlatCycloneTrack
            {
                Tracked = tracked == 1, U = reader.ReadDouble(), V = reader.ReadDouble(),
                PressureDeficit = reader.ReadDouble(), WindSpeed = reader.ReadDouble(),
                Phase = reader.ReadDouble(), AgeDays = reader.ReadDouble(), OceanCooling = reader.ReadDouble()
            };
        }

        static void ValidateCycloneTrack(FlatCycloneTrack track, double day)
        {
            Check(track.U >= 0 && track.U < 1 && track.V >= 0 && track.V <= 1, "Invalid cyclone coordinates.");
            Check(track.PressureDeficit >= 0 && track.PressureDeficit <= 200 && track.WindSpeed >= 0 && track.WindSpeed <= 150,
                "Invalid cyclone pressure or wind.");
            Check(track.OceanCooling >= 0 && track.OceanCooling <= 30, "Invalid cyclone ocean cooling.");
            // Allow spin-up before day zero and unwrapped rotation over the storm's lifetime.
            Check(track.AgeDays >= 0 && track.AgeDays <= day + 32 && Math.Abs(track.Phase) <= (day + 32) * 32,
                "Invalid cyclone age or rotation.");
        }

        public static byte[] Encode(WorldData w)
        {
            Validate(w);
            byte[] body;
            using(var m=new MemoryStream())using(var b=new BinaryWriter(m,Encoding.UTF8,true))
            {
                b.Write(Version);b.Write(w.Width);b.Write(w.Height);StringWrite(b,w.Seed,512);b.Write((byte)w.Preset);b.Write(w.Day);b.Write(w.ExpansionRemainder);
                b.Write(w.Nations.Count);
                foreach(var n in w.Nations){StringWrite(b,n.Name,256);b.Write(n.Capital);b.Write(n.Color);b.Write(n.Food);b.Write(n.Refuges);b.Write(n.ExpansionClock);}
                for(int i=0;i<w.Count;i++)
                {
                    b.Write(w.Elevation[i]);b.Write(w.WaterLevel[i]);b.Write((byte)w.Water[i]);b.Write((byte)w.Biomes[i]);b.Write((byte)w.Climates[i]);b.Write((byte)w.Buildings[i]);
                    b.Write(w.Owner[i]);b.Write(w.Downstream[i]);b.Write(w.DrainOrder[i]);b.Write(w.Fertility[i]);b.Write(w.Runoff[i]);b.Write(w.Population[i]);b.Write(w.CoastDistance[i]);
                    for(int j=0;j<12;j++){b.Write(w.Temperature[i*12+j]);b.Write(w.Precipitation[i*12+j]);}
                }
                foreach(int owner in w.ZoneOwner)b.Write(owner);
                b.Write(w.WildlifeInitialized);b.Write(w.Wildlife.Count);
                foreach(var a in w.Wildlife)
                {
                    b.Write(a.Id);b.Write(a.HomeTile);b.Write(a.GoalTile);b.Write((byte)a.Species);b.Write((byte)a.State);
                    b.Write(a.X);b.Write(a.Y);b.Write(a.Hunger);b.Write(a.Fatigue);b.Write(a.Facing);b.Write(a.AnimationTime);b.Write(a.MoveSpeed);b.Write(a.RandomState);
                }
                var cyclones = w.Cyclones;
                if (cyclones != null)
                {
                    b.Write(cyclones.StepIndex);
                    WriteCycloneTrack(b, cyclones.North);
                    WriteCycloneTrack(b, cyclones.South);
                    WriteCycloneTrack(b, cyclones.PreviousNorth);
                    WriteCycloneTrack(b, cyclones.PreviousSouth);
                }
                b.Flush();body=m.ToArray();
            }
            byte[] digest;using(var sha=SHA256.Create())digest=sha.ComputeHash(body);
            using(var m=new MemoryStream())using(var b=new BinaryWriter(m))
            {b.Write(Encoding.ASCII.GetBytes(Magic));b.Write(body.Length);b.Write(digest);b.Write(body);return m.ToArray();}
        }
        public static WorldData Decode(byte[] file)
        {
            Check(file!=null&&file.Length>=64&&file.Length<=MaxFileBytes,"Save length invalid.");
            using(var stream=new MemoryStream(file))using(var r=new BinaryReader(stream))
            {
                Check(Encoding.ASCII.GetString(r.ReadBytes(Magic.Length))==Magic,"This is not an ORBIS 2D save (legacy V4 is separate).");
                int size=r.ReadInt32();byte[] expected=r.ReadBytes(32);
                Check(size>=0&&size==stream.Length-stream.Position,"Save payload length mismatch.");
                byte[] body=r.ReadBytes(size);byte[] actual;using(var sha=SHA256.Create())actual=sha.ComputeHash(body);
                int difference=0;for(int i=0;i<32;i++)difference|=expected[i]^actual[i];Check(difference==0,"Save checksum mismatch.");
                using(var m=new MemoryStream(body))using(var b=new BinaryReader(m))
                {
                    int version=b.ReadInt32();Check(version>=1&&version<=Version,"Unsupported save version.");int width=b.ReadInt32(),height=b.ReadInt32();string seed=StringRead(b,512);var preset=(TerrainPreset)b.ReadByte();
                    var w=new WorldData(width,height,seed,preset){Day=b.ReadDouble(),ExpansionRemainder=b.ReadDouble()};
                    int nations=b.ReadInt32();Check(nations>=0&&nations<=WorldData.MaxNations,"Invalid nation count.");
                    for(int i=0;i<nations;i++)w.Nations.Add(new Nation{Name=StringRead(b,256),Capital=b.ReadInt32(),Color=b.ReadUInt32(),Food=b.ReadDouble(),Refuges=b.ReadDouble(),ExpansionClock=b.ReadDouble()});
                    for(int i=0;i<w.Count;i++)
                    {
                        w.Elevation[i]=b.ReadInt16();w.WaterLevel[i]=b.ReadInt16();w.Water[i]=(WaterKind)b.ReadByte();w.Biomes[i]=(Biome)b.ReadByte();w.Climates[i]=(Koppen)b.ReadByte();w.Buildings[i]=(Structure)b.ReadByte();
                        w.Owner[i]=b.ReadInt32();w.Downstream[i]=b.ReadInt32();w.DrainOrder[i]=b.ReadInt32();w.Fertility[i]=b.ReadSingle();w.Runoff[i]=b.ReadSingle();w.Population[i]=b.ReadSingle();w.CoastDistance[i]=b.ReadUInt16();
                        for(int j=0;j<12;j++){w.Temperature[i*12+j]=b.ReadSingle();w.Precipitation[i*12+j]=b.ReadSingle();}
                    }
                    for(int i=0;i<w.ZoneCount;i++)w.ZoneOwner[i]=b.ReadInt32();
                    if(version>=2)
                    {
                        byte initialized=b.ReadByte();Check(initialized<=1,"Invalid wildlife initialization flag.");w.WildlifeInitialized=initialized==1;
                        int count=b.ReadInt32();Check(count>=0&&count<=FlatWildlifeSimulation.MaxActors,"Invalid wildlife count.");
                        for(int i=0;i<count;i++)w.Wildlife.Add(new WildlifeActor
                        {
                            Id=b.ReadInt32(),HomeTile=b.ReadInt32(),GoalTile=b.ReadInt32(),Species=(WildlifeSpecies)b.ReadByte(),State=(WildlifeState)b.ReadByte(),
                            X=b.ReadDouble(),Y=b.ReadDouble(),Hunger=b.ReadSingle(),Fatigue=b.ReadSingle(),Facing=b.ReadSingle(),AnimationTime=b.ReadSingle(),MoveSpeed=b.ReadSingle(),RandomState=b.ReadUInt32()
                        });
                    }
                    // V3 appends one fixed-size state once the world simulation has initialized it.
                    // A zero-length tail also distinguishes an untouched world from four inactive tracks.
                    if (version >= 3 && m.Position < m.Length)
                    {
                        Check(m.Length - m.Position == CycloneStateBytes, "Invalid cyclone state length.");
                        w.Cyclones = new FlatCycloneWeather
                        {
                            StepIndex = b.ReadInt64(), North = ReadCycloneTrack(b), South = ReadCycloneTrack(b),
                            PreviousNorth = ReadCycloneTrack(b), PreviousSouth = ReadCycloneTrack(b)
                        };
                    }
                    Check(m.Position==m.Length,"Trailing data is not permitted.");Validate(w);
                    w.Moisture=FlatMoisture.Build(w,System.Threading.CancellationToken.None);
                    FlatTerrainAnalysis.Build(w,System.Threading.CancellationToken.None,false);return w;
                }
            }
        }
        public static void Validate(WorldData w)
        {
            Check(w.Nations.Count<=WorldData.MaxNations&&w.Preset<=TerrainPreset.Earth,"Invalid settings.");
            Check(Finite(w.Day)&&w.Day>=0&&w.Day<1e9&&Finite(w.ExpansionRemainder)&&w.ExpansionRemainder>=0&&w.ExpansionRemainder<1e6,"Invalid time.");
            var cyclones = w.Cyclones;
            if (cyclones != null)
            {
                Check(cyclones.StepIndex == FlatCycloneWeather.StepAt(w.Day), "Cyclone clock disagrees with the saved world day.");
                ValidateCycloneTrack(cyclones.North, w.Day);
                ValidateCycloneTrack(cyclones.South, w.Day);
                ValidateCycloneTrack(cyclones.PreviousNorth, w.Day);
                ValidateCycloneTrack(cyclones.PreviousSouth, w.Day);
            }
            var rank=new int[w.Count];var seen=new bool[w.Count];
            for(int j=0;j<w.Count;j++){int i=w.DrainOrder[j];Check(i>=0&&i<w.Count&&!seen[i],"Drain order is not a permutation.");seen[i]=true;rank[i]=j;}
            for(int i=0;i<w.Count;i++)
            {
                Check(w.Elevation[i]>=-4000&&w.Elevation[i]<=4000&&w.WaterLevel[i]>=-4000&&w.WaterLevel[i]<=4000,"Elevation out of range.");
                // Imported physical terrain retains metre precision; only the editing brush uses preset bands.
                Check(w.WaterLevel[i]>=w.Elevation[i],"Filled water level below its bed.");
                bool ocean=w.Water[i]>=WaterKind.DeepOcean&&w.Water[i]<=WaterKind.ShallowOcean;
                Check(!ocean||(w.Elevation[i]<=0&&w.WaterLevel[i]==0&&w.Water[i]==HeightBands.Ocean(w.Elevation[i])),"Inconsistent ocean classification.");
                Check(w.Water[i]<=WaterKind.River&&w.Biomes[i]<=Biome.Wetland&&w.Climates[i]>=Koppen.Af&&w.Climates[i]<=Koppen.EF&&w.Buildings[i]<=Structure.Bridge,"Invalid classification.");
                Check(w.Owner[i]>=-1&&w.Owner[i]<w.Nations.Count&&w.Owner[i]==w.ZoneOwner[w.ZoneOf(i)],"Tile/Zone owner mismatch.");
                int parent=w.Downstream[i];Check(parent>=-1&&parent<w.Count&&parent!=i,"Invalid downstream index.");
                if(parent>=0)
                {
                    Check(FlatHydrology.IsDrainageNeighbor(w,i,parent)&&rank[parent]<rank[i],"Drainage cycle or disconnected edge.");
                    Check(w.WaterLevel[parent]<=w.WaterLevel[i],"Drainage routes uphill in spill height.");
                }
                Check(FMath.Finite(w.Fertility[i])&&w.Fertility[i]>=0&&w.Fertility[i]<=1&&FMath.Finite(w.Runoff[i])&&w.Runoff[i]>=0&&w.Runoff[i]<1e10f&&FMath.Finite(w.Population[i])&&w.Population[i]>=0&&w.Population[i]<=1e8f,"Invalid simulation value.");
                Check(w.Population[i]==0||(w.Owner[i]>=0&&w.Habitable(i)),"Population on uninhabitable/unowned tile.");
                for(int m=0;m<12;m++){float t=w.Temperature[i*12+m],p=w.Precipitation[i*12+m];Check(FMath.Finite(t)&&t>=-100&&t<=100&&FMath.Finite(p)&&p>=0&&p<=10000,"Invalid monthly climate.");}
            }
            for(int z=0;z<w.ZoneCount;z++)Check(w.ZoneOwner[z]>=-1&&w.ZoneOwner[z]<w.Nations.Count,"Invalid zone owner.");
            for(int c=0;c<w.Nations.Count;c++)
            {
                var n=w.Nations[c];Check(n!=null&&n.Name!=null&&n.Name.Length<=128,"Invalid civilization name.");
                Check(n.Capital==-1||(n.Capital>=0&&n.Capital<w.Count&&w.Owner[n.Capital]==c&&w.Habitable(n.Capital)),"Invalid capital.");
                Check(Finite(n.Food)&&n.Food>=0&&n.Food<=1e6&&Finite(n.Refuges)&&n.Refuges>=0&&n.Refuges<=1e9&&Finite(n.ExpansionClock)&&n.ExpansionClock>=0&&n.ExpansionClock<30,"Invalid civilization state.");
            }
            Check(w.Wildlife.Count<=FlatWildlifeSimulation.MaxActors&&(w.WildlifeInitialized||w.Wildlife.Count==0),"Invalid wildlife population.");
            var actorIds=new HashSet<int>();
            foreach(var a in w.Wildlife)
            {
                Check(a!=null&&a.Id>0&&actorIds.Add(a.Id),"Invalid or duplicate wildlife identity.");
                Check(a.Species>=WildlifeSpecies.Rabbit&&a.Species<=WildlifeSpecies.Butterfly&&a.State<=WildlifeState.Sleeping,"Invalid wildlife classification.");
                Check(a.HomeTile>=0&&a.HomeTile<w.Count&&a.GoalTile>=0&&a.GoalTile<w.Count,"Invalid wildlife habitat or destination.");
                Check(Finite(a.X)&&a.X>=0&&a.X<w.Width&&Finite(a.Y)&&a.Y>=0&&a.Y<w.Height,"Invalid wildlife position.");
                Check(FMath.Finite(a.Hunger)&&a.Hunger>=0&&a.Hunger<=1&&FMath.Finite(a.Fatigue)&&a.Fatigue>=0&&a.Fatigue<=1,"Invalid wildlife needs.");
                Check((a.Facing==1||a.Facing==-1)&&FMath.Finite(a.AnimationTime)&&a.AnimationTime>=0&&a.AnimationTime<1e9f&&
                    FMath.Finite(a.MoveSpeed)&&a.MoveSpeed>=0&&a.MoveSpeed<=8&&a.RandomState!=0,"Invalid wildlife animation or randomness.");
            }
        }
        public static void WriteAtomic(string path,WorldData world)
        {
            byte[] bytes=Encode(world);string temp=path+".tmp",backup=path+".bak";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));File.WriteAllBytes(temp,bytes);
            // Backup is retained even on platforms without File.Replace.
            if(File.Exists(path))
            {
                try{File.Replace(temp,path,backup);return;}
                catch(PlatformNotSupportedException){}catch(IOException){}
                File.Copy(path,backup,true);File.Delete(path);
            }
            File.Move(temp,path);
        }
        public static WorldData Read(string path)
        {var f=new FileInfo(path);Check(f.Exists&&f.Length<=MaxFileBytes,"Save missing or oversized.");return Decode(File.ReadAllBytes(path));}
    }
}
