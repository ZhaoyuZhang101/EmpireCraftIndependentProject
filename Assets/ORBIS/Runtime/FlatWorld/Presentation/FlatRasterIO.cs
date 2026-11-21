using System;
using System.IO;
using System.Threading;
namespace Orbis.Flat
{
    public static class FlatRasterIO
    {
        // Portable 16-bit PNG: north-first; black=-4000m, white=4000m, sea~32768.
        public static void Export(WorldData world,string path)
        {
            var h=new ushort[world.Count];var water=new byte[world.Count*4];
            for(int y=0;y<world.Height;y++)for(int x=0;x<world.Width;x++)
            {
                int i=y*world.Width+x,j=(world.Height-1-y)*world.Width+x;
                int metres=FMath.Clamp(world.Elevation[i],-4000,4000);
                h[j]=(ushort)Math.Round((metres+4000)/8000.0*65535);
                var c=FlatPalette.Water(world.Water[i]);water[j*4]=c.r;water[j*4+1]=c.g;water[j*4+2]=c.b;water[j*4+3]=255;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllBytes(path,OrbisRasterCodec.Gray16(world.Width,world.Height,h));
            File.WriteAllBytes(Path.ChangeExtension(path,"water.png"),OrbisRasterCodec.RGBA8(world.Width,world.Height,water));
            var rivers=world.Facts?.RiverCurves;
            if(rivers!=null)
            {
                int width=rivers.OverviewWidth,height=width/2;
                var ink=rivers.DrawOverview(CancellationToken.None);
                var rgba=new byte[width*height*4];
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    int p=(y*width+x)*4;
                    rgba[p]=14;rgba[p+1]=79;rgba[p+2]=125;
                    rgba[p+3]=ink[(height-1-y)*width+x];
                }
                File.WriteAllBytes(Path.ChangeExtension(path,"rivers.png"),OrbisRasterCodec.RGBA8(width,height,rgba));
            }
            File.WriteAllText(Path.ChangeExtension(path,"metadata.json"),"{\"format\":\"ORBIS_FLAT_HEIGHT_1\",\"northFirst\":true,\"minMeters\":-4000,\"maxMeters\":4000,\"seaMeters\":0,\"heightIsBedNotLakeSurface\":true,\"waterImageIsPreviewOnly\":true}");
        }
        public static WorldData Import(string path,string seed,CancellationToken cancel)
            => Import(path,seed,cancel,false);

        public static WorldData Import(string path,string seed,CancellationToken cancel,bool preserveMetres)
        {
            var f=new FileInfo(path);if(!f.Exists||f.Length>OrbisRasterCodec.MaxBytes)throw new InvalidDataException("Height image missing or larger than 32 MiB.");
            var raster=OrbisRasterCodec.Read(File.ReadAllBytes(path));
            var w=new WorldData(raster.width,raster.height,seed,TerrainPreset.Continents);
            for(int y=0;y<w.Height;y++)for(int x=0;x<w.Width;x++)
            {
                int j=(w.Height-1-y)*w.Width+x,p=j*4;
                if(Math.Abs(raster.rgba[p]-raster.rgba[p+1])>1||Math.Abs(raster.rgba[p+1]-raster.rgba[p+2])>1)throw new InvalidDataException("Height map must be grayscale, not a colored satellite/map image.");
                short meters=(short)Math.Round(raster.gray[j]/65535.0*8000-4000);
                w.Elevation[y*w.Width+x]=preserveMetres?meters:HeightBands.Quantize(meters);
            }
            FlatGenerator.Rebuild(w,cancel);return w;
        }
    }
}
