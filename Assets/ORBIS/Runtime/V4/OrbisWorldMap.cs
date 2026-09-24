using System;
using System.IO;
using UnityEngine;

namespace Orbis
{
    [Serializable] public sealed class OrbisMapSave
    {
        public int version=1;
        public string heightPNG,riverPNG;
        public float seaGray=.475f,heightSpan=2;
    }
    public sealed class OrbisWorldMap
    {
        public readonly int width,height;
        public readonly float[] elevations;
        public readonly byte[] rivers;
        public readonly float seaGray,heightSpan;
        public readonly byte[] heightFile,riverFile;
        float northMean,southMean;
        public bool HasRivers {get;private set;}
        public OrbisWorldMap(byte[] heightBytes,byte[] riverBytes,float sea=.475f,float span=2)
        {
            if(float.IsNaN(sea)||sea<0||sea>1||float.IsNaN(span)||span<=0||span>4)throw new ArgumentException("海平面灰度须为 0–1，高度跨度须为 0–4。");
            var h=OrbisRasterCodec.Read(heightBytes);var r=riverBytes==null?null:OrbisRasterCodec.Read(riverBytes);
            if(h.width!=h.height*2)throw new InvalidDataException("全球地图须为 2:1 等经纬图，例如 1024×512。不要把局部 DEM 直接包裹成星球。");
            if(r!=null&&(r.width!=h.width||r.height!=h.height))throw new InvalidDataException("高度图与河流图必须具有完全相同的尺寸和地理对齐。");
            width=h.width;height=h.height;seaGray=sea;heightSpan=span;elevations=new float[width*height];rivers=r==null?new byte[width*height*4]:r.rgba;
            for(int i=0;i<elevations.Length;i++)
            {int p=i*4;if(Math.Abs(h.rgba[p]-h.rgba[p+1])>1||Math.Abs(h.rgba[p+1]-h.rgba[p+2])>1)throw new InvalidDataException("高度图必须为灰度图。");
                elevations[i]=Mathf.Clamp((h.gray[i]/65535f-sea)*span,-.95f,1.05f);if(RiverWeight(rivers,p)>0)HasRivers=true;}
            for(int x=0;x<width;x++){northMean+=elevations[x]/width;southMean+=elevations[(height-1)*width+x]/width;}
            // Canonical PNG snapshots keep full saves independent of the source files.
            heightFile=OrbisRasterCodec.Gray16(width,height,h.gray);riverFile=OrbisRasterCodec.RGBA8(width,height,rivers);
        }
        // ORBIS native: white/black/transparent background; blue pixels carry width in blue intensity.
        // Green/red/yellow source/junction markers remain connected nodes. Not a full EU4 palette interpreter.
        public static float RiverWeight(byte[] rgba,int p)
        {
            if(rgba[p+3]<128)return 0;int r=rgba[p],g=rgba[p+1],b=rgba[p+2];
            if(b>40&&b>r+25&&b>g+15)return Mathf.Lerp(.3f,2.2f,b/255f);
            if(g>180&&r<90&&b<90)return .3f;
            if(r>180&&g<90&&b<90)return .6f;
            if(r>180&&g>180&&b<90)return .6f;
            return 0;
        }
        public static Vector3 Direction(float x,float y,int w,int h)
        {float lon=((x+.5f)/w-.5f)*Mathf.PI*2,lat=(.5f-(y+.5f)/h)*Mathf.PI;return new Vector3(Mathf.Sin(lon)*Mathf.Cos(lat),Mathf.Sin(lat),Mathf.Cos(lon)*Mathf.Cos(lat));}
        public void Pixel(Vector3 p,out float x,out float y)
        {p.Normalize();x=(Mathf.Atan2(p.x,p.z)/(Mathf.PI*2)+.5f)*width-.5f;y=(.5f-Mathf.Asin(Mathf.Clamp(p.y,-1,1))/Mathf.PI)*height-.5f;}
        int Index(int x,int y)=>Mathf.Clamp(y,0,height-1)*width+((x%width)+width)%width;
        public float Sample(Vector3 p)
        {
            Pixel(p,out float x,out float y);int a=Mathf.FloorToInt(x),b=Mathf.FloorToInt(y);float u=x-a,v=y-b;
            float result=Mathf.Lerp(Mathf.Lerp(elevations[Index(a,b)],elevations[Index(a+1,b)],u),Mathf.Lerp(elevations[Index(a,b+1)],elevations[Index(a+1,b+1)],u),v);
            // A pole is one point, not an entire longitude ring.
            float pole=Mathf.Clamp01(Mathf.Abs(p.normalized.y)*height-(height-1));
            if(pole>0)result=Mathf.Lerp(result,p.y>0?northMean:southMean,pole);
            return result;
        }
        public OrbisMapSave Save()=>new OrbisMapSave{heightPNG=Convert.ToBase64String(heightFile),riverPNG=Convert.ToBase64String(riverFile),seaGray=seaGray,heightSpan=heightSpan};
        public static OrbisWorldMap Restore(OrbisMapSave s)
        {
            if(s==null)return null;
            if(s.version!=1||string.IsNullOrEmpty(s.heightPNG)||s.heightPNG.Length>OrbisRasterCodec.MaxBytes*4/3||s.riverPNG==null||s.riverPNG.Length>OrbisRasterCodec.MaxBytes*4/3)throw new InvalidDataException("双图存档无效。");
            return new OrbisWorldMap(Convert.FromBase64String(s.heightPNG),Convert.FromBase64String(s.riverPNG),s.seaGray,s.heightSpan);
        }
    }
    public sealed partial class OrbisWorld
    {
        public OrbisWorldMap MapSource {get;private set;}
        public float TerritoryClock;
        void RefreshAuthoredRiverMask()
        {
            foreach(var tile in tiles)tile.authoredRiver=0;
            if(MapSource==null||!MapSource.HasRivers)return;
            var sampler=new OrbisSphericalSampler(grid);int hint=0;
            for(int y=0;y<MapSource.height;y++)for(int x=0;x<MapSource.width;x++)
            {
                float value=OrbisWorldMap.RiverWeight(MapSource.rivers,(y*MapSource.width+x)*4);if(value<=0)continue;
                Vector3 p=OrbisWorldMap.Direction(x,y,MapSource.width,MapSource.height);var s=sampler.Locate(p,hint);hint=s.a;
                int id=s.weights.x>=s.weights.y&&s.weights.x>=s.weights.z?s.a:s.weights.y>=s.weights.z?s.b:s.c;
                if(tiles[id].h>0)tiles[id].authoredRiver=Mathf.Max(tiles[id].authoredRiver,value);
            }
        }
        public void ImportWorldMap(OrbisWorldMap source)
        {
            if(source==null)throw new ArgumentNullException(nameof(source));
            MapSource=source;TerritoryClock=0;civs.Clear();expeditions.Clear();events.Clear();war=false;
            foreach(var t in tiles){t.edit=0;t.owner=-1;t.pop=0;t.city=false;}
            Terrain();Weather=null;for(int i=0;i<40;i++)Climate(1,0);Weather=new OrbisWeather(this);UpdateDischarge();
            geography.Build(this);RefreshNames();UpdateStats();Revision++;Log("已按高度图与河流图创建世界；经纬图 "+source.width+"×"+source.height+"，逻辑地块编号保持不变。");
        }
    }
}
