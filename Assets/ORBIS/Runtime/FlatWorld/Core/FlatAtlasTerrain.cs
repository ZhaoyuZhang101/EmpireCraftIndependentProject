using System;
using System.IO;
using System.Threading;
namespace Orbis.Flat
{
    public static class FlatAtlasTerrain
    {
        public static WorldData Read(byte[] bytes,string seed,CancellationToken cancel)
        {
            using(var stream=new MemoryStream(bytes))using(var reader=new BinaryReader(stream))
            {
                if(reader.ReadUInt32()!=0x3154414f)throw new InvalidDataException("World Atlas terrain signature");
                int width=reader.ReadInt32(),height=reader.ReadInt32();uint hash=reader.ReadUInt32();
                if(width<32||width>1024||height!=width/2||bytes.Length!=16+width*height*2||hash!=FMath.Seed(seed))throw new InvalidDataException("World Atlas dimensions or seed mismatch");
                var world=new WorldData(width,height,seed,TerrainPreset.Continents);
                for(int i=0;i<world.Count;i++){short h=reader.ReadInt16();if(h < -4000||h>4000)throw new InvalidDataException("Elevation out of range");world.Elevation[i]=h;}
                FlatGenerator.Rebuild(world,cancel);return world;
            }
        }
    }
}
