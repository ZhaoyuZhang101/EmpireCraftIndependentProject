using System;
using System.Collections.Generic;
using UnityEngine;
using Pathfinding;
using Orbis.Planet;

namespace Orbis.Flat
{
    /// <summary>Bounded asynchronous Astar requests on an immutable spherical graph snapshot.</summary>
    public sealed class FlatWildlifePathfinder : MonoBehaviour,IWildlifePathfinder
    {
        sealed class Request
        {
            public int Start,Goal;
            public bool Flying;
            public Action<int[]> Complete;
        }
        sealed class Traversal : ITraversalProvider
        {
            readonly Dictionary<GraphNode,int> ids;
            readonly int[] neighbours;
            readonly byte[] edges;
            public Traversal(Dictionary<GraphNode,int> nodeIds,int[] adjacent,byte[] groundEdges)
            {ids=nodeIds;neighbours=adjacent;edges=groundEdges;}
            public bool CanTraverse(Pathfinding.Path path,GraphNode node)=>DefaultITraversalProvider.CanTraverse(path,node);
            public bool CanTraverse(Pathfinding.Path path,GraphNode from,GraphNode to)
            {
                if(!CanTraverse(path,to)||!ids.TryGetValue(from,out int a)||!ids.TryGetValue(to,out int b))return false;
                for(int edge=0;edge<4;edge++)if(neighbours[a*4+edge]==b)return (edges[a]&(1<<edge))!=0;
                return false;
            }
        }
        readonly Queue<Request> requests=new Queue<Request>();
        WorldData world;
        AstarPath engine;
        PointGraph graph;
        PointNode[] nodes;
        Dictionary<GraphNode,int> ids;
        Traversal traversal;
        Structure[] structures;
        FlatTerrainFacts facts;
        int revision=-1,terrainRevision=-1,generation;
        bool disposed;
        public int PendingRequests {get;private set;}
        public int CompletedPaths {get;private set;}
        public int QueuedRequests=>requests.Count;
        public bool Ready=>graph!=null&&!disposed;
        public bool CanRequestPath=>Ready&&requests.Count<32;
        public int WorkerCount=>engine?AstarPath.CalculateThreadCount(engine.threadCount):0;

        public void Initialize(WorldData data)
        {
            world=data??throw new ArgumentNullException(nameof(data));
            engine=FlatPlanetPathfinder.SharedEngine(transform);
            Rebuild();
        }
        public void RequestPath(int start,int goal,bool flying,Action<int[]> completed)
        {
            if(completed==null)throw new ArgumentNullException(nameof(completed));
            if(!CanRequestPath||world==null||start<0||goal<0||start>=world.Count||goal>=world.Count)
            {completed(Array.Empty<int>());return;}
            requests.Enqueue(new Request{Start=start,Goal=goal,Flying=flying,Complete=completed});
        }
        bool Changed()
        {
            if(terrainRevision!=world.TerrainRevision||facts!=world.Facts)return true;
            if(revision==world.Revision)return false;
            revision=world.Revision;
            for(int i=0;i<structures.Length;i++)if(structures[i]!=world.Buildings[i])return true;
            return false;
        }
        void Rebuild()
        {
            generation++;
            var nextNodes=new PointNode[world.Count];
            var nextIds=new Dictionary<GraphNode,int>(world.Count);
            var neighbours=new int[world.Count*4];var edges=new byte[world.Count];
            graph=FlatPlanetPathfinder.ReplacePointGraph(engine,graph,next=>
            {
                next.name="ORBIS wildlife sphere";
                var directions=new PlanetVector[world.Count];
                for(int i=0;i<world.Count;i++)
                {
                    var location=PlanetCoordinateSystem.FromMapUV((i%world.Width+.5)/world.Width,(i/world.Width+.5)/world.Height);
                    var d=PlanetCoordinateSystem.SurfaceNormal(location);directions[i]=d;
                    nextNodes[i]=next.AddNode((Int3)new Vector3((float)d.X*1000,(float)d.Y*1000,(float)d.Z*1000));
                    nextNodes[i].Tag=FlatWildlifeSimulation.IsGroundTile(world,i)?0u:1u;
                    nextIds.Add(nextNodes[i],i);
                }
                for(int i=0;i<world.Count;i++)for(int edge=0;edge<4;edge++)
                {
                    int adjacent=world.SphereNeighbor(i,edge);neighbours[i*4+edge]=adjacent;
                    if(FlatWildlifeSimulation.CanStep(world,i,adjacent,false))edges[i]|=(byte)(1<<edge);
                    if(adjacent<=i)continue;
                    double arc=PlanetLocalProjection.Distance(directions[i],directions[adjacent],1000);
                    GraphNode.Connect(nextNodes[i],nextNodes[adjacent],(uint)Math.Max(1,Math.Round(arc*1000)));
                }
            });
            nodes=nextNodes;ids=nextIds;traversal=new Traversal(ids,neighbours,edges);
            structures=(Structure[])world.Buildings.Clone();facts=world.Facts;
            terrainRevision=world.TerrainRevision;revision=world.Revision;
        }
        void Update()
        {
            if(disposed||world==null||!engine)return;
            if(Changed())Rebuild();
            for(int budget=0;budget<2&&PendingRequests<8&&requests.Count>0;budget++)Submit(requests.Dequeue());
        }
        void Submit(Request request)
        {
            if(!request.Flying&&(!FlatWildlifeSimulation.IsGroundTile(world,request.Start)||!FlatWildlifeSimulation.IsGroundTile(world,request.Goal)))
            {request.Complete(Array.Empty<int>());return;}
            int version=generation;
            var nodeIds=ids;
            var expectedStart=nodes[request.Start];var expectedEnd=nodes[request.Goal];
            PendingRequests++;
            var path=ABPath.Construct((Vector3)expectedStart.position,(Vector3)expectedEnd.position,completed=>
            {
                PendingRequests--;
                if(disposed||version!=generation){request.Complete(Array.Empty<int>());return;}
                if(completed.error||completed.path.Count==0||completed.path[0]!=expectedStart||completed.path[completed.path.Count-1]!=expectedEnd)
                {request.Complete(Array.Empty<int>());return;}
                var route=new int[completed.path.Count];
                for(int i=0;i<route.Length;i++)route[i]=nodeIds[completed.path[i]];
                for(int i=1;i<route.Length;i++)if(!FlatWildlifeSimulation.CanStep(world,route[i-1],route[i],request.Flying))
                {request.Complete(Array.Empty<int>());return;}
                CompletedPaths++;request.Complete(route);
            });
            path.nnConstraint.graphMask=GraphMask.FromGraph(graph);
            path.enabledTags=request.Flying?-1:1;
            if(!request.Flying)path.traversalProvider=traversal;
            path.heuristic=Heuristic.None;
            AstarPath.StartPath(path);
        }
        void OnDestroy()
        {
            disposed=true;generation++;
            while(requests.Count>0)requests.Dequeue().Complete(Array.Empty<int>());
            FlatPlanetPathfinder.ReleasePointGraph(engine,graph);graph=null;
        }
    }
}
