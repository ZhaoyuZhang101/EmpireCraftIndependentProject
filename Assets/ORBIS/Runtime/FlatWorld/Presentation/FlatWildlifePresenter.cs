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
        MeshRenderer display;
        Vector2[] previousPositions,directions;
        float[] screenFacing;
        Vector3 renderSun;
        bool renderNatural,motionAdvanced;
        public FlatWildlifeSimulation Simulation {get;private set;}
        public FlatWildlifePathfinder Pathfinder {get;private set;}
        public int VisibleActors {get;private set;}

        public void Initialize(WorldData data,FlatScrollNavigator navigator,FlatMapRenderer renderer,Camera camera)
        {
            world=data;navigation=navigator;map=renderer;viewCamera=camera;
            Pathfinder=gameObject.AddComponent<FlatWildlifePathfinder>();Pathfinder.Initialize(world);
            Simulation=new FlatWildlifeSimulation(world,Pathfinder);
            previousPositions=new Vector2[world.Wildlife.Count];directions=new Vector2[world.Wildlife.Count];
            screenFacing=new float[world.Wildlife.Count];
            for(int i=0;i<world.Wildlife.Count;i++)
            {
                var actor=world.Wildlife[i];previousPositions[i]=new Vector2((float)actor.X,(float)actor.Y);
                directions[i]=new Vector2(actor.Facing,0);
                screenFacing[i]=actor.Facing;
            }
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
            renderSun=sun;renderNatural=natural;motionAdvanced=seconds>0;
        }
        void BeforeCameraRender(Camera camera)
        {
            if(camera!=viewCamera||!isActiveAndEnabled)return;
            RefreshArtwork();
        }
        void RefreshArtwork()
        {
            VisibleActors=0;
            bool visible=renderNatural&&navigation.IsFlat&&navigation.DetailWeight>.001f;
            if(!visible){display.enabled=false;return;}
            material.CopyPropertiesFromMaterial(navigation.SurfaceMaterial);
            material.renderQueue=(int)RenderQueue.Transparent-10;
            material.SetFloat("_WildlifeOcclusionReady",1);
            vertices.Clear();pixels.Clear();actors.Clear();states.Clear();geography.Clear();motion.Clear();cellMappings.Clear();indices.Clear();
            float pixelScale=(float)(Math.PI*2/world.Width/32);
            Vector3 right=viewCamera.transform.right*pixelScale,up=viewCamera.transform.up*pixelScale;
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
                if(actor.MoveSpeed>.001f&&Math.Abs(travel.x)>.01f)screenFacing[i]=travel.x<0?-1:1;
                float variant=FMath.Unit((uint)actor.Id^FMath.Seed(world.Seed));
                var actorData=new Vector4((float)actor.Species,screenFacing[i],actor.AnimationTime,variant);
                var stateData=new Vector4((float)actor.State,actor.MoveSpeed,navigation.DetailWeight,world.Facts.Wetness[tile]);
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
            foreach(var actor in world.Wildlife)
            {
                if(FlatWildlifeSimulation.Tile(world,actor)!=tile)continue;
                description.Append("\n").Append(SpeciesNames[(int)actor.Species]).Append(" #").Append(actor.Id).Append(" · ").Append(StateNames[(int)actor.State]);
                description.Append("\n饥饿 ").Append((actor.Hunger*100).ToString("F0")).Append("% · 疲劳 ").Append((actor.Fatigue*100).ToString("F0")).Append('%');
                if(++shown==4)break;
            }
            return description.ToString();
        }
        void OnDestroy()
        {
            Camera.onPreCull-=BeforeCameraRender;
            Simulation?.Dispose();
            if(mesh)Destroy(mesh);if(material)Destroy(material);
        }
    }
}
