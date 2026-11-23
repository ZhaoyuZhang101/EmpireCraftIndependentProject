using System;
using System.IO;
using UnityEngine;
namespace Orbis
{
    public static class OrbisMapIO
    {
        public static string ImportFolder=>Path.Combine(Application.persistentDataPath,"MapImport");
        static byte[] ReadBounded(string path)
        {
            var file=new FileInfo(path);if(!file.Exists)throw new FileNotFoundException("找不到地图："+path);
            if(file.Length<32||file.Length>OrbisRasterCodec.MaxBytes)throw new InvalidDataException($"每张地图须在 32 字节至 {OrbisRasterCodec.MaxBytes/1048576} MiB 之间。");return File.ReadAllBytes(path);
        }
        public static OrbisWorldMap Read(string heightPath,string riverPath,float sea=.475f,float span=2)
        {return new OrbisWorldMap(ReadBounded(heightPath),string.IsNullOrWhiteSpace(riverPath)?null:ReadBounded(riverPath),sea,span);}
        public static OrbisWorldMap ReadFolder(string folder)
        {
            if(string.IsNullOrWhiteSpace(folder))folder=ImportFolder;
            string h=Path.Combine(folder,"heightmap.png"),r=Path.Combine(folder,"rivers.png");
            if(!File.Exists(h))h=Path.Combine(folder,"heightmap.bmp");if(!File.Exists(r))r=Path.Combine(folder,"rivers.bmp");
            return Read(h,File.Exists(r)?r:null);
        }
        public static OrbisWorldMap Example()
        {
            var h=Resources.Load<TextAsset>("ORBIS/V4/ExampleHeight");var r=Resources.Load<TextAsset>("ORBIS/V4/ExampleRivers");
            if(!h||!r)throw new FileNotFoundException("双图示例缺失。");return new OrbisWorldMap(h.bytes,r.bytes);
        }
        public static string Export(OrbisPlanetView view,string folder=null)
        {
            // Export BASE terrain, not a carved bed that would be carved a second time on import.
            int width=view.world.MapSource?.width??1024,height=view.world.MapSource?.height??512;
            var heights=new ushort[width*height];int hint=0;
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                Vector3 p=OrbisWorldMap.Direction(x,y,width,height);var sample=view.SurfaceSampler.Locate(p,hint);hint=sample.a;
                float h=view.TerrainHeight(p,sample);heights[y*width+x]=(ushort)Mathf.RoundToInt(Mathf.Clamp01((h+.95f)/2)*65535);
            }
            byte[] rivers;
            if(view.world.MapSource!=null&&view.world.MapSource.HasRivers)rivers=(byte[])view.world.MapSource.rivers.Clone();
            else
            {
                rivers=new byte[width*height*4];for(int i=0;i<rivers.Length;i++)rivers[i]=255;
                foreach(var reach in view.Rivers.reaches)
                {
                    // Width is an attribute, never the width of a painted blue band in the topology raster.
                    int samples=Mathf.Max(2,Mathf.CeilToInt(Vector3.Angle(reach.a,reach.b)*Mathf.Deg2Rad*width/Mathf.PI*2));
                    for(int k=0;k<=samples;k++)
                    {
                        float t=k/(float)samples;Vector3 p=Vector3.Slerp(reach.a,reach.b,t);
                        int x=(Mathf.FloorToInt((Mathf.Atan2(p.x,p.z)/(2*Mathf.PI)+.5f)*width)%width+width)%width;
                        int y=Mathf.Clamp(Mathf.FloorToInt((.5f-Mathf.Asin(Mathf.Clamp(p.y,-1,1))/Mathf.PI)*height),0,height-1);
                        float q=Mathf.Lerp(reach.qa,reach.qb,t),w=Mathf.Lerp(reach.wa,reach.wb,t);
                        float factor=w/Mathf.Max(.0007f,.00075f+Mathf.Sqrt(q)*.00033f);int blue=Mathf.Clamp(Mathf.RoundToInt((factor-.3f)/1.9f*255),60,255);
                        int i=(y*width+x)*4;bool blank=rivers[i]==255;rivers[i]=0;rivers[i+1]=0;rivers[i+2]=(byte)(blank?blue:Mathf.Max(blue,rivers[i+2]));rivers[i+3]=255;
                    }
                }
            }
            string root=string.IsNullOrWhiteSpace(folder)?Path.Combine(Application.persistentDataPath,"MapExports"):folder;
            string output=Path.Combine(root,"orbis-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output,"heightmap.png"),OrbisRasterCodec.Gray16(width,height,heights));
            File.WriteAllBytes(Path.Combine(output,"rivers.png"),OrbisRasterCodec.RGBA8(width,height,rivers));
            File.WriteAllText(Path.Combine(output,"README.txt"),"ORBIS V4 双图世界模板\n2:1 等经纬图，顶部为北，左侧为西；两图严格对齐。\nheightmap.png：16 位线性灰度，h=(gray/65535-0.475)*2。\nrivers.png：单像素中心线，蓝色亮度编码宽度提示。\n仅地形/河网模板；人口、国家、相机与时间请另用保存世界。\n栅格化有分辨率损失；不宣称双图重建逐顶点无损。\n");
            return output;
        }
    }
}
