using UnityEngine;
namespace Orbis.Flat
{
    public static class FlatGlobeGeometry
    {
        // The orbit silhouette and its cloud ground use this radius per metre.
        public const float HeightScale = .000006f;
        public const double GroundRadiusMetres = 6371008.8;
        // A metre viewed on the local chart is 2.5 times taller than true scale.
        const float LocalHeightScale = (float)(2.5 / GroundRadiusMetres);
        public const float MaximumHeight = HeightBands.MaximumBed;
        internal const float LocalHeightSpan = (MaximumHeight - HeightBands.MinimumBed) * LocalHeightScale;
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
        public static void LocalArtworkAxes(Vector4 cellAxes,float latitudeScale,out Vector2 eastPixel,out Vector2 northPixel)
        {
            // cellAxes maps screen-aligned art pixels to geographic cells. Invert it to
            // place one geographic art pixel using the same relief, scale and rotation.
            float inverse=1/(32*(cellAxes.x*cellAxes.w-cellAxes.y*cellAxes.z));
            eastPixel=new Vector2(cellAxes.w,-cellAxes.y)*inverse;
            northPixel=new Vector2(-cellAxes.z,cellAxes.x)*(inverse*latitudeScale);
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
        public static Vector4 ReliefParameters(float pixels,float morph,float centerHeight)
        {
            float weight=ReliefWeight(pixels,morph);
            // Heights arrive in globe radii. Keep the optical viewing angle independent
            // of the vertical exaggeration used to project terrain and attached objects.
            return new Vector4(weight,centerHeight,ReliefTilt,weight*(LocalHeightScale/HeightScale));
        }
        public static Vector3 ReliefPosition(Vector2 point,float height,Vector4 relief)
        {
            float relative=(height-relief.y)*relief.w;
            float perspective=ReliefCameraHeight/(ReliefCameraHeight-relative);
            return new Vector3(point.x*perspective,(point.y+relative*relief.z)*perspective,height*relief.w);
        }
        public static float ReliefClipWeight(float height,Vector4 relief,float morph)
        {
            return 1-(height-relief.y)*relief.w*morph/ReliefCameraHeight;
        }
        public static Vector3 PerspectiveWeights(Vector3 weights,Vector3 clipWeights)
        {
            var corrected=new Vector3(weights.x/clipWeights.x,weights.y/clipWeights.y,weights.z/clipWeights.z);
            return corrected/(corrected.x+corrected.y+corrected.z);
        }
        // Locate the same triangle and diagonal as the radial surface mesh. Fractional ring
        // follows the straight chord between sector vertices, including the outer boundary.
        public static bool TryLatticeTriangle(Vector2 point,float radius,Vector2[] lattice,int rings,int sectors,
            out Vector3Int vertices,out Vector3 weights)
        {
            vertices=default;weights=default;
            if(point.sqrMagnitude>radius*radius)return false;
            int sector=FMath.Wrap(Mathf.FloorToInt(Mathf.Atan2(point.y,point.x)*sectors/(2*Mathf.PI)),sectors);
            int stride=sectors+1;
            var right=lattice[rings*stride+sector];var left=lattice[rings*stride+sector+1];
            var scaled=point*(rings/radius);
            float determinant=right.x*left.y-right.y*left.x;
            float u=Mathf.Max(0,(scaled.x*left.y-scaled.y*left.x)/determinant);
            float v=Mathf.Max(0,(right.x*scaled.y-right.y*scaled.x)/determinant);
            float ringPoint=u+v;
            if(ringPoint>rings+1e-4f)return false;
            int ring=Mathf.Min(rings-1,Mathf.FloorToInt(ringPoint));
            float outer=Mathf.Clamp01(ringPoint-ring);
            int a=ring*stride+sector,b=a+stride;
            if(ring>0&&v<=ring*(1-outer))
            {
                float innerLeft=v/ring;
                vertices=new Vector3Int(a,b,a+1);weights=new Vector3(1-outer-innerLeft,outer,innerLeft);
            }
            else
            {
                float outerRight=u/(ring+1);
                vertices=new Vector3Int(a+1,b,b+1);weights=new Vector3(1-outer,outerRight,outer-outerRight);
            }
            return true;
        }
        public static Rect ReliefBounds(Vector2 projected,Vector4 relief)
        {
            float minimum=(HeightBands.MinimumBed*HeightScale-relief.y)*relief.w;
            float maximum=(MaximumHeight*HeightScale-relief.y)*relief.w;
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
            // The rendered vertices are floats, but the ray's barycentric signs must
            // agree on a shared edge. In the narrow polar fan float dot/cross products
            // can reject both neighbours even at the exact viewport centre.
            double abx=(double)b.x-a.x,aby=(double)b.y-a.y,abz=(double)b.z-a.z;
            double acx=(double)c.x-a.x,acy=(double)c.y-a.y,acz=(double)c.z-a.z;
            var direction=ray.direction;
            double hx=direction.y*acz-direction.z*acy;
            double hy=direction.z*acx-direction.x*acz;
            double hz=direction.x*acy-direction.y*acx;
            double determinant=abx*hx+aby*hy+abz*hz;
            if(System.Math.Abs(determinant)<1e-10)return false;
            var origin=ray.origin;
            double qx=(double)origin.x-a.x,qy=(double)origin.y-a.y,qz=(double)origin.z-a.z;
            double u=(qx*hx+qy*hy+qz*hz)/determinant;
            if(u<0||u>1)return false;
            double cx=qy*abz-qz*aby,cy=qz*abx-qx*abz,cz=qx*aby-qy*abx;
            double v=(direction.x*cx+direction.y*cy+direction.z*cz)/determinant;
            if(v<0||u+v>1)return false;
            double t=(acx*cx+acy*cy+acz*cz)/determinant;
            if(t<0)return false;
            distance=(float)t;
            weights=new Vector3((float)(1-u-v),(float)u,(float)v);
            return true;
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
            return Mathf.Clamp(w.Elevation[i]+(w.Facts.ShoreDistance[i]>0?w.Facts.Depth[i]:0),HeightBands.MinimumBed,MaximumHeight);
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
            float blend=(float)FlatSurfaceSampling.PolarRingWeight(latitudeUV,rows);
            return 1+Mathf.Lerp(polar,Mathf.Clamp(height,HeightBands.MinimumBed,MaximumHeight),blend)*HeightScale;
        }
    }
}
