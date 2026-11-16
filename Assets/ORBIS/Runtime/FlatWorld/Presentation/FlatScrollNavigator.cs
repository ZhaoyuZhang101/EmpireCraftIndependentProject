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
        float Radius=>Mathf.Lerp(Mathf.PI-.0001f,Mathf.Min(1.4f,Mathf.Max(.04f,viewCamera.orthographicSize*Mathf.Sqrt(1+viewCamera.aspect*viewCamera.aspect)*1.3f)),morph);
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
            bool wasFlat=morph>=1;StepZoom(dt);
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
                Vector2 before=Vector2.zero;bool anchored=zoomAnchor&&wasFlat&&TryProjectionPoint(zoomScreen,out before);
                PlaceFlatCamera(altitude);
                if(anchored&&TryProjectionPoint(zoomScreen,out var after)&&(before-after).sqrMagnitude>1e-10f){MoveCenter(before-after);PlaceFlatCamera(altitude);}
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
            projection.Move(tiles.x*TileScale,tiles.y*TileScale);
            // Parallel transport is useful for the move itself, but its bearing
            // drifts relative to geographic north over a long polar traverse.
            projection=new PlanetLocalProjection(projection.Center);Frame();
        }
        void Bind()
        {
            if(!surface)return;
            surface.SetVector("_LocalCenter",V(projection.Frame.Up));surface.SetVector("_LocalEast",V(projection.Frame.East));surface.SetVector("_LocalNorth",V(projection.Frame.North));
            surface.SetVector("_ScrollNormal",normal);surface.SetVector("_ScrollRight",east);surface.SetVector("_ScrollUp",north);
            surface.SetFloat("_ScrollMorph",morph);surface.SetFloat("_ScrollSpin",spin);surface.SetFloat("_LocalRadius",Radius);
            surface.SetFloat("_PixelsPerTile",pixels);surface.SetFloat("_DetailWeight",DetailWeight);surface.SetVector("_ScrollPoleHeights",poles);surface.SetFloat("_ScrollHeightScale",FlatGlobeGeometry.HeightScale);
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
            if(atlasMode)return atlas.TryProjectionPoint(screen,out point);point=Vector2.zero;var ray=viewCamera.ScreenPointToRay(screen);if(!new Plane(normal,normal).Raycast(ray,out float t))return false;
            var offset=ray.GetPoint(t)-normal;point=new Vector2(Vector3.Dot(offset,east),Vector3.Dot(offset,north))/TileScale;return true;
        }
        public bool TryFlatPoint(Vector2 screen,out Vector2 tile)
        {
            if(atlasMode)return atlas.TryFlatPoint(screen,out tile);tile=Vector2.zero;if(!TryGetPlanetLocation(screen,out var p))return false;tile=new Vector2((float)PlanetCoordinateSystem.MapU(p)*world.Width,(float)PlanetCoordinateSystem.MapV(p)*world.Height);return true;
        }
        public bool TryGetPlanetLocation(Vector2 screen,out PlanetLocation location)
        {
            if(atlasMode)return atlas.TryGetPlanetLocation(screen,out location);location=new PlanetLocation();var ray=viewCamera.ScreenPointToRay(screen);
            if(IsFlat){if(!TryProjectionPoint(screen,out var xy))return false;location=PlanetCoordinateSystem.FromWorld(projection.Exp(xy.x*TileScale,xy.y*TileScale),new PlanetVector(),1);return true;}
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
            float nearest=float.PositiveInfinity;Vector2 hit=Vector2.zero;var vertices=new Vector3[lattice.Length];float radius=Radius;
            for(int i=0;i<vertices.Length;i++)vertices[i]=Position(lattice[i]*radius);
            for(int i=0;i<triangles.Length;i+=3)
            {
                int a=triangles[i],b=triangles[i+1],c=triangles[i+2];var ab=vertices[b]-vertices[a];var ac=vertices[c]-vertices[a];var h=Vector3.Cross(ray.direction,ac);float det=Vector3.Dot(ab,h);if(Mathf.Abs(det)<1e-9f)continue;
                var q=ray.origin-vertices[a];float u=Vector3.Dot(q,h)/det;if(u<0||u>1)continue;var cross=Vector3.Cross(q,ab);float v=Vector3.Dot(ray.direction,cross)/det;if(v<0||u+v>1)continue;
                float t=Vector3.Dot(ac,cross)/det;if(t<0||t>=nearest)continue;nearest=t;hit=(lattice[a]*(1-u-v)+lattice[b]*u+lattice[c]*v)*radius;
            }
            if(float.IsInfinity(nearest))return false;location=PlanetCoordinateSystem.FromWorld(projection.Exp(hit.x,hit.y),new PlanetVector(),1);return true;
        }
        Vector3 Position(Vector2 xy)
        {
            var p=projection.Exp(xy.x,xy.y);var location=PlanetCoordinateSystem.FromWorld(p,new PlanetVector(),1);
            int x=FMath.Wrap((int)(PlanetCoordinateSystem.MapU(location)*world.Width),world.Width),y=Mathf.Clamp((int)(PlanetCoordinateSystem.MapV(location)*world.Height),0,world.Height-1);
            float radius=FlatGlobeGeometry.Radius(Mathf.Max(0,world.Elevation[y*world.Width+x]),(float)PlanetCoordinateSystem.MapV(location),world.Height,poles);
            return Vector3.Lerp(Spin*Reflect(V(p))*radius,normal+east*xy.x+north*xy.y,morph);
        }
        public int Pick(Vector2 screen)=>TryFlatPoint(screen,out var tile)?FlatScrollMath.Tile(tile.x,Math.Min(world.Height-.0001,tile.y),world.Width,world.Height):-1;
        public void SetRoute(int[] ids){route=ids??new int[0];if(atlas)atlas.SetRoute(route);UpdateRoute();}
        void UpdateRoute()
        {
            if(route.Length<2){if(routeLine)routeLine.enabled=false;return;}
            if(!routeLine){var go=new GameObject("Spherical route");go.transform.SetParent(transform,false);routeLine=go.AddComponent<LineRenderer>();routeLine.sharedMaterial=new Material(Shader.Find("Sprites/Default"));routeLine.startColor=routeLine.endColor=new Color(1,.85f,.45f);}
            routeLine.enabled=!atlasMode;routeLine.widthMultiplier=.0015f;routeLine.positionCount=route.Length;
            for(int i=0;i<route.Length;i++){var p=PlanetCoordinateSystem.SurfaceNormal(PlanetCoordinateSystem.FromMapUV((route[i]%world.Width+.5)/world.Width,(route[i]/world.Width+.5)/world.Height));var xy=projection.Log(p);routeLine.SetPosition(i,Position(new Vector2((float)xy.X,(float)xy.Y))+normal*.0002f);}
        }
        void OnDestroy(){if(mesh)Destroy(mesh);if(surface)Destroy(surface);if(routeLine&&routeLine.sharedMaterial)Destroy(routeLine.sharedMaterial);}
    }
}
