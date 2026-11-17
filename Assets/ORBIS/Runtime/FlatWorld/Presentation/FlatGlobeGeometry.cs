using UnityEngine;
namespace Orbis.Flat
{
    public static class FlatGlobeGeometry
    {
        // One vertical scale for the presentation. Authoritative metres are unchanged.
        public const float HeightScale = .000006f;
        public const float MaximumHeight = 4000f;
        public static Vector2 PolarHeights(WorldData world)
        {
            double south=0,north=0;
            for(int x=0;x<world.Width;x++)
            { south+=Height(world,x,0); north+=Height(world,x,world.Height-1); }
            return new Vector2((float)(south/world.Width),(float)(north/world.Width));
        }
        static float Height(WorldData w,int x,int y)
        {
            int i=y*w.Width+x;
            return Mathf.Clamp(w.Elevation[i]+(w.Facts.ShoreDistance[i]>0?w.Facts.Depth[i]:0),0,MaximumHeight);
        }
        public static float SurfaceHeight(WorldData world,Vector2 uv)
        {
            float x=uv.x*world.Width-.5f;
            float y=uv.y*world.Height-.5f;
            int column=Mathf.FloorToInt(x),row=Mathf.FloorToInt(y);
            Vector3 bottomLeft=Fields(world,column,row),bottomRight=Fields(world,column+1,row);
            Vector3 topLeft=Fields(world,column,row+1),topRight=Fields(world,column+1,row+1);
            var bottom=Vector3.Lerp(bottomLeft,bottomRight,x-column);
            var top=Vector3.Lerp(topLeft,topRight,x-column);
            var fields=Vector3.Lerp(bottom,top,y-row);
            return fields.x+(fields.y>0?fields.z:0);
        }
        static Vector3 Fields(WorldData world,int x,int y)
        {
            int index=Mathf.Clamp(y,0,world.Height-1)*world.Width+FMath.Wrap(x,world.Width);
            return new Vector3(world.Elevation[index],world.Facts.ShoreDistance[index],world.Facts.Depth[index]);
        }
        public static float Radius(float height,float latitudeUV,int rows,Vector2 poles)
        {
            float polar=Mathf.Lerp(poles.x,poles.y,latitudeUV>=.5f?1:0);
            float blend=Mathf.SmoothStep(0,1,Mathf.Clamp01(Mathf.Min(latitudeUV,1-latitudeUV)*rows));
            return 1+Mathf.Lerp(polar,Mathf.Clamp(height,0,MaximumHeight),blend)*HeightScale;
        }
    }
}
