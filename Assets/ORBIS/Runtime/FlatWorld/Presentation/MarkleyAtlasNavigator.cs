using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Orbis.Planet;

namespace Orbis.Flat
{
    /// <summary>
    /// A single Markley UV lattice becomes a sphere or a continuous plane in the vertex shader.
    /// Copies are render-only; tile IDs, editing, paths and saves always address WorldData once.
    /// </summary>
    public sealed class MarkleyAtlasNavigator : MonoBehaviour
    {
        const float Tau = Mathf.PI * 2, StartDistance = 1.60f, EndDistance = 1.08f;
        const float Fov = 42, DefaultEndGap = .42f;
        const int CopyRadius = 2; // bounded cover; no duplicated simulation
        Camera viewCamera;
        WorldData world;
        FlatMapRenderer maps;
        Mesh lattice, flatMesh, routeMesh;
        readonly List<MeshFilter> sheetFilters = new List<MeshFilter>();
        bool flatGeometry;
        Material surface, routeMaterial;
        readonly List<MeshRenderer> surfaces = new List<MeshRenderer>();
        readonly List<MeshRenderer> routes = new List<MeshRenderer>();
        readonly List<MaterialPropertyBlock> blocks = new List<MaterialPropertyBlock>();
        Vector2 anchor = new Vector2(.5f, .5f);
        MarkleyMapPosition mapPosition;
        readonly List<Vector2Int> sheetCopies=new List<Vector2Int>();
        Vector2Int primaryCopy;
        Vector2 pan
        {
            get { return new Vector2((float)mapPosition.X,(float)mapPosition.Y); }
            set { mapPosition.Set(value.x,value.y,world.Width,world.Height); }
        }
        public PlanetTangentFrame LocalFrame { get; private set; }
        Vector3 east = Vector3.right, north = Vector3.up, normal = Vector3.back;
        float distance = 3.1f, targetDistance = 3.1f, yaw = 0, pitch = 18;
        float endGap = DefaultEndGap, pixels = 16, entryPixels = 16, entryViewportHeight = 1, morph, displaySpin;
        double lockedDay, currentDay;
        bool landingLocked, atlasPending, exitPending, entryScaleRecorded;
        int focusTile=-1;int pendingFocus=-1; bool focusZoom;Vector2 focusCenter; float targetPixels=-1; Vector2 zoomScreen; bool zoomAnchor;
        Vector2 strategicZoomPoint;
        bool hasStrategicZoomPoint;
        int[] route = new int[0];
        Vector2 lastRoutePan = new Vector2(float.NaN, float.NaN);
        Vector3 lastRouteNormal;
        float lastRoutePixels = -1;
        Vector2 polarHeights; int heightRevision=-1;
        int meshColumns,meshRows,lastRouteRevision=-1;
        readonly List<Vector3> routeVertices = new List<Vector3>();
        readonly List<Color> routeColors = new List<Color>();
        readonly List<int> routeIndices = new List<int>();
        public bool IsFlat { get { return morph >= 1; } }
        public bool IsGlobe { get { return morph <= 0; } }
        public bool IsTransitioning { get { return morph > 0 && morph < 1; } }
        public bool Overview { get { return IsFlat && pixels < 24; } }
        public bool IsDetailed { get { return IsFlat && pixels >= 24; } }
        public float DetailWeight { get { return Mathf.SmoothStep(0,1,Mathf.InverseLerp(12,32,pixels))*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.72f,1,morph)); } }
        public float Morph { get { return morph; } }
        public float DisplaySpin { get { return displaySpin; } }
        public float PixelsPerTile { get { return pixels; } }
        public float ReturnPixelsPerTile { get { return Mathf.Max(.25f,entryPixels*viewCamera.pixelHeight/Mathf.Max(1,entryViewportHeight)); } }
        public float Distance { get { return distance; } }
        public float MapHeight {get{return (float)(world.Width/MarkleyProjection.Aspect);}}
        public Vector2 ProjectionSize {get{return new Vector2(world.Width,MapHeight);}}
        internal Vector2 ChartCenter=>new Vector2(pan.x/world.Width,pan.y/MapHeight);
        internal float RequestedPixels => targetPixels > 0 ? targetPixels : pixels;
        internal bool AtOverviewLimit => pixels <= FitPixels() * 1.001f && RequestedPixels <= FitPixels() * 1.001f;
        internal bool IsZooming => targetPixels > 0;
        internal bool IsFocusing => focusZoom;
        public Vector2 CenterTile {get{return Vector2.Scale(MarkleyProjection.GeoUV(pan.x/world.Width,pan.y/MapHeight),new Vector2(world.Width,world.Height));}}
        public Material SurfaceMaterial { get { return surface; } }
        public int VertexCount { get { return lattice ? lattice.vertexCount : 0; } }
        public int ActiveSheets { get; private set; }
        public string ModeName { get { return IsFlat ? (IsDetailed ? "2D 详细图" : "2D 总览") : IsGlobe ? "星球" : "大气展开 " + Mathf.RoundToInt(morph * 100) + "%"; } }
        Vector4 ReliefParameters
        {
            get
            {
                var uv=new Vector2(pan.x/world.Width,pan.y/MapHeight);
                return new Vector4(FlatGlobeGeometry.ReliefWeight(pixels,morph),TerrainHeight(uv),FlatGlobeGeometry.ReliefTilt,0);
            }
        }

        public void Initialize(WorldData data, Camera target, FlatMapRenderer renderer)
        {
            if (data == null || !target || !renderer) throw new ArgumentNullException();
            world = data; viewCamera = target; maps = renderer;
            surface = NewMaterial("ORBIS/FlatWorld/ScrollSurface");
            // Large enough for smooth silhouette; scalar fields retain the original tile resolution.
            int columns=Mathf.Clamp(world.Width,256,768),rows=Mathf.CeilToInt((float)(columns/MarkleyProjection.Aspect));
            meshColumns=columns;meshRows=rows;
            var vertices = new Vector3[(columns + 1) * (rows + 1)];
            var uv = new Vector2[vertices.Length];
            var indices = new int[columns * rows * 6];
            for (int y = 0; y <= rows; y++) for (int x = 0; x <= columns; x++)
            {
                int i = y * (columns + 1) + x;
                uv[i] = new Vector2((float)x / columns, (float)y / rows);
                vertices[i] = new Vector3(uv[i].x, uv[i].y, 0);
            }
            int k = 0;
            for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
            {
                int a = y * (columns + 1) + x, b = a + columns + 1;
                indices[k++] = a; indices[k++] = b; indices[k++] = a + 1;
                indices[k++] = a + 1; indices[k++] = b; indices[k++] = b + 1;
            }
            lattice = new Mesh { name = "ORBIS shared globe-to-scroll UV lattice", indexFormat = IndexFormat.UInt32 };
            lattice.vertices = vertices; lattice.uv = uv; lattice.triangles = indices;
            // Vertex displacement happens on the GPU; bounds must cover all five sheets and both endpoints.
            lattice.bounds = new Bounds(Vector3.zero, Vector3.one * 80);
            lattice.UploadMeshData(true);
            flatMesh = new Mesh { name = "Exact flat endpoint quad" };
            flatMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one };
            flatMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            flatMesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            flatMesh.bounds = lattice.bounds;
            flatMesh.UploadMeshData(true);
            for(int row=-2;row<=3;row++)for(int col=-2;col<=2;col++)
            {
                sheetCopies.Add(new Vector2Int(col,row));var mr=NewRenderer("Markley terrain sheet "+col+","+row,lattice,surface);surfaces.Add(mr);
                mr.shadowCastingMode=ShadowCastingMode.On;sheetFilters.Add(mr.GetComponent<MeshFilter>());
                var block=new MaterialPropertyBlock();block.SetVector("_MarkleyCopy",new Vector4(col,row,0,0));blocks.Add(block);mr.SetPropertyBlock(block);
            }
            pan=new Vector2(world.Width*.5f,MapHeight*.5f);
            Tick(0, 0); SyncMaterials();
        }
        static Material NewMaterial(string path)
        {
            var shader = Resources.Load<Shader>(path);
            if (!shader || !shader.isSupported) throw new InvalidOperationException("Missing/unsupported shader: " + path + ". See Console; no silent scene replacement.");
            return new Material(shader) { name = path + " runtime" };
        }
        MeshRenderer NewRenderer(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            return r;
        }
        public void SetViewport(Rect screenRect)
        {
            if (screenRect.width > 8 && screenRect.height > 8) viewCamera.pixelRect = screenRect;
        }
        static Vector3 ToVector3(PlanetVector p) { return new Vector3((float)p.X,(float)p.Y,(float)p.Z); }
        static Vector3 Reflect(Vector3 p) { p.z = -p.z; return p; }
        Quaternion Spin(float radians) { return Quaternion.AngleAxis(radians * Mathf.Rad2Deg, Vector3.up); }
        void LockPlane(Vector2 uv,double day)
        {
            pan=Vector2.Scale(MarkleyProjection.Project(PlanetCoordinateSystem.FromMapUV(uv.x,uv.y)),ProjectionSize);
            AlignFrame(day);landingLocked=true;
        }
        void AlignFrame(double day)
        {
            lockedDay=day;double u=pan.x/world.Width,v=pan.y/MapHeight;
            var location=MarkleyProjection.Unproject(u,v);anchor=new Vector2((float)PlanetCoordinateSystem.MapU(location),(float)PlanetCoordinateSystem.MapV(location));
            LocalFrame=PlanetCoordinateSystem.LocalFrame(location,new PlanetVector(),1);
            Vector3 up=ToVector3(LocalFrame.Up),right=ToVector3(MarkleyProjection.Direction(u+1e-5,v)-MarkleyProjection.Direction(u-1e-5,v));
            right=Vector3.ProjectOnPlane(right,up).normalized;if(right.sqrMagnitude<.1f)right=ToVector3(LocalFrame.East);
            Vector3 vertical=Vector3.Cross(up,right).normalized;Quaternion spin=Spin((float)(FlatSolarClock.Phase(day)*Tau));
            normal=spin*Reflect(up);east=spin*Reflect(right);north=spin*Reflect(vertical);
            primaryCopy=new Vector2Int(Mathf.FloorToInt(pan.x/world.Width),Mathf.FloorToInt(pan.y/MapHeight));
        }
        void CaptureLanding(Vector2 screen, double day)
        {
            Vector2 uv;
            if (!TryGlobeUV(screen, out uv))
            {
                if (!TryGlobeUV(viewCamera.pixelRect.center, out uv)) uv = anchor;
            }
            entryScaleRecorded=false;LockPlane(uv, day);
            endGap = viewCamera.pixelHeight * TileScale / (2 * LandingPixels() * Mathf.Tan(Fov * Mathf.Deg2Rad * .5f));
            
        }
        void ReanchorForExit(double day)
        {
            // A rigid change of tangent frame at the EXACT flat endpoint leaves the screen image unchanged.
            targetPixels=-1;pendingFocus=-1;focusZoom=false;
            AlignFrame(day);landingLocked=true;
            pitch = Mathf.Asin(Mathf.Clamp(normal.y, -1, 1)) * Mathf.Rad2Deg;
            yaw = Mathf.Atan2(-normal.x, -normal.z) * Mathf.Rad2Deg;
            endGap = viewCamera.orthographicSize / Mathf.Tan(Fov * Mathf.Deg2Rad * .5f);
            distance = EndDistance; targetDistance = EndDistance;
            atlasPending = false; 
            Tick(0,day); // Rebind viewCamera, terrain and cloud picking frame before the next render.
        }
        public void Tick(float dt, double day)
        {
            currentDay = day;
            if(heightRevision!=world.TerrainRevision){polarHeights=FlatGlobeGeometry.PolarHeights(world);heightRevision=world.TerrainRevision;}
            if(IsFlat && targetPixels>0)
            {
                Vector2 before=strategicZoomPoint;bool hit=zoomAnchor&&(hasStrategicZoomPoint||TryReliefPoint(zoomScreen,out before));
                if(!hit)before=Vector2.zero;
                pixels=(float)FlatScrollMath.ExpApproach(pixels,targetPixels,14,dt);
                if(focusZoom)
                {
                    focusCenter=ProjectedTile(focusTile,pan);
                    float blend=1-Mathf.Exp(-14*dt);
                    mapPosition.Move(PlanetCoordinateSystem.NearestOffset(focusCenter.x-mapPosition.X,world.Width)*blend,
                        (focusCenter.y-mapPosition.Y)*blend,world.Width,world.Height);
                }
                if(Mathf.Abs(pixels-targetPixels)<.001f){pixels=targetPixels;targetPixels=-1;if(focusZoom)pan=focusCenter;focusZoom=false;}
                viewCamera.orthographicSize=viewCamera.pixelHeight*TileScale/(2*Mathf.Max(.1f,pixels));
                viewCamera.ResetProjectionMatrix();
                if(hit)strategicZoomPoint=AnchorSheet(before,zoomScreen);
            }
            distance = (float)FlatScrollMath.ExpApproach(distance, targetDistance, 11, dt);
            if (Mathf.Abs(distance - targetDistance) < .00002f) distance = targetDistance;
            float raw = Mathf.InverseLerp(StartDistance, EndDistance, distance);
            morph = (float)FlatScrollMath.Smooth(raw);
            if (distance <= EndDistance) morph = 1;
            if (distance >= StartDistance) morph = 0;
            if (morph > 0 && !landingLocked) CaptureLanding(viewCamera.pixelRect.center, day);
            // In space the sphere really rotates. During landing we converge to the fixed map frame.
            double spinDay = landingLocked ? lockedDay + (day - lockedDay) * (1 - morph) * (1 - morph) : day;
            displaySpin = (float)(FlatSolarClock.Phase(spinDay) * Tau);
            viewCamera.clearFlags = CameraClearFlags.SolidColor; viewCamera.backgroundColor = IsFlat ? new Color(.045f, .075f, .09f) : Color.black;
            Orbis.OrbisCelestialBackdrop.Apply(viewCamera,morph<.5f);
            viewCamera.allowHDR = true; viewCamera.allowMSAA = false;
            viewCamera.nearClipPlane = .002f; viewCamera.farClipPlane = 80; viewCamera.fieldOfView = Fov;
            Quaternion orbit = Quaternion.Euler(pitch, yaw, 0);
            if (IsFlat)
            {
                pixels=Mathf.Max(pixels,MinimumPixels());
                if (atlasPending)
                {
                    atlasPending = false; 
                    pixels = FitPixels();
                }
                else if (!viewCamera.orthographic)
                {
                    pixels = Mathf.Max(.25f, viewCamera.pixelHeight * TileScale / (2 * endGap * Mathf.Tan(Fov * Mathf.Deg2Rad * .5f)));
                    // Record the actual landing scale; preserve world extent across viewport resizes.
                    if(!entryScaleRecorded){entryPixels=pixels;entryViewportHeight=viewCamera.pixelHeight;entryScaleRecorded=true;}
                }
                NormalizePan();
                viewCamera.orthographic = true; viewCamera.orthographicSize = viewCamera.pixelHeight * TileScale / (2 * Mathf.Max(.1f, pixels));
                viewCamera.transform.SetPositionAndRotation(normal * (1 + endGap), Quaternion.LookRotation(-normal, north));
                viewCamera.ResetProjectionMatrix();
            }
            else
            {
                viewCamera.orthographic = false;
                float gap = Mathf.Lerp(StartDistance - 1, endGap, morph);
                Vector3 planetPosition = orbit * new Vector3(0, 0, -Mathf.Max(StartDistance, distance));
                viewCamera.transform.SetPositionAndRotation(Vector3.Lerp(planetPosition, normal * (1 + gap), morph), Quaternion.Slerp(orbit, Quaternion.LookRotation(-normal, north), morph));
                if (morph <= 0)
                {
                    viewCamera.ResetProjectionMatrix();
                    if (distance > StartDistance && targetDistance >= StartDistance) landingLocked = false;
                    pixels = Mathf.Clamp(viewCamera.pixelHeight * TileScale / (2 * Mathf.Max(.05f, distance - 1) * Mathf.Tan(Fov * Mathf.Deg2Rad * .5f)), .25f, 64);
                }
                else
                {
                    // Normalize perspective homogeneous w at the target plane before interpolation.
                    // Both matrices give identical x/y scale at z=-gap; endpoint 1 is exact orthographic.
                    float half = gap * Mathf.Tan(Fov * Mathf.Deg2Rad * .5f), aspect = viewCamera.aspect;
                    Matrix4x4 p = Matrix4x4.Perspective(Fov, aspect, viewCamera.nearClipPlane, viewCamera.farClipPlane);
                    Matrix4x4 o = Matrix4x4.Ortho(-half * aspect, half * aspect, -half, half, viewCamera.nearClipPlane, viewCamera.farClipPlane);
                    float projectionMix = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.62f, 1, morph));
                    Matrix4x4 blend = new Matrix4x4();
                    for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) blend[r, c] = Mathf.Lerp(p[r, c] / gap, o[r, c], projectionMix);
                    viewCamera.projectionMatrix = blend;
                    pixels = viewCamera.pixelHeight * TileScale / (2 * half);
                }
            }
            if(IsFlat&&pendingFocus>=0)
            {
                focusTile=pendingFocus;focusCenter=ProjectedTile(pendingFocus,pan);focusZoom=true;
                pendingFocus=-1;targetPixels=48;zoomAnchor=false;NormalizePan();
            }
            if(IsFlat&&exitPending&&targetPixels<0)
            {
                exitPending=false;ReanchorForExit(day);targetDistance=3.1f;return;
            }
            CommitProjection();
        }
        float TileScale { get { return Tau / world.Width; } }
        float LandingPixels() { return Mathf.Max(FitPixels(),8); }
        float MinimumPixels(){return Mathf.Max(viewCamera.pixelWidth/(world.Width*3f),viewCamera.pixelHeight/(MapHeight*4f));}
        float FitPixels(){return Mathf.Max(MinimumPixels(),Mathf.Min(viewCamera.pixelWidth/(float)world.Width,viewCamera.pixelHeight/MapHeight)*.94f);}

        // The strategic view is always the flat Markley chart. Its owner controls entry
        // into and return from the local surface, including a shared geographic anchor.
        internal void ShowStrategic(PlanetLocation location, float chartPixels, double day)
        {
            pan = NearestChartPoint(location);
            AlignFrame(day);
            landingLocked = true;
            distance = targetDistance = EndDistance;
            morph = 1;
            viewCamera.orthographic = true;
            pixels = chartPixels > 0 ? Mathf.Max(MinimumPixels(), chartPixels) : FitPixels();
            endGap = DefaultEndGap;
            pendingFocus = -1;
            targetPixels = -1;
            atlasPending = exitPending = focusZoom = zoomAnchor = hasStrategicZoomPoint = false;
            Tick(0, day);
        }

        internal void ZoomStrategic(float requested, Vector2 screen)
        {
            if (!zoomAnchor || screen != zoomScreen || !hasStrategicZoomPoint)
                hasStrategicZoomPoint = TryReliefPoint(screen, out strategicZoomPoint);
            targetPixels = Mathf.Max(FitPixels(), requested);
            zoomScreen = screen;
            zoomAnchor = true;
            focusZoom = false;
        }

        internal void FocusStrategic(int tile, float chartPixels)
        {
            focusTile = tile;
            focusZoom = true;
            zoomAnchor = false;
            targetPixels = Mathf.Max(FitPixels(), chartPixels);
        }

        internal Vector2 NearestChartPoint(PlanetLocation location)
        {
            var uv = MarkleyProjection.Nearest(MarkleyProjection.Project(location), ChartCenter);
            return Vector2.Scale(uv, ProjectionSize);
        }

        // The inverse table is also sampled by the visible shader. Measure the local
        // ground angle per equal-length chart step on the specific copy under the cursor.
        internal void DetailMetric(Vector2 point, out PlanetLocation location, out PlanetVector right, out float magnification)
        {
            const double step = .00015;
            double u = point.x / world.Width, v = point.y / MapHeight;
            var center = MarkleyProjection.Direction(u, v);
            var left = MarkleyProjection.Direction(u - step, v);
            var eastward = MarkleyProjection.Direction(u + step, v);
            var bottom = MarkleyProjection.Direction(u, v - step * MarkleyProjection.Aspect);
            var top = MarkleyProjection.Direction(u, v + step * MarkleyProjection.Aspect);
            location = PlanetCoordinateSystem.FromWorld(center, default, 1);
            right = eastward - left;
            if (PlanetVector.Dot(right, right) < 1e-16) right = eastward - center;
            double angle = PlanetLocalProjection.Distance(left, center, 1) + PlanetLocalProjection.Distance(center, eastward, 1)
                + PlanetLocalProjection.Distance(bottom, center, 1) + PlanetLocalProjection.Distance(center, top, 1);
            magnification = (float)(4 * step * PlanetCoordinateSystem.Tau / Math.Max(1e-12, angle));
        }

        internal Vector2 AnchorSheet(Vector2 point, Vector2 screen)
        {
            for (int i = 0; i < 8; i++)
            {
                if (!TryReliefPoint(screen, out var hit)) break;
                var delta = point - hit;
                if (delta.sqrMagnitude * pixels * pixels < .000009f) break;
                var oldPan = pan;
                mapPosition.Move(delta.x, delta.y, world.Width, world.Height);
                // Move keeps the centre within two chart rows. Rebase the selected
                // sheet by the exact same deck translation, including its half-width shift.
                point += (pan - oldPan) - delta;
            }
            return point;
        }
        void NormalizePan()
        {
            mapPosition.Normalize(world.Width,world.Height);
        }
        Vector2 RenderPan
        {
            get {return pan;}
        }
        void CommitProjection()
        {
            // Input callbacks can run AFTER FlatWorldApp.Update. Rebase the material and
            // culling copies together; never expose new sheet visibility with yesterday's pan.
            if(surface)ApplyProjection(surface);
            UpdateSheets(); UpdateRoute();
        }
        void UpdateSheets()
        {
            bool useQuad=IsFlat&&FlatGlobeGeometry.ReliefWeight(pixels,morph)<=0;
            if (flatGeometry != useQuad)
            {
                flatGeometry = useQuad;
                foreach (var filter in sheetFilters) filter.sharedMesh = flatGeometry ? flatMesh : lattice;
            }
            ActiveSheets = 0;
            float halfX=viewCamera.pixelWidth/Mathf.Max(.1f,pixels)*.5f,halfY=viewCamera.pixelHeight/Mathf.Max(.1f,pixels)*.5f;
            float maximumHeight=FlatGlobeGeometry.MaximumHeight*FlatGlobeGeometry.HeightScale;
            float reliefPadding=FlatGlobeGeometry.ReliefWeight(pixels,morph)*maximumHeight*(FlatGlobeGeometry.ReliefTilt/TileScale+Mathf.Max(halfX,halfY)/FlatGlobeGeometry.ReliefCameraHeight);
            for(int i=0;i<surfaces.Count;i++)
            {
                var copy=sheetCopies[i];float left=copy.x*world.Width,bottom=copy.y*MapHeight;
                bool visible=IsGlobe?copy==primaryCopy:left+world.Width>=pan.x-halfX-2-reliefPadding&&left<=pan.x+halfX+2+reliefPadding&&bottom+MapHeight>=pan.y-halfY-2-reliefPadding&&bottom<=pan.y+halfY+2+reliefPadding;
                if(IsTransitioning&&copy==primaryCopy)visible=true;surfaces[i].enabled=visible;if(visible)ActiveSheets++;
            }
        }

        public void SyncMaterials()
        {
            if (!maps || !surface) return;
            surface.CopyPropertiesFromMaterial(maps.Material);
            CommitProjection();
        }
        void ApplyProjection(Material m)
        {
            Vector2 p = RenderPan;
            MarkleyProjection.Bind(m);m.SetVector("_ProjectionSize",new Vector4(world.Width,MapHeight,1f/world.Width,1f/MapHeight));
            m.SetVector("_MapSize", new Vector4(world.Width, world.Height, 1f / world.Width, 1f / world.Height));
            m.SetVector("_ScrollAnchor", new Vector4(anchor.x, anchor.y, 0, 0));
            m.SetVector("_ScrollPan", new Vector4(p.x, p.y, 0, 0));
            m.SetVector("_ScrollRight", east); m.SetVector("_ScrollUp", north); m.SetVector("_ScrollNormal", normal);
            m.SetFloat("_ScrollMorph", morph); m.SetFloat("_ScrollSpin", displaySpin);
            m.SetFloat("_PixelsPerTile", pixels);
            m.SetFloat("_DetailWeight", DetailWeight);
            m.SetVector("_ScrollPoleHeights",polarHeights);
            m.SetFloat("_ScrollHeightScale",FlatGlobeGeometry.HeightScale);
            m.SetVector("_ScrollRelief",ReliefParameters);
        }
        public void Drag(Vector2 screenDelta)
        {
            if(IsFlat&&screenDelta.sqrMagnitude>0&&TryReliefPoint(viewCamera.pixelRect.center-screenDelta,out var point))PanBy(point-pan);
            else if(!IsGlobe)PanBy(-screenDelta/Mathf.Max(.1f,pixels));
            else {yaw+=screenDelta.x*.25f;pitch=Mathf.Clamp(pitch-screenDelta.y*.2f,-89.5f,89.5f);Tick(0,currentDay);if(landingLocked&&targetDistance<StartDistance)CaptureLanding(viewCamera.pixelRect.center,currentDay);}
        }
        public void PanBy(Vector2 tileDelta)
        {
            if(IsGlobe)return;focusZoom=false;zoomAnchor=false;targetPixels=-1;
            mapPosition.Move(tileDelta.x,tileDelta.y,world.Width,world.Height);
            if(IsTransitioning){AlignFrame(currentDay);pitch=Mathf.Asin(Mathf.Clamp(normal.y,-1,1))*Mathf.Rad2Deg;yaw=Mathf.Atan2(-normal.x,-normal.z)*Mathf.Rad2Deg;}
            Tick(0,currentDay);
        }
        Vector2 ProjectedTile(int tile,Vector2 near)
        {
            var location=PlanetCoordinateSystem.FromMapUV((tile%world.Width+.5)/world.Width,(tile/world.Width+.5)/world.Height);
            var canonical=MarkleyProjection.Project(location);var anchorUV=new Vector2(near.x/world.Width,near.y/MapHeight);
            return Vector2.Scale(MarkleyProjection.Nearest(canonical,anchorUV),ProjectionSize);
        }
        public void Wheel(float wheel, Vector2 screen, double day)
        {
            wheel = Mathf.Clamp(wheel, -24, 24);if(Mathf.Abs(wheel)<.00001f)return;
            hasStrategicZoomPoint=false;
            if(exitPending&&wheel<0)targetPixels=pixels;
            exitPending=false;
            if (IsFlat)
            {
                if(wheel<0)targetDistance=EndDistance;
                float minimum = ReturnPixelsPerTile;
                float currentTarget=targetPixels>0?targetPixels:pixels;
                float desired=currentTarget*Mathf.Exp(-wheel*.075f);
                if(wheel>0 && desired<=minimum)
                {
                    zoomAnchor=false;focusZoom=false;pendingFocus=-1;
                    // Already at/beyond the landing scale: fold at THIS centre and zoom.
                    if(pixels<=minimum*1.001f)
                    { ReanchorForExit(day);targetDistance=3.1f;return; }
                    // Cross the threshold without needing another wheel notch after easing settles.
                    targetPixels=minimum;exitPending=true;return;
                }
                // Explicit atlas mode may be wider than the return threshold; zooming IN
                // from there stays continuous instead of snapping back to the landing scale.
                targetPixels=Mathf.Clamp(desired,Mathf.Min(minimum,pixels),Mathf.Max(64,minimum));
                zoomScreen=screen;zoomAnchor=true;focusZoom=false;
            }
            else
            {
                float from=((wheel<0&&targetDistance>distance)||(wheel>0&&targetDistance<distance))?distance:targetDistance;
                float desired = Mathf.Clamp(from * Mathf.Exp(wheel * .035f), EndDistance, 6);
                if (desired < StartDistance && !landingLocked) CaptureLanding(screen, day);
                targetDistance = desired;
            }
        }
        public void Toggle(double day)
        {
            if (IsFlat || (IsTransitioning && targetDistance < StartDistance)) GoGlobe(day);
            else { if (!landingLocked) CaptureLanding(viewCamera.pixelRect.center, day); targetDistance = EndDistance; atlasPending = false; }
        }
        public void GoGlobe(double day)
        {
            // M returns immediately from the current view. ReanchorForExit derives
            // the perspective end gap from the actual orthographic size, preserving scale.
            exitPending=false;
            if (IsFlat) ReanchorForExit(day);
            atlasPending = false;  targetPixels=-1;pendingFocus=-1;focusZoom=false;targetDistance = 3.1f;
        }
        public void OpenAtlas(double day)
        {
            exitPending=false;
            if (IsFlat) { targetPixels=FitPixels();zoomAnchor=false;focusZoom=false;pendingFocus=-1; Tick(0, day); return; }
            if (!landingLocked) CaptureLanding(viewCamera.pixelRect.center, day);
            atlasPending = false; targetDistance = EndDistance;
        }
        public void Home(double day)
        {
            if (IsFlat) OpenAtlas(day);
            else { targetDistance = 3.1f; yaw = 0; pitch = 18; atlasPending = false; }
        }
        public void Focus(int tile, double day)
        {
            if (tile < 0 || tile >= world.Count) return;exitPending=false;
            Vector2 uv = new Vector2((tile % world.Width + .5f) / world.Width, (tile / world.Width + .5f) / world.Height);
            if (IsFlat) { focusTile=tile;focusCenter=ProjectedTile(tile,pan);focusZoom=true; targetPixels=48;zoomAnchor=false;NormalizePan();Tick(0, day); }
            else
            {
                entryScaleRecorded=false;LockPlane(uv, day);
                // Preserve the orbit pose; Tick smoothly rotates toward the selected tangent frame.
                endGap=viewCamera.pixelHeight*TileScale/(2*LandingPixels()*Mathf.Tan(Fov*Mathf.Deg2Rad*.5f));
                pendingFocus=tile;targetDistance=EndDistance;
            }
        }
        public void Recenter(PlanetLocation location,double day)
        {
            zoomAnchor=false;focusZoom=false;exitPending=false;atlasPending=false;
            pendingFocus=-1;targetPixels=-1;targetDistance=distance;
            pan=Vector2.Scale(MarkleyProjection.Nearest(MarkleyProjection.Project(location),ChartCenter),ProjectionSize);
            AlignFrame(day);landingLocked=!IsGlobe;
            pitch=Mathf.Asin(Mathf.Clamp(normal.y,-1,1))*Mathf.Rad2Deg;
            yaw=Mathf.Atan2(-normal.x,-normal.z)*Mathf.Rad2Deg;
            Tick(0,day);
        }
        internal FlatMinimapProjection MinimapProjection()
        {
            return new FlatMinimapProjection(viewCamera,world,null,ChartCenter,displaySpin,morph,
                normal,east,north,polarHeights,ReliefParameters,float.PositiveInfinity);
        }
        public bool TryProjectionPoint(Vector2 screen, out Vector2 tile)
        {
            tile = Vector2.zero; if (morph < .5f) return false;
            Ray ray = FlatGlobeGeometry.ScreenRay(viewCamera,screen); var plane = new Plane(normal, normal);
            float t; if (!plane.Raycast(ray, out t)) return false;
            Vector3 offset = ray.GetPoint(t) - normal;
            // Use canonical pan here, not RenderPan: caller's cursor delta is continuous under wrap.
            tile = pan + new Vector2(Vector3.Dot(offset, east), Vector3.Dot(offset, north)) / TileScale;
            return true;
        }
        public bool TryFlatPoint(Vector2 screen,out Vector2 tile)
        {
            if(!TryReliefPoint(screen,out Vector2 projected)){tile=Vector2.zero;return false;}
            tile=Vector2.Scale(MarkleyProjection.GeoUV(projected.x/world.Width,projected.y/MapHeight),new Vector2(world.Width,world.Height));return true;
        }
        float VertexHeight(Vector2 uv)
        {
            var geographic=MarkleyProjection.GeoUV(uv.x,uv.y);
            return FlatGlobeGeometry.Radius(FlatGlobeGeometry.SurfaceHeight(world,geographic),geographic.y,world.Height,polarHeights)-1;
        }
        float TerrainHeight(Vector2 uv)
        {
            float x=uv.x*meshColumns,y=uv.y*meshRows;
            int column=Mathf.FloorToInt(x),row=Mathf.FloorToInt(y);
            float tx=x-column,ty=y-row;
            var a=new Vector2((float)column/meshColumns,(float)row/meshRows);
            var dx=new Vector2(1f/meshColumns,0);var dy=new Vector2(0,1f/meshRows);
            float b=VertexHeight(a+dy),c=VertexHeight(a+dx);
            if(tx+ty<=1)return VertexHeight(a)*(1-tx-ty)+b*ty+c*tx;
            return c*(1-ty)+b*(1-tx)+VertexHeight(a+dx+dy)*(tx+ty-1);
        }
        Vector3 FlatPosition(Vector2 uv,float height,Vector4 reliefParameters)
        {
            var point=(Vector2.Scale(uv,ProjectionSize)-pan)*TileScale;
            var relief=FlatGlobeGeometry.ReliefPosition(point,height,reliefParameters);
            return normal*(1+relief.z)+east*relief.x+north*relief.y;
        }
        public bool TryReliefPoint(Vector2 screen,out Vector2 point)
        {
            if(!TryProjectionPoint(screen,out point))return false;
            var relief=ReliefParameters;
            if(!IsFlat||relief.x<=0)return true;
            var projected=(point-pan)*TileScale;
            var bounds=FlatGlobeGeometry.ReliefBounds(projected,relief);
            int firstColumn=Mathf.FloorToInt((pan.x+bounds.xMin/TileScale)/world.Width*meshColumns)-1;
            int lastColumn=Mathf.FloorToInt((pan.x+bounds.xMax/TileScale)/world.Width*meshColumns)+1;
            int first=Mathf.FloorToInt((pan.y+bounds.yMin/TileScale)/MapHeight*meshRows)-1;
            int last=Mathf.FloorToInt((pan.y+bounds.yMax/TileScale)/MapHeight*meshRows)+1;
            var ray=FlatGlobeGeometry.ScreenRay(viewCamera,screen);
            float nearest=float.PositiveInfinity;Vector2 hit=Vector2.zero;
            for(int row=first;row<=last;row++)for(int column=firstColumn;column<=lastColumn;column++)
            {
                var a=new Vector2((float)column/meshColumns,(float)row/meshRows);
                var b=new Vector2((float)column/meshColumns,(float)(row+1)/meshRows);
                var c=new Vector2((float)(column+1)/meshColumns,(float)row/meshRows);
                var d=new Vector2((float)(column+1)/meshColumns,(float)(row+1)/meshRows);
                float ha=VertexHeight(a),hb=VertexHeight(b),hc=VertexHeight(c),hd=VertexHeight(d);
                Vector3 pa=FlatPosition(a,ha,relief),pb=FlatPosition(b,hb,relief);
                Vector3 pc=FlatPosition(c,hc,relief),pd=FlatPosition(d,hd,relief);
                float wa=FlatGlobeGeometry.ReliefClipWeight(ha,relief,morph),wb=FlatGlobeGeometry.ReliefClipWeight(hb,relief,morph);
                float wc=FlatGlobeGeometry.ReliefClipWeight(hc,relief,morph),wd=FlatGlobeGeometry.ReliefClipWeight(hd,relief,morph);
                if(FlatGlobeGeometry.IntersectTriangle(ray,pa,pb,pc,out var weights,out float distance)&&distance<nearest)
                {nearest=distance;weights=FlatGlobeGeometry.PerspectiveWeights(weights,new Vector3(wa,wb,wc));hit=a*weights.x+b*weights.y+c*weights.z;}
                if(FlatGlobeGeometry.IntersectTriangle(ray,pc,pb,pd,out weights,out distance)&&distance<nearest)
                {nearest=distance;weights=FlatGlobeGeometry.PerspectiveWeights(weights,new Vector3(wc,wb,wd));hit=c*weights.x+b*weights.y+d*weights.z;}
            }
            if(float.IsInfinity(nearest))return false;
            point=Vector2.Scale(hit,ProjectionSize);
            return true;
        }
        public bool TryGetPlanetLocation(Vector2 screen,out PlanetLocation location)
        {
            location=new PlanetLocation();Vector2 uv;
            if(IsFlat&&TryFlatPoint(screen,out uv)){location=PlanetCoordinateSystem.FromMapUV(uv.x/world.Width,uv.y/world.Height);return true;}
            if(IsGlobe&&TryGlobeUV(screen,out uv)){location=PlanetCoordinateSystem.FromMapUV(uv.x,uv.y);return true;}return false;
        }
        public int Pick(Vector2 screen)
        {
            Vector2 tile;
            if (IsFlat) return TryFlatPoint(screen, out tile) ? FlatScrollMath.Tile(tile.x, tile.y, world.Width, world.Height) : -1;
            Vector2 uv; return IsGlobe && TryGlobeUV(screen, out uv) ? FlatScrollMath.Tile(uv.x * world.Width, Math.Min(world.Height - .0001, uv.y * world.Height), world.Width, world.Height) : -1;
        }
        public bool TryActorSurface(PlanetLocation location,out Vector3 position,out Vector2 geographicEast,out Vector2 geographicNorth,out Vector4 cellAxes)
        {
            position=Vector3.zero;geographicEast=Vector2.right;geographicNorth=Vector2.up;
            cellAxes=Vector4.zero;
            if(!IsFlat)return false;
            var reference=new Vector2(pan.x/world.Width,pan.y/MapHeight);var relief=ReliefParameters;
            position=ActorSurface(location,reference,relief)+normal*.00015f;
            double step=Math.PI*2/world.Width*.01;
            var dx=(ActorSurface(new PlanetLocation(location.Longitude+step,location.Latitude),reference,relief)-
                    ActorSurface(new PlanetLocation(location.Longitude-step,location.Latitude),reference,relief))/.02f;
            var dy=(ActorSurface(new PlanetLocation(location.Longitude,location.Latitude+step),reference,relief)-
                    ActorSurface(new PlanetLocation(location.Longitude,location.Latitude-step),reference,relief))/.02f;
            return FlatGlobeGeometry.TryActorFrame(viewCamera,dx,dy,TileScale/32,out geographicEast,out geographicNorth,out cellAxes);
        }
        Vector3 ActorSurface(PlanetLocation location,Vector2 reference,Vector4 relief)
        {
            var uv=MarkleyProjection.Nearest(MarkleyProjection.Project(location),reference);
            return FlatPosition(uv,TerrainHeight(uv),relief);
        }
        bool TryGlobeUV(Vector2 screen, out Vector2 uv)
        {
            uv = Vector2.zero; Ray ray = viewCamera.ScreenPointToRay(screen);
            float b = Vector3.Dot(ray.origin, ray.direction), disc = b * b - ray.origin.sqrMagnitude + 1.09f * 1.09f;
            if (disc < 0) return false;
            float lo = Mathf.Max(0, -b - Mathf.Sqrt(disc)), end = -b + Mathf.Sqrt(disc);
            if (end <= lo) return false;
            Quaternion inverse = Quaternion.Inverse(Spin(displaySpin));
            float previous = RadialError(ray.GetPoint(lo), inverse);
            for (int j = 1; j <= 160; j++)
            {
                float hi = Mathf.Lerp(lo, end, 1f / (161 - j));
                float value = RadialError(ray.GetPoint(hi), inverse);
                if (previous >= 0 && value <= 0)
                {
                    for (int k = 0; k < 20; k++) { float mid = (lo + hi) * .5f; if (RadialError(ray.GetPoint(mid), inverse) > 0) lo = mid; else hi = mid; }
                    uv = UV(Reflect(inverse * ray.GetPoint((lo + hi) * .5f)).normalized); return true;
                }
                lo = hi; previous = value;
            }
            return false;
        }
        static Vector2 UV(Vector3 source)
        {
            var location=PlanetCoordinateSystem.FromWorld(new PlanetVector(source.x,source.y,source.z),new PlanetVector(),1);
            return new Vector2((float)PlanetCoordinateSystem.MapU(location),(float)PlanetCoordinateSystem.MapV(location));
        }
        float RadialError(Vector3 point, Quaternion inverse)
        {
            if (point.sqrMagnitude < .000001f) return -1;
            Vector2 uv = UV(Reflect(inverse * point).normalized);
            float h=FlatGlobeGeometry.SurfaceHeight(world,uv);
            return point.magnitude - FlatGlobeGeometry.Radius(h,uv.y,world.Height,polarHeights);
        }
        public void SetRoute(int[] ids)
        {
            route = ids ?? new int[0]; lastRoutePixels = -1;
            if (route.Length < 2 && !routeMesh) return;
            if (!routeMesh)
            {
                routeMaterial = NewMaterial("ORBIS/FlatWorld/Route"); routeMesh = new Mesh { name = "Wrapped path preview", indexFormat = IndexFormat.UInt32 }; routeMesh.MarkDynamic();
                routes.Add(NewRenderer("Markley route",routeMesh,routeMaterial));
            }
            UpdateRoute();
        }
        void UpdateRoute()
        {
            if(!routeMesh)return;foreach(var renderer in routes)renderer.enabled=IsFlat&&route.Length>1;
            if(!IsFlat||route.Length<2)return;
            if(pan==lastRoutePan&&normal==lastRouteNormal&&pixels==lastRoutePixels&&lastRouteRevision==world.TerrainRevision)return;
            lastRoutePan=pan;lastRouteNormal=normal;lastRoutePixels=pixels;
            lastRouteRevision=world.TerrainRevision;
            var v=routeVertices;var cs=routeColors;var triangles=routeIndices;v.Clear();cs.Clear();triangles.Clear();
            float width=Mathf.Clamp(1.5f/Mathf.Max(1,pixels),.07f,.20f)*TileScale;
            Vector3 lift=normal*.00006f;Vector2 near=new Vector2(pan.x/world.Width,pan.y/MapHeight);
            float halfX=viewCamera.pixelWidth/Mathf.Max(1,pixels)*.6f,halfY=viewCamera.pixelHeight/Mathf.Max(1,pixels)*.6f;
            halfY+=FlatGlobeGeometry.MaximumHeight*FlatGlobeGeometry.HeightScale*FlatGlobeGeometry.ReliefTilt/TileScale;
            var relief=ReliefParameters;
            for(int i=1;i<route.Length;i++)
            {
                var p0=MarkleyProjection.Project(PlanetCoordinateSystem.FromMapUV((route[i-1]%world.Width+.5)/world.Width,(route[i-1]/world.Width+.5)/world.Height));
                var p1=MarkleyProjection.Project(PlanetCoordinateSystem.FromMapUV((route[i]%world.Width+.5)/world.Width,(route[i]/world.Width+.5)/world.Height));p1=MarkleyProjection.Nearest(p1,p0);
                for(int row=-2;row<=3;row++)for(int col=-1;col<=1;col++){
                    int k=(int)Math.Floor(row/2.0);bool odd=row-2*k==1;float sign=odd?-1:1;
                    Vector2 a=new Vector2((float)(odd?.75-p0.x-k*.5:p0.x-k*.5),odd?row+1-p0.y:row+p0.y);
                    a.x=near.x+(float)PlanetCoordinateSystem.NearestOffset(a.x-near.x,1)+col;
                    Vector2 b=a+(p1-p0)*sign;a=Vector2.Scale(a,ProjectionSize)-pan;b=Vector2.Scale(b,ProjectionSize)-pan;
                    if(Mathf.Min(a.x,b.x)>halfX||Mathf.Max(a.x,b.x)<-halfX||Mathf.Min(a.y,b.y)>halfY||Mathf.Max(a.y,b.y)<-halfY)continue;
                    var ua=Vector2.Scale(a+pan,new Vector2(1f/world.Width,1f/MapHeight));
                    var ub=Vector2.Scale(b+pan,new Vector2(1f/world.Width,1f/MapHeight));
                    Vector3 pa=FlatPosition(ua,TerrainHeight(ua),relief)+lift,pb=FlatPosition(ub,TerrainHeight(ub),relief)+lift;
                    Vector3 side=Vector3.Cross((pb-pa).normalized,normal)*width;int start=v.Count;
                    v.Add(pa-side);v.Add(pa+side);v.Add(pb-side);v.Add(pb+side);for(int c=0;c<4;c++)cs.Add(new Color(1,.85f,.45f));
                    triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);triangles.Add(start+2);triangles.Add(start+1);triangles.Add(start+3);
                }
            }
            routeMesh.Clear();routeMesh.SetVertices(v);routeMesh.SetColors(cs);routeMesh.SetTriangles(triangles,0);routeMesh.RecalculateBounds();
        }
        void OnDestroy()
        {
            if (surface) Destroy(surface); if (flatMesh) Destroy(flatMesh); if (lattice) Destroy(lattice);
            if (routeMesh) Destroy(routeMesh); if (routeMaterial) Destroy(routeMaterial);
        }
    }
}
