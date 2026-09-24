using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace Orbis.Editor
{
    public static class OrbisV4Validation
    {
        [Serializable]sealed class Report{public string utc,scope="Unity editor production-code tests";public int assertions;public bool passed;public string error;}
        static int assertions;
        static void Check(bool condition,string message){assertions++;if(!condition)throw new InvalidOperationException(message);}
        [MenuItem("Tools/ORBIS/V4/Validate Upgrade (Production Code)")]
        public static void Run()
        {
            var report=new Report{utc=DateTime.UtcNow.ToString("o")};assertions=0;
            try
            {
                Codec();Selection();River();Territory();Shaders();report.passed=true;
                Debug.Log("ORBIS V4 驗證通過："+assertions+" 個斷言。這些不是 GPU 效能或長時間遊玩測試。");
            }
            catch(Exception e){report.passed=false;report.error=e.ToString();Debug.LogException(e);}
            report.assertions=assertions;Directory.CreateDirectory(Application.persistentDataPath);
            File.WriteAllText(Path.Combine(Application.persistentDataPath,"orbis-v4-editor-validation.json"),JsonUtility.ToJson(report,true));
        }
        static void Codec()
        {
            const int width=16,height=8;var values=new ushort[width*height];for(int i=0;i<values.Length;i++)values[i]=(ushort)((i*487+1)%65536);
            byte[] encoded=OrbisRasterCodec.Gray16(width,height,values);var decoded=OrbisRasterCodec.Read(encoded);
            Check(decoded.width==width&&decoded.height==height,"PNG dimensions");for(int i=0;i<values.Length;i++)Check(decoded.gray[i]==values[i],"16-bit roundtrip precision");
            byte[] rgba=new byte[width*height*4];for(int i=0;i<rgba.Length;i++)rgba[i]=(byte)((i*37)%256);
            var colors=OrbisRasterCodec.Read(OrbisRasterCodec.RGBA8(width,height,rgba));for(int i=0;i<rgba.Length;i++)Check(colors.rgba[i]==rgba[i],"RGBA roundtrip");
            encoded[encoded.Length/2]^=32;bool rejected=false;try{OrbisRasterCodec.Read(encoded);}catch(InvalidDataException){rejected=true;}Check(rejected,"Corrupt PNG must be rejected");
            string fixtures=Path.Combine(Application.dataPath,"../Validation/Fixtures");
            var f=OrbisRasterCodec.Read(File.ReadAllBytes(Path.Combine(fixtures,"gray16_filters.png")));
            for(int i=0;i<values.Length;i++)Check(f.gray[i]==values[i],"PNG filters 0–4");
            var bmp=OrbisRasterCodec.Read(File.ReadAllBytes(Path.Combine(fixtures,"indexed8.bmp")));
            var png=OrbisRasterCodec.Read(File.ReadAllBytes(Path.Combine(fixtures,"indexed8.png")));
            for(int i=0;i<bmp.rgba.Length;i++)Check(bmp.rgba[i]==png.rgba[i],"BMP palette/origin matches PNG");
            var source=OrbisMapIO.Example();var restored=OrbisWorldMap.Restore(source.Save());
            for(int i=0;i<100;i++){Vector3 p=OrbisWorldMap.Direction(i*9,i*3,1024,512);Check(Mathf.Abs(source.Sample(p)-restored.Sample(p))<1e-6f,"Embedded maps survive full-save roundtrip");}
        }
        static void Selection()
        {
            var grid=SphereGrid.CreateTiles(24);Check(grid.points.Length==3456,"Stable logical tile count");
            var counts=new Dictionary<ulong,int>();int a=0,b=grid.neighbors[a][0];
            foreach(int tile in new[]{a,b})foreach(var edge in Edges(grid.tileCorners[tile])){if(counts.ContainsKey(edge))counts[edge]++;else counts[edge]=1;}
            int external=0;foreach(var entry in counts)if(entry.Value==1)external++;Check(external==6,"Two adjacent quads must have six external edges, not eight");
        }
        static IEnumerable<ulong> Edges(int[] corners)
        {for(int i=0;i<corners.Length;i++){int a=corners[i],b=corners[(i+1)%corners.Length];yield return ((ulong)(uint)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);}}
        static void River()
        {
            OrbisWorldMap map=OrbisMapIO.Example();var world=new OrbisWorld("V4-RIVER-TEST");world.ImportWorldMap(map);
            var river=new OrbisRiverNetwork();river.Build(world,map.Sample);Check(river.reaches.Count>0,"Imported river is connected to render model");Check(river.MaxUpliftWarnings==0,"Example river must not float above its DEM");
            float minWidth=10,maxWidth=0;bool upland=false,mouth=false;foreach(var r in river.reaches)
            {
                Check(r.ha+2e-6f>=r.hb,"Water must not rise downstream");Check(r.wa>0&&r.wb>0,"Positive channel width");
                Vector3 mid=Vector3.Slerp(r.a,r.b,.5f);float raw=1+map.Sample(mid)*.075f,cut=river.Carve(mid,raw);
                Check(cut<=raw+1e-6f,"Carving must never raise terrain");
                minWidth=Mathf.Min(minWidth,r.wa);maxWidth=Mathf.Max(maxWidth,r.wb);if(r.ha>.1f)upland=true;if(r.mb>.9f&&Mathf.Abs(r.hb)<1e-5f)mouth=true;
            }
            Check(maxWidth>minWidth*1.2f,"Width varies with flow and authored hints");Check(upland,"Upland river must not be forced to sea level");Check(mouth,"A mouth reaches sea-level continuously");
            var globe=new Mesh();var atlas=new Mesh();try{river.FillMesh(globe,false);river.FillMesh(atlas,true);Check(globe.vertexCount>0&&atlas.vertexCount==globe.vertexCount*3,"Same reach model with atlas seam copies");}finally{UnityEngine.Object.DestroyImmediate(globe);UnityEngine.Object.DestroyImmediate(atlas);}
            var saved=OrbisSaveMigration.Read(JsonUtility.ToJson(world.Save()));var loaded=OrbisWorld.Restore(saved);
            Check(loaded.MapSource!=null&&loaded.tiles.Length==world.tiles.Length,"Version 4 full save keeps map source and tile topology");
        }
        static void Territory()
        {
            var world=new OrbisWorld("V4-NATION-TEST");int land=-1;for(int i=0;i<world.tiles.Length;i++)if(world.tiles[i].h>0&&world.tiles[i].lake<=.018f){land=i;break;}
            int civ=world.FoundNation(land,2);Check(civ>=0,"Region founding");world.UpdateStats();Check(Mathf.Abs(world.Population-500)<.01f,"Do not clone population for every owned tile");
            int owned=0;foreach(var t in world.tiles)if(t.owner==civ)owned++;Check(owned>0,"Founding claims dry land");
            int before=world.civs.Count;Check(world.FoundNation(land,2)<0&&world.civs.Count==before,"Cannot found over another nation");
            Check(!world.ClaimRegion(land,2,-1),"Cannot claim with invalid nation");
            float mass=0;float[] heights={.8f,.4f,.2f,.1f};foreach(float v in heights)mass+=v;
            OrbisTerrainEvolution.Relax(heights,new[]{new[]{1},new[]{0,2},new[]{1,3},new[]{2}},3,.05f);float after=0;foreach(float v in heights)after+=v;Check(Mathf.Abs(mass-after)<1e-5f,"Thermal relaxation conserves height sum");
        }
        static void Shaders()
        {
            foreach(string path in new[]{"ORBIS/Surface","ORBIS/RiverWater","ORBIS/Selection","ORBIS/SpriteWorld"})
            {Shader shader=Resources.Load<Shader>(path);Check(shader!=null,"Missing shader "+path);Check(!ShaderUtil.ShaderHasError(shader),"Shader compile errors: "+path);}
        }
    }
}
