using System;
using UnityEngine;
using UnityEngine.Rendering;
using Orbis.Planet;
namespace Orbis.Flat
{
    // Sphere data is authoritative. The local chart and strategic atlas are presentation only.
    public sealed class FlatScrollNavigator : MonoBehaviour
    {
        const float Fov=42,Start=1.6f,End=1.08f,Tau=Mathf.PI*2;
        // One zoom coordinate spans globe, unfold and local chart: altitude=EndGap*exp(zoom*ZoomRate).
        // A single critically damped spring drives it, so no hand-off stalls at the fold endpoints.
        const float EndGap=.22f,ZoomRate=.075f,ZoomOmega=18,MaxAltitude=5,HomeAltitude=2.1f;
        Camera viewCamera;WorldData world;FlatMapRenderer maps;PlanetLocalProjection projection;
        Mesh mesh;Material surface;MeshRenderer terrain;LineRenderer routeLine;MarkleyAtlasNavigator atlas;bool atlasMode;
        float distance=3.1f,morph,pixels=16,spin,zoom,targetZoom,zoomVelocity;
        Vector2 zoomScreen;bool zoomAnchor;Quaternion orbitRotation=Quaternion.Euler(18,0,0);
        double currentDay,lockDay;bool locked;Vector3 normal,east,north;Vector2 poles;int revision=-1;int[] route=new int[0];
        const int Rings=128,Sectors=256;Vector2[] lattice;int[] triangles;
        Vector4[] pickPositions;int[] pickVertexStamps,pickCellStamps;int pickGeneration;
        Ray pickRay;Vector4 pickRelief;float pickRadius,pickNearest;Vector2 pickHit;
        public PlanetTangentFrame LocalFrame=>projection.Frame;
        public bool IsFlat=>atlasMode||morph>=1;
        public bool IsGlobe=>!atlasMode&&morph<=0;
        public bool IsTransitioning=>!atlasMode&&morph>0&&morph<1;
        public bool Overview=>IsFlat&&PixelsPerTile<24;
        public bool IsDetailed=>IsFlat&&PixelsPerTile>=24;
        public bool IsAtlas=>atlasMode;
        public float Morph=>atlasMode?1:morph;
        public float DisplaySpin=>atlasMode?atlas.DisplaySpin:spin;
        public float Distance=>distance;
        public float PixelsPerTile=>atlasMode?atlas.PixelsPerTile:pixels;
        public float ReturnPixelsPerTile=>EntryPixels;
        public float DetailWeight=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(12,32,PixelsPerTile))*Morph;
        public float MapHeight=>atlasMode?atlas.MapHeight:world.Height;
        public Vector2 ProjectionSize=>new Vector2(world.Width,MapHeight);
        public Vector2 CenterTile {get{var c=projection.Center;return atlasMode?atlas.CenterTile:new Vector2((float)PlanetCoordinateSystem.MapU(c)*world.Width,(float)PlanetCoordinateSystem.MapV(c)*world.Height);}}
        public Material SurfaceMaterial=>atlasMode?atlas.SurfaceMaterial:surface;
        public int VertexCount=>mesh?mesh.vertexCount:0;
        public int ActiveSheets=>atlasMode?atlas.ActiveSheets:1;
        public string ModeName=>atlasMode?"全球战略图":IsGlobe?"星球":IsFlat?"局部地表":"进入地表 "+Mathf.RoundToInt(morph*100)+"%";
        float TileScale=>Tau/world.Width;
        static float HalfTan=>Mathf.Tan(Fov*Mathf.Deg2Rad*.5f);
        float EntryPixels=>viewCamera.pixelHeight*TileScale/(2*EndGap*HalfTan);
        static float ZoomFor(float altitude)=>Mathf.Log(altitude/EndGap)/ZoomRate;
        static float Altitude(float z)=>EndGap*Mathf.Exp(z*ZoomRate);
        float ZoomForPixels(float p)=>ZoomFor(EndGap*EntryPixels/p);
        static float FoldZoom=>ZoomFor(Start-1);
        float MinZoom=>ZoomForPixels(Mathf.Max(96,EntryPixels*4));
        static float MaxZoom=>ZoomFor(MaxAltitude);
        float Radius=>Mathf.Lerp(Mathf.PI-.0001f,Mathf.Min(1.4f,Mathf.Max(.04f,viewCamera.orthographicSize*Mathf.Sqrt(1+viewCamera.aspect*viewCamera.aspect)*1.3f+FlatGlobeGeometry.MaximumHeight*FlatGlobeGeometry.HeightScale*FlatGlobeGeometry.ReliefTilt)),morph);
        Vector4 ReliefParameters
        {
            get
            {
                var center=projection.Center;
                var uv=new Vector2((float)PlanetCoordinateSystem.MapU(center),(float)PlanetCoordinateSystem.MapV(center));
                float height=FlatGlobeGeometry.Radius(FlatGlobeGeometry.SurfaceHeight(world,uv),uv.y,world.Height,poles)-1;
                return new Vector4(FlatGlobeGeometry.ReliefWeight(pixels,morph),height,FlatGlobeGeometry.ReliefTilt,0);
            }
        }
        static Vector3 V(PlanetVector p)=>new Vector3((float)p.X,(float)p.Y,(float)p.Z);
        static Vector3 Reflect(Vector3 p){p.z=-p.z;return p;}
        Quaternion Spin=>Quaternion.AngleAxis(spin*Mathf.Rad2Deg,Vector3.up);
        public void Initialize(WorldData data,Camera target,FlatMapRenderer renderer)
        {
            world=data;viewCamera=target;maps=renderer;projection=new PlanetLocalProjection(new PlanetLocation(0,0));
            var shader=Resources.Load<Shader>("ORBIS/FlatWorld/LocalSurface");if(!shader)throw new InvalidOperationException("LocalSurface shader missing");
            surface=new Material(shader);mesh=new Mesh{name="Geodesic local patch",indexFormat=IndexFormat.UInt32};
            lattice=new Vector2[(Rings+1)*(Sectors+1)];var vertices=new Vector3[lattice.Length];
            for(int r=0;r<=Rings;r++)for(int s=0;s<=Sectors;s++){int i=r*(Sectors+1)+s;float a=Tau*s/Sectors;lattice[i]=new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r/Rings;vertices[i]=lattice[i];}
            triangles=new int[Rings*Sectors*6];int k=0;
            for(int r=0;r<Rings;r++)for(int s=0;s<Sectors;s++){int a=r*(Sectors+1)+s,b=a+Sectors+1;triangles[k++]=a;triangles[k++]=b;triangles[k++]=a+1;triangles[k++]=a+1;triangles[k++]=b;triangles[k++]=b+1;}
            mesh.vertices=vertices;mesh.uv=lattice;mesh.triangles=triangles;mesh.bounds=new Bounds(Vector3.zero,Vector3.one*20);
            pickPositions=new Vector4[lattice.Length];pickVertexStamps=new int[lattice.Length];pickCellStamps=new int[Rings*Sectors];
            var go=new GameObject("Local spherical terrain",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(transform,false);
            go.GetComponent<MeshFilter>().sharedMesh=mesh;terrain=go.GetComponent<MeshRenderer>();terrain.sharedMaterial=surface;terrain.shadowCastingMode=ShadowCastingMode.On;
            zoom=targetZoom=ZoomFor(HomeAltitude);
            Tick(0,0);SyncMaterials();
        }
        void Frame(){normal=Spin*Reflect(V(projection.Frame.Up));east=Spin*Reflect(V(projection.Frame.East));north=Spin*Reflect(V(projection.Frame.North));}
        public void SetViewport(Rect rect){if(rect.width>8&&rect.height>8)viewCamera.pixelRect=rect;}
        public void Tick(float dt,double day)
        {
            currentDay=day;if(atlasMode){atlas.Tick(dt,day);return;}
            if(revision!=world.TerrainRevision){poles=FlatGlobeGeometry.PolarHeights(world);revision=world.TerrainRevision;}
            bool wasFlat=morph>=1;
            float previousZoom=zoom;
            StepZoom(dt);
            // Morph is linear in altitude, so the apparent scale keeps the same log rate
            // through the fold as on the chart and the globe: no ease-out stall at either end.
            float altitude=Altitude(zoom);
            float nextMorph=altitude>=Start-1?0:altitude<=EndGap?1:(Start-1-altitude)/(Start-1-EndGap);
            distance=altitude>=Start-1?1+altitude:Mathf.Lerp(Start,End,nextMorph);
            // Pick against the camera and geometry that were actually displayed last frame.
            if(nextMorph>0&&!locked)Capture(viewCamera.pixelRect.center,day);
            morph=nextMorph;
            var previousSpin=Spin;
            spin=(float)(FlatSolarClock.Phase(locked?lockDay+(day-lockDay)*(1-morph)*(1-morph):day)*Tau);
            // While unfolding/folding, both camera endpoints belong to the same geographic
            // frame. Release this transport at the globe endpoint so idle cameras stay free.
            if(locked)orbitRotation=Spin*Quaternion.Inverse(previousSpin)*orbitRotation;
            Frame();
            viewCamera.allowHDR=true;viewCamera.nearClipPlane=.001f;viewCamera.farClipPlane=80;viewCamera.fieldOfView=Fov;viewCamera.orthographic=IsFlat;
            OrbisCelestialBackdrop.Apply(viewCamera,morph<.95f);
            if(IsFlat)
            {
                // Keep the geography under the wheel cursor fixed while the chart scales.
                PlanetLocation before=new PlanetLocation();
                bool anchored=zoomAnchor&&wasFlat&&zoom!=previousZoom&&TryGetPlanetLocation(zoomScreen,out before);
                PlaceFlatCamera(altitude);
                if(anchored)
                {
                    var anchor=PlanetCoordinateSystem.SurfaceNormal(before);
                    for(int iteration=0;iteration<6;iteration++)
                    {
                        if(!TryReliefPoint(zoomScreen,out var after))break;
                        var expected=projection.StereographicLog(anchor);
                        double error=Math.Abs(expected.X-after.x*TileScale)+Math.Abs(expected.Y-after.y*TileScale);
                        if(error<TileScale/Mathf.Max(1,pixels)*.005f)break;
                        if(!projection.TryAnchorStereographicNorthUp(anchor,after.x*TileScale,after.y*TileScale))break;
                        Frame();
                        PlaceFlatCamera(altitude);
                    }
                }
                // Any later fold starts from the chart's own top-down pose.
                orbitRotation=Quaternion.LookRotation(-normal,north);
            }
            else
            {
                Quaternion orbit=orbitRotation;float gap=Mathf.Min(altitude,Start-1);
                viewCamera.transform.SetPositionAndRotation(Vector3.Lerp(orbit*new Vector3(0,0,-Mathf.Max(Start,distance)),normal*(1+gap),morph),Quaternion.Slerp(orbit,Quaternion.LookRotation(-normal,north),morph));
                viewCamera.orthographicSize=gap*HalfTan;pixels=viewCamera.pixelHeight*TileScale/(2*viewCamera.orthographicSize);
                if(morph==0){viewCamera.ResetProjectionMatrix();if(targetZoom>=FoldZoom)locked=false;}
                else
                {
                    float h=viewCamera.orthographicSize;var p=Matrix4x4.Perspective(Fov,viewCamera.aspect,viewCamera.nearClipPlane,80);var o=Matrix4x4.Ortho(-h*viewCamera.aspect,h*viewCamera.aspect,-h,h,viewCamera.nearClipPlane,80);
                    var mix=new Matrix4x4();float t=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.62f,1,morph));
                    for(int r=0;r<4;r++)for(int c=0;c<4;c++)mix[r,c]=Mathf.Lerp(p[r,c]/gap,o[r,c],t);viewCamera.projectionMatrix=mix;
                }
            }
            Bind();UpdateRoute();
        }
        void StepZoom(float dt)
        {
            if(dt<=0)return;
            // Exact critically damped step: smooth start between wheel notches, no overshoot from rest, stable for any dt.
            float delta=zoom-targetZoom,temp=(zoomVelocity+ZoomOmega*delta)*dt,decay=Mathf.Exp(-ZoomOmega*dt);
            zoomVelocity=(zoomVelocity-ZoomOmega*temp)*decay;zoom=targetZoom+(delta+temp)*decay;
            if(Mathf.Abs(zoom-targetZoom)<.002f&&Mathf.Abs(zoomVelocity)<.05f){zoom=targetZoom;zoomVelocity=0;}
        }
        void StopZoom(){targetZoom=zoom;zoomVelocity=0;zoomAnchor=false;}
        void PlaceFlatCamera(float altitude)
        {
            viewCamera.orthographicSize=altitude*HalfTan;pixels=viewCamera.pixelHeight*TileScale/(2*viewCamera.orthographicSize);
            viewCamera.transform.SetPositionAndRotation(normal*(1+EndGap),Quaternion.LookRotation(-normal,north));viewCamera.ResetProjectionMatrix();
        }
        void MoveCenter(Vector2 tiles)
        {
            var direction=LocalDirection(tiles*TileScale);
            projection=new PlanetLocalProjection(PlanetCoordinateSystem.FromWorld(direction,new PlanetVector(),1));
            Frame();
        }
        PlanetVector LocalDirection(Vector2 xy)
        {
            if(morph<=0)return projection.Exp(xy.x,xy.y);
            var conformal=projection.StereographicExp(xy.x,xy.y);
            if(morph>=1)return conformal;
            var direction=projection.Exp(xy.x,xy.y)*(1-morph)+conformal*morph;
            return direction*(1/Math.Sqrt(PlanetVector.Dot(direction,direction)));
        }
        Vector2 LocalPoint(PlanetVector direction)
        {
            var geodesic=projection.Log(direction);
            if(morph<=0)return new Vector2((float)geodesic.X,(float)geodesic.Y);
            var conformal=projection.StereographicLog(direction);
            if(morph>=1)return new Vector2((float)conformal.X,(float)conformal.Y);
            double angle=Math.Sqrt(geodesic.X*geodesic.X+geodesic.Y*geodesic.Y);
            if(angle<1e-12)return Vector2.zero;
            // Both endpoint projections are radial. Invert their blend on the same ray.
            double low=angle,high=Math.Min(Math.PI,Math.Sqrt(conformal.X*conformal.X+conformal.Y*conformal.Y));
            for(int iteration=0;iteration<18;iteration++)
            {
                double radius=(low+high)*.5;
                double mapped=PlanetLocalProjection.Distance(projection.Frame.Up,LocalDirection(new Vector2((float)radius,0)),1);
                if(mapped<angle)low=radius;else high=radius;
            }
            double scale=(low+high)*.5/angle;
            return new Vector2((float)(geodesic.X*scale),(float)(geodesic.Y*scale));
        }
        void Bind()
        {
            if(!surface)return;
            surface.SetVector("_LocalCenter",V(projection.Frame.Up));surface.SetVector("_LocalEast",V(projection.Frame.East));surface.SetVector("_LocalNorth",V(projection.Frame.North));
            surface.SetVector("_ScrollNormal",normal);surface.SetVector("_ScrollRight",east);surface.SetVector("_ScrollUp",north);
            surface.SetFloat("_ScrollMorph",morph);surface.SetFloat("_ScrollSpin",spin);surface.SetFloat("_LocalRadius",Radius);
            surface.SetFloat("_PixelsPerTile",pixels);surface.SetFloat("_DetailWeight",DetailWeight);surface.SetVector("_ScrollPoleHeights",poles);surface.SetFloat("_ScrollHeightScale",FlatGlobeGeometry.HeightScale);
            surface.SetVector("_ScrollRelief",ReliefParameters);
            surface.SetVector("_MapSize",new Vector4(world.Width,world.Height,1f/world.Width,1f/world.Height));
        }
        public void SyncMaterials(){if(atlasMode){atlas.SyncMaterials();return;}surface.CopyPropertiesFromMaterial(maps.Material);Bind();}
        void Capture(Vector2 screen,double day)
        {
            if(TryGetPlanetLocation(screen,out var p))projection=new PlanetLocalProjection(p);
            // A geographic chart has true east/right and north/up. Inheriting the
            // orbit camera's roll made polar water and coastlines appear rotated.
            lockDay=day;locked=true;Frame();
        }
        public void Drag(Vector2 delta)
        {
            if(atlasMode){atlas.Drag(delta);return;}
            StopZoom();
            if(IsGlobe)
            {
                // Continue from the returned pose, never reconstruct it from stale Euler angles.
                // Both controls use the returned viewport axes. World-up yaw degenerates
                // into roll near a pole and becomes collinear with pitch after long pans.
                orbitRotation=orbitRotation*Quaternion.Euler(-delta.y*.2f,delta.x*.25f,0);
                Tick(0,currentDay);
            }
            else if(IsFlat&&delta.sqrMagnitude>0&&TryReliefPoint(viewCamera.pixelRect.center-delta,out var point))PanBy(point);
            else PanBy(-delta/Mathf.Max(1,pixels));
        }
        public void PanBy(Vector2 delta)
        {
            if(atlasMode){atlas.PanBy(delta);return;}if(IsGlobe)return;
            StopZoom();
            // Transport both ends of the camera interpolation by the same rigid rotation.
            // Otherwise dragging changes the distance to the tilted patch and looks like zooming.
            var oldBasis=Quaternion.LookRotation(normal,north);
            MoveCenter(delta);
            orbitRotation=Quaternion.LookRotation(normal,north)*Quaternion.Inverse(oldBasis)*orbitRotation;
            Tick(0,currentDay);
        }
        public void Wheel(float wheel,Vector2 screen,double day)
        {
            if(atlasMode){atlas.Wheel(wheel,screen,day);return;}
            wheel=Mathf.Clamp(wheel,-24,24);if(Mathf.Abs(wheel)<.00001f)return;
            // Globe, fold and chart share one coordinate, so a burst of notches flows across
            // the fold without a pause. Reversing direction starts from the displayed zoom.
            float from=((wheel>0&&targetZoom<zoom)||(wheel<0&&targetZoom>zoom))?zoom:targetZoom;
            float desired=Mathf.Clamp(from+wheel,MinZoom,MaxZoom);
            // Unfolding always captures the viewport centre, independent of the wheel cursor.
            if(desired<FoldZoom&&!locked)Capture(viewCamera.pixelRect.center,day);
            zoomAnchor=IsFlat;zoomScreen=screen;targetZoom=desired;
        }
        public void Toggle(double day){if(IsFlat||(IsTransitioning&&targetZoom<FoldZoom))GoGlobe(day);else{Capture(viewCamera.pixelRect.center,day);zoomAnchor=false;targetZoom=0;}}
        public void GoGlobe(double day)
        {
            if(atlasMode){var c=atlas.CenterTile;projection=new PlanetLocalProjection(PlanetCoordinateSystem.FromMapUV(c.x/world.Width,c.y/world.Height));atlasMode=false;atlas.gameObject.SetActive(false);terrain.enabled=true;locked=true;lockDay=day;spin=(float)(FlatSolarClock.Phase(day)*Tau);Frame();morph=1;distance=End;zoom=0;zoomVelocity=0;}
            orbitRotation=Quaternion.LookRotation(-normal,north);zoomAnchor=false;targetZoom=ZoomFor(HomeAltitude);Tick(0,day);
        }
        public void OpenAtlas(double day)
        {
            if(!atlas){var go=new GameObject("Global strategic atlas");go.transform.SetParent(transform,false);atlas=go.AddComponent<MarkleyAtlasNavigator>();atlas.Initialize(world,viewCamera,maps);}
            atlas.gameObject.SetActive(true);atlas.OpenAtlas(day);atlas.Tick(3,day);atlas.OpenAtlas(day);atlas.Tick(3,day);atlasMode=true;terrain.enabled=false;if(routeLine)routeLine.enabled=false;
        }
        public void Home(double day){if(atlasMode){atlas.Home(day);return;}GoGlobe(day);orbitRotation=Quaternion.Euler(18,0,0);}
        public void Focus(int tile,double day)
        {
            if(tile<0||tile>=world.Count)return;if(atlasMode){atlas.Focus(tile,day);return;}
            projection=new PlanetLocalProjection(PlanetCoordinateSystem.FromMapUV((tile%world.Width+.5)/world.Width,(tile/world.Width+.5)/world.Height));lockDay=day;locked=true;Frame();
            zoomAnchor=false;targetZoom=ZoomForPixels(Mathf.Max(48,EntryPixels));
        }
        public bool TryProjectionPoint(Vector2 screen,out Vector2 point)
        {
            if(atlasMode)return atlas.TryProjectionPoint(screen,out point);point=Vector2.zero;var ray=FlatGlobeGeometry.ScreenRay(viewCamera,screen);if(!new Plane(normal,normal).Raycast(ray,out float t))return false;
            var offset=ray.GetPoint(t)-normal;point=new Vector2(Vector3.Dot(offset,east),Vector3.Dot(offset,north))/TileScale;return true;
        }
        public bool TryReliefPoint(Vector2 screen,out Vector2 point)
        {
            if(atlasMode)return atlas.TryReliefPoint(screen,out point);
            if(!TryProjectionPoint(screen,out point))return false;
            pickRelief=ReliefParameters;
            if(!IsFlat||pickRelief.x<=0)return true;
            pickRay=FlatGlobeGeometry.ScreenRay(viewCamera,screen);pickRadius=Radius;pickNearest=float.PositiveInfinity;
            unchecked{pickGeneration++;}
            if(pickGeneration==0){Array.Clear(pickVertexStamps,0,pickVertexStamps.Length);Array.Clear(pickCellStamps,0,pickCellStamps.Length);pickGeneration=1;}
            var projected=point*TileScale;
            var bounds=FlatGlobeGeometry.ReliefBounds(projected,pickRelief);
            var closest=new Vector2(Mathf.Clamp(0,bounds.xMin,bounds.xMax),Mathf.Clamp(0,bounds.yMin,bounds.yMax));
            float minimum=closest.magnitude;
            float maximum=Mathf.Max(Mathf.Max(new Vector2(bounds.xMin,bounds.yMin).magnitude,new Vector2(bounds.xMax,bounds.yMin).magnitude),
                                    Mathf.Max(new Vector2(bounds.xMin,bounds.yMax).magnitude,new Vector2(bounds.xMax,bounds.yMax).magnitude));
            int first=Mathf.Max(0,Mathf.FloorToInt(minimum/pickRadius*Rings)-1);
            int last=Mathf.Min(Rings-1,Mathf.FloorToInt(maximum/pickRadius*Rings)+1);
            for(int ring=first;ring<=last;ring++)
            {
                float inner=Mathf.Max(1e-10f,pickRadius*ring/Rings);
                float outer=pickRadius*(ring+1)/Rings;
                if(bounds.xMin>outer+.000001f||bounds.xMax< -outer-.000001f)continue;
                float c0=bounds.xMin/inner,c1=bounds.xMax/inner,c2=bounds.xMin/outer,c3=bounds.xMax/outer;
                float a=Mathf.Acos(Mathf.Clamp(Mathf.Min(Mathf.Min(c0,c1),Mathf.Min(c2,c3)),-1,1));
                float b=Mathf.Acos(Mathf.Clamp(Mathf.Max(Mathf.Max(c0,c1),Mathf.Max(c2,c3)),-1,1));
                int low=Mathf.FloorToInt(Mathf.Min(a,b)/Tau*Sectors)-1;
                int high=Mathf.CeilToInt(Mathf.Max(a,b)/Tau*Sectors)+1;
                PickRingArc(ring,low,high,bounds);
                PickRingArc(ring,Sectors-high,Sectors-low,bounds);
            }
            if(float.IsInfinity(pickNearest))return false;
            point=pickHit/TileScale;
            return true;
        }
        void PickRingArc(int ring,int first,int last,Rect bounds)
        {
            for(int sector=first;sector<=last;sector++)
            {
                int column=FMath.Wrap(sector,Sectors),cell=ring*Sectors+column;
                if(pickCellStamps[cell]==pickGeneration)continue;
                pickCellStamps[cell]=pickGeneration;
                int a=ring*(Sectors+1)+column,b=a+Sectors+1;
                Vector2 p0=lattice[a]*pickRadius,p1=lattice[a+1]*pickRadius,p2=lattice[b]*pickRadius,p3=lattice[b+1]*pickRadius;
                float left=Mathf.Min(Mathf.Min(p0.x,p1.x),Mathf.Min(p2.x,p3.x));
                float right=Mathf.Max(Mathf.Max(p0.x,p1.x),Mathf.Max(p2.x,p3.x));
                float bottom=Mathf.Min(Mathf.Min(p0.y,p1.y),Mathf.Min(p2.y,p3.y));
                float top=Mathf.Max(Mathf.Max(p0.y,p1.y),Mathf.Max(p2.y,p3.y));
                if(bounds.xMax<left-1e-6f||bounds.xMin>right+1e-6f||bounds.yMin>top+1e-6f||bounds.yMax<bottom-1e-6f)continue;
                PickTriangle(a,b,a+1);
                PickTriangle(a+1,b,b+1);
            }
        }
        Vector4 PickVertex(int index)
        {
            if(pickVertexStamps[index]!=pickGeneration)
            {
                pickVertexStamps[index]=pickGeneration;
                pickPositions[index]=ProjectedVertex(lattice[index]*pickRadius,pickRelief);
            }
            return pickPositions[index];
        }
        void PickTriangle(int a,int b,int c)
        {
            var pa=PickVertex(a);var pb=PickVertex(b);var pc=PickVertex(c);
            if(!FlatGlobeGeometry.IntersectTriangle(pickRay,pa,pb,pc,out var weights,out float distance)||distance>=pickNearest)return;
            weights=FlatGlobeGeometry.PerspectiveWeights(weights,new Vector3(pa.w,pb.w,pc.w));
            pickNearest=distance;
            pickHit=(lattice[a]*weights.x+lattice[b]*weights.y+lattice[c]*weights.z)*pickRadius;
        }
        public bool TryFlatPoint(Vector2 screen,out Vector2 tile)
        {
            if(atlasMode)return atlas.TryFlatPoint(screen,out tile);tile=Vector2.zero;if(!TryGetPlanetLocation(screen,out var p))return false;tile=new Vector2((float)PlanetCoordinateSystem.MapU(p)*world.Width,(float)PlanetCoordinateSystem.MapV(p)*world.Height);return true;
        }
        public bool TryGetPlanetLocation(Vector2 screen,out PlanetLocation location)
        {
            if(atlasMode)return atlas.TryGetPlanetLocation(screen,out location);location=new PlanetLocation();var ray=viewCamera.ScreenPointToRay(screen);
            if(IsFlat){if(!TryReliefPoint(screen,out var xy))return false;location=PlanetCoordinateSystem.FromWorld(LocalDirection(xy*TileScale),new PlanetVector(),1);return true;}
            if(IsGlobe)
            {
                // Avoid the inverse near/far projection's float cancellation after a long
                // transition. The globe endpoint is an ordinary perspective camera.
                var rect=viewCamera.pixelRect;var ndc=(screen-rect.center)*2;
                float tangent=Mathf.Tan(Fov*Mathf.Deg2Rad*.5f);
                var direction=viewCamera.transform.TransformDirection(new Vector3(ndc.x/rect.width*viewCamera.aspect*tangent,ndc.y/rect.height*tangent,1)).normalized;
                ray=new Ray(viewCamera.transform.position,direction);
                float b=Vector3.Dot(ray.origin,ray.direction),d=b*b-ray.origin.sqrMagnitude+1;if(d<0)return false;
                float t=-b-Mathf.Sqrt(d);if(t<0)return false;var p=Reflect(Quaternion.Inverse(Spin)*ray.GetPoint(t)).normalized;
                location=PlanetCoordinateSystem.FromWorld(new PlanetVector(p.x,p.y,p.z),new PlanetVector(),1);return true;
            }
            // Picking follows the deformed vertices throughout the transition.
            float nearest=float.PositiveInfinity;Vector2 hit=Vector2.zero;var vertices=new Vector4[lattice.Length];float radius=Radius;
            var relief=ReliefParameters;
            for(int i=0;i<vertices.Length;i++)vertices[i]=ProjectedVertex(lattice[i]*radius,relief);
            for(int i=0;i<triangles.Length;i+=3)
            {
                int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                if(!FlatGlobeGeometry.IntersectTriangle(ray,vertices[a],vertices[b],vertices[c],out var weights,out float distance)||distance>=nearest)continue;
                weights=FlatGlobeGeometry.PerspectiveWeights(weights,new Vector3(vertices[a].w,vertices[b].w,vertices[c].w));
                nearest=distance;hit=(lattice[a]*weights.x+lattice[b]*weights.y+lattice[c]*weights.z)*radius;
            }
            if(float.IsInfinity(nearest))return false;location=PlanetCoordinateSystem.FromWorld(LocalDirection(hit),new PlanetVector(),1);return true;
        }
        Vector3 Position(Vector2 xy)
        {
            return ProjectedVertex(xy,ReliefParameters);
        }
        Vector4 ProjectedVertex(Vector2 xy,Vector4 reliefParameters)
        {
            var p=LocalDirection(xy);
            var location=PlanetCoordinateSystem.FromWorld(p,new PlanetVector(),1);
            var uv=new Vector2((float)PlanetCoordinateSystem.MapU(location),(float)PlanetCoordinateSystem.MapV(location));
            float radius=FlatGlobeGeometry.Radius(FlatGlobeGeometry.SurfaceHeight(world,uv),uv.y,world.Height,poles);
            var relief=FlatGlobeGeometry.ReliefPosition(xy,radius-1,reliefParameters);
            var plane=normal*(1+relief.z)+east*relief.x+north*relief.y;
            Vector3 position=Vector3.Lerp(Spin*Reflect(V(p))*radius,plane,morph);
            return new Vector4(position.x,position.y,position.z,FlatGlobeGeometry.ReliefClipWeight(radius-1,reliefParameters,morph));
        }
        public int Pick(Vector2 screen)=>TryFlatPoint(screen,out var tile)?FlatScrollMath.Tile(tile.x,Math.Min(world.Height-.0001,tile.y),world.Width,world.Height):-1;
        public void SetRoute(int[] ids){route=ids??new int[0];if(atlas)atlas.SetRoute(route);UpdateRoute();}
        void UpdateRoute()
        {
            if(route.Length<2){if(routeLine)routeLine.enabled=false;return;}
            if(!routeLine){var go=new GameObject("Spherical route");go.transform.SetParent(transform,false);routeLine=go.AddComponent<LineRenderer>();routeLine.sharedMaterial=new Material(Shader.Find("Sprites/Default"));routeLine.startColor=routeLine.endColor=new Color(1,.85f,.45f);}
            routeLine.enabled=!atlasMode;routeLine.widthMultiplier=.0015f;routeLine.positionCount=route.Length;
            var relief=ReliefParameters;
            for(int i=0;i<route.Length;i++){var p=PlanetCoordinateSystem.SurfaceNormal(PlanetCoordinateSystem.FromMapUV((route[i]%world.Width+.5)/world.Width,(route[i]/world.Width+.5)/world.Height));routeLine.SetPosition(i,(Vector3)ProjectedVertex(LocalPoint(p),relief)+normal*.0002f);}
        }
        void OnDestroy(){if(mesh)Destroy(mesh);if(surface)Destroy(surface);if(routeLine&&routeLine.sharedMaterial)Destroy(routeLine.sharedMaterial);}
    }
}
