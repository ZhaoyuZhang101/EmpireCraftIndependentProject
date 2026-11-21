using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orbis
{
    public static class OrbisTerrainEvolution
    {
        // Conservative thermal relaxation, not a claim of a full geological erosion simulation.
        public static void Relax(float[] heights,int[][] neighbors,int iterations,float talus)
        {
            float[] delta=new float[heights.Length];
            for(int step=0;step<iterations;step++)
            {
                Array.Clear(delta,0,delta.Length);
                for(int i=0;i<heights.Length;i++)
                {
                    int low=i;foreach(int j in neighbors[i])if(heights[j]<heights[low])low=j;
                    float excess=heights[i]-heights[low]-talus;if(excess<=0)continue;
                    float transfer=excess*.12f;delta[i]-=transfer;delta[low]+=transfer;
                }
                for(int i=0;i<heights.Length;i++)heights[i]+=delta[i];
            }
        }
    }
    public sealed partial class OrbisPlanetView
    {
        public float TerrainHeight(Vector3 p,OrbisSphericalSampler.Sample s)
        {
            Vector3 w=s.weights;Tile a=world.tiles[s.a],b=world.tiles[s.b],c=world.tiles[s.c];
            if(world.MapSource!=null)return Mathf.Clamp(world.MapSource.Sample(p)+a.edit*w.x+b.edit*w.y+c.edit*w.z,-.95f,1.05f);
            float h=a.h*w.x+b.h*w.y+c.h*w.z;
            float wet=(a.h<=0||a.lake>.018f?w.x:0)+(b.h<=0||b.lake>.018f?w.y:0)+(c.h<=0||c.lake>.018f?w.z:0);
            float gate=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.035f,.4f,h))*(1-wet);
            if(gate<=0)return Mathf.Clamp(h,-.95f,1.05f); // Ocean/flat lowland: identical zero-detail result.
            Vector3 warp=new Vector3(world.noise.Fbm(p*4,2),world.noise.Fbm(p*4+Vector3.one*7,2),world.noise.Fbm(p*4+Vector3.one*13,2));
            Vector3 q=p+warp*.009f;float broad=world.noise.Fbm(q*48,3),ridge=1-Mathf.Abs(world.noise.Fbm(q*95,3)*1.7f);
            return Mathf.Clamp(h+(broad*.07f+(ridge-.65f)*.035f)*gate,-.95f,1.05f);
        }
        public float UncarvedRadius(Vector3 p,OrbisSphericalSampler.Sample s,out float wet,out float depth,out float h)
        {
            if(world.IsPresentationOnly)
            {
                var source=world.FlatSource;int id=world.FlatTile(p);var kind=source.Water[id];
                bool area=Orbis.Flat.FlatTerrainAnalysis.AreaWater(kind);
                h=source.Elevation[id]/7000f;wet=area?1:0;
                depth=area?Mathf.Max(.05f,(source.WaterLevel[id]-source.Elevation[id])/7000f*120):0;
                if(area)return 1+source.WaterLevel[id]/7000f*.075f;
                world.MapSource.Pixel(p,out float x,out float y);
                var uv=new Vector2((x+.5f)/source.Width,1-(y+.5f)/source.Height);
                float metres=source.Facts==null?source.Elevation[id]:Orbis.Flat.FlatGlobeGeometry.SurfaceHeight(source,uv);
                return 1+metres/7000f*.075f;
            }
            h=TerrainHeight(p,s);wet=0;depth=0;if(h<=0){wet=1;depth=Mathf.Max(.05f,-h*160);return 1;}
            Vector3 w=s.weights;float lakeWeight=0,lakeLevel=0;
            for(int j=0;j<3;j++){Tile t=world.tiles[j==0?s.a:j==1?s.b:s.c];if(t.lake>.018f&&t.h>0){float weight=w[j];lakeWeight+=weight;lakeLevel+=t.flood*weight;}}
            if(lakeWeight>.5f){lakeLevel/=lakeWeight;if(lakeLevel>h){wet=1;depth=(lakeLevel-h)*120;return 1+lakeLevel*.075f;}}
            return 1+h*.075f;
        }
        public void RefineAppearance(ref Color color,ref List<Vector4> waters,int index,float wet,float depth,float height,Vector4 climate)
        {
            if(layer!=0)return;
            if(wet<.5f&&color.a>.05f)
            {
                Color sand=new Color(.58f,.46f,.29f),grass=new Color(.14f,.36f,.20f);
                color=Color.Lerp(sand,grass,Mathf.InverseLerp(.12f,.7f,climate.x));
                color=Color.Lerp(color,new Color(.4f,.39f,.36f),Mathf.InverseLerp(.45f,.95f,height));
                color=Color.Lerp(color,new Color(.88f,.93f,.95f),climate.y);
            }
            color.a=wet;waters[index]=new Vector4(depth,0,wet>.5f&&height>0?1:0,height);
        }
        public void SampleAppearance(Vector3 p,OrbisSphericalSampler.Sample s,out Color color,out Vector4 climate,out Vector4 water)
        {
            Vector3 w=s.weights;color=GetTileColor(s.a)*w.x+GetTileColor(s.b)*w.y+GetTileColor(s.c)*w.z;
            climate=TileClimate(s.a)*w.x+TileClimate(s.b)*w.y+TileClimate(s.c)*w.z;water=TileWater(s.a)*w.x+TileWater(s.b)*w.y+TileWater(s.c)*w.z;
            if(layer!=0)return;UncarvedRadius(p,s,out float wet,out float depth,out float h);
            RefineLocalAppearance(ref color,ref water,climate,wet,depth,h);
        }
        public void SampleAppearanceCached(OrbisSphericalSampler.Sample s,Vector3 geometry,
            out Color color,out Vector4 climate,out Vector4 water)
        {
            Vector3 w=s.weights;
            color=tilePalette[s.a]*w.x+tilePalette[s.b]*w.y+tilePalette[s.c]*w.z;
            climate=tileClimate[s.a]*w.x+tileClimate[s.b]*w.y+tileClimate[s.c]*w.z;
            water=tileWater[s.a]*w.x+tileWater[s.b]*w.y+tileWater[s.c]*w.z;
            if(layer==0)RefineLocalAppearance(ref color,ref water,climate,geometry.x,geometry.y,geometry.z);
        }
        static void RefineLocalAppearance(ref Color color,ref Vector4 water,Vector4 climate,float wet,float depth,float h)
        {
            if(wet<.5f&&color.a>.05f){color=Color.Lerp(new Color(.58f,.46f,.29f),new Color(.14f,.36f,.2f),Mathf.InverseLerp(.12f,.7f,climate.x));color=Color.Lerp(color,new Color(.4f,.39f,.36f),Mathf.InverseLerp(.45f,.95f,h));color=Color.Lerp(color,new Color(.88f,.93f,.95f),climate.y);}
            color.a=wet;water=new Vector4(depth,0,wet>.5f&&h>0?1:0,h);
        }
        public float VisibleRadiusAt(Vector3 p)=>Rivers.VisibleRadius(p,RadiusAt(p));
    }
}
