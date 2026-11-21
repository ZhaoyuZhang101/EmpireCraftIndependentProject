using System;
using System.Collections.Generic;
using System.IO;
using Orbis.Flat;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
    public static class RiverAtlasRegression
    {
        // Every root sees the same signed river-bank distance. The screen
        // receiver can be dry, including when relief has moved its foliage.
        internal static Texture2D BindUniformBank(Material material,int width,int height,float edge)
        {
            var texture=new Texture2D(width,height+4,TextureFormat.RGBAFloat,false,true);
            var header=new Color[width*height];
            for(int i=0;i<header.Length;i++)header[i]=new Color(1,0,0,0);
            texture.SetPixels(0,0,width,height,header);
            WriteUniformBank(texture,edge);
            material.SetTexture("_RiverAtlas",texture);
            material.SetVector("_RiverAtlasLayout",new Vector4(width,height+4,4,height));
            return texture;
        }
        internal static void WriteUniformBank(Texture2D texture,float edge)
        {
            var bank=new Color[16];
            for(int i=0;i<bank.Length;i++)bank[i]=new Color(edge,1,.1f,0);
            texture.SetPixels(0,texture.height-4,4,4,bank);texture.Apply();
        }

        // The old sampling checks run at the atlas lattice nodes. These points
        // lie between nodes on real Chinese rivers, including their confluences.
        public static void CheckSurveyFootprint(FlatMapRenderer map)
        {
            var curves=map.World.Facts.RiverCurves;
            if(!map.RiverAtlasReady||!curves.HasSurvey)throw new InvalidOperationException("Expected a published Earth river field.");
            var positions=new List<Vector2>();var expected=new List<FlatRiverSample>();
            var joins=new List<Vector2>();
            int measuredSections=0;
            void AddControl(Vector2 p)
            {
                var sample=curves.Sample(p.x,p.y);
                if(sample.HalfWidth<=0||Mathf.Abs(sample.Distance)>1.5f*sample.HalfWidth)return;
                // Two equally distant branches can pick different flow/depth
                // at their medial boundary after finite-precision upload.
                float step=Mathf.Min(.002f,sample.HalfWidth*.15f);
                var a=curves.Sample(p.x-step,p.y+step);var b=curves.Sample(p.x+step,p.y-step);
                if(Mathf.Abs(a.HalfWidth-b.HalfWidth)>.001f||a.FlowX*b.FlowX+a.FlowY*b.FlowY<.94f)return;
                positions.Add(p);expected.Add(sample);
            }
            for(int index=0;index<curves.ActiveTileCount;index++)
            {
                int tile=curves.GetActiveTile(index),x=tile%curves.Width,y=tile/curves.Width;
                double lon=(x+.5)/curves.Width*360-180,lat=(y+.5)/curves.Height*180-90;
                if(lon<100||lon>123||lat<20||lat>41)continue;
                for(int j=0;j<5;j++)for(int i=0;i<5;i++)
                    AddControl(new Vector2(x+(i+.37f)/5,y+(j+.63f)/5));
                // GRWL reaches can be narrower than the off-lattice grid.
                // Probe both banks at each actual Chinese segment's midpoint.
                for(int j=0;j<curves.TileSegmentCount(tile);j++)
                {
                    var edge=curves.TileSegment(tile,j);
                    var mid=new Vector2((edge.A.X+edge.B.X)*.5f,(edge.A.Y+edge.B.Y)*.5f);
                    if((int)mid.x!=x||(int)mid.y!=y)continue;
                    int id=curves.TileSegmentIndex(tile,j);
                    if(id>0)
                    {
                        var previousSegment=curves.SegmentAt(id-1);
                        var station=new Vector2(edge.A.X,edge.A.Y);
                        var previousEnd=new Vector2(previousSegment.B.X,previousSegment.B.Y);
                        if(previousSegment.Reach==edge.Reach&&(station-previousEnd).sqrMagnitude<1e-8f&&
                            edge.A.FlowX*previousSegment.B.FlowX+edge.A.FlowY*previousSegment.B.FlowY>.995f)
                        {
                            float epsilon=Mathf.Clamp(edge.A.Radius*.005f,.0003f,.002f);
                            joins.Add(Vector2.MoveTowards(station,new Vector2(previousSegment.A.X,previousSegment.A.Y),epsilon));
                            joins.Add(Vector2.MoveTowards(station,new Vector2(edge.B.X,edge.B.Y),epsilon));
                        }
                    }
                    float metric=Mathf.Max(.08f,Mathf.Cos((mid.y/curves.Height-.5f)*Mathf.PI));
                    var perpendicular=new Vector2(edge.A.Y-edge.B.Y,(edge.B.X-edge.A.X)*metric).normalized;
                    perpendicular.x/=metric;
                    perpendicular*=(edge.A.Radius+edge.B.Radius)*.5f;
                    AddControl(mid);AddControl(mid+perpendicular*.8f);AddControl(mid-perpendicular*.8f);
                    AddControl(mid+perpendicular*1.2f);AddControl(mid-perpendicular*1.2f);
                    measuredSections++;
                }
            }
            if(measuredSections<100||positions.Count<12)throw new Exception("Insufficient sub-cell Chinese river controls.");
            const int width=256;int height=(positions.Count+width-1)/width;
            var points=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};
            var pixels=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
            var material=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/RiverAtlasProbe.shader"));
            var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;
            try
            {
                var input=new Color[width*height];
                for(int i=0;i<positions.Count;i++)input[i]=new Color(positions[i].x,positions[i].y,0,0);
                points.SetPixels(input);points.Apply();
                material.SetTexture("_ProbeCells",points);material.SetFloat("_ProbePoints",1);
                material.SetTexture("_RiverAtlas",map.RiverAtlasTexture);
                material.SetVector("_RiverAtlasLayout",map.Material.GetVector("_RiverAtlasLayout"));
                material.SetFloat("_RiverAtlasReady",1);
                material.SetVector("_MapSize",new Vector4(curves.Width,curves.Height,1f/curves.Width,1f/curves.Height));
                ShaderUtil.CompilePass(material,0,true);
                Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();
                var output=pixels.GetPixels();float edgeError=0,widthError=0;int lost=0;
                for(int i=0;i<positions.Count;i++)
                {
                    Color value=output[i];var sample=expected[i];
                    float radius=new Vector2(value.b,value.a).magnitude;
                    edgeError=Mathf.Max(edgeError,Mathf.Abs(value.r-sample.Distance));
                    widthError=Mathf.Max(widthError,Mathf.Abs(radius-sample.HalfWidth));
                    if(sample.Distance<-Mathf.Max(.00004f,sample.HalfWidth*.05f)&&value.r>=0)lost++;
                }
                string result=$"China off-lattice river sections, grid {curves.Width}, segments {measuredSections}, points {positions.Count}, lost wet sites {lost}, max bank error {edgeError:G5}, half-width error {widthError:G5}";
                if(!(edgeError<.003f&&widthError<.003f&&lost==0))throw new Exception(result);
                Debug.Log("PASS "+result);
                int pairs=Math.Min(joins.Count,input.Length)/2;
                for(int i=0;i<2*pairs;i++)input[i]=new Color(joins[i].x,joins[i].y,0,0);
                points.SetPixels(input);points.Apply();material.SetFloat("_ProbeBankProfile",1);
                Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();
                var banks=pixels.GetPixels();var differences=new List<float>();int turning=0;
                for(int i=0;i<pairs;i++)
                {
                    var a=banks[2*i];var b=banks[2*i+1];
                    // Adjacent centreline sites can belong to different channels
                    // at a confluence. Require comparable widths at this join.
                    if(a.a<=0||b.a<=0||Mathf.Abs(a.a-b.a)>.03f*Math.Max(a.a,b.a)||
                        Mathf.Abs(a.b+a.a)>.04f*a.a||Mathf.Abs(b.b+b.a)>.04f*b.a)continue;
                    if(!(Mathf.Abs(a.g)<=1.001f&&Mathf.Abs(b.g)<=1.001f))throw new Exception("Invalid station curvature");
                    if(Mathf.Abs(a.g)>.05f)turning++;
                    differences.Add(Mathf.Abs(a.g-b.g));
                }
                differences.Sort();
                if(differences.Count<100||turning<20)throw new Exception("Insufficient curved survey station controls");
                float p99=differences[(int)(differences.Count*.99f)];
                string continuity=$"China point bars across {differences.Count} station joins, {turning} curved, p99 signed bend jump {p99:G5}";
                if(p99>.065f)throw new Exception(continuity);
                Debug.Log("PASS "+continuity);
            }
            finally
            {
                RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
                foreach(var obj in new UnityEngine.Object[]{points,pixels,material})UnityEngine.Object.DestroyImmediate(obj);
            }
        }
        public static void CheckPagedSeasons()
        {
            const int width=128,height=64;
            var world=FlatGenerator.Generate(width,height,"RIVER-SEASON-PAGES",TerrainPreset.Continents,default);
            var seasons=world.Facts.RiverCurves.Seasons;
            if(seasons==null)throw new InvalidOperationException("Procedural test world has no seasonal rivers.");
            // Give every channel and tile its own signal, including our seam and polar probes.
            for(int month=0;month<12;month++)
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                seasons.Packed[(month/4*world.Count+y*width+x)*4+month%4]=(byte)(17+(x*7+y*19+month*29)%211);
            var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/RiverAtlasProbe.shader");
            var probe=new Material(shader);
            var target=RenderTexture.GetTemporary(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var image=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            var previous=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;
            var positions=new[]{new Vector2(-.25f,17.75f),new Vector2(width-.25f,17.75f),
                new Vector2(33.5f,-.75f),new Vector2(width-1.5f,height+.25f),new Vector2(71.125f,24.25f)};
            float worst=0;int samples=0;
            float ReadStage(Vector2 point)
            {
                probe.SetVector("_ProbeOrigin",new Vector4(point.x,point.y,0,0));
                Graphics.Blit(Texture2D.whiteTexture,target,probe);RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,1,1),0,0);image.Apply();
                return image.GetPixel(0,0).r;
            }
            try
            {
                ShaderUtil.allowAsyncCompilation=false;ShaderUtil.CompilePass(probe,0,true);
                probe.SetVector("_MapSize",new Vector4(width,height,1f/width,1f/height));
                probe.SetFloat("_ProbeStage",1);probe.SetFloat("_ProbePoints",0);probe.SetVector("_ProbeSpan",Vector4.zero);
                foreach(int limit in new[]{width,width*2})
                {
                    var root=new GameObject("River season texture limit regression");
                    try
                    {
                        var map=root.AddComponent<FlatMapRenderer>();
                        var bindWorld=typeof(FlatMapRenderer).GetMethod("BindWorld",System.Reflection.BindingFlags.NonPublic|
                            System.Reflection.BindingFlags.Instance);
                        bindWorld.Invoke(map,new object[]{world,limit});
                        map.SetVegetationDay(0,probe);
                        bool paged=limit==width;
                        var texture=map.RiverSeasonTexture;
                        if(!texture||texture.width!=width||texture.height!=(paged?2:3)*height||texture.height>limit)
                            throw new Exception("River season texture exceeds the configured dimensions.");
                        for(int pass=0;pass<=12;pass++)
                        {
                            int month=pass%12,panel=month/4;double day=(pass+.625)*30;
                            map.SetVegetationDay(day,probe);
                            var raw=texture.GetPixelData<byte>(0);int bytes=world.Count*4;
                            for(int bank=0;bank<(paged?2:3);bank++)
                            {
                                int source=(paged?(panel+bank)%3:bank)*bytes;
                                for(int j=0;j<bytes;j++)
                                    if(raw[bank*bytes+j]!=seasons.Packed[source+j])throw new Exception("Incorrect river month bank contents.");
                            }
                            foreach(var point in positions)
                            {
                                float actual=ReadStage(point),expected=seasons.Ratio(point.x,point.y,day);
                                float error=Math.Abs(actual-expected);worst=Math.Max(worst,error);samples++;
                                // The RGBA8 field stores ratios in steps of 1/60. Filtered sites can round one sample unit.
                                float tolerance=point.x%1==.5f?.0001f:1f/60+.0001f;
                                if(!(error<tolerance))throw new Exception($"River stage mismatch at {point}, month {month}, {actual} vs {expected}.");
                            }
                            // An unsubmitted CPU-only change must survive a repeat bind, while the GPU keeps its original byte.
                            int channel=(paged?0:panel*bytes)+month%4;
                            byte original=raw[channel],marker=(byte)(original^128);
                            raw[channel]=marker;
                            map.SetVegetationDay(pass*30,probe);
                            if(texture.GetPixelData<byte>(0)[channel]!=marker||Math.Abs(ReadStage(new Vector2(.5f,.5f))-original/60f)>.0001f)
                                throw new Exception("An unchanged river month bank was recopied or uploaded.");
                            raw=texture.GetPixelData<byte>(0);raw[channel]=original;
                        }
                    }
                    finally
                    {
                        var map=root.GetComponent<FlatMapRenderer>();
                        if(map&&map.RiverSeasonTexture)UnityEngine.Object.DestroyImmediate(map.RiverSeasonTexture);
                        UnityEngine.Object.DestroyImmediate(root);
                    }
                }
                Debug.Log($"PASS river season textures at two device limits, {samples} GPU samples across all twelve months, maximum error {worst:G6}");
            }
            finally
            {
                RenderTexture.active=previous;ShaderUtil.allowAsyncCompilation=async;
                RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(probe);
            }
        }
        [MenuItem("Tools/ORBIS/Local Planet/Validate uploaded continuous rivers")]
        public static void Run()
        {
            var map=UnityEngine.Object.FindFirstObjectByType<FlatMapRenderer>(FindObjectsInactive.Include);
            if(map==null||!map.RiverAtlasReady)
                throw new InvalidOperationException("Run a world and wait for its river field to finish uploading.");
            var curves=map.World.Facts.RiverCurves;
            var positions=new List<Vector2>();
            int shiftedLand=0;
            for(int k=0;k<curves.ActiveTileCount;k++)
            {
                int tile=curves.GetActiveTile(k),x=tile%curves.Width,y=tile/curves.Width;
                foreach(float dx in new[]{0f,.125f,.5f,.875f,1f})
                foreach(float dy in new[]{0f,.5f,1f})
                    positions.Add(new Vector2(x+dx,Mathf.Min(curves.Height-.001f,y+dy)));
                if(map.World.Water[tile]==WaterKind.Land)shiftedLand++;
            }
            // Identical wrapped and clamped coordinates exercise both addressing edges.
            foreach(float x in new[]{-.125f,0f,curves.Width-.125f,(float)curves.Width})
            foreach(float y in new[]{-.001f,.001f,curves.Height-.001f,curves.Height+.001f})
                positions.Add(new Vector2(x,y));
            const int width=256;
            int height=Mathf.CeilToInt(positions.Count/(float)width);
            var points=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true)
            {filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            var image=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
            var material=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/RiverAtlasProbe.shader"));
            var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;
            bool async=ShaderUtil.allowAsyncCompilation;
            var report=new List<string>();
            try
            {
                var pixels=new Color[width*height];
                for(int k=0;k<positions.Count;k++)pixels[k]=new Color(positions[k].x,positions[k].y,0,0);
                points.SetPixels(pixels);points.Apply();
                material.SetTexture("_ProbeCells",points);material.SetFloat("_ProbePoints",1);
                material.SetTexture("_VisualTex",map.VisualTexture);
                material.SetTexture("_RiverAtlas",map.RiverAtlasTexture);
                material.SetVector("_RiverAtlasLayout",map.Material.GetVector("_RiverAtlasLayout"));
                material.SetFloat("_RiverAtlasReady",1);
                material.SetVector("_MapSize",new Vector4(curves.Width,curves.Height,1f/curves.Width,1f/curves.Height));
                ShaderUtil.allowAsyncCompilation=false;ShaderUtil.CompilePass(material,0,true);
                Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();pixels=image.GetPixels();
                float distanceError=0,depthError=0,relativeDepthError=0,flowWidthError=0;
                string depthDetail="";
                int invalid=0,wetSamples=0;
                for(int k=0;k<positions.Count;k++)
                {
                    Vector2 point=positions[k];
                    var expected=curves.Sample(point.x,Mathf.Clamp(point.y,0,curves.Height-.0001f));
                    Color actual=pixels[k];
                    if(float.IsNaN(actual.r+actual.g+actual.b+actual.a)||float.IsInfinity(actual.r+actual.g+actual.b+actual.a))invalid++;
                    distanceError=Mathf.Max(distanceError,Mathf.Abs(actual.r-expected.Distance));
                    // At polar clamp points the address can fall between raster nodes.
                    if(point.y<0||point.y>=curves.Height-.01f)continue;
                    float depthDifference=Mathf.Abs(actual.g-expected.Depth);
                    if(depthDifference>depthError)
                    {
                        depthError=depthDifference;
                        depthDetail=", deepest error at "+point+" CPU="+expected.Depth+" GPU="+actual.g;
                    }
                    relativeDepthError=Mathf.Max(relativeDepthError,depthDifference/Mathf.Max(1,expected.Depth));
                    flowWidthError=Mathf.Max(flowWidthError,Mathf.Abs(actual.b-expected.FlowX*expected.HalfWidth),Mathf.Abs(actual.a-expected.FlowY*expected.HalfWidth));
                    if(expected.Distance<0)wetSamples++;
                }
                report.Add((distanceError<.006f&&relativeDepthError<.0007f&&flowWidthError<.001f&&invalid==0&&wetSamples>0?"PASS ":"FAIL ")+
                    "GPU uploaded field matches global CPU geometry, samples="+positions.Count+", wet="+wetSamples+", active land receivers="+shiftedLand+
                    ", distanceError="+distanceError+", depthError="+depthError+", relativeDepthError="+relativeDepthError+", tangentWidthError="+flowWidthError+", invalid="+invalid+depthDetail);
                if(curves.Seasons!=null)
                {
                    material.SetTexture("_RiverSeasonTex",map.RiverSeasonTexture);
                    material.SetFloat("_RiverSeasonReady",1);
                    int seasonalSamples=0,changed=0;float bankError=0,depthFraction=0,widthError=0;
                    // Include interpolation between December and January.
                    foreach(double day in new[]{30.0,136.5,345.0})
                    {
                        map.SetVegetationDay(day,material);
                        Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;
                        image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();var seasonalPixels=image.GetPixels();
                        for(int k=0;k<positions.Count;k++)
                        {
                            var point=positions[k];
                            if(point.y<.01f||point.y>curves.Height-.01f||pixels[k].r>.18f||pixels[k].g<=0)continue;
                            var cpu=curves.SampleAtDay(point.x,point.y,day);var gpu=seasonalPixels[k];
                            bankError=Mathf.Max(bankError,Mathf.Abs(gpu.r-cpu.Distance));
                            depthFraction=Mathf.Max(depthFraction,Mathf.Abs(gpu.g-cpu.Depth)/Mathf.Max(1,cpu.Depth));
                            widthError=Mathf.Max(widthError,Mathf.Abs(new Vector2(gpu.b,gpu.a).magnitude-cpu.HalfWidth));
                            if(Mathf.Abs(gpu.r-pixels[k].r)>.003f)changed++;
                            seasonalSamples++;
                        }
                    }
                    report.Add((seasonalSamples>0&&changed>0&&bankError<.006f&&depthFraction<.002f&&widthError<.001f?"PASS ":"FAIL ")+
                        "seasonal GPU water matches CPU banks, widths and depths, samples="+seasonalSamples+", changed="+changed+
                        ", bankError="+bankError+", widthError="+widthError+", relativeDepthError="+depthFraction);
                }
                int clones=0;
                foreach(var candidate in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if(candidate==map.Material||candidate.shader==null)continue;
                    string shader=candidate.shader.name;
                    if(shader!="ORBIS/FlatWorld/LocalSurface"&&shader!="ORBIS/FlatWorld/ScrollSurface")continue;
                    if(candidate.GetTexture("_VisualTex")!=map.VisualTexture)continue;
                    clones++;
                    report.Add((candidate.GetTexture("_RiverAtlas")==map.RiverAtlasTexture&&candidate.GetFloat("_RiverAtlasReady")>.5f?"PASS ":"FAIL ")+candidate.name+" shares the published river field");
                }
                report.Add((clones>0?"PASS ":"FAIL ")+"navigation surface bindings checked, materials="+clones);
                report.Add("INFO atlas="+map.RiverAtlasTexture.width+"x"+map.RiverAtlasTexture.height+", format="+map.RiverAtlasTexture.format+", readable="+map.RiverAtlasTexture.isReadable);
                if(report.Exists(line=>line.StartsWith("FAIL")))throw new InvalidOperationException(string.Join("\n",report));
            }
            finally
            {
                map.SetVegetationDay(map.World.Day);
                Directory.CreateDirectory("ValidationOutput/RiverAtlas");
                File.WriteAllLines("ValidationOutput/RiverAtlas/river-atlas-regression.txt",report);
                RenderTexture.active=previous;ShaderUtil.allowAsyncCompilation=async;RenderTexture.ReleaseTemporary(target);
                foreach(var resource in new UnityEngine.Object[]{points,image,material})UnityEngine.Object.DestroyImmediate(resource);
            }
        }
    }
}
