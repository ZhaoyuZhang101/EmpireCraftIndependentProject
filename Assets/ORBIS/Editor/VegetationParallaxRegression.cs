using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Orbis.Flat;
using Orbis.Planet;

namespace Orbis.Editor
{
    public static class VegetationParallaxRegression
    {
        const int Width=512, Height=256, Size=192;

        [MenuItem("Tools/ORBIS/Local Planet/Validate vegetation under terrain parallax")]
        public static void Run()
        {
            var probe=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/VegetationParallaxProbe.shader"));
            var field=new Texture2D(Width,Height,TextureFormat.RGBAFloat,false,true);
            var info=new Texture2D(Width,Height,TextureFormat.RGBA32,false,true);
            int atlasRows=Mathf.CeilToInt((float)(Width/MarkleyProjection.Aspect));
            var mesh=CreateLattice(256,128,false);
            var atlasMesh=CreateLattice(Width,atlasRows,true);
            var drawMesh=mesh;
            var target=RenderTexture.GetTemporary(Size,Size,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;
            var read=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true);
            var preview=new Texture2D(Size,Size,TextureFormat.RGB24,false,true);
            var commands=new CommandBuffer();
            var plantHeights=new FlatPlantHeightCache();
            var atlasHeights=new FlatPlantHeightCache();
            bool async=ShaderUtil.allowAsyncCompilation;
            var report=new List<string>();
            string folder="ValidationOutput/VegetationParallax";
            Directory.CreateDirectory(folder);
            try
            {
                ShaderUtil.allowAsyncCompilation=false;
                ShaderUtil.CompilePass(probe,0,true);
                if(ShaderUtil.ShaderHasError(probe.shader))throw new Exception("Parallax probe shader failed to compile");
                field.wrapModeU=info.wrapModeU=TextureWrapMode.Repeat;
                field.wrapModeV=info.wrapModeV=TextureWrapMode.Clamp;
                info.filterMode=FilterMode.Point;
                probe.SetTexture("_VisualTex",field);
                probe.SetTexture("_InfoTex",info);
                probe.SetTexture("_HydroA",Texture2D.blackTexture);
                probe.SetTexture("_HydroB",Texture2D.blackTexture);
                probe.SetVector("_MapSize",new Vector4(Width,Height,1f/Width,1f/Height));
                probe.SetFloat("_LocalPhysicalArtwork",1);
                probe.SetFloat("_LocalRadius",.04f);
                probe.SetFloat("_ScrollMorph",1);
                probe.SetFloat("_ScrollHeightScale",FlatGlobeGeometry.HeightScale);
                probe.SetVector("_ScrollPoleHeights",Vector4.zero);
                MarkleyProjection.Bind(probe);
                float atlasHeight=(float)(Width/MarkleyProjection.Aspect);
                probe.SetVector("_ProjectionSize",new Vector4(Width,atlasHeight,0,0));
                var fields=new Color[Width*Height];
                var types=new Color32[fields.Length];
                int acrossTile=0;
                for(int i=0;i<types.Length;i++)types[i]=new Color32(0,1,0,0);
                foreach(var tile in new[]{new Vector2Int(0,145),new Vector2Int(376,222)})
                {
                    int index=tile.y*Width+tile.x;
                    types[index]=default;info.SetPixels32(types);info.Apply();types[index]=new Color32(0,1,0,0);
                    probe.SetVector("_ProbeTile",new Vector4(tile.x,tile.y,0,0));
                    foreach(var kind in new[]{"tree","reed","grass","panned-tree","atlas-tree"})
                    {
                        bool atlas=kind=="atlas-tree",tree=kind.EndsWith("tree",StringComparison.Ordinal);
                        drawMesh=mesh;probe.DisableKeyword("ORBIS_ATLAS_PLANTS");
                        SetFrame(probe,tile.x+.5f,tile.y+.5f);
                        probe.SetFloat("_ProbeHalfExtent",.008f);probe.SetVector("_ScrollRelief",Vector4.zero);
                        probe.SetFloat("_ProbeCover",kind=="grass"?1:10);probe.SetFloat("_ProbeKind",3);
                        var roots=Render()[Size*(Size/2)+Size/2];
                        float x=tile.x+(tree?roots.r:roots.b);
                        float y=tile.y+(tree?roots.g:roots.a);
                        float latitude=(y/Height-.5f)*Mathf.PI;
                        if(kind=="panned-tree")SetFrame(probe,x+.22f,y+.28f*Mathf.Cos(latitude));
                        else SetFrame(probe,x,y);
                        float extent=(tree?1f:.22f)*Mathf.PI*2/Width*Mathf.Cos(latitude);
                        probe.SetFloat("_ProbeHalfExtent",extent);
                        probe.SetFloat("_ProbeKind",0);
                        if(tree)probe.DisableKeyword("ORBIS_PROBE_REEDS");else probe.EnableKeyword("ORBIS_PROBE_REEDS");
                        var chart=MarkleyProjection.Project(PlanetCoordinateSystem.FromMapUV(x/Width,y/Height));
                        if(atlas)
                        {
                            drawMesh=atlasMesh;probe.EnableKeyword("ORBIS_ATLAS_PLANTS");
                            probe.SetVector("_ScrollPan",new Vector4(chart.x*Width,chart.y*atlasHeight,0,0));
                            probe.SetFloat("_ProbeHalfExtent",extent*1.8f);
                        }
                        Color[] reference=null;
                        foreach(int slope in new[]{0,1,-1,2})
                        {
                            for(int row=0;row<Height;row++)for(int col=0;col<Width;col++)
                            {
                                float dx=Mathf.Repeat(col+.5f-x+Width/2f,Width)-Width/2f,dy=row+.5f-y;
                                float relief=slope==2?2400*dx*dy-1600*dy:slope*(1400*dx+2800*dy);
                                float h=Mathf.Clamp(1500+relief,-1600,6500);
                                fields[row*Width+col]=new Color(h,-4,0,2);
                            }
                            field.SetPixels(fields);field.Apply();
                            // Exaggerate the slope to reproduce the distortion on a small fixture.
                            float anchorHeight=atlas?AtlasHeight(chart,atlasRows,fields):1500;
                            probe.SetVector("_ScrollRelief",new Vector4(1,anchorHeight*FlatGlobeGeometry.HeightScale,.72f,.28f));
                            if(atlas)atlasHeights.Bind(probe,field,Width,atlasRows,true);
                            else plantHeights.Bind(probe,field,256,128);
                            var pixels=Render();
                            preview.SetPixels(pixels);preview.Apply();
                            File.WriteAllBytes(Path.Combine(folder,$"{kind}-{tile.y}-{slope}.png"),preview.EncodeToPNG());
                            if(slope==0){reference=pixels;continue;}
                            int intersection=0,union=0;
                            for(int py=0;py<Size;py++)for(int px=0;px<Size;px++)
                            {
                                // Isolate one reed from neighbouring plants and ground pebbles.
                                int northPixel=SystemInfo.graphicsUVStartsAtTop?Size-1-py:py;
                                if(!tree&&(px<Size*.34f||px>Size*.77f||northPixel<Size*.49f||northPixel>Size*.97f))continue;
                                int n=py*Size+px;
                                bool a=Ink(reference[n],tree),b=Ink(pixels[n],tree);
                                if(tree&&b&&pixels[n].a>.5f)acrossTile++;
                                if(a||b)union++;
                                if(a&&b)intersection++;
                            }
                            float overlap=intersection/(float)Math.Max(1,union);
                            report.Add($"{(union>25&&overlap>.96f?"PASS":"FAIL")} {kind}, row {tile.y}, slope {slope}, silhouette overlap {overlap:P2} ({intersection}/{union})");
                        }
                    }
                }
                report.Add($"{(acrossTile>0?"PASS":"FAIL")} crown coverage beyond the source tile, {acrossTile} pixels");
                Color[] Render()
                {
                    commands.Clear();commands.SetRenderTarget(target);
                    commands.ClearRenderTarget(false,true,Color.black);
                    commands.DrawMesh(drawMesh,Matrix4x4.identity,probe,0,0);
                    Graphics.ExecuteCommandBuffer(commands);
                    RenderTexture.active=target;
                    read.ReadPixels(new Rect(0,0,Size,Size),0,0);read.Apply();
                    return read.GetPixels();
                }
            }
            finally
            {
                File.WriteAllLines(Path.Combine(folder,"silhouettes.txt"),report);
                RenderTexture.active=previous;ShaderUtil.allowAsyncCompilation=async;
                commands.Release();RenderTexture.ReleaseTemporary(target);
                plantHeights.Dispose();atlasHeights.Dispose();
                foreach(var resource in new UnityEngine.Object[]{read,preview,mesh,atlasMesh,probe,info,field})
                    UnityEngine.Object.DestroyImmediate(resource);
            }
            if(report.Exists(s=>s.StartsWith("FAIL")))throw new Exception(string.Join("\n",report));
        }

        static bool Ink(Color pixel,bool tree) => tree?Mathf.Max(pixel.r,pixel.g)>.008f:
            pixel.g>pixel.b*1.3f&&Mathf.Max(pixel.r,pixel.g)>.018f;

        static float AtlasHeight(Vector2 chart,int rows,Color[] fields)
        {
            float x=chart.x*Width,y=chart.y*rows;
            int col=Mathf.FloorToInt(x),row=Mathf.FloorToInt(y);
            float tx=x-col,ty=y-row;
            float Vertex(int i,int j)
            {
                var uv=MarkleyProjection.GeoUV(i/(double)Width,j/(double)rows);
                float u=uv.x*Width-.5f,v=uv.y*Height-.5f;
                int cx=Mathf.FloorToInt(u),cy=Mathf.FloorToInt(v);
                float Sample(int a,int b)=>fields[Mathf.Clamp(b,0,Height-1)*Width+(a+Width)%Width].r;
                return Mathf.Lerp(Mathf.Lerp(Sample(cx,cy),Sample(cx+1,cy),u-cx),
                    Mathf.Lerp(Sample(cx,cy+1),Sample(cx+1,cy+1),u-cx),v-cy);
            }
            float east=Vertex(col+1,row),north=Vertex(col,row+1);
            return tx+ty<=1?(1-tx-ty)*Vertex(col,row)+tx*east+ty*north:
                (1-ty)*east+(1-tx)*north+(tx+ty-1)*Vertex(col+1,row+1);
        }

        static void SetFrame(Material material,float x,float y)
        {
            float lon=(x/Width-.5f)*Mathf.PI*2,lat=(y/Height-.5f)*Mathf.PI;
            var east=new Vector3(Mathf.Cos(lon),0,-Mathf.Sin(lon));
            var up=new Vector3(Mathf.Sin(lon)*Mathf.Cos(lat),Mathf.Sin(lat),Mathf.Cos(lon)*Mathf.Cos(lat));
            material.SetVector("_LocalCenter",up);material.SetVector("_LocalEast",east);
            material.SetVector("_LocalNorth",Vector3.Cross(up,east));
        }

        static Mesh CreateLattice(int sectors,int rings,bool atlas)
        {
            var vertices=new Vector3[(rings+1)*(sectors+1)];
            var indices=new int[rings*sectors*6];
            int n=0;
            for(int r=0;r<=rings;r++)for(int s=0;s<=sectors;s++)
            {
                int a=r*(sectors+1)+s;
                float angle=2*Mathf.PI*s/sectors;
                vertices[a]=atlas?new Vector3(s/(float)sectors,r/(float)rings):
                    new Vector3(Mathf.Cos(angle),Mathf.Sin(angle))*(r/(float)rings);
                if(r==rings||s==sectors)continue;
                int b=a+sectors+1;
                indices[n++]=a;indices[n++]=b;indices[n++]=a+1;
                indices[n++]=a+1;indices[n++]=b;indices[n++]=b+1;
            }
            return new Mesh{indexFormat=IndexFormat.UInt32,vertices=vertices,triangles=indices};
        }
    }
}
