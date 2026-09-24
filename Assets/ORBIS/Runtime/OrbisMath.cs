using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orbis
{
    // Mulberry32 and permutation noise ported from ORBIS VI.1.
    [Serializable] public sealed class RandomStream
    {
        public uint state;
        public RandomStream(uint seed) { state = seed; }
        public static uint Hash(string s) { unchecked { uint h=2166136261; foreach(char c in s) h=(h^c)*16777619; return h; } }
        public double Next() { unchecked { uint t=state+=0x6D2B79F5; t=(t^(t>>15))*(t|1); t^=t+((t^(t>>7))*(t|61)); return (t^(t>>14))/4294967296.0; } }
        public float Range(float a,float b) { return a+(b-a)*(float)Next(); }
        public Vector3 Unit() { float y=Range(-1,1), a=Range(0,Mathf.PI*2), r=Mathf.Sqrt(1-y*y); return new Vector3(Mathf.Cos(a)*r,y,Mathf.Sin(a)*r); }
    }
    public sealed class Noise3D
    {
        readonly int[] p=new int[512];
        public Noise3D(uint seed) { var r=new RandomStream(seed); for(int i=0;i<256;i++)p[i]=i; for(int i=255;i>0;i--){int j=(int)(r.Next()*(i+1)),t=p[i];p[i]=p[j];p[j]=t;} for(int i=0;i<256;i++)p[i+256]=p[i]; }
        static float Fade(float t)=>t*t*t*(t*(t*6-15)+10);
        static float Mix(float a,float b,float t)=>a+(b-a)*t;
        static float Grad(int h,float x,float y,float z) { h&=15;float u=h<8?x:y,v=h<4?y:(h==12||h==14?x:z);return ((h&1)!=0?-u:u)+((h&2)!=0?-v:v); }
        public float Get(float x,float y,float z) {
            int X=Mathf.FloorToInt(x)&255,Y=Mathf.FloorToInt(y)&255,Z=Mathf.FloorToInt(z)&255;
            x-=Mathf.Floor(x);y-=Mathf.Floor(y);z-=Mathf.Floor(z);
            float u=Fade(x),v=Fade(y),w=Fade(z);int A=p[X]+Y,AA=p[A]+Z,AB=p[A+1]+Z,B=p[X+1]+Y,BA=p[B]+Z,BB=p[B+1]+Z;
            return Mix(Mix(Mix(Grad(p[AA],x,y,z),Grad(p[BA],x-1,y,z),u),Mix(Grad(p[AB],x,y-1,z),Grad(p[BB],x-1,y-1,z),u),v),Mix(Mix(Grad(p[AA+1],x,y,z-1),Grad(p[BA+1],x-1,y,z-1),u),Mix(Grad(p[AB+1],x,y-1,z-1),Grad(p[BB+1],x-1,y-1,z-1),u),v),w);
        }
        public float Fbm(Vector3 p,int n=5) { float a=.57f,s=0;for(int i=0;i<n;i++){s+=a*Get(p.x,p.y,p.z);p=new Vector3(p.x*2.02f+3.2f,p.y*2.03f+6.3f,p.z*2.01f+1.7f);a*=.5f;}return s; }
    }
    public sealed class SphereGrid
    {
        public Vector3[] points;
        public int[] triangles;
        public int[][] neighbors;
        private SphereGrid() { }
        public Vector3[] corners;
        public int[][] tileCorners;
        static Vector3 CubePoint(Vector3 q)
        {
            float xx=q.x*q.x,yy=q.y*q.y,zz=q.z*q.z;
            return new Vector3(q.x*Mathf.Sqrt(1-yy/2-zz/2+yy*zz/3),q.y*Mathf.Sqrt(1-zz/2-xx/2+zz*xx/3),q.z*Mathf.Sqrt(1-xx/2-yy/2+xx*yy/3)).normalized;
        }
        // Each Tile is a real quadrilateral, with four shared corner IDs and four neighbors.
        // The dual triangulation connects tile centers only to interpolate the continuous surface.
        public static SphereGrid CreateTiles(int resolution=24)
        {
            var grid=new SphereGrid();var points=new List<Vector3>();var cornerPositions=new List<Vector3>();
            var lookup=new Dictionary<Vector3Int,int>();var adjacency=new List<HashSet<int>>();
            var cornerTiles=new List<List<int>>();var cornersPerTile=new List<int[]>();var edgeOwner=new Dictionary<long,int>();
            Vector3Int[] normals={Vector3Int.right,Vector3Int.left,Vector3Int.up,Vector3Int.down,new Vector3Int(0,0,1),new Vector3Int(0,0,-1)};
            for(int face=0;face<6;face++){
                Vector3Int normal=normals[face],u=Math.Abs(normal.x)==1?Vector3Int.up:Vector3Int.right;
                Vector3 cross=Vector3.Cross((Vector3)normal,(Vector3)u);Vector3Int v=new Vector3Int((int)cross.x,(int)cross.y,(int)cross.z);
                for(int y=0;y<resolution;y++)for(int x=0;x<resolution;x++){
                    int tile=points.Count;adjacency.Add(new HashSet<int>());int[] ids=new int[4];int[] dx={0,1,1,0},dy={0,0,1,1};Vector3 center=Vector3.zero;
                    for(int k=0;k<4;k++){
                        Vector3Int key=normal*resolution+u*(2*(x+dx[k])-resolution)+v*(2*(y+dy[k])-resolution);
                        if(!lookup.TryGetValue(key,out int corner)){corner=cornerPositions.Count;lookup[key]=corner;cornerPositions.Add(CubePoint((Vector3)key/resolution));cornerTiles.Add(new List<int>());}
                        ids[k]=corner;center+=cornerPositions[corner];cornerTiles[corner].Add(tile);
                    }
                    points.Add(center.normalized);cornersPerTile.Add(ids);
                    for(int k=0;k<4;k++){int a=ids[k],b=ids[(k+1)%4];long key=((long)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);if(edgeOwner.TryGetValue(key,out int neighbor)){adjacency[tile].Add(neighbor);adjacency[neighbor].Add(tile);}else edgeOwner[key]=tile;}
                }
            }
            var triangles=new List<int>();
            for(int corner=0;corner<cornerPositions.Count;corner++){
                Vector3 normal=cornerPositions[corner],east=Vector3.Cross(Mathf.Abs(normal.y)>.9f?Vector3.right:Vector3.up,normal).normalized,north=Vector3.Cross(normal,east);
                var tiles=cornerTiles[corner];tiles.Sort((a,b)=>Mathf.Atan2(Vector3.Dot(points[a],north),Vector3.Dot(points[a],east)).CompareTo(Mathf.Atan2(Vector3.Dot(points[b],north),Vector3.Dot(points[b],east))));
                for(int k=1;k<tiles.Count-1;k++){int a=tiles[0],b=tiles[k],c=tiles[k+1];if(Vector3.Dot(Vector3.Cross(points[b]-points[a],points[c]-points[a]),normal)<0){int swap=b;b=c;c=swap;}triangles.AddRange(new[]{a,b,c});}
            }
            grid.points=points.ToArray();grid.corners=cornerPositions.ToArray();grid.tileCorners=cornersPerTile.ToArray();grid.triangles=triangles.ToArray();grid.neighbors=new int[points.Count][];
            for(int i=0;i<points.Count;i++){grid.neighbors[i]=new int[adjacency[i].Count];adjacency[i].CopyTo(grid.neighbors[i]);Array.Sort(grid.neighbors[i]);}
            return grid;
        }
        public SphereGrid(int level=4) {
            float t=(1+Mathf.Sqrt(5))/2;
            var v=new List<Vector3>{new Vector3(-1,t,0),new Vector3(1,t,0),new Vector3(-1,-t,0),new Vector3(1,-t,0),new Vector3(0,-1,t),new Vector3(0,1,t),new Vector3(0,-1,-t),new Vector3(0,1,-t),new Vector3(t,0,-1),new Vector3(t,0,1),new Vector3(-t,0,-1),new Vector3(-t,0,1)};
            for(int i=0;i<v.Count;i++)v[i]=v[i].normalized;
            var f=new List<int>{0,11,5,0,5,1,0,1,7,0,7,10,0,10,11,1,5,9,5,11,4,11,10,2,10,7,6,7,1,8,3,9,4,3,4,2,3,2,6,3,6,8,3,8,9,4,9,5,2,4,11,6,2,10,8,6,7,9,8,1};
            for(int l=0;l<level;l++) {
                var cache=new Dictionary<long,int>();var next=new List<int>();
                for(int i=0;i<f.Count;i+=3){int a=f[i],b=f[i+1],c=f[i+2],ab=Mid(a,b,v,cache),bc=Mid(b,c,v,cache),ca=Mid(c,a,v,cache);next.AddRange(new[]{a,ab,ca,b,bc,ab,c,ca,bc,ab,bc,ca});}f=next;
            }
            points=v.ToArray();triangles=f.ToArray();var n=new HashSet<int>[v.Count];for(int i=0;i<n.Length;i++)n[i]=new HashSet<int>();
            for(int i=0;i<f.Count;i+=3)for(int k=0;k<3;k++){int a=f[i+k],b=f[i+(k+1)%3];n[a].Add(b);n[b].Add(a);}
            neighbors=new int[v.Count][];for(int i=0;i<n.Length;i++){neighbors[i]=new int[n[i].Count];n[i].CopyTo(neighbors[i]);Array.Sort(neighbors[i]);}
        }
        static int Mid(int a,int b,List<Vector3> v,Dictionary<long,int> cache){long k=((long)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);if(cache.TryGetValue(k,out int id))return id;id=v.Count;v.Add((v[a]+v[b]).normalized);cache[k]=id;return id;}
        public int Nearest(Vector3 p){int best=0;float score=-2;for(int i=0;i<points.Length;i++){float d=Vector3.Dot(points[i],p);if(d>score){score=d;best=i;}}return best;}
    }
    internal sealed class MinHeap
    {
        struct Item { public int id;public float h;public Item(int i,float v){id=i;h=v;} }
        readonly List<Item> a=new List<Item>(); public int Count=>a.Count;
        public void Push(int id,float h){int i=a.Count;a.Add(new Item(id,h));while(i>0){int p=(i-1)/2;if(a[p].h<=h)break;a[i]=a[p];i=p;}a[i]=new Item(id,h);}
        public int Pop(){int result=a[0].id;Item tail=a[a.Count-1];a.RemoveAt(a.Count-1);if(a.Count>0){int i=0;while(i*2+1<a.Count){int j=i*2+1;if(j+1<a.Count&&a[j+1].h<a[j].h)j++;if(a[j].h>=tail.h)break;a[i]=a[j];i=j;}a[i]=tail;}return result;}
    }
}
