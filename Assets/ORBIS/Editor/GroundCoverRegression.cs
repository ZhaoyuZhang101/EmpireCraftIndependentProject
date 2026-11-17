using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
    public static class GroundCoverRegression
    {
        [MenuItem("Tools/ORBIS/Local Planet/Validate ecological ground dressing")]
        public static void Run()
        {
            const int width=256,height=128;
            var lines=new List<string>();
            var material=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/GroundCoverProbe.shader"));
            var ecology=new Texture2D(16,8,TextureFormat.RGBA32,false,true){filterMode=FilterMode.Point,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};
            var image=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
            var field=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            var wind=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;
            bool async=ShaderUtil.allowAsyncCompilation;
            try
            {
                material.SetTexture("_EcologyTex",ecology);material.SetFloat("_EcologyReady",1);
                material.SetVector("_MapSize",new Vector4(16,8,1f/16,1f/8));
                material.SetVector("_ProbeOrigin",new Vector4(7,3,0,0));
                ShaderUtil.allowAsyncCompilation=false;ShaderUtil.CompilePass(material,0,true);
                Func<int,Color[]> sample=cover=>
                {
                    var pixels=new Color32[16*8];
                    for(int i=0;i<pixels.Length;i++)pixels[i]=new Color32((byte)cover,(byte)(cover==10?11:2),190,107);
                    ecology.SetPixels32(pixels);ecology.Apply();
                    Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;
                    image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();return image.GetPixels();
                };
                var grass=sample(1);var marsh=sample(10);var forest=sample(6);var dry=sample(2);
                Check(lines,Difference(grass,marsh)>.001,"wetland reeds differ from meadow flowers and grasses");
                Check(lines,Difference(grass,forest)>.001,"forest floor differs from open meadow");
                Check(lines,Difference(grass,dry)>.001,"dry steppe has distinct scrub and mineral ground");
                Check(lines,Difference(grass,sample(1))<.000001,"ground details are stable between repeated renders");
                material.SetVector("_ProbeOrigin",new Vector4(23,3,0,0));
                Check(lines,Difference(grass,sample(1))<.001,"geographic ground details agree at wrapped longitude");
                material.SetVector("_ProbeOrigin",new Vector4(3,2,0,0));
                material.SetVector("_ProbeSpan",new Vector4(8,4,0,0));
                int flowers=0;
                foreach(var pixel in sample(11))if(pixel.b>pixel.g*1.02f&&pixel.r>pixel.g*1.02f)flowers++;
                Check(lines,flowers>0,"alpine meadows contain visible wildflowers, petal pixels="+flowers);
                material.SetVector("_ProbeSpan",Vector4.zero);
                material.SetVector("_ProbeOrigin",new Vector4(7,3,0,0));material.SetFloat("_ProbeWater",1);
                Check(lines,Difference(sample(1),sample(10))<.000001,"ground dressing excludes water receivers");
                bool finite=true;
                foreach(var set in new[]{grass,marsh,forest,dry})foreach(var pixel in set)
                    finite&=!float.IsNaN(pixel.r+pixel.g+pixel.b)&&!float.IsInfinity(pixel.r+pixel.g+pixel.b)&&pixel.r>=0&&pixel.g>=0&&pixel.b>=0;
                Check(lines,finite,"ground cover remains finite and nonnegative");
                field.SetPixel(0,0,new Color(120,-3,0,1));field.Apply();
                material.SetTexture("_VisualTex",field);material.SetTexture("_HydroA",Texture2D.blackTexture);
                material.SetTexture("_HydroB",Texture2D.blackTexture);material.SetFloat("_ProbeMode",1);
                material.SetFloat("_ProbeCover",1);
                int oak=0,birch=0,shrub=0;
                foreach(var pixel in sample(1))if(pixel.g>.5f)
                {
                    int species=Mathf.RoundToInt(pixel.r);
                    if(species==0)oak++;else if(species==2)birch++;else if(species==6)shrub++;
                }
                Check(lines,oak>0&&birch>0&&shrub>0,"sparse grassland placement retains all selected tree types");
                material.SetFloat("_ProbeCover",4);oak=0;shrub=0;
                foreach(var pixel in sample(4))if(pixel.g>.5f)
                {
                    int species=Mathf.RoundToInt(pixel.r);
                    if(species==0)oak++;else if(species==6)shrub++;
                }
                Check(lines,oak>0&&shrub>0,"shrubland density preserves its occasional broadleaf trees");
                material.SetFloat("_ProbeMode",0);material.SetFloat("_ProbeWater",0);
                material.SetFloat("_VegetationWindReady",1);material.SetTexture("_VegetationWindTex",wind);
                wind.SetPixel(0,0,new Color(1,.5f,1,.5f));wind.Apply();
                foreach(int cover in new[]{1,10,11})
                {
                    material.SetFloat("_SkyClock",0);var calm=sample(cover);
                    material.SetFloat("_SkyClock",1.7f);var swaying=sample(cover);
                    Check(lines,Difference(calm,swaying)>.00005,"ground cover="+cover+" bends with prevailing wind");
                    Check(lines,Difference(swaying,sample(cover))==0,"ground cover="+cover+" freezes with the visual clock");
                }
                wind.SetPixel(0,0,Color.clear);wind.Apply();
                material.SetFloat("_SkyClock",0);var still=sample(1);
                material.SetFloat("_SkyClock",3);
                Check(lines,Difference(still,sample(1))==0,"calm wind leaves grass and flowers still");
                material.SetFloat("_ProbeMode",2);
                foreach(int cover in new[]{1,10,11})
                {
                    material.SetFloat("_ProbeCover",cover);
                    bool inside=true;
                    foreach(var layout in sample(cover))
                    {
                        float widthExtent=cover==10?3:2;
                        float heightExtent=cover==10?layout.a+1:layout.b+2;
                        inside&=layout.r-1-1.44f>=0&&layout.r+widthExtent+1.44f<8
                            &&layout.g>=0&&layout.g+heightExtent+.63f<8;
                    }
                    Check(lines,inside,"ground cover="+cover+" keeps complete wind-bent leaves inside clump boundaries");
                }
            }
            finally
            {
                Directory.CreateDirectory("ValidationOutput/Ecology");File.WriteAllLines("ValidationOutput/Ecology/ground-cover.txt",lines);
                RenderTexture.active=previous;ShaderUtil.allowAsyncCompilation=async;RenderTexture.ReleaseTemporary(target);
                foreach(var resource in new UnityEngine.Object[]{material,ecology,image,field,wind})UnityEngine.Object.DestroyImmediate(resource);
            }
        }

        static double Difference(Color[] first,Color[] second)
        {
            double difference=0;
            for(int i=0;i<first.Length;i++)difference+=Math.Abs(first[i].r-second[i].r)+Math.Abs(first[i].g-second[i].g)+Math.Abs(first[i].b-second[i].b);
            return difference/(first.Length*3);
        }

        static void Check(List<string> lines,bool passed,string description)
        {
            lines.Add((passed?"PASS ":"FAIL ")+description);
            if(!passed)throw new InvalidOperationException(description);
        }
    }
}
