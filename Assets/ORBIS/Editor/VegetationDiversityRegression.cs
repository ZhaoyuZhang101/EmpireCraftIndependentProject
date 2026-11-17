using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
    public static class VegetationDiversityRegression
    {
        const int Resolution=128;
        static readonly string[] Names={"broadleaf","pine","birch","palm","acacia","willow","shrub","tropical"};

        [MenuItem("Tools/ORBIS/Local Planet/Validate vegetation species diversity")]
        public static void Run()
        {
            var lines=new List<string>();
            RenderTexture previousTarget=RenderTexture.active;
            bool previousAsync=ShaderUtil.allowAsyncCompilation;
            Material material=null;
            Texture2D image=null;
            Texture2D atlas=null;
            RenderTexture target=null;
            const string outputDirectory="ValidationOutput/Ecology";
            try
            {
                const string includePath="Assets/ORBIS/Resources/ORBIS/FlatWorld/FlatVegetationArt.cginc";
                string metadataGuid=Array.Find(File.ReadAllLines(includePath+".meta"),line=>line.StartsWith("guid:",StringComparison.Ordinal));
                metadataGuid=metadataGuid==null?"":metadataGuid.Substring(5).Trim();
                Check(lines,Guid.TryParseExact(metadataGuid,"N",out _)&&metadataGuid==AssetDatabase.AssetPathToGUID(includePath),"vegetation geometry include has a valid imported GUID");
                Shader shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/VegetationDiversityProbe.shader");
                if(shader==null)throw new Exception("Vegetation diversity probe shader is missing.");
                material=new Material(shader);
                image=new Texture2D(Resolution,Resolution,TextureFormat.RGBAFloat,false,true);
                atlas=new Texture2D(Resolution*Names.Length,Resolution*2,TextureFormat.RGBA32,false,true);
                target=RenderTexture.GetTemporary(Resolution,Resolution,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
                ShaderUtil.allowAsyncCompilation=false;
                ShaderUtil.CompilePass(material,0,true);
                foreach(var message in ShaderUtil.GetShaderMessages(shader))
                    if(message.severity.ToString()=="Error"||message.message.IndexOf("uninitialized",StringComparison.OrdinalIgnoreCase)>=0)
                        throw new Exception(message.message);

                material.SetVector("_MapSize",new Vector4(16,8,1f/16,1f/8));
                material.SetVector("_ProbeRoot",new Vector2(7,3));
                material.SetFloat("_ProbeSeed",.37f);
                var westSun=new Vector3(-.8f,.35f,.7f).normalized;
                var eastSun=new Vector3(.8f,.35f,.7f).normalized;
                var crowns=new Color[Names.Length][];
                var shadows=new Color[Names.Length][];
                var means=new Color[Names.Length];

                for(int kind=0;kind<Names.Length;kind++)
                {
                    material.SetFloat("_ProbeType",kind);
                    material.SetFloat("_ProbeLatitude",0);
                    material.SetVector("_ProbeSun",westSun);
                    Color[] artwork=Render(material,target,image,0);
                    crowns[kind]=Render(material,target,image,5);
                    shadows[kind]=Render(material,target,image,1);
                    Color[] normals=Render(material,target,image,2);
                    Check(lines,AllFinite(artwork)&&AllFinite(shadows[kind])&&AllFinite(normals),Names[kind]+" produces finite color, normals and shadow");
                    int occupied=0;
                    int boundary=0;
                    int normalErrors=0;
                    Color mean=Color.clear;
                    for(int y=0;y<Resolution;y++)
                    for(int x=0;x<Resolution;x++)
                    {
                        int index=y*Resolution+x;
                        if(artwork[index].a>.5f&&(x==0||y==0||x==Resolution-1||y==Resolution-1))boundary++;
                        if(crowns[kind][index].a<.5f)continue;
                        occupied++;
                        mean+=artwork[index];
                        Color normal=normals[index];
                        float length=Mathf.Sqrt(normal.r*normal.r+normal.g*normal.g+normal.b*normal.b);
                        if(Mathf.Abs(length-1)>.002f)normalErrors++;
                    }
                    means[kind]=mean/Math.Max(1,occupied);
                    Check(lines,occupied>180,Names[kind]+" crown remains legible with "+occupied+" pixels");
                    Check(lines,boundary==0,Names[kind]+" fits the artwork bounds; clipped pixels="+boundary);
                    Check(lines,normalErrors==0,Names[kind]+" retains unit surface normals; invalid pixels="+normalErrors);
                    Check(lines,MaxDifference(artwork,Render(material,target,image,0))<.000001f,Names[kind]+" artwork is deterministic");
                    Check(lines,MaxDifference(shadows[kind],Render(material,target,image,1))<.000001f,Names[kind]+" shadow is deterministic");

                    material.SetVector("_ProbeSun",eastSun);
                    Color[] movedShadow=Render(material,target,image,1);
                    float shadowTravel=ShadowCentroidX(shadows[kind])-ShadowCentroidX(movedShadow);
                    Check(lines,shadowTravel>1,Names[kind]+" shadow follows the light direction; travel="+Number(shadowTravel));
                    float foliageResponse=MeanDifference(artwork,Render(material,target,image,0))*Resolution*Resolution/Math.Max(1,occupied);
                    Check(lines,foliageResponse>.025f,Names[kind]+" foliage follows the light direction; response="+Number(foliageResponse));
                    material.SetVector("_ProbeSun",westSun);

                    foreach(float latitude in new[]{80f,-80f,89f,-89f})
                    {
                        material.SetFloat("_ProbeLatitude",latitude);
                        int changed=MaskDifference(crowns[kind],Render(material,target,image,5));
                        Check(lines,changed<=4,Names[kind]+" keeps its polar silhouette at latitude="+latitude+"; changed pixels="+changed);
                    }
                    material.SetFloat("_ProbeLatitude",0);
                    material.SetVector("_ProbeRoot",new Vector2(23,3));
                    Check(lines,MaskDifference(crowns[kind],Render(material,target,image,5))<=4,Names[kind]+" wraps at the longitude seam");
                    material.SetVector("_ProbeRoot",new Vector2(7,3));
                    foreach(float seed in new[]{0f,.249f,.5f,.749f,.999f})
                    {
                        material.SetFloat("_ProbeSeed",seed);
                        Check(lines,AllFinite(Render(material,target,image,0))&&AllFinite(Render(material,target,image,1)),Names[kind]+" stays finite at seed="+seed);
                        int outside=0;
                        foreach(Color color in Render(material,target,image,6))if(color.r>.5f)outside++;
                        Check(lines,outside==0,Names[kind]+" fits the renderer silhouette bounds at seed="+seed+"; outside pixels="+outside);
                    }
                    material.SetFloat("_ProbeSeed",.37f);
                    if(kind==3)
                    {
                        material.SetFloat("_ProbeSeed",.34f);var firstPalm=Render(material,target,image,5);
                        material.SetFloat("_ProbeSeed",.44f);var secondPalm=Render(material,target,image,5);
                        float variation=MaskDifference(firstPalm,secondPalm)/(float)Math.Max(1,MaskUnion(firstPalm,secondPalm));
                        Check(lines,variation>.12f,"palm fronds vary their fan orientation between seeds; silhouette change="+Number(variation));
                        material.SetFloat("_ProbeSeed",.9f);var trunk=Render(material,target,image,7);
                        double baseX=0,topX=0;int baseCount=0,topCount=0;
                        for(int y=0;y<Resolution;y++)for(int x=0;x<Resolution;x++)
                        {
                            if(trunk[y*Resolution+x].a<.5f)continue;
                            float height=(y+.5f)/Resolution*22-3;
                            if(height>=0&&height<3){baseX+=x;baseCount++;}
                            if(height>=10&&height<13){topX+=x;topCount++;}
                        }
                        float lean=(float)(topX/Math.Max(1,topCount)-baseX/Math.Max(1,baseCount));
                        Check(lines,baseCount>0&&topCount>0&&Mathf.Abs(lean)>4,"palm trunk bends continuously towards the crown; pixel lean="+Number(lean));
                        material.SetFloat("_ProbeSeed",.37f);
                        // The production shadow ray scales horizontal sunlight by .40.
                        float alignedSunY=-.7f*(.46947156f/.88294759f)/.40f;
                        material.SetVector("_ProbeSun",new Vector3(0,alignedSunY,.7f));
                        var matchedShadow=Render(material,target,image,1);
                        var matchedCrown=Render(material,target,image,5);
                        int mismatches=0;
                        for(int i=0;i<matchedShadow.Length;i++)if((matchedShadow[i].r>.5f)!=(matchedCrown[i].a>.5f))mismatches++;
                        Check(lines,mismatches<=4,"palm shadow matches its actual fronds when light and view align; changed pixels="+mismatches);
                        material.SetFloat("_ProbeSeed",0);
                        material.SetVector("_ProbePoint",new Vector2(.697166f,19.017145f));
                        Color joint=Render(material,target,image,8)[0];
                        Check(lines,joint.r>.99f&&joint.g>.99f&&joint.b>.7f,"palm frond interior remains solid through its curve joint; shadow="+Number(joint.g)+" edge="+Number(joint.b));
                        material.SetFloat("_ProbeSeed",.37f);
                        material.SetVector("_ProbeSun",westSun);
                    }
                    var spritePreview=new Color[artwork.Length];
                    var shadowPreview=new Color[artwork.Length];
                    var ground=new Color(.36f,.35f,.30f,1);
                    for(int i=0;i<artwork.Length;i++)
                    {
                        spritePreview[i]=Color.Lerp(ground,artwork[i],artwork[i].a);
                        spritePreview[i].a=1;
                        shadowPreview[i]=ground*(1-shadows[kind][i].r*.60f);
                        shadowPreview[i].a=1;
                    }
                    atlas.SetPixels(kind*Resolution,Resolution,Resolution,Resolution,spritePreview);
                    atlas.SetPixels(kind*Resolution,0,Resolution,Resolution,shadowPreview);
                }

                material.SetFloat("_ProbeType",2.5f);
                Check(lines,MaskDifference(crowns[3],Render(material,target,image,5))==0,"palm geometry uses the same rounded species ID as trunk and foliage");
                material.SetFloat("_ProbeType",3.5f);
                Check(lines,MaskDifference(crowns[4],Render(material,target,image,5))==0,"upper palm boundary retains the next species silhouette");

                for(int first=0;first<Names.Length;first++)
                for(int second=first+1;second<Names.Length;second++)
                {
                    string pair=Names[first]+" / "+Names[second];
                    float silhouetteDifference=MaskDifference(crowns[first],crowns[second])/(float)Math.Max(1,MaskUnion(crowns[first],crowns[second]));
                    float shadowDifference=MeanDifference(shadows[first],shadows[second]);
                    Color difference=means[first]-means[second];
                    float paletteDistance=Mathf.Sqrt(difference.r*difference.r+difference.g*difference.g+difference.b*difference.b);
                    Check(lines,silhouetteDifference>.025f,pair+" has distinct crown silhouettes; difference="+Number(silhouetteDifference));
                    Check(lines,shadowDifference>.002f,pair+" has distinct matching shadows; difference="+Number(shadowDifference));
                    Check(lines,paletteDistance>.012f,pair+" has distinct foliage palettes; distance="+Number(paletteDistance));
                }

                CheckSpecies(lines,material,target,image,3,3,.5f,.2f,0,4,"savanna selects acacia");
                CheckSpecies(lines,material,target,image,4,8,.4f,.2f,0,6,"shrubland selects low shrubs");
                CheckSpecies(lines,material,target,image,5,2,.7f,.5f,0,7,"tropical seasonal forest selects tropical crowns");
                CheckSpecies(lines,material,target,image,6,14,.3f,.1f,0,2,"deciduous forest includes birch");
                CheckSpecies(lines,material,target,image,6,14,.3f,.9f,0,0,"deciduous forest includes broadleaf");
                CheckSpecies(lines,material,target,image,7,15,.5f,.9f,0,1,"temperate rainforest includes evergreen crowns");
                CheckSpecies(lines,material,target,image,8,26,.5f,.5f,0,1,"boreal forest selects pine");
                CheckSpecies(lines,material,target,image,9,1,.8f,.2f,1,3,"tropical shores include palms");
                CheckSpecies(lines,material,target,image,9,1,.8f,.8f,0,7,"inland rainforest selects tropical broadleaf");
                CheckSpecies(lines,material,target,image,10,14,.9f,.5f,1,5,"wetlands include willow");
                CheckSpecies(lines,material,target,image,11,29,.3f,.5f,0,6,"alpine cover selects low shrubs");
                CheckSpecies(lines,material,target,image,-1,20,0,.5f,0,1,"legacy cold climate keeps pine");
                CheckSpecies(lines,material,target,image,-1,14,0,.5f,0,0,"legacy temperate climate keeps broadleaf");

                material.SetFloat("_ProbeMode",4);
                foreach(var root in new[]{new Vector2(0,0),new Vector2(7,3),new Vector2(15,7)})
                {
                    material.SetVector("_ProbeRoot",root);
                    Color placement=Render(material,target,image,4)[0];
                    Check(lines,placement.r>=8&&placement.r<=24&&placement.g>=6&&placement.g<=20&&placement.b>=.78f&&placement.b<=1.08f,"tree anchor and scale remain bounded at "+root);
                }

                Directory.CreateDirectory(outputDirectory);
                atlas.Apply();
                Directory.CreateDirectory("ValidationOutput/Ecology");
                File.WriteAllBytes("ValidationOutput/Ecology/vegetation-species.png",atlas.EncodeToPNG());
                if(lines.Exists(line=>line.StartsWith("FAIL",StringComparison.Ordinal)))throw new Exception(string.Join("\n",lines));
                Debug.Log("Vegetation diversity regression passed "+lines.Count+" checks.");
            }
            catch(Exception error)
            {
                lines.Add("FAIL "+error.Message);
                throw;
            }
            finally
            {
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllLines(outputDirectory+"/vegetation-diversity-regression.txt",lines);
                RenderTexture.active=previousTarget;
                ShaderUtil.allowAsyncCompilation=previousAsync;
                if(target!=null)RenderTexture.ReleaseTemporary(target);
                if(material!=null)UnityEngine.Object.DestroyImmediate(material);
                if(image!=null)UnityEngine.Object.DestroyImmediate(image);
                if(atlas!=null)UnityEngine.Object.DestroyImmediate(atlas);
            }
        }

        static Color[] Render(Material material,RenderTexture target,Texture2D image,int mode)
        {
            material.SetFloat("_ProbeMode",mode);
            Graphics.Blit(Texture2D.whiteTexture,target,material);
            RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,Resolution,Resolution),0,0);
            image.Apply();
            return image.GetPixels();
        }

        static void CheckSpecies(List<string> lines,Material material,RenderTexture target,Texture2D image,float cover,float climate,float wetness,float seed,float shore,int expected,string message)
        {
            material.SetVector("_ProbeEcology",new Vector4(cover,0,wetness,seed));
            material.SetVector("_ProbeInfo",new Vector4(0,0,0,climate));
            material.SetFloat("_ProbeShore",shore);
            float actual=Render(material,target,image,3)[0].r;
            Check(lines,Mathf.Abs(actual-expected)<.001f,message+"; selected="+Number(actual));
        }

        static bool AllFinite(Color[] colors)
        {
            foreach(Color color in colors)
            {
                if(float.IsNaN(color.r)||float.IsInfinity(color.r)||
                   float.IsNaN(color.g)||float.IsInfinity(color.g)||
                   float.IsNaN(color.b)||float.IsInfinity(color.b)||
                   float.IsNaN(color.a)||float.IsInfinity(color.a))return false;
            }
            return true;
        }

        static int MaskDifference(Color[] first,Color[] second)
        {
            int changed=0;
            for(int i=0;i<first.Length;i++)if((first[i].a>.5f)!=(second[i].a>.5f))changed++;
            return changed;
        }

        static int MaskUnion(Color[] first,Color[] second)
        {
            int occupied=0;
            for(int i=0;i<first.Length;i++)if(first[i].a>.5f||second[i].a>.5f)occupied++;
            return occupied;
        }

        static float MaxDifference(Color[] first,Color[] second)
        {
            float maximum=0;
            for(int i=0;i<first.Length;i++)
            {
                Color difference=first[i]-second[i];
                maximum=Mathf.Max(maximum,Mathf.Abs(difference.r),Mathf.Abs(difference.g),Mathf.Abs(difference.b),Mathf.Abs(difference.a));
            }
            return maximum;
        }

        static float MeanDifference(Color[] first,Color[] second)
        {
            double total=0;
            for(int i=0;i<first.Length;i++)
                total+=Math.Abs(first[i].r-second[i].r)+Math.Abs(first[i].g-second[i].g)+Math.Abs(first[i].b-second[i].b);
            return (float)(total/(first.Length*3));
        }

        static float ShadowCentroidX(Color[] colors)
        {
            double sum=0;
            double weight=0;
            for(int i=0;i<colors.Length;i++)
            {
                sum+=(i%Resolution)*colors[i].r;
                weight+=colors[i].r;
            }
            return (float)(sum/Math.Max(.000001,weight));
        }

        static string Number(float number)
        {
            return number.ToString("0.000000",CultureInfo.InvariantCulture);
        }


        static void Check(List<string> lines,bool pass,string message)
        {
            lines.Add((pass?"PASS ":"FAIL ")+message);
        }
    }
}
