using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.Editor
{
    public static class WildlifeActorRegression
    {
        [MenuItem("Tools/ORBIS/Local Planet/Validate wildlife actor artwork")]
        public static void Run()
        {
            var shader=Resources.Load<Shader>("ORBIS/FlatWorld/WildlifeActors");
            if(!shader||!shader.isSupported)throw new Exception("Wildlife actor shader is unsupported.");
            var material=new Material(shader);
            var mesh=new Mesh();
            var cameraObject=new GameObject("Wildlife artwork test camera");
            var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=24;
            camera.transform.SetPositionAndRotation(new Vector3(0,0,-5),Quaternion.identity);
            camera.nearClipPlane=.1f;camera.farClipPlane=10;
            var target=RenderTexture.GetTemporary(192,192,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var image=new Texture2D(192,192,TextureFormat.RGBAFloat,false,true);
            var commands=new CommandBuffer();var old=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;
            var report=new List<string>();
            try
            {
                ShaderUtil.allowAsyncCompilation=false;ShaderUtil.CompilePass(material,0,true);
                foreach(var message in ShaderUtil.GetShaderMessages(shader))if(message.severity.ToString()=="Error")throw new Exception(message.message);
                float tileScale=(float)(Math.PI*2/512),probeStep=tileScale*.01f;
                foreach(float height in new[]{0f,.018f})
                {
                    var relief=new Vector4(1,0,.72f,0);
                    var dx=(Orbis.Flat.FlatGlobeGeometry.ReliefPosition(new Vector2(probeStep,0),height,relief)-Orbis.Flat.FlatGlobeGeometry.ReliefPosition(new Vector2(-probeStep,0),height,relief))/.02f;
                    var dy=(Orbis.Flat.FlatGlobeGeometry.ReliefPosition(new Vector2(0,probeStep),height,relief)-Orbis.Flat.FlatGlobeGeometry.ReliefPosition(new Vector2(0,-probeStep),height,relief))/.02f;
                    bool valid=Orbis.Flat.FlatGlobeGeometry.TryActorFrame(camera,dx,dy,tileScale/32,out var east,out var north,out var mapping);
                    float expected=(.18f-height)/(.18f*32);
                    Require(report,valid&&Mathf.Abs(mapping.x-expected)<.00001f&&Mathf.Abs(mapping.w-expected)<.00001f,
                        "actor occlusion uses final relief perspective at height="+height+" cellPerPixel="+mapping.x);
                }
                foreach(float latitude in new[]{0f,60f,-80f,85f,89.65f})foreach(float roll in new[]{0f,37f,121f})
                {
                    float scale=Mathf.Cos(latitude*Mathf.Deg2Rad);
                    var rotation=Quaternion.AngleAxis(roll,Vector3.forward);
                    var dx=rotation*Vector3.right*(tileScale*scale);var dy=rotation*Vector3.up*tileScale;
                    bool valid=Orbis.Flat.FlatGlobeGeometry.TryActorFrame(camera,dx,dy,tileScale/32,out _,out _,out var mapping);
                    Orbis.Flat.FlatGlobeGeometry.LocalArtworkAxes(mapping,scale,out var east,out var north);
                    Require(report,valid&&(east-(Vector2)(rotation*Vector3.right)*scale).magnitude<.00001f
                        &&(north-(Vector2)(rotation*Vector3.up)*scale).magnitude<.00001f,
                        $"equal ground-length actor pixels follow geographic axes at latitude={latitude}, roll={roll}");
                }
                material.SetVector("_FlatSun",Vector3.forward);material.SetFloat("_FlatDayNight",1);
                material.SetTexture("_SkyTex",Texture2D.blackTexture);material.SetFloat("_SkyReady",0);
                mesh.vertices=new[]{new Vector3(-24,-24,0),new Vector3(24,-24,0),new Vector3(-24,24,0),new Vector3(24,24,0)};
                mesh.uv=new[]{new Vector2(-24,-24),new Vector2(24,-24),new Vector2(-24,24),new Vector2(24,24)};
                mesh.triangles=new[]{0,2,1,1,2,3};mesh.RecalculateBounds();
                foreach(int species in new[]{1,2,3,4})
                {
                    SetData(mesh,species,0,0,.5f);
                    var idle=Sample(mesh,material,camera,target,image,commands);
                    SetData(mesh,species,0,0,.5f);
                    var paused=Sample(mesh,material,camera,target,image,commands);
                    Require(report,Difference(idle,paused)<.000001f,"paused actor clock produces identical pixels; species="+species);
                    SetData(mesh,species,0,0,2.2f);
                    var idleLater=Sample(mesh,material,camera,target,image,commands);
                    Require(report,Difference(idle,idleLater)>.000001f,"idle animal retains breathing and small living motions; species="+species);
                    SetData(mesh,species,1,.2f,.5f);
                    var walking=Sample(mesh,material,camera,target,image,commands);
                    SetData(mesh,species,1,.2f,.8f);
                    var later=Sample(mesh,material,camera,target,image,commands);
                    Require(report,Difference(walking,later)>.00001f,"walking art and particles follow the actor animation clock; species="+species);
                    SetData(mesh,species,5,0,.5f);
                    var sleeping=Sample(mesh,material,camera,target,image,commands);
                    Require(report,Difference(idle,sleeping)>.00001f,"sleeping art has a compact resting silhouette; species="+species);
                    SetData(mesh,species,5,0,2.2f);
                    var breathing=Sample(mesh,material,camera,target,image,commands);
                    Require(report,Difference(sleeping,breathing)>.000001f,"sleeping animal breathes on its pause-controlled clock; species="+species);
                    SetData(mesh,species,3,0,.5f);
                    var eating=Sample(mesh,material,camera,target,image,commands);
                    Require(report,Difference(idle,eating)>.000001f,"feeding art lowers or moves the head; species="+species);
                    bool finite=true;int visible=0;
                    foreach(var pixel in walking)
                    {
                        finite&=!float.IsNaN(pixel.r+pixel.g+pixel.b+pixel.a)&&!float.IsInfinity(pixel.r+pixel.g+pixel.b+pixel.a);
                        if(pixel.a>.01f)visible++;
                    }
                    Require(report,finite&&visible>50&&visible<walking.Length/2,"batched actor produces bounded finite artwork; species="+species+" pixels="+visible);
                }
                SetData(mesh,1,0,0,.5f);
                var centre=new Vector4(32.5f/64,16.5f/32,.3f,.2f);
                mesh.SetUVs(3,new List<Vector4>{centre,centre,centre,centre});
                var unobstructed=Sample(mesh,material,camera,target,image,commands);
                var objects=new Texture2D(64,32,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};
                try
                {
                    var pixels=new Color[64*32];for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(0,0,1f/255,0);
                    objects.SetPixels(pixels);objects.Apply();material.SetTexture("_InfoTex",objects);
                    material.SetVector("_MapSize",new Vector4(64,32,1f/64,1f/32));
                    material.SetFloat("_WildlifeOcclusionReady",1);material.SetFloat("_DetailWeight",1);material.SetFloat("_LocalPhysicalArtwork",1);
                    var occluded=Sample(mesh,material,camera,target,image,commands);
                    Require(report,Difference(unobstructed,occluded)>.0001f,"foreground objects occlude actor pixels using their actual painted silhouettes");
                }
                finally{UnityEngine.Object.DestroyImmediate(objects);}
            }
            finally
            {
                RenderTexture.active=old;ShaderUtil.allowAsyncCompilation=async;
                commands.Release();RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(cameraObject);
            }
            if(Application.isPlaying&&AstarPath.active)CheckGraphReplacement(report);
            Directory.CreateDirectory("ValidationOutput/wildlife-actors");File.WriteAllLines("ValidationOutput/wildlife-actors/artwork-regression.txt",report);
            if(report.Exists(line=>line.StartsWith("FAIL",StringComparison.Ordinal)))throw new Exception(string.Join("\n",report));
        }
        static void CheckGraphReplacement(List<string> report)
        {
            var engine=AstarPath.active;
            Func<int> graphCount=()=>Array.FindAll(engine.data.graphs,g=>g!=null).Length;
            int before=graphCount();
            var world=new Flat.WorldData(64,32,"graph-lifecycle",Flat.TerrainPreset.Continents);
            world.Facts=new Flat.FlatTerrainFacts(world.Count);
            for(int i=0;i<world.Count;i++)
            {
                world.Elevation[i]=100;world.Biomes[i]=Flat.Biome.Grassland;world.Facts.ShoreDistance[i]=-8;
            }
            var holder=new GameObject("ORBIS graph replacement regression");
            try
            {
                var actors=holder.AddComponent<Flat.FlatWildlifePathfinder>();actors.Initialize(world);
                var routes=holder.AddComponent<Flat.FlatPlanetPathfinder>();
                int from=world.Index(30,16),to=world.Index(34,16);
                bool routesWork=true,countsMatch=true;
                for(int edit=0;edit<3;edit++)
                {
                    world.Buildings[from]=edit==1?Flat.Structure.Road:Flat.Structure.None;world.Touch(from);
                    actors.SendMessage("Update");
                    var route=routes.Find(world,from,to);
                    routesWork&=route.Length>0&&route[0]==from&&route[route.Length-1]==to;
                    countsMatch&=graphCount()==before+2;
                }
                Require(report,routesWork&&countsMatch,"editing terrain replaces both owned Astar graphs and preserves working routes");
            }
            finally{UnityEngine.Object.DestroyImmediate(holder);engine.FlushWorkItems();}
            Require(report,graphCount()==before,"unloading a map releases its two Astar graphs without touching other graphs");
        }
        static void SetData(Mesh mesh,int species,int state,float speed,float clock)
        {
            var actor=new Vector4(species,1,clock,.31f);var behaviour=new Vector4(state,speed,1,.4f);
            var geographic=new Vector4(.5f,.5f,.3f,.2f);var travel=new Vector4(1,0,1,15);
            mesh.SetUVs(1,new List<Vector4>{actor,actor,actor,actor});
            mesh.SetUVs(2,new List<Vector4>{behaviour,behaviour,behaviour,behaviour});
            mesh.SetUVs(3,new List<Vector4>{geographic,geographic,geographic,geographic});
            mesh.SetUVs(4,new List<Vector4>{travel,travel,travel,travel});
            var axes=new Vector4(1f/32,0,0,1f/32);
            mesh.SetUVs(5,new List<Vector4>{axes,axes,axes,axes});
        }
        static Color[] Sample(Mesh mesh,Material material,Camera camera,RenderTexture target,Texture2D image,CommandBuffer commands)
        {
            commands.Clear();commands.SetRenderTarget(target);commands.SetViewport(new Rect(0,0,192,192));commands.ClearRenderTarget(true,true,Color.clear);
            commands.SetViewProjectionMatrices(camera.worldToCameraMatrix,GL.GetGPUProjectionMatrix(camera.projectionMatrix,false));
            commands.DrawMesh(mesh,Matrix4x4.identity,material,0,0);Graphics.ExecuteCommandBuffer(commands);
            RenderTexture.active=target;image.ReadPixels(new Rect(0,0,192,192),0,0);image.Apply();return image.GetPixels();
        }
        static float Difference(Color[] a,Color[] b)
        {
            double difference=0;
            for(int i=0;i<a.Length;i++)difference+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)+Math.Abs(a[i].a-b[i].a);
            return (float)(difference/a.Length);
        }
        static void Require(List<string> report,bool passed,string label)=>report.Add((passed?"PASS ":"FAIL ")+label);
    }
}
