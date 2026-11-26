using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Orbis.Planet;
namespace Orbis.Flat
{
    // Sphere data is authoritative. The local chart and strategic atlas are presentation only.
    public sealed partial class FlatScrollNavigator : MonoBehaviour
    {
        const float Fov=42,Start=1.6f,End=1.08f,Tau=Mathf.PI*2;
        // One zoom coordinate spans globe, unfold and local chart: altitude=EndGap*exp(zoom*ZoomRate).
        // A single critically damped spring drives it, so no hand-off stalls at the fold endpoints.
        const float EndGap=.22f,ZoomRate=.075f,ZoomOmega=18,MaxAltitude=5,HomeAltitude=2.1f;
        Camera viewCamera;WorldData world;FlatMapRenderer maps;PlanetLocalProjection projection;
        Mesh mesh,routeMesh;Material surface,routeMaterial;MeshRenderer terrain,routeRenderer;MarkleyAtlasNavigator atlas;bool atlasMode;
        readonly List<Vector3> routeVertices=new List<Vector3>();
        readonly List<int> routeIndices=new List<int>();
        float distance=3.1f,morph,pixels=16,spin,zoom,targetZoom,zoomVelocity;
        Vector2 zoomScreen;bool zoomAnchor;Quaternion orbitRotation=Quaternion.Euler(18,0,0);
        PlanetVector zoomGroundAnchor;
        double currentDay,lockDay;bool locked;Vector3 normal,east,north;Vector2 poles;int revision=-1;
        PlanetLocation[] route=Array.Empty<PlanetLocation>();bool seaRoute;
        public IReadOnlyList<PlanetLocation> RoutePoints => route;
        const int Rings=128,Sectors=256;Vector2[] lattice;int[] triangles;
        float foldHeightWeight;
        readonly FlatPlantHeightCache plantHeights=new FlatPlantHeightCache();
#if UNITY_EDITOR
        readonly FlatShaderPreview editorPreview = new FlatShaderPreview();
#endif
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
        internal float MinimapMorph=>atlasMode?atlas.Morph:morph;
        internal Vector2 MinimapOrigin=>atlasMode?atlas.ChartCenter:CenterTile;
        public float Distance=>distance;
        public float PixelsPerTile=>atlasMode?atlas.PixelsPerTile:pixels;
        public float ReturnPixelsPerTile=>EntryPixels;
        public float DetailWeight=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(12,32,PixelsPerTile))*Morph;
        public float MapHeight=>atlasMode?atlas.MapHeight:world.Height;
        public Vector2 ProjectionSize=>new Vector2(world.Width,MapHeight);
        public Vector2 CenterTile {get{var c=projection.Center;return atlasMode?atlas.CenterTile:new Vector2((float)PlanetCoordinateSystem.MapU(c)*world.Width,(float)PlanetCoordinateSystem.MapV(c)*world.Height);}}
        public Material SurfaceMaterial=>atlasMode?atlas.SurfaceMaterial:surface;
        public bool UsingShaderPreview
        {
            get
            {
#if UNITY_EDITOR
                return atlasMode ? (atlas && atlas.UsingShaderPreview) : editorPreview.IsActive;
#else
                return false;
#endif
            }
        }
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
        float Radius
        {
            get
            {
                float offAxis=IsFlat||morph<=0||!viewCamera?0:Vector3.Angle(viewCamera.transform.rotation*Vector3.back,normal)*Mathf.Deg2Rad;
                return (float)PlanetScrollNavigationMath.ComputeLocalRadius(
                    viewCamera.orthographicSize,viewCamera.aspect,morph,
                    FlatGlobeGeometry.LocalHeightSpan,FlatGlobeGeometry.ReliefTilt,FlatGlobeGeometry.ReliefCameraHeight,offAxis);
            }
        }
        Vector4 ReliefParameters
        {
            get
            {
                var center=projection.Center;
                var uv=new Vector2((float)PlanetCoordinateSystem.MapU(center),(float)PlanetCoordinateSystem.MapV(center));
                float height=FlatGlobeGeometry.Radius(FlatGlobeGeometry.SurfaceHeight(world,uv),uv.y,world.Height,poles)-1;
                return FlatGlobeGeometry.ReliefParameters(pixels,morph,height);
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
        public void SetViewport(Rect rect)
        {
            if(!FMath.Finite(rect.x)||!FMath.Finite(rect.y)||!FMath.Finite(rect.width)||!FMath.Finite(rect.height)||rect.width<=8||rect.height<=8)return;
            viewCamera.pixelRect=rect;
            if(atlasMode&&atlas)atlas.SetViewport(rect);
            else if(surface)Bind();
        }
        public void Tick(float dt,double day)
        {
            currentDay=day;
            if(revision!=world.TerrainRevision){poles=FlatGlobeGeometry.PolarHeights(world);revision=world.TerrainRevision;}
            if(atlasMode){TickStrategic(dt,day);return;}
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
                bool atlasHeading=atlasAligning||atlasExitPending;
                bool anchored=zoomAnchor&&wasFlat&&zoom!=previousZoom;
                if(!atlasHeading&&!zoomAnchor&&dt>0&&projection.AlignNorth(dt*Math.PI*.5))Frame();
                PlaceFlatCamera(altitude);
                if(anchored)AnchorLocalPoint(zoomGroundAnchor,zoomScreen,altitude,atlasHeading);
                if(TickAtlasDetail(dt,day,altitude))return;
                // Any later fold starts from the chart's own top-down pose.
                orbitRotation=Quaternion.LookRotation(-normal,north);
            }
            else
            {
                Quaternion orbit=orbitRotation;float gap=Mathf.Min(altitude,Start-1);
                Quaternion pose=Quaternion.Slerp(orbit,Quaternion.LookRotation(-normal,north),morph);
                float cameraDistance=(float)PlanetScrollNavigationMath.CameraDistanceForUnfold(distance,altitude,morph);
                viewCamera.transform.SetPositionAndRotation(pose*new Vector3(0,0,-cameraDistance),pose);
                viewCamera.orthographicSize=gap*HalfTan;
                // A distant perspective globe covers fewer pixels per ground tile than the fold endpoint.
                pixels=viewCamera.pixelHeight*TileScale/(2*altitude*HalfTan);
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
            double currentZoom=zoom,currentVelocity=zoomVelocity;
            PlanetScrollNavigationMath.StepZoomSpring(ref currentZoom,ref currentVelocity,targetZoom,MinZoom,MaxZoom,dt);
            zoom=(float)currentZoom;zoomVelocity=(float)currentVelocity;
        }
        void StopZoom(){targetZoom=zoom;zoomVelocity=0;zoomAnchor=false;atlasExitPending=false;}
        void SetLocalZoomAnchor(Vector2 screen,PlanetVector ground)
        {zoomScreen=screen;zoomGroundAnchor=ground;zoomAnchor=true;}
        void PlaceFlatCamera(float altitude)
        {
            viewCamera.orthographicSize=altitude*HalfTan;pixels=viewCamera.pixelHeight*TileScale/(2*viewCamera.orthographicSize);
            viewCamera.transform.SetPositionAndRotation(normal*(1+EndGap),Quaternion.LookRotation(-normal,north));viewCamera.ResetProjectionMatrix();
        }
        void MoveCenter(Vector2 tiles)
        {
            projection.MoveChart(tiles.x*TileScale,tiles.y*TileScale,morph);
            Frame();
        }
        // Invert the visible, displaced triangles under the cursor after each scale/frame change.
        // A camera-centred relief reference can move the hit slightly when the centre moves.
        void AnchorLocalPoint(PlanetVector anchor,Vector2 screen,float altitude,bool preserveHeading=false)
        {
            for(int iteration=0;iteration<8;iteration++)
            {
                if(!TryReliefPoint(screen,out var hit))return;
                var desired=hit*TileScale;
                var actual=projection.StereographicLog(anchor);
                double error=Math.Abs(actual.X-desired.x)+Math.Abs(actual.Y-desired.y);
                if(error<TileScale/Mathf.Max(1,pixels)*.005)return;
                projection.AnchorStereographic(anchor,desired.x,desired.y,preserveHeading);
                Frame();PlaceFlatCamera(altitude);
            }
        }
        internal PlanetVector LocalDirection(Vector2 xy)
        {
            return projection.LocalExp(xy.x,xy.y,morph);
        }
        Vector2 LocalPoint(PlanetVector direction)
        {
            var point=projection.LocalLog(direction,morph);
            return new Vector2((float)point.X,(float)point.Y);
        }
        void Bind()
        {
            if(!surface)return;
            float radius=Radius;
            // Filter when a moving ring spans several source cells. The stationary
            // globe keeps its silhouette and the final plane uses the full relief.
            float cellsPerRing=radius/(Rings*TileScale);
            foldHeightWeight=maps.FoldHeights==null || !IsTransitioning ? 0 :
                Mathf.SmoothStep(0,1,Mathf.InverseLerp(.5f,2,cellsPerRing))*Mathf.SmoothStep(0,1,morph/.06f);
            var relief=ReliefParameters;
            var mapSize=new Vector4(world.Width,world.Height,1f/world.Width,1f/world.Height);
            float riverNarrowing=FlatRiverCurves.GlobeWidthReduction(world.Preset,morph);
            var area=maps.LocalEditFootprint;
            var footprint=new Vector4(area.x,area.y,area.width,area.height);
            var focus=ComputeSelectionFocus(area,relief);
            ApplyProjectionProperties(surface,radius,relief,mapSize,riverNarrowing,footprint,focus);
            plantHeights.Bind(surface,maps.VisualTexture,Sectors,Rings);
#if UNITY_EDITOR
            if(terrain&&terrain.sharedMaterial&&terrain.sharedMaterial!=surface)
                ApplyProjectionProperties(terrain.sharedMaterial,radius,relief,mapSize,riverNarrowing,footprint,focus);
#endif
        }
        void ApplyProjectionProperties(Material target,float radius,Vector4 relief,Vector4 mapSize,float riverNarrowing,Vector4 footprint,Vector4 focus)
        {
            target.SetVector("_LocalCenter",V(projection.Frame.Up));target.SetVector("_LocalEast",V(projection.Frame.East));target.SetVector("_LocalNorth",V(projection.Frame.North));
            target.SetVector("_ScrollNormal",normal);target.SetVector("_ScrollRight",east);target.SetVector("_ScrollUp",north);
            target.SetFloat("_ScrollMorph",morph);target.SetFloat("_ScrollSpin",spin);target.SetFloat("_LocalRadius",radius);
            target.SetFloat("_FoldHeightWeight",foldHeightWeight);
            if(foldHeightWeight>0)target.SetTexture("_FoldHeightTex",maps.FoldHeights.Texture);
            target.SetFloat("_RiverGlobeNarrowing",riverNarrowing);
            target.SetFloat("_PixelsPerTile",pixels);target.SetFloat("_DetailWeight",DetailWeight);target.SetVector("_ScrollPoleHeights",poles);target.SetFloat("_ScrollHeightScale",FlatGlobeGeometry.HeightScale);
            target.SetVector("_ScrollRelief",relief);
            target.SetVector("_MapSize",mapSize);
            target.SetFloat("_LocalPhysicalArtwork",1);
            target.SetVector("_LocalEditFootprint",footprint);
            target.SetVector("_LocalFocus",focus);
        }
        public void SyncMaterials()
        {
            if(atlasMode){atlas.SyncMaterials();return;}
            surface.CopyPropertiesFromMaterial(maps.Material);Bind();
#if UNITY_EDITOR
            var draw = editorPreview.ForDraw(surface, surface.FindPass("SCROLL_SURFACE"), surface.FindPass("SCROLL_DEPTH"));
            if (terrain.sharedMaterial != draw) terrain.sharedMaterial = draw;
#endif
        }
        void Capture(Vector2 screen,double day)
        {
            if(TryGetPlanetLocation(screen,out var p))
            {
                projection=new PlanetLocalProjection(p);
                if(projection.NorthAlignmentWeight<1)
                {
                    var right=Reflect(Quaternion.Inverse(Spin)*viewCamera.transform.right);
                    projection.AlignEast(new PlanetVector(right.x,right.y,right.z));
                    projection.AlignNorth(Math.PI);
                }
            }
            lockDay=day;locked=true;Frame();
        }
        public void Drag(Vector2 delta)
        {
            if(atlasMode){atlasEnterPending=false;atlas.Drag(delta);return;}
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
            if(atlasMode){atlasEnterPending=false;atlas.PanBy(delta);return;}if(IsGlobe)return;
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
            if(RouteAtlasWheel(wheel,screen,day))return;
            wheel=Mathf.Clamp(wheel,-24,24);if(Mathf.Abs(wheel)<.00001f)return;
            // Globe, fold and chart share one coordinate, so a burst of notches flows across
            // the fold without a pause. Reversing direction starts from the displayed zoom
            // and clears opposing spring velocity so fast wheel gestures never overshoot.
            double velocity=zoomVelocity;
            float desired=(float)PlanetScrollNavigationMath.ApplyWheelTarget(zoom,targetZoom,ref velocity,wheel,MinZoom,MaxZoom);
            zoomVelocity=(float)velocity;
            // Unfolding always captures the viewport centre, independent of the wheel cursor.
            if(desired<FoldZoom&&!locked)Capture(viewCamera.pixelRect.center,day);
            if(!IsFlat)zoomAnchor=false;
            else if(!zoomAnchor||screen!=zoomScreen||zoom==targetZoom)
            {
                zoomAnchor=TryGetPlanetLocation(screen,out var location);
                if(zoomAnchor)SetLocalZoomAnchor(screen,PlanetCoordinateSystem.SurfaceNormal(location));
            }
            targetZoom=desired;
        }
        public void Toggle(double day){if(IsFlat||(IsTransitioning&&targetZoom<FoldZoom))GoGlobe(day);else{Capture(viewCamera.pixelRect.center,day);zoomAnchor=false;targetZoom=0;}}
        public void GoGlobe(double day)
        {
            detailFromAtlas=false;atlasAligning=false;atlasExitPending=false;atlasEnterPending=false;
            if(atlasMode)
            {
                var center=atlas.CenterTile;
                SetStrategicSurface(false);
                projection=new PlanetLocalProjection(PlanetCoordinateSystem.FromMapUV(center.x/world.Width,center.y/world.Height));
                locked=false;lockDay=day;spin=(float)(FlatSolarClock.Phase(day)*Tau);Frame();
                zoom=ZoomFor(HomeAltitude);zoomVelocity=0;
            }
            orbitRotation=Quaternion.LookRotation(-normal,north);zoomAnchor=false;targetZoom=ZoomFor(HomeAltitude);Tick(0,day);
        }
        public void OpenAtlas(double day)
        {
            OpenStrategic(day);
        }
        public void Home(double day){GoGlobe(day);orbitRotation=Quaternion.Euler(18,0,0);Tick(0,day);}
        public void Recenter(PlanetLocation location,double day)
        {
            if(atlasMode){atlasEnterPending=false;atlas.Recenter(location,day);return;}
            StopZoom();
            projection=new PlanetLocalProjection(location);
            lockDay=day;locked=!IsGlobe;
            spin=(float)(FlatSolarClock.Phase(day)*Tau);
            Frame();
            orbitRotation=Quaternion.LookRotation(-normal,north);
            Tick(0,day);
        }
        internal FlatMinimapProjection MinimapProjection()
        {
            if(atlasMode)return atlas.MinimapProjection();
            return new FlatMinimapProjection(viewCamera,world,this,Vector2.zero,spin,morph,
                normal,east,north,poles,ReliefParameters,Radius);
        }
        public void Focus(int tile,double day)
        {
            if(tile>=0&&tile<world.Count)Focus(TileLocation(tile),day);
        }
        public void Focus(PlanetLocation location,double day)
        {
            if(atlasMode){FocusStrategic(location);return;}
            atlasAligning=false;atlasExitPending=false;
            projection=new PlanetLocalProjection(location);lockDay=day;locked=true;Frame();
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
            if(!IsFlat)return true;
            if(pickRelief.x<=0)return FlatGlobeGeometry.TryLatticeTriangle(point*TileScale,Radius,lattice,Rings,Sectors,out _,out _);
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
            float nearest=float.PositiveInfinity;Vector2 hit=Vector2.zero;float radius=Radius;
            var relief=ReliefParameters;
            // The next flat pick advances its generation before reusing this storage.
            for(int i=0;i<pickPositions.Length;i++)pickPositions[i]=ProjectedVertex(lattice[i]*radius,relief);
            for(int i=0;i<triangles.Length;i+=3)
            {
                int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                if(!FlatGlobeGeometry.IntersectTriangle(ray,pickPositions[a],pickPositions[b],pickPositions[c],out var weights,out float distance)||distance>=nearest)continue;
                weights=FlatGlobeGeometry.PerspectiveWeights(weights,new Vector3(pickPositions[a].w,pickPositions[b].w,pickPositions[c].w));
                nearest=distance;hit=(lattice[a]*weights.x+lattice[b]*weights.y+lattice[c]*weights.z)*radius;
            }
            if(float.IsInfinity(nearest))return false;location=PlanetCoordinateSystem.FromWorld(LocalDirection(hit),new PlanetVector(),1);return true;
        }
        Vector3 Position(Vector2 xy)
        {
            return ProjectedVertex(xy,ReliefParameters);
        }
        internal float FoldSphereRadius(Vector2 uv,float rawRadius)
        {
            if(foldHeightWeight<=0)return rawRadius;
            float filtered=FlatGlobeGeometry.Radius(maps.FoldHeights.Sample(uv),uv.y,FlatFoldHeights.Columns/2,poles);
            return Mathf.Lerp(rawRadius,filtered,foldHeightWeight);
        }
        Vector4 ProjectedVertex(Vector2 xy,Vector4 reliefParameters)
        {
            var p=LocalDirection(xy);
            var location=PlanetCoordinateSystem.FromWorld(p,new PlanetVector(),1);
            var uv=new Vector2((float)PlanetCoordinateSystem.MapU(location),(float)PlanetCoordinateSystem.MapV(location));
            float radius=foldHeightWeight<1||reliefParameters.w>0
                ?FlatGlobeGeometry.Radius(FlatGlobeGeometry.SurfaceHeight(world,uv),uv.y,world.Height,poles):1;
            var relief=FlatGlobeGeometry.ReliefPosition(xy,radius-1,reliefParameters);
            var plane=normal*(1+relief.z)+east*relief.x+north*relief.y;
            Vector3 position=Vector3.Lerp(Spin*Reflect(V(p))*FoldSphereRadius(uv,radius),plane,morph);
            return new Vector4(position.x,position.y,position.z,FlatGlobeGeometry.ReliefClipWeight(radius-1,reliefParameters,morph));
        }
        public int Pick(Vector2 screen)=>TryFlatPoint(screen,out var tile)?FlatScrollMath.Tile(tile.x,Math.Min(world.Height-.0001,tile.y),world.Width,world.Height):-1;
        Vector4 ComputeSelectionFocus(RectInt area,Vector4 relief)
        {
            var focus=Vector4.zero;int tile=maps.LocalFocusTile;
            if(morph>=1&&tile>=0&&tile<world.Count)
            {
                var picked = area.width == 0 ? maps.SelectedLocation : null;
                var location = picked ?? TileLocation(tile);
                var point=LocalPoint(PlanetCoordinateSystem.SurfaceNormal(location));
                if(TryRenderedSurface(point,relief,false,out var position,out _,out _))
                    focus=new Vector4(Vector3.Dot(position-normal,east),Vector3.Dot(position-normal,north),
                        pixels/TileScale,picked.HasValue ? 7 : Mathf.Clamp(pixels*.5f,5,22));
            }
            return focus;
        }
        bool TryRenderedSurface(Vector2 point,Vector4 relief,bool needAxes,out Vector3 position,out Vector3 axisX,out Vector3 axisY)
        {
            position=axisX=axisY=default;
            float radius=Radius;
            if(!FlatGlobeGeometry.TryLatticeTriangle(point,radius,lattice,Rings,Sectors,out var face,out var barycentric))return false;
            if(IsFlat&&relief.x<=0)
            {
                position=normal+east*point.x+north*point.y;axisX=east;axisY=north;return true;
            }
            var a=lattice[face.x]*radius;var b=lattice[face.y]*radius;var c=lattice[face.z]*radius;
            var pa=ProjectedVertex(a,relief);var pb=ProjectedVertex(b,relief);var pc=ProjectedVertex(c,relief);
            // Undo the shader's clip-W weighting to forward-project a geographic point.
            var weights=Vector3.Scale(barycentric,new Vector3(pa.w,pb.w,pc.w));
            float sum=weights.x+weights.y+weights.z;
            position=((Vector3)pa*weights.x+(Vector3)pb*weights.y+(Vector3)pc*weights.z)/sum;
            if(needAxes)
            {
                // Differentiate the rational interpolation inside this rendered triangle.
                var ta=((Vector3)pa-position)*pa.w;
                var ab=((Vector3)pb-position)*pb.w-ta;var ac=((Vector3)pc-position)*pc.w-ta;
                var db=b-a;var dc=c-a;float divisor=(db.x*dc.y-db.y*dc.x)*sum;
                axisX=(ab*dc.y-ac*db.y)/divisor;axisY=(ac*db.x-ab*dc.x)/divisor;
            }
            return true;
        }
        public bool TryActorSurface(PlanetLocation location,out Vector3 position,out Vector2 geographicEast,out Vector2 geographicNorth,out Vector4 cellAxes)
        {
            if(atlasMode)return atlas.TryActorSurface(location,out position,out geographicEast,out geographicNorth,out cellAxes);
            position=Vector3.zero;geographicEast=Vector2.right;geographicNorth=Vector2.up;
            cellAxes=new Vector4(1f/32,0,0,1f/32);
            if(!IsFlat)return false;
            var direction=PlanetCoordinateSystem.SurfaceNormal(location);
            var point=LocalPoint(direction);
            if(!TryRenderedSurface(point,ReliefParameters,true,out position,out var axisX,out var axisY))return false;
            position+=normal*.00015f;
            var geographic=PlanetCoordinateSystem.LocalFrame(location,new PlanetVector(),1);
            // Use actual geographic tangents, including a transported or rotated camera at a pole.
            const double step=.0005;
            var ex=(LocalPoint(direction+geographic.East*step)-LocalPoint(direction-geographic.East*step))
                *(float)(TileScale*Math.Cos(location.Latitude)/(2*step));
            var ny=(LocalPoint(direction+geographic.North*step)-LocalPoint(direction-geographic.North*step))
                *(float)(Math.PI/world.Height/(2*step));
            var dx=axisX*ex.x+axisY*ex.y;var dy=axisX*ny.x+axisY*ny.y;
            return FlatGlobeGeometry.TryActorFrame(viewCamera,dx,dy,TileScale/32,out geographicEast,out geographicNorth,out cellAxes);
        }
        internal static PlanetLocation[] TileLocations(WorldData data,int[] ids)
        {
            if(ids==null||ids.Length==0)return Array.Empty<PlanetLocation>();
            var locations=new PlanetLocation[ids.Length];
            for(int i=0;i<ids.Length;i++)locations[i]=PlanetCoordinateSystem.FromMapUV(
                (ids[i]%data.Width+.5)/data.Width,(ids[i]/data.Width+.5)/data.Height);
            return locations;
        }
        public void SetRoute(int[] ids)=>SetGeographicRoute(TileLocations(world,ids),false);
        internal void SetGeographicRoute(PlanetLocation[] points,bool marine)
        {
            route=points;seaRoute=marine;
            if(atlas)atlas.SetGeographicRoute(points,marine);
            UpdateRoute();
        }
        void UpdateRoute()
        {
            if(route.Length<2){if(routeRenderer)routeRenderer.enabled=false;return;}
            if(!routeRenderer)
            {
                var go=new GameObject("Spherical route",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(transform,false);
                routeMesh=new Mesh{name="Clipped local route",indexFormat=IndexFormat.UInt32};routeMesh.MarkDynamic();
                routeMaterial=new Material(Shader.Find("Sprites/Default")){color=new Color(1,.85f,.45f)};
                go.GetComponent<MeshFilter>().sharedMesh=routeMesh;routeRenderer=go.GetComponent<MeshRenderer>();routeRenderer.sharedMaterial=routeMaterial;
                routeRenderer.shadowCastingMode=ShadowCastingMode.Off;
            }
            routeRenderer.enabled=!atlasMode;routeVertices.Clear();routeIndices.Clear();
            var relief=ReliefParameters;
            float halfWidth=seaRoute?1.05f*TileScale/Mathf.Max(.25f,pixels):.00075f;
            Vector3 previousPosition=Vector3.zero;bool connected=false;
            for(int i=0;i<route.Length;i++)
            {
                var p=PlanetCoordinateSystem.SurfaceNormal(route[i]);
                bool valid=TryRenderedSurface(LocalPoint(p),relief,false,out var position,out _,out _);
                if(valid)
                {
                    position+=normal*.0002f;
                    if(connected)
                    {
                        var side=Vector3.Cross(position-previousPosition,viewCamera.transform.forward).normalized*halfWidth;
                        int start=routeVertices.Count;
                        routeVertices.Add(previousPosition-side);routeVertices.Add(previousPosition+side);routeVertices.Add(position-side);routeVertices.Add(position+side);
                        routeIndices.Add(start);routeIndices.Add(start+1);routeIndices.Add(start+2);routeIndices.Add(start+2);routeIndices.Add(start+1);routeIndices.Add(start+3);
                    }
                    previousPosition=position;
                }
                // An off-patch or unsolved endpoint ends only its adjacent segments.
                connected=valid;
            }
            routeMesh.Clear();routeMesh.SetVertices(routeVertices);routeMesh.SetTriangles(routeIndices,0);routeMesh.RecalculateBounds();
        }
        void OnDestroy()
        {
#if UNITY_EDITOR
            editorPreview.Dispose();
#endif
            plantHeights.Dispose();if(mesh)Destroy(mesh);if(surface)Destroy(surface);if(routeMesh)Destroy(routeMesh);if(routeMaterial)Destroy(routeMaterial);
        }
    }
}
