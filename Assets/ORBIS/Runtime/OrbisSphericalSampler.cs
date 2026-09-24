using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orbis
{
    /// <summary>Piecewise-linear interpolation on the actual cube-sphere dual mesh.
    /// No latitude division, face clamping, or nearest-cell-only advection.</summary>
    public sealed class OrbisSphericalSampler
    {
        public struct Sample
        {
            public int a,b,c;
            public Vector3 weights;
            public float Read(float[] values) => values[a]*weights.x+values[b]*weights.y+values[c]*weights.z;
        }
        readonly SphereGrid grid;
        readonly int[][] incident, adjacent;
        readonly Vector3[] rowA,rowB,rowC;
        public OrbisSphericalSampler(SphereGrid source)
        {
            grid=source;
            var stars=new List<int>[grid.points.Length];var edges=new HashSet<int>[stars.Length];
            for(int i=0;i<stars.Length;i++){stars[i]=new List<int>();edges[i]=new HashSet<int>();}
            int count=grid.triangles.Length/3;rowA=new Vector3[count];rowB=new Vector3[count];rowC=new Vector3[count];
            for(int k=0;k<count;k++)
            {
                int a=grid.triangles[k*3],b=grid.triangles[k*3+1],c=grid.triangles[k*3+2];
                Vector3 x=grid.points[a],y=grid.points[b],z=grid.points[c];
                float det=Vector3.Dot(x,Vector3.Cross(y,z));
                if(Mathf.Abs(det)<1e-10f)throw new ArgumentException("Degenerate weather triangle.");
                rowA[k]=Vector3.Cross(y,z)/det;rowB[k]=Vector3.Cross(z,x)/det;rowC[k]=Vector3.Cross(x,y)/det;
                stars[a].Add(k);stars[b].Add(k);stars[c].Add(k);
                edges[a].Add(b);edges[a].Add(c);edges[b].Add(a);edges[b].Add(c);edges[c].Add(a);edges[c].Add(b);
            }
            incident=new int[stars.Length][];adjacent=new int[stars.Length][];
            for(int i=0;i<stars.Length;i++){incident[i]=stars[i].ToArray();adjacent[i]=new int[edges[i].Count];edges[i].CopyTo(adjacent[i]);Array.Sort(adjacent[i]);}
        }
        public Sample Locate(Vector3 direction,int hint=0)
        {
            Vector3 p=direction.normalized;int best=Mathf.Clamp(hint,0,grid.points.Length-1);
            for(int step=0;step<128;step++)
            {
                int next=best;float score=Vector3.Dot(p,grid.points[best]);
                foreach(int n in adjacent[best]){float s=Vector3.Dot(p,grid.points[n]);if(s>score+1e-8f){score=s;next=n;}}
                if(next==best)break;best=next;
            }
            foreach(int tri in incident[best])if(TryTriangle(p,tri,out Sample result))return result;
            // Rare Voronoi/triangulation disagreement at a cube corner: inspect the one-ring too.
            foreach(int n in adjacent[best])foreach(int tri in incident[n])if(TryTriangle(p,tri,out Sample result))return result;
            // Correctness fallback (normally unused), never silently pin transported fields to a cell.
            for(int tri=0;tri<rowA.Length;tri++)if(TryTriangle(p,tri,out Sample result))return result;
            throw new InvalidOperationException("Unable to locate weather direction on closed sphere.");
        }
        bool TryTriangle(Vector3 p,int tri,out Sample result)
        {
            Vector3 w=new Vector3(Vector3.Dot(rowA[tri],p),Vector3.Dot(rowB[tri],p),Vector3.Dot(rowC[tri],p));
            float sum=w.x+w.y+w.z;
            if(sum>0 && Mathf.Min(w.x,Mathf.Min(w.y,w.z))>=-1e-5f)
            {
                w.x=Mathf.Max(0,w.x);w.y=Mathf.Max(0,w.y);w.z=Mathf.Max(0,w.z);w/=w.x+w.y+w.z;
                result=new Sample{a=grid.triangles[tri*3],b=grid.triangles[tri*3+1],c=grid.triangles[tri*3+2],weights=w};return true;
            }
            result=default;return false;
        }
        public static Vector3 Backtrace(Vector3 normal,Vector3 velocity,float dt)
        {
            Vector3 tangent=Vector3.ProjectOnPlane(velocity,normal);float speed=tangent.magnitude;
            if(speed<1e-8f)return normal;
            float angle=Mathf.Min(.025f,speed*dt);
            return (normal*Mathf.Cos(angle)-tangent/speed*Mathf.Sin(angle)).normalized;
        }
    }
}
