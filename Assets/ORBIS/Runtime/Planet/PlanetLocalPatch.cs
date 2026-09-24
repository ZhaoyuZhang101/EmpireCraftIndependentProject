using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.Planet
{
    // Shared physical samples produce both terrain and water; no second height generator.
    public sealed class PlanetLocalPatch : IDisposable
    {
        public Mesh Terrain { get; private set; }
        public Mesh Water { get; private set; }
        public Texture2D Flow { get; private set; }
        public PlanetTangentFrame Frame { get; private set; }
        public float Units { get; private set; }
        public float HalfExtent { get; private set; }
        public PlanetWaterSample[] Samples { get; private set; }
        public static Vector3 LocalUnity(PlanetVector p,float units)
        { return new Vector3((float)(p.X/units),(float)(p.Z/units),(float)(p.Y/units)); }
        public PlanetLocalPatch(PlanetSurfaceData source,PlanetLocation location,PlanetProfile profile)
        {
            Frame=source.FrameAt(location);Units=Mathf.Max(1,profile.metersPerRenderUnit);
            HalfExtent=Mathf.Min(profile.patchHalfExtentMeters,(float)source.Radius*.1f);
            int n=Mathf.Clamp(profile.patchSegments,16,128),side=n+1;
            var terrain=new Vector3[side*side];var water=new Vector3[terrain.Length];var uv=new Vector2[terrain.Length];
            var color=new Color[terrain.Length];var flow=new Color[terrain.Length];Samples=new PlanetWaterSample[terrain.Length];
            var landIndices=new List<int>(n*n*6);var wetIndices=new List<int>(n*n*6);
            for(int y=0;y<=n;y++)for(int x=0;x<=n;x++)
            {
                int id=y*side+x;double e=((double)x/n*2-1)*HalfExtent,north=((double)y/n*2-1)*HalfExtent;
                var at=source.AtLocal(Frame,e,north);var sample=source.Sample(at);Samples[id]=sample;
                at.Altitude=sample.GroundHeight;terrain[id]=LocalUnity(Frame.ToLocal(PlanetCoordinateSystem.ToWorld(at,source.Center,source.Radius)),Units);
                water[id]=LocalUnity(Frame.ToLocal(sample.Position),Units);uv[id]=new Vector2((float)x/n,(float)y/n);
                color[id]=Color.Lerp(new Color(.23f,.34f,.17f),new Color(.5f,.46f,.36f),Mathf.Clamp01((float)sample.GroundHeight/4000));
                var velocity=Frame.DirectionToLocal(sample.FlowVelocity);
                // Signed East/North in metres/second; B depth metres, A coverage. Not SW3's encoded flow UV yet.
                flow[id]=new Color((float)velocity.X,(float)velocity.Y,(float)sample.Depth,(float)sample.Coverage);
            }
            for(int y=0;y<n;y++)for(int x=0;x<n;x++)
            {
                int a=y*side+x,b=a+side;
                AddTriangle(landIndices,a,b,a+1);AddTriangle(landIndices,a+1,b,b+1);
                // Conservative shoreline triangulation: never span a dry vertex with water.
                if(Samples[a].Coverage>.5&&Samples[b].Coverage>.5&&Samples[a+1].Coverage>.5)AddTriangle(wetIndices,a,b,a+1);
                if(Samples[a+1].Coverage>.5&&Samples[b].Coverage>.5&&Samples[b+1].Coverage>.5)AddTriangle(wetIndices,a+1,b,b+1);
            }
            Terrain=Create("ORBIS physical terrain",terrain,uv,color,landIndices);
            Water=Create("ORBIS physical water",water,uv,null,wetIndices);
            Flow=new Texture2D(side,side,TextureFormat.RGBAFloat,false,true){name="ORBIS East North depth coverage",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            Flow.SetPixels(flow);Flow.Apply(false,false);
        }
        static void AddTriangle(List<int> indices,int a,int b,int c){indices.Add(a);indices.Add(b);indices.Add(c);}
        static Mesh Create(string name,Vector3[] vertices,Vector2[] uv,Color[] colors,List<int> indices)
        {
            var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.vertices=vertices;mesh.uv=uv;
            if(colors!=null)mesh.colors=colors;mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        public void Dispose(){if(Terrain)UnityEngine.Object.Destroy(Terrain);if(Water)UnityEngine.Object.Destroy(Water);if(Flow)UnityEngine.Object.Destroy(Flow);}
    }
}
