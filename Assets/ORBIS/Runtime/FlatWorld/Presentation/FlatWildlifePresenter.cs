using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using Orbis.Planet;

namespace Orbis.Flat
{
    /// <summary>Persistent simulated actors, batched into one view-dependent pixel-art mesh.</summary>
    public sealed class FlatWildlifePresenter : MonoBehaviour
    {
        static readonly string[] SpeciesNames={"","兔子","鹿","鸟","蝴蝶"};
        static readonly string[] StateNames={"休息","闲逛","寻找食物","进食","寻找休息处","睡觉"};
        static readonly string[] StormModeNames={"","顺风伏低行进","避风蹲伏","顶风俯身行进"};
        public const byte StormModeCalm=0,StormModeSeekingShelter=1,StormModeSheltered=2,StormModeBracingHeadwind=3;
        readonly List<Vector3> vertices=new List<Vector3>();
        readonly List<Vector2> pixels=new List<Vector2>();
        readonly List<Vector4> actors=new List<Vector4>(),states=new List<Vector4>(),geography=new List<Vector4>(),motion=new List<Vector4>(),cellMappings=new List<Vector4>();
        readonly List<int> indices=new List<int>();
        readonly StringBuilder description=new StringBuilder(256);
        WorldData world;
        FlatScrollNavigator navigation;
        FlatMapRenderer map;
        Camera viewCamera;
        Mesh mesh;
        Material material;
#if UNITY_EDITOR
        readonly FlatShaderPreview editorPreview = new FlatShaderPreview();
#endif
        MeshRenderer display;
        Vector2[] previousPositions,directions,stormWinds;
        float[] screenFacing;
        byte[] stormModes;
        Vector3 renderSun;
        bool renderNatural,motionAdvanced;
        public FlatWildlifeSimulation Simulation {get;private set;}
        public FlatWildlifePathfinder Pathfinder {get;private set;}
        public int VisibleActors {get;private set;}
        public byte GetActorStormMode(int index)=>stormModes!=null&&(uint)index<stormModes.Length?stormModes[index]:StormModeCalm;
        public Vector2 GetActorStormWind(int index)=>stormWinds!=null&&(uint)index<stormWinds.Length?stormWinds[index]:Vector2.zero;

        // Exact CPU mirror of FlatCycloneWind.cginc so wildlife presentation shares the identical published storm field (zero allocations).
        public static Vector2 SamplePublishedCycloneWind(double u,double v,Vector4 cycloneNorth,Vector4 cycloneSouth)
        {
            if(cycloneNorth.w+cycloneSouth.w<=.025f)return Vector2.zero;
            double lon=(u-.5)*Math.PI*2.0,lat=(v-.5)*Math.PI;
            double sl=Math.Sin(lon),cl=Math.Cos(lon),sp=Math.Sin(lat),cp=Math.Cos(lat);
            var up=new Vector3((float)(sl*cp),(float)sp,(float)(cl*cp));
            var east=new Vector3((float)cl,0f,(float)(-sl));
            var north=new Vector3((float)(-sp*sl),(float)cp,(float)(-sp*cl));
            Vector3 flow=EvaluateCycloneWind3D(up,cycloneNorth,1f)+EvaluateCycloneWind3D(up,cycloneSouth,-1f);
            return new Vector2(Vector3.Dot(east,flow),Vector3.Dot(north,flow));
        }

        static Vector3 EvaluateCycloneWind3D(Vector3 up,Vector4 centre,float spin)
        {
            float strength=centre.w;
            if(strength<=.025f)return Vector3.zero;
            var c=new Vector3(centre.x,centre.y,centre.z);
            float distance=(up-c).magnitude;
            float extent=Mathf.Lerp(.115f,.218f,Mathf.Sqrt(Mathf.Clamp01(strength)));
            if(distance>=extent*1.15f)return Vector3.zero;
            float r=distance/extent;
            Vector3 inward=c-up*Vector3.Dot(up,c);
            float inSq=inward.sqrMagnitude;
            inward/=Mathf.Sqrt(Mathf.Max(1e-7f,inSq));
            float s0=Smoothstep(.015f,.12f,r),s1=Smoothstep(.16f,1.15f,r);
            float speed=strength*s0*(1f-s1);
            return (Vector3.Cross(inward,up)*spin+inward*.23f)*(speed*1.9f);
        }

        static float Smoothstep(float edge0,float edge1,float x)
        {
            float t=Mathf.Clamp01((x-edge0)/Mathf.Max(1e-6f,edge1-edge0));
            return t*t*(3f-2f*t);
        }

        void EnsureActorBuffers()
        {
            int n=world?.Wildlife?.Count??0;
            if(previousPositions!=null&&previousPositions.Length==n)return;
            previousPositions=new Vector2[n];directions=new Vector2[n];stormWinds=new Vector2[n];
            screenFacing=new float[n];stormModes=new byte[n];
            for(int i=0;i<n;i++)
            {
                var actor=world.Wildlife[i];
                previousPositions[i]=new Vector2((float)actor.X,(float)actor.Y);
                directions[i]=new Vector2(actor.Facing,0);
                screenFacing[i]=actor.Facing;
            }
        }

        public void Initialize(WorldData data,FlatScrollNavigator navigator,FlatMapRenderer renderer,Camera camera)
        {
            world=data;navigation=navigator;map=renderer;viewCamera=camera;
            Pathfinder=gameObject.AddComponent<FlatWildlifePathfinder>();Pathfinder.Initialize(world);
            Simulation=new FlatWildlifeSimulation(world,Pathfinder);
            EnsureActorBuffers();
            var shader=Resources.Load<Shader>("ORBIS/FlatWorld/WildlifeActors");
            if(!shader||!shader.isSupported)throw new InvalidOperationException("WildlifeActors shader is unavailable.");
            material=new Material(shader){name="ORBIS simulated wildlife"};
            mesh=new Mesh{name="Batched wildlife actors",indexFormat=IndexFormat.UInt32};mesh.MarkDynamic();
            var go=new GameObject("Wildlife actor artwork",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(transform,false);
            go.GetComponent<MeshFilter>().sharedMesh=mesh;display=go.GetComponent<MeshRenderer>();display.sharedMaterial=material;
            display.shadowCastingMode=ShadowCastingMode.Off;display.receiveShadows=false;display.enabled=false;
            map.Material.SetFloat("_WildlifeActors",1);
            Camera.onPreCull+=BeforeCameraRender;
        }
        public void Tick(float seconds,double calendarDay,Vector3 sun,bool natural,float pathWaitSeconds=-1)
        {
            Simulation.Tick(seconds,calendarDay,pathWaitSeconds>=0?pathWaitSeconds:seconds);
            EnsureActorBuffers();
            UpdateStormPresentationState();
            renderSun=sun;renderNatural=natural;motionAdvanced=seconds>0;
        }

        // Pure presentation classification reusing pre-allocated buffers (zero GC, zero coordinate/path mutation).
        void UpdateStormPresentationState()
        {
            Vector4 cycN=map&&map.Sky?map.Sky.PublishedCycloneNorth:Vector4.zero;
            Vector4 cycS=map&&map.Sky?map.Sky.PublishedCycloneSouth:Vector4.zero;
            bool anyStorm=cycN.w+cycS.w>.025f;
            int count=world.Wildlife.Count;
            for(int i=0;i<count;i++)
            {
                if(!anyStorm)
                {
                    stormWinds[i]=Vector2.zero;
                    stormModes[i]=StormModeCalm;
                    continue;
                }
                var actor=world.Wildlife[i];
                Vector2 wind=SamplePublishedCycloneWind(actor.X/world.Width,actor.Y/world.Height,cycN,cycS);
                stormWinds[i]=wind;
                float windMag=wind.magnitude;
                float storm=Smoothstep(.22f,1.20f,windMag);
                if(storm<.08f)
                {
                    stormModes[i]=StormModeCalm;
                    continue;
                }
                Vector2 windDir=windMag>1e-4f?wind/windMag:Vector2.right;
                int tile=FlatWildlifeSimulation.Tile(world,actor);
                bool sheltered=IsTileSheltered(tile,windDir);
                bool moving=actor.MoveSpeed>.001f;
                if(sheltered||!moving)
                {
                    stormModes[i]=StormModeSheltered;
                }
                else
                {
                    float headwind=-Vector2.Dot(directions[i],windDir);
                    stormModes[i]=headwind>.20f?StormModeBracingHeadwind:StormModeSeekingShelter;
                }
            }
        }

        bool IsTileSheltered(int tile,Vector2 windDir)
        {
            if(world?.Facts==null||tile<0||tile>=world.Count)return false;
            var veg=world.Facts.Vegetation[tile];
            if((veg>=VegetationCover.SeasonalForest&&veg<=VegetationCover.TropicalRainforest)||veg==VegetationCover.Shrubland)
                return true;
            int tx=tile%world.Width,ty=tile/world.Width;
            for(int dy=-1;dy<=1;dy++)
            for(int dx=-1;dx<=1;dx++)
            {
                if(dx==0&&dy==0)continue;
                int ny=ty+dy;
                if(ny<0||ny>=world.Height)continue;
                int n=world.Index(tx+dx,ny);
                if(world.Buildings[n]==Structure.Settlement&&(dx*windDir.x+dy*windDir.y<=.45f))
                    return true;
            }
            return false;
        }
        void BeforeCameraRender(Camera camera)
        {
            if(camera!=viewCamera||!isActiveAndEnabled)return;
            RefreshArtwork();
        }
        void RefreshArtwork()
        {
            EnsureActorBuffers();
            VisibleActors=0;
            bool visible=renderNatural&&navigation.IsFlat&&!navigation.UsingShaderPreview&&navigation.DetailWeight>.001f;
            if(!visible){display.enabled=false;return;}
            material.CopyPropertiesFromMaterial(navigation.SurfaceMaterial);
            material.renderQueue=(int)RenderQueue.Transparent-10;
            material.SetFloat("_WildlifeOcclusionReady",1);
            material.SetFloat("_LocalPhysicalArtwork",navigation.IsAtlas?0:1);
            if(navigation.IsAtlas)material.EnableKeyword("ORBIS_ATLAS_PLANTS");
            else material.DisableKeyword("ORBIS_ATLAS_PLANTS");
#if UNITY_EDITOR
            var draw = editorPreview.ForDraw(material, 0);
            if (display.sharedMaterial != draw) display.sharedMaterial = draw;
#endif
            vertices.Clear();pixels.Clear();actors.Clear();states.Clear();geography.Clear();motion.Clear();cellMappings.Clear();indices.Clear();
            float pixelScale=(float)(Math.PI*2/world.Width/32);
            Vector3 screenRight=viewCamera.transform.right*pixelScale,screenUp=viewCamera.transform.up*pixelScale;
            for(int i=0;i<world.Wildlife.Count;i++)
            {
                var actor=world.Wildlife[i];
                var current=new Vector2((float)actor.X,(float)actor.Y);
                var delta=current-previousPositions[i];delta.x=(float)FlatScrollMath.Shortest(delta.x,world.Width);
                if(motionAdvanced&&delta.sqrMagnitude>1e-12f)directions[i]=delta.normalized;
                previousPositions[i]=current;
                var location=PlanetCoordinateSystem.FromMapUV(actor.X/world.Width,actor.Y/world.Height);
                if(!navigation.TryActorSurface(location,out var ground,out var east,out var north,out var cellAxes))continue;
                var viewport=viewCamera.WorldToViewportPoint(ground);
                if(viewport.z<0||viewport.x<-.1f||viewport.x>1.1f||viewport.y<-.1f||viewport.y>1.1f)continue;
                int tile=FlatWildlifeSimulation.Tile(world,actor);
                var frame=PlanetCoordinateSystem.LocalFrame(location,new PlanetVector(),1);
                var light=new PlanetVector(renderSun.x,renderSun.y,renderSun.z);
                float sunEast=(float)PlanetVector.Dot(frame.East,light),sunNorth=(float)PlanetVector.Dot(frame.North,light);
                var sunChart=east*sunEast+north*sunNorth;
                var travel=east*directions[i].x+north*directions[i].y;
                var right=screenRight;var up=screenUp;
                float facing=actor.Facing;
                if(navigation.IsAtlas)
                {
                    if(actor.MoveSpeed>.001f&&Math.Abs(travel.x)>.01f)screenFacing[i]=travel.x<0?-1:1;
                    facing=screenFacing[i];
                }
                else
                {
                    float latitudeScale=Mathf.Max(.000001f,(float)Math.Cos(location.Latitude));
                    FlatGlobeGeometry.LocalArtworkAxes(cellAxes,latitudeScale,out var eastPixel,out var northPixel);
                    right=screenRight*eastPixel.x+screenUp*eastPixel.y;
                    up=screenRight*northPixel.x+screenUp*northPixel.y;
                    // Artwork, shadows, travel and the occluder's geographic lookup share these axes.
                    sunChart=new Vector2(sunEast,sunNorth);
                    travel=new Vector2(directions[i].x*latitudeScale,directions[i].y).normalized;
                    cellAxes=new Vector4(1f/32,0,0,latitudeScale/32);
                }
                // Presentation-only storm posture adjustments (never mutates authoritative simulation state or coordinates):
                // stationary/sheltered actors turn to brace into gales; flying birds/butterflies hunker down in severe winds.
                Vector2 localWind=stormWinds[i];
                float renderSpeed=actor.MoveSpeed;
                float stormStrength=Smoothstep(.22f,1.20f,localWind.magnitude);
                if(stormStrength>=.35f)
                {
                    if(actor.Species==WildlifeSpecies.Bird||actor.Species==WildlifeSpecies.Butterfly)
                        renderSpeed=0f;
                    if(renderSpeed<=.001f&&Mathf.Abs(localWind.x)>.05f)
                        facing=localWind.x<0f?1f:-1f;
                }
                float variant=FMath.Unit((uint)actor.Id^FMath.Seed(world.Seed));
                var actorData=new Vector4((float)actor.Species,facing,actor.AnimationTime,variant);
                var stateData=new Vector4((float)actor.State,renderSpeed,navigation.DetailWeight,world.Facts.Wetness[tile]);
                var geoData=new Vector4((float)(actor.X/world.Width),(float)(actor.Y/world.Height),sunChart.x,sunChart.y);
                var motionData=new Vector4(travel.x,travel.y,actor.Activity,actor.LocalTemperature);
                int start=vertices.Count;
                AddVertex(ground,right,up,new Vector2(-24,-24),actorData,stateData,geoData,motionData,cellAxes);
                AddVertex(ground,right,up,new Vector2(24,-24),actorData,stateData,geoData,motionData,cellAxes);
                AddVertex(ground,right,up,new Vector2(-24,24),actorData,stateData,geoData,motionData,cellAxes);
                AddVertex(ground,right,up,new Vector2(24,24),actorData,stateData,geoData,motionData,cellAxes);
                indices.Add(start);indices.Add(start+2);indices.Add(start+1);indices.Add(start+1);indices.Add(start+2);indices.Add(start+3);
                VisibleActors++;
            }
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetUVs(0,pixels);mesh.SetUVs(1,actors);mesh.SetUVs(2,states);
            mesh.SetUVs(3,geography);mesh.SetUVs(4,motion);mesh.SetUVs(5,cellMappings);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();
            display.enabled=VisibleActors>0;
        }
        void AddVertex(Vector3 centre,Vector3 right,Vector3 up,Vector2 pixel,Vector4 actor,Vector4 state,Vector4 geographic,Vector4 movement,Vector4 mapping)
        {
            vertices.Add(centre+right*pixel.x+up*pixel.y);pixels.Add(pixel);
            actors.Add(actor);states.Add(state);geography.Add(geographic);motion.Add(movement);
            cellMappings.Add(mapping);
        }
        public string DescribeTile(int tile)
        {
            description.Clear();int shown=0;
            for(int i=0;i<world.Wildlife.Count;i++)
            {
                var actor=world.Wildlife[i];
                if(FlatWildlifeSimulation.Tile(world,actor)!=tile)continue;
                byte sMode=GetActorStormMode(i);
                description.Append("\n").Append(SpeciesNames[(int)actor.Species]).Append(" #").Append(actor.Id).Append(" · ").Append(StateNames[(int)actor.State]);
                if(sMode>0&&sMode<StormModeNames.Length)description.Append(" · ").Append(StormModeNames[sMode]);
                description.Append("\n饥饿 ").Append((actor.Hunger*100).ToString("F0")).Append("% · 疲劳 ").Append((actor.Fatigue*100).ToString("F0")).Append('%');
                if(++shown==4)break;
            }
            return description.ToString();
        }
        void OnDestroy()
        {
#if UNITY_EDITOR
            editorPreview.Dispose();
#endif
            Camera.onPreCull-=BeforeCameraRender;
            Simulation?.Dispose();
            if(mesh)Destroy(mesh);if(material)Destroy(material);
        }
    }
}
