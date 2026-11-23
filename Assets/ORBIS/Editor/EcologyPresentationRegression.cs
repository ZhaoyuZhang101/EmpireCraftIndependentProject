using System;
using System.Collections.Generic;
using System.IO;
using Orbis.Flat;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
    public static class EcologyPresentationRegression
    {
        [MenuItem("Tools/ORBIS/Local Planet/Validate ecology presentation data")]
        public static void Run()
        {
            var report=new List<string>();
            try
            {
                var ecologyField=typeof(FlatMapRenderJobs.Pixel).GetField("Ecology");
                Check(report,ecologyField!=null,"render jobs publish derived ecology metadata");
                var defaults=Resources.LoadAll<TextAsset>("ORBIS/WorldAtlas");
                var terrain=Array.Find(defaults,asset=>asset.name=="Default"&&FlatAtlasTerrain.HasSignature(asset.bytes));
                Check(report,terrain!=null&&AssetDatabase.GetAssetPath(terrain).EndsWith(".bytes"),"default terrain selection rejects same-name JSON metadata");
                Check(report,!FlatAtlasTerrain.HasSignature(new byte[]{79,65,84,49})&&!FlatAtlasTerrain.HasSignature(null),"truncated and missing atlas data fail signature validation");
                var world=FlatGenerator.Generate(64,32,"ECOLOGY-PRESENTATION",TerrainPreset.Continents,default);
                byte[] before=FlatSave.Encode(world);
                var first=new Color32[world.Count];
                using(var jobs=new FlatMapRenderJobs(world))
                {
                    for(int pass=0;pass<2;pass++)
                    {
                        world.MarkAllDirty();
                        jobs.Schedule(pass==0?MapLayer.Natural:MapLayer.Nations,true);
                        jobs.CompleteInitialWarmup();
                        if(!jobs.TryComplete(world,pass==0?MapLayer.Natural:MapLayer.Nations,out bool accepted)||!accepted)
                            throw new InvalidOperationException("Ecology render batch failed to publish.");
                        uint seed=FMath.Seed(world.Seed);
                        bool correct=true,stable=true;
                        foreach(var chunk in jobs.Chunks)
                        for(int y=0;y<chunk.Height;y++)for(int x=0;x<chunk.Width;x++)
                        {
                            int tile=(chunk.Y+y)*world.Width+chunk.X+x;
                            var packed=(Color32)ecologyField.GetValue(jobs.GetPixel(chunk.Offset+y*chunk.Width+x));
                            correct&=packed.r==(byte)world.Facts.Vegetation[tile]&&(packed.g&15)==(byte)world.Biomes[tile];
                            correct&=(packed.g>>4)-1==(int)world.Facts.Trees[tile];
                            correct&=Math.Abs(packed.b/255f-world.Facts.Wetness[tile])<=1f/255;
                            correct&=packed.a==(byte)(FMath.Hash((uint)tile^seed)&255);
                            if(pass==0)first[tile]=packed;
                            else stable&=packed.Equals(first[tile]);
                        }
                        Check(report,correct,"cover, biome, climate-suitable tree, wetness and seed match source values, pass="+pass);
                        if(pass>0)Check(report,stable,"visual traits stay stable across overlay and publication changes");
                    }
                    CheckClimateLayers(world,jobs,report);
                }
                CheckVegetationSeasons(world,report);
                CheckTreeMetadata(world,report);
                byte[] after=FlatSave.Encode(world);
                bool unchanged=before.Length==after.Length;
                for(int i=0;unchanged&&i<before.Length;i++)unchanged=before[i]==after[i];
                Check(report,unchanged,"ecology presentation preserves serialized world state byte-for-byte");
            }
            finally
            {
                Directory.CreateDirectory("ValidationOutput/Ecology");
                File.WriteAllLines("ValidationOutput/Ecology/presentation-data.txt",report);
            }
        }

        static void CheckTreeMetadata(WorldData world,List<string> report)
        {
            var root=new GameObject("Koppen tree metadata regression");
            var material=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/VegetationArtProbe.shader"));
            var image=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            var target=RenderTexture.GetTemporary(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;
            try
            {
                var map=root.AddComponent<FlatMapRenderer>();map.Initialize(world);
                material.SetTexture("_EcologyTex",map.EcologyTexture);material.SetTexture("_InfoTex",map.InfoTexture);
                material.SetVector("_MapSize",new Vector4(world.Width,world.Height,1f/world.Width,1f/world.Height));
                material.SetFloat("_EcologyReady",1);material.SetFloat("_ProbeMode",2);
                ShaderUtil.CompilePass(material,0,true);
                foreach(int tile in new[]{0,17,world.Width-1,world.Count/2,world.Count-1})
                {
                    material.SetVector("_ProbeRoot",new Vector4(tile%world.Width,tile/world.Width,0,0));
                    Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;
                    image.ReadPixels(new Rect(0,0,1,1),0,0);image.Apply();var value=image.GetPixel(0,0);
                    Check(report,Mathf.Abs(value.r-(int)world.Facts.Trees[tile])<.001f&&
                        Mathf.Abs(value.b-value.r)<.001f&&Mathf.Abs(value.g-(int)world.Biomes[tile])<.001f,
                        "rendered tree selection decodes Koppen-derived type and original biome, tile="+tile);
                }
                material.SetFloat("_EcologyReady",0);
                Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,1,1),0,0);image.Apply();
                Check(report,image.GetPixel(0,0).b<0,"older rendering data keeps the climate-based fallback path");
            }
            finally
            {
                RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(root);
            }
        }
        static void CheckClimateLayers(WorldData world,FlatMapRenderJobs jobs,List<string> report)
        {
            foreach(var layer in new[]{MapLayer.Temperature,MapLayer.Rain})
            {
                var current=new Color32[world.Count];int changed=0;
                for(int year=0;year<=2;year+=2)
                {
                    world.MarkAllDirty();jobs.Schedule(layer,false,year);jobs.CompleteInitialWarmup();
                    Check(report,jobs.TryComplete(world,layer,out bool valid,year)&&valid,"climate map publishes the requested year "+year);
                    for(int i=0;i<world.Count;i++)
                    {
                        var color=jobs.GetPixel(i).Display;
                        if(year==0)current[i]=color;else if(!color.Equals(current[i]))changed++;
                    }
                }
                Check(report,changed>world.Count/4,layer+" shows regional changes in the same month two years later");
                world.MarkAllDirty();jobs.Schedule(layer,false,2);jobs.CompleteInitialWarmup();
                Check(report,jobs.TryComplete(world,layer,out bool stale,0)&&!stale,"an old preview cannot replace the requested current climate");
            }
        }
        static void CheckVegetationSeasons(WorldData world,List<string> report)
        {
            var root=new GameObject("Climate vegetation cache regression");
            var probe=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/TerrainQualityProbe.shader"));
            var treeProbe=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/VegetationDiversityProbe.shader"));
            var target=RenderTexture.GetTemporary(64,64,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var image=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
            var previous=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;ShaderUtil.CompilePass(probe,0,true);ShaderUtil.CompilePass(treeProbe,0,true);
                var map=root.AddComponent<FlatMapRenderer>();map.Initialize(world);
                var texture=map.VegetationTexture;
                Check(report,texture&&texture.width==(world.Width+2)*3&&texture.height==(world.Height+2)*4,"all vegetation seasons share one padded GPU cache");
                int identity=texture.GetInstanceID();uint updates=texture.updateCount;double maximumError=0;
                foreach(int tile in new[]{0,world.Width-1,world.Count/2+17,world.Count-1})
                for(int month=0;month<12;month++)foreach(double fraction in new[]{.001,.5,.999})
                {
                    double day=(month+fraction)*30+(month%3)*360;
                    map.SetVegetationDay(day);probe.CopyPropertiesFromMaterial(map.Material);
                    probe.SetFloat("_ProbeMode",20);probe.SetVector("_ProbeOrigin",new Vector4(tile%world.Width+.5f,tile/world.Width+.5f,0,0));
                    probe.SetVector("_ProbeSpan",new Vector4(.000001f,.000001f,0,0));
                    Graphics.Blit(Texture2D.whiteTexture,target,probe);RenderTexture.active=target;
                    image.ReadPixels(new Rect(0,0,64,64),0,0);image.Apply();Color sample=image.GetPixel(32,32);
                    FlatEcology.SampleSurfaceSeason(world,tile,day,out float growth,out float foliage,out float snow,out float soil);
                    maximumError=Math.Max(maximumError,Math.Max(Math.Abs(sample.r-growth),Math.Abs(sample.g-foliage)));
                    maximumError=Math.Max(maximumError,Math.Max(Math.Abs(sample.b-snow),Math.Abs(sample.a-soil)));
                }
                Check(report,maximumError<.002,"GPU growth, foliage, snow and soil match local climate through every month and year wrap; maximum error="+maximumError);
                Check(report,texture.GetInstanceID()==identity&&texture.updateCount==updates,"calendar changes reuse vegetation texture data without uploads");
                map.SetVegetationDay(0);probe.CopyPropertiesFromMaterial(map.Material);probe.SetFloat("_ProbeMode",6);
                probe.SetVector("_ProbeSpan",new Vector4(.000001f,.000001f,0,0));
                probe.SetVector("_ProbeOrigin",new Vector4(-.00001f,world.Height*.45f,0,0));
                Graphics.Blit(Texture2D.whiteTexture,target,probe);RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,64,64),0,0);image.Apply();Color seam=image.GetPixel(32,32);
                probe.SetVector("_ProbeOrigin",new Vector4(world.Width-.00001f,world.Height*.45f,0,0));
                Graphics.Blit(Texture2D.whiteTexture,target,probe);RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,64,64),0,0);image.Apply();Color wrapped=image.GetPixel(32,32);
                Check(report,Math.Abs(seam.r-wrapped.r)+Math.Abs(seam.g-wrapped.g)<.001,"seasonal vegetation remains continuous at the geographic seam");
                probe.SetFloat("_VegetationSeasonReady",0);
                Graphics.Blit(Texture2D.whiteTexture,target,probe);RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,64,64),0,0);image.Apply();Color legacy=image.GetPixel(32,32);
                Check(report,legacy.r==1&&legacy.g==1,"renderers without a seasonal cache preserve their existing vegetation");
                treeProbe.SetFloat("_ProbeMode",9);treeProbe.SetFloat("_ProbeType",0);treeProbe.SetFloat("_ProbeSeed",.37f);
                treeProbe.SetVector("_ProbeSun",new Vector3(-.5f,.4f,.8f).normalized);
                double summerCrown=0,winterCrown=0,summerShadow=0,winterShadow=0;
                foreach(float foliage in new[]{1f,.05f})
                {
                    treeProbe.SetFloat("_ProbeFoliage",foliage);Graphics.Blit(Texture2D.whiteTexture,target,treeProbe);RenderTexture.active=target;
                    image.ReadPixels(new Rect(0,0,64,64),0,0);image.Apply();double crown=0,shadow=0;
                    foreach(var color in image.GetPixels()){crown+=color.r;shadow+=color.g;}
                    if(foliage>.5f){summerCrown=crown;summerShadow=shadow;}else{winterCrown=crown;winterShadow=shadow;}
                }
                Check(report,summerCrown>winterCrown*1.5&&summerShadow>winterShadow*1.5,"seasonal leaf growth changes canopy and its geometric shadow together");
                var originalFacts=world.Facts;int revision=world.TerrainRevision;
                try
                {
                    var replacement=originalFacts.Clone();
                    int offset=FlatEcology.SeasonOffset(world,17,0);
                    replacement.VegetationSeasons[offset]^=255;
                    world.Facts=replacement;world.TerrainRevision++;
                    map.SetVegetationDay(0);
                    Check(report,map.Material.GetFloat("_VegetationSeasonReady")==1&&texture.updateCount==updates,"a pending terrain revision retains its published seasonal vegetation");
                    map.Flush(true);
                    var jobs=(FlatMapRenderJobs)typeof(FlatMapRenderer).GetField("renderJobs",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(map);
                    jobs.CompleteInitialWarmup();map.Flush();
                    Check(report,map.VegetationTexture.GetInstanceID()==identity&&texture.GetRawTextureData()[offset]==replacement.VegetationSeasons[offset],"completed terrain data refreshes the seasonal cache in place");
                    map.SetVegetationDay(0);
                    probe.CopyPropertiesFromMaterial(map.Material);probe.SetFloat("_ProbeMode",6);
                    probe.SetVector("_ProbeOrigin",new Vector4(17.5f,.5f,0,0));
                    probe.SetVector("_ProbeSpan",new Vector4(.000001f,.000001f,0,0));
                    Graphics.Blit(Texture2D.whiteTexture,target,probe);RenderTexture.active=target;
                    image.ReadPixels(new Rect(0,0,64,64),0,0);image.Apply();
                    FlatEcology.SampleSeason(world,17,0,out float publishedGrowth,out _);
                    Check(report,Math.Abs(image.GetPixel(32,32).r-publishedGrowth)<.002,"published seasonal revisions reach the rendered GPU data");
                }
                finally{world.Facts=originalFacts;world.TerrainRevision=revision;}
            }
            finally
            {
                RenderTexture.active=previous;ShaderUtil.allowAsyncCompilation=async;RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(probe);UnityEngine.Object.DestroyImmediate(treeProbe);UnityEngine.Object.DestroyImmediate(root);
            }
        }

        public static void CheckLargeSeasonTexture(FlatMapRenderer map)
        {
            var world=map.World;
            var texture=map.VegetationTexture;
            if(world.Width!=4096||!texture||texture.width!=world.Width||texture.height!=world.Height*2)
                throw new InvalidOperationException("The 4096 world must retain two full-resolution vegetation months on the GPU.");
            var probe=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/TerrainQualityProbe.shader"));
            var target=RenderTexture.GetTemporary(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var image=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            var previous=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;
            Color ReadTile(int tile)
            {
                probe.SetVector("_ProbeOrigin",new Vector4(tile%world.Width+.5f,tile/world.Width+.5f,0,0));
                Graphics.Blit(Texture2D.whiteTexture,target,probe);RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,1,1),0,0);
                return image.GetPixel(0,0);
            }
            try
            {
                ShaderUtil.allowAsyncCompilation=false;ShaderUtil.CompilePass(probe,0,true);
                foreach(int month in new[]{0,1,6,11})
                {
                    double day=(month+.42)*30;
                    map.SetVegetationDay(day);
                    int channel=(month%2)*world.Count*4;
                    var raw=texture.GetPixelData<byte>(0);
                    byte original=raw[channel],marker=(byte)(original^128);
                    raw[channel]=marker;
                    try
                    {
                        // The CPU marker must survive this same-pair bind without reaching the GPU.
                        map.SetVegetationDay(month*30);probe.CopyPropertiesFromMaterial(map.Material);
                        probe.SetFloat("_ProbeMode",20);probe.SetVector("_ProbeSpan",new Vector4(.000001f,.000001f,0,0));
                        probe.SetVector("_ClimateYearHeat",Vector4.zero);probe.SetVector("_ClimateYearRain",Vector4.zero);
                        if(texture.GetPixelData<byte>(0)[channel]!=marker||Math.Abs(ReadTile(0).r-original/255f)>.0001f)
                            throw new Exception("An unchanged vegetation month pair was recopied or uploaded.");
                    }
                    finally { raw=texture.GetPixelData<byte>(0);raw[channel]=original; }
                    map.SetVegetationDay(day);probe.CopyPropertiesFromMaterial(map.Material);
                    probe.SetFloat("_ProbeMode",20);probe.SetVector("_ProbeSpan",new Vector4(.000001f,.000001f,0,0));
                    foreach(int tile in new[]{0,world.Width-1,world.Index(2458,1558),world.Count-1})
                    {
                        var sample=ReadTile(tile);
                        FlatEcology.SampleSurfaceSeason(world,tile,day,out float growth,out float foliage,out float snow,out float soil);
                        if(Math.Abs(sample.r-growth)>.003||Math.Abs(sample.g-foliage)>.003||
                           Math.Abs(sample.b-snow)>.003||Math.Abs(sample.a-soil)>.003)
                            throw new Exception($"4096 vegetation texture differs at tile {tile}, month {month}.");
                    }
                }
            }
            finally
            {
                map.SetVegetationDay(world.Day);
                RenderTexture.active=previous;ShaderUtil.allowAsyncCompilation=async;
                RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(probe);
            }
        }
        static void Check(List<string> report,bool passed,string description)
        {
            report.Add((passed?"PASS ":"FAIL ")+description);
            if(!passed)throw new InvalidOperationException(description);
        }
    }
}
