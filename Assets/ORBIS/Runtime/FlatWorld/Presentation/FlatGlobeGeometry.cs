using UnityEngine;
namespace Orbis.Flat
{
    public static class FlatGlobeGeometry
    {
        // One vertical scale for the presentation. Authoritative metres are unchanged.
        public const float HeightScale = .000006f;
        public const float MaximumHeight = 4000f;
        public const float ReliefTilt = .72f;
        public const float ReliefCameraHeight = .18f;
        public static bool TryActorFrame(Camera camera,Vector3 eastDerivative,Vector3 northDerivative,float pixelScale,
            out Vector2 east,out Vector2 north,out Vector4 cellAxes)
        {
            var right=camera.transform.right;var up=camera.transform.up;
            var dx=new Vector2(Vector3.Dot(eastDerivative,right),Vector3.Dot(eastDerivative,up));
            var dy=new Vector2(Vector3.Dot(northDerivative,right),Vector3.Dot(northDerivative,up));
            east=dx.normalized;north=dy.normalized;cellAxes=Vector4.zero;
            float determinant=dx.x*dy.y-dx.y*dy.x;
            if(Mathf.Abs(determinant)<1e-14f)return false;
            cellAxes=new Vector4(dy.y,-dx.y,-dy.x,dx.x)*(pixelScale/determinant);
            return true;
        }
        public static Ray ScreenRay(Camera camera,Vector2 screen)
        {
            if(!camera.orthographic)return camera.ScreenPointToRay(screen);
            var rect=camera.pixelRect;
            var offset=(screen-rect.center)*2;
            var transform=camera.transform;
            var origin=transform.position
                +transform.right*(offset.x/rect.width*camera.orthographicSize*camera.aspect)
                +transform.up*(offset.y/rect.height*camera.orthographicSize);
            return new Ray(origin,transform.forward);
        }
        public static float ReliefWeight(float pixels,float morph)
        {
            return Mathf.SmoothStep(0,1,Mathf.InverseLerp(16,48,pixels))
                *Mathf.SmoothStep(0,1,Mathf.InverseLerp(.75f,1,morph));
        }
        public static Vector3 ReliefPosition(Vector2 point,float height,Vector4 relief)
        {
            float relative=(height-relief.y)*relief.x;
            float perspective=ReliefCameraHeight/(ReliefCameraHeight-relative);
            return new Vector3(point.x*perspective,(point.y+relative*relief.z)*perspective,height*relief.x);
        }
        public static float ReliefClipWeight(float height,Vector4 relief,float morph)
        {
            return 1-(height-relief.y)*relief.x*morph/ReliefCameraHeight;
        }
        public static Vector3 PerspectiveWeights(Vector3 weights,Vector3 clipWeights)
        {
            var corrected=new Vector3(weights.x/clipWeights.x,weights.y/clipWeights.y,weights.z/clipWeights.z);
            return corrected/(corrected.x+corrected.y+corrected.z);
        }
        public static Rect ReliefBounds(Vector2 projected,Vector4 relief)
        {
            float minimum=-relief.y*relief.x;
            float maximum=(MaximumHeight*HeightScale-relief.y)*relief.x;
            var near=projected*(1-maximum/ReliefCameraHeight)-Vector2.up*(maximum*relief.z);
            var far=projected*(1-minimum/ReliefCameraHeight)-Vector2.up*(minimum*relief.z);
            // World-space float rounding can straddle the centre fan's sector
            // boundary. Keep the angular search as conservative as cell culling.
            const float padding=1e-6f;
            return Rect.MinMaxRect(Mathf.Min(near.x,far.x)-padding,Mathf.Min(near.y,far.y)-padding,
                                  Mathf.Max(near.x,far.x)+padding,Mathf.Max(near.y,far.y)+padding);
        }
        public static bool IntersectTriangle(Ray ray,Vector3 a,Vector3 b,Vector3 c,out Vector3 weights,out float distance)
        {
            weights=Vector3.zero;distance=0;
            Vector3 ab=b-a,ac=c-a,h=Vector3.Cross(ray.direction,ac);
            float determinant=Vector3.Dot(ab,h);
            if(Mathf.Abs(determinant)<1e-10f)return false;
            var q=ray.origin-a;
            float u=Vector3.Dot(q,h)/determinant;
            if(u<0||u>1)return false;
            var cross=Vector3.Cross(q,ab);
            float v=Vector3.Dot(ray.direction,cross)/determinant;
            if(v<0||u+v>1)return false;
            distance=Vector3.Dot(ac,cross)/determinant;
            weights=new Vector3(1-u-v,u,v);
            return distance>=0;
        }
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
            float bottomLeft=SurfaceCorner(world,column,row),bottomRight=SurfaceCorner(world,column+1,row);
            float topLeft=SurfaceCorner(world,column,row+1),topRight=SurfaceCorner(world,column+1,row+1);
            return Mathf.Lerp(Mathf.Lerp(bottomLeft,bottomRight,x-column),Mathf.Lerp(topLeft,topRight,x-column),y-row);
        }
        static float SurfaceCorner(WorldData world,int x,int y)
        {
            return Height(world,FMath.Wrap(x,world.Width),Mathf.Clamp(y,0,world.Height-1));
        }
        public static float Radius(float height,float latitudeUV,int rows,Vector2 poles)
        {
            float polar=Mathf.Lerp(poles.x,poles.y,latitudeUV>=.5f?1:0);
            float blend=Mathf.SmoothStep(0,1,Mathf.Clamp01(Mathf.Min(latitudeUV,1-latitudeUV)*rows));
            return 1+Mathf.Lerp(polar,Mathf.Clamp(height,0,MaximumHeight),blend)*HeightScale;
        }
    }
}
