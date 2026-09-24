using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis
{
    /// <summary>One river model for channel incision, globe mesh, atlas mesh and export.
    /// Topological drainage is separate from visually refined channel centerlines.</summary>
    public sealed class OrbisRiverNetwork
    {
        public struct Reach
        {
            public Vector3 a,b;
            public float ha,hb,wa,wb,da,db,qa,qb,ma,mb;
        }
        sealed class Node
        {
            public Vector3 p;public float h,level,flow,widthHint=1;
            public int parent=-1,mainChild=-1;public bool lake,ocean;
            public readonly List<int> neighbors=new List<int>();
        }
        readonly List<Node> nodes=new List<Node>();
        public readonly List<Reach> reaches=new List<Reach>();
        readonly Dictionary<Vector3Int,List<int>> spatial=new Dictionary<Vector3Int,List<int>>();
        public int ImportedComponents {get;private set;}
        public int ClosedComponents {get;private set;}
        public int MaxUpliftWarnings {get;private set;}
        public int Revision {get;private set;}
        Func<Vector3,float> ground;
        static float Width(Node n)=>Mathf.Clamp((.00075f+Mathf.Sqrt(Mathf.Max(0,n.flow))*.00033f)*n.widthHint,.0007f,.009f);
        static float Depth(Node n)=>Mathf.Clamp(.008f+Mathf.Sqrt(Mathf.Max(0,n.flow))*.0038f,.008f,.075f);
        public void Build(OrbisWorld world,Func<Vector3,float> terrain)
        {
            ground=terrain;nodes.Clear();reaches.Clear();spatial.Clear();ImportedComponents=ClosedComponents=MaxUpliftWarnings=0;
            if(world.MapSource!=null&&world.MapSource.HasRivers)BuildRaster(world.MapSource);
            else BuildProcedural(world);
            for(int i=0;i<nodes.Count;i++)
            {
                Node n=nodes[i];if(n.parent<0)continue;Node p=nodes[n.parent];
                if(p.mainChild<0||nodes[p.mainChild].flow<n.flow)p.mainChild=i;
            }
            for(int i=0;i<nodes.Count;i++)
            {
                Node n=nodes[i];if(n.parent<0||n.ocean||n.lake)continue;
                if(world.MapSource==null||!world.MapSource.HasRivers){if(n.flow<2.2f)continue;}
                AddChannel(i,world.MapSource!=null&&world.MapSource.HasRivers);
            }
            BuildIndex();Revision++;
        }
        void BuildProcedural(OrbisWorld w)
        {
            var order=new List<int>();
            for(int i=0;i<w.tiles.Length;i++)
            {
                Tile t=w.tiles[i];var n=new Node{p=w.grid.points[i],h=t.h,flow=t.flow,parent=t.downstream,lake=t.lake>.018f,ocean=t.h<=0};
                n.level=n.ocean?0:n.lake?t.flood:Mathf.Max(0,t.h-Depth(n)*.28f);nodes.Add(n);order.Add(i);
            }
            // Priority-Flood already supplies a strictly decreasing receiver potential in the world.
            order.Sort((a,b)=>w.tiles[a].flood.CompareTo(w.tiles[b].flood));
            foreach(int i in order){Node n=nodes[i];if(n.parent>=0&&!n.ocean)n.level=Mathf.Max(n.level,nodes[n.parent].level+.000001f);}
        }
        void BuildRaster(OrbisWorldMap map)
        {
            int w=map.width,h=map.height;var pixelToNode=new Dictionary<int,int>();
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                int pixel=y*w+x;float weight=OrbisWorldMap.RiverWeight(map.rivers,pixel*4);if(weight<=0)continue;
                if(nodes.Count>=120000)throw new InvalidOperationException("河流图超过 120,000 个节点；请用单像素中心线，不要涂满河面或海洋。");
                Vector3 p=OrbisWorldMap.Direction(x,y,w,h);float bed=ground(p);
                pixelToNode[pixel]=nodes.Count;nodes.Add(new Node{p=p,h=bed,flow=1,widthHint=weight,ocean=bed<=0});
            }
            foreach(var item in pixelToNode)
            {
                int x=item.Key%w,y=item.Key/w,id=item.Value;
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                {
                    if((dx==0&&dy==0)||y+dy<0||y+dy>=h)continue;int nx=(x+dx+w)%w,key=(y+dy)*w+nx;
                    if(!pixelToNode.TryGetValue(key,out int j))continue;
                    // A diagonal is permitted only when neither orthogonal bridge exists.
                    if(dx!=0&&dy!=0&&(pixelToNode.ContainsKey(y*w+nx)||pixelToNode.ContainsKey((y+dy)*w+x)))continue;
                    nodes[id].neighbors.Add(j);
                }
            }
            var componentsSeen=new bool[nodes.Count];var seen=new bool[nodes.Count];var order=new List<int>();
            for(int start=0;start<nodes.Count;start++)if(!componentsSeen[start])
            {
                ImportedComponents++;var component=new List<int>();var queue=new Queue<int>();queue.Enqueue(start);componentsSeen[start]=true;
                int low=start;
                while(queue.Count>0){int i=queue.Dequeue();component.Add(i);if(nodes[i].h<nodes[low].h)low=i;foreach(int j in nodes[i].neighbors)if(!componentsSeen[j]){componentsSeen[j]=true;queue.Enqueue(j);}}
                var heap=new MinHeap();bool hasSea=false;
                foreach(int i in component)if(nodes[i].ocean){hasSea=true;seen[i]=true;nodes[i].level=0;heap.Push(i,0);}
                if(!hasSea)
                {
                    ClosedComponents++;
                    // Prefer a low endpoint to avoid directing a disconnected river out of its middle.
                    int end=-1;foreach(int i in component)if(nodes[i].neighbors.Count<=1&&(end<0||nodes[i].h<nodes[end].h))end=i;
                    if(end>=0)low=end;nodes[low].level=nodes[low].h-.004f;seen[low]=true;heap.Push(low,nodes[low].level);
                }
                while(heap.Count>0)
                {
                    int i=heap.Pop();order.Add(i);
                    foreach(int j in nodes[i].neighbors)if(!seen[j])
                    {seen[j]=true;Node n=nodes[j];n.parent=i;n.level=Mathf.Max(n.h-.006f,nodes[i].level+.000001f);if(n.level>n.h+.02f)MaxUpliftWarnings++;heap.Push(j,n.level);}
                }
            }
            for(int k=order.Count-1;k>=0;k--){Node n=nodes[order[k]];if(n.parent>=0)nodes[n.parent].flow+=n.flow;}
        }
        static Vector3 Hermite(Vector3 a,Vector3 b,Vector3 ta,Vector3 tb,float t)
        {float t2=t*t,t3=t2*t;return ((2*t3-3*t2+1)*a+(t3-2*t2+t)*ta+(-2*t3+3*t2)*b+(t3-t2)*tb).normalized;}
        void AddChannel(int index,bool authored)
        {
            Node a=nodes[index],b=nodes[a.parent];float len=Vector3.Distance(a.p,b.p);if(len<1e-7f)return;
            int steps=authored?3:16;
            Vector3 incoming=a.mainChild>=0?nodes[a.mainChild].p:a.p-(b.p-a.p);
            Vector3 next=b.parent>=0?nodes[b.parent].p:b.p+(b.p-a.p);
            Vector3 ta=Vector3.ProjectOnPlane(b.p-incoming,a.p).normalized*len,tb=Vector3.ProjectOnPlane(next-a.p,b.p).normalized*len;
            if(Vector3.Dot(ta,b.p-a.p)<0)ta=b.p-a.p;if(Vector3.Dot(tb,b.p-a.p)<0)tb=b.p-a.p;
            var path=new Vector3[steps+1];for(int k=0;k<=steps;k++)path[k]=Hermite(a.p,b.p,ta,tb,k/(float)steps);
            path[0]=a.p;path[steps]=b.p;
            if(!authored)SeekValley(path,a.level,b.level,len);
            // Stop descending exactly at the actual coast, then carry a level, fading estuary into the sea.
            float shore=1;
            if(b.ocean)
            {
                for(int k=1;k<=steps;k++)if(ground(path[k])<=0)
                {float lo=0,hi=1;for(int q=0;q<12;q++){float m=(lo+hi)*.5f;if(ground(Vector3.Slerp(path[k-1],path[k],m))>0)lo=m;else hi=m;}shore=((k-1)+(lo+hi)*.5f)/steps;break;}
                shore=Mathf.Max(.01f,shore);
            }
            float wa=Width(a),wb=Width(b),da=Depth(a),db=Depth(b);
            for(int k=0;k<steps;k++)
            {
                float u=k/(float)steps,v=(k+1)/(float)steps;
                float ma=b.ocean?Mathf.SmoothStep(0,1,Mathf.InverseLerp(shore*.72f,1,u)):b.lake?u:0;
                float mb=b.ocean?Mathf.SmoothStep(0,1,Mathf.InverseLerp(shore*.72f,1,v)):b.lake?v:0;
                reaches.Add(new Reach{a=path[k],b=path[k+1],ha=Mathf.Lerp(a.level,b.level,Mathf.Min(1,u/shore)),hb=Mathf.Lerp(a.level,b.level,Mathf.Min(1,v/shore)),
                    wa=Mathf.Lerp(wa,wb,u)*(1+ma*.55f),wb=Mathf.Lerp(wa,wb,v)*(1+mb*.55f),da=Mathf.Lerp(da,db,u),db=Mathf.Lerp(da,db,v),
                    qa=Mathf.Lerp(a.flow,b.flow,u),qb=Mathf.Lerp(a.flow,b.flow,v),ma=ma,mb=mb});
            }
        }
        void SeekValley(Vector3[] path,float from,float to,float length)
        {
            // Dynamic programming through a terrain-sampled corridor. Lowest excavation cost wins;
            // slope limits the available meander width. No time-dependent sine-wiggle centerline.
            int n=path.Length,lanes=9;float slope=Mathf.Abs(from-to)/Mathf.Max(.001f,length),corridor=length*Mathf.Lerp(.24f,.055f,Mathf.Clamp01(slope*.3f));
            var candidates=new Vector3[n,lanes];var cost=new float[n,lanes];var previous=new int[n,lanes];
            for(int k=0;k<n;k++)
            {
                float t=k/(float)(n-1);Vector3 tangent=path[Math.Min(n-1,k+1)]-path[Math.Max(0,k-1)],side=Vector3.Cross(path[k],tangent).normalized;
                float width=corridor*4*t*(1-t),level=Mathf.Lerp(from,to,t);
                for(int j=0;j<lanes;j++)
                {
                    Vector3 p=(path[k]+side*((j-4)/4f)*width).normalized;candidates[k,j]=p;
                    float delta=ground(p)-level,local=Mathf.Max(0,delta)*5+Mathf.Max(0,-delta)*12+Mathf.Abs(j-4)*.0002f;
                    if(k==0){cost[k,j]=j==4?0:1e20f;continue;}
                    float best=1e20f;int parent=4;
                    for(int q=Math.Max(0,j-2);q<=Math.Min(8,j+2);q++){float c=cost[k-1,q]+(j-q)*(j-q)*.0012f;if(c<best){best=c;parent=q;}}
                    cost[k,j]=best+local;previous[k,j]=parent;
                }
            }
            int lane=4;for(int k=n-1;k>0;k--){path[k]=candidates[k,lane];lane=previous[k,lane];}
            var copy=(Vector3[])path.Clone();for(int k=1;k<n-1;k++)path[k]=(copy[k-1]*.2f+copy[k]*.6f+copy[k+1]*.2f).normalized;
        }
        static Vector3Int Key(Vector3 p)=>new Vector3Int(Mathf.FloorToInt(p.x*32),Mathf.FloorToInt(p.y*32),Mathf.FloorToInt(p.z*32));
        void BuildIndex()
        {
            for(int i=0;i<reaches.Count;i++)
            {
                var s=reaches[i];float margin=Mathf.Max(s.wa,s.wb)*3.5f+.001f;Vector3Int min=Key(Vector3.Min(s.a,s.b)-Vector3.one*margin),max=Key(Vector3.Max(s.a,s.b)+Vector3.one*margin);
                for(int z=min.z;z<=max.z;z++)for(int y=min.y;y<=max.y;y++)for(int x=min.x;x<=max.x;x++)
                {var key=new Vector3Int(x,y,z);if(!spatial.TryGetValue(key,out var list)){list=new List<int>();spatial[key]=list;}list.Add(i);}
            }
        }
        public float Carve(Vector3 p,float radius)
        {
            if(!spatial.TryGetValue(Key(p),out var list))return radius;
            foreach(int i in list)
            {
                Reach s=reaches[i];Vector3 d=s.b-s.a;float t=Mathf.Clamp01(Vector3.Dot(p-s.a,d)/Mathf.Max(1e-12f,d.sqrMagnitude));
                float distance=Vector3.Distance(p,Vector3.Lerp(s.a,s.b,t).normalized),width=Mathf.Lerp(s.wa,s.wb,t),bank=width*3.5f;if(distance>=bank)continue;
                float water=1+Mathf.Lerp(s.ha,s.hb,t)*.075f,depth=Mathf.Lerp(s.da,s.db,t)*.075f;
                // Full-width river bed with a smooth bank ramp. Never replace the height with sea level.
                float bottom=water-depth*(1-.28f*Mathf.Pow(Mathf.Clamp01(distance/width),2));
                float cut=Mathf.Lerp(bottom,radius,Mathf.SmoothStep(0,1,Mathf.InverseLerp(width*.92f,bank,distance)));
                radius=Mathf.Min(radius,cut);
            }
            return radius;
        }
        public float VisibleRadius(Vector3 p,float terrainRadius)
        {
            if(!spatial.TryGetValue(Key(p),out var list))return terrainRadius;
            foreach(int i in list){Reach s=reaches[i];Vector3 d=s.b-s.a;float t=Mathf.Clamp01(Vector3.Dot(p-s.a,d)/Mathf.Max(1e-12f,d.sqrMagnitude));if(Vector3.Distance(p,Vector3.Lerp(s.a,s.b,t).normalized)<=Mathf.Lerp(s.wa,s.wb,t))terrainRadius=Mathf.Max(terrainRadius,1+Mathf.Lerp(s.ha,s.hb,t)*.075f);}
            return terrainRadius;
        }
        public void FillMesh(Mesh mesh,bool map)
        {
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var geo=new List<Vector3>();var water=new List<Vector4>();var flow=new List<Vector3>();var profile=new List<Vector2>();var triangles=new List<int>();
            // Shared endpoint tangents prevent cracks at every subdivided segment and confluence.
            var tangentSum=new Dictionary<Vector3,Vector3>();
            foreach(var s in reaches){Vector3 t=(s.b-s.a).normalized;if(!tangentSum.ContainsKey(s.a))tangentSum[s.a]=Vector3.zero;if(!tangentSum.ContainsKey(s.b))tangentSum[s.b]=Vector3.zero;tangentSum[s.a]+=t;tangentSum[s.b]+=t;}
            foreach(var s in reaches)
            {
                Vector3 leftA=Vector3.Cross(s.a,tangentSum[s.a]).normalized,leftB=Vector3.Cross(s.b,tangentSum[s.b]).normalized;
                int copies=map?3:1;
                for(int copy=0;copy<copies;copy++)
                {
                    int start=vertices.Count;float refX=OrbisMapView.Project(s.a).x,shift=map?(copy-1)*Mathf.PI*2:0;
                    for(int end=0;end<2;end++)for(int across=0;across<5;across++)
                    {
                        float x=across/4f,signed=x*2-1;Vector3 p=((end==0?s.a:s.b)+(end==0?leftA:leftB)*signed*(end==0?s.wa:s.wb)).normalized;
                        float h=end==0?s.ha:s.hb,depth=end==0?s.da:s.db,q=end==0?s.qa:s.qb,mouth=end==0?s.ma:s.mb;
                        Vector3 v;
                        if(map){v=OrbisMapView.Project(p);v.x+=Mathf.Round((refX-v.x)/(Mathf.PI*2))*Mathf.PI*2+shift;v.z=.006f;}
                        else v=p*(1+h*.075f+.000028f);
                        vertices.Add(v);normals.Add(p);geo.Add(p);flow.Add(tangentSum[end==0?s.a:s.b].normalized);
                        water.Add(new Vector4(depth*120*(1-.85f*Mathf.Pow(Mathf.Abs(signed),3)),1-mouth,x,Mathf.Clamp(.25f+Mathf.Sqrt(q)*.05f,.25f,2.5f)));
                        profile.Add(new Vector2(x,mouth));
                    }
                    for(int j=0;j<4;j++)triangles.AddRange(new[]{start+j,start+j+5,start+j+1,start+j+1,start+j+5,start+j+6});
                }
            }
            mesh.Clear();mesh.indexFormat=IndexFormat.UInt32;mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.SetNormals(normals);mesh.SetUVs(0,geo);mesh.SetUVs(2,water);mesh.SetUVs(3,flow);mesh.SetUVs(4,profile);mesh.RecalculateBounds();
        }
    }
}
