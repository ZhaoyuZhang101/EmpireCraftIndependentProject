using System;
using System.Collections.Generic;
using UnityEngine;
using Pathfinding;

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
        sealed class WildlifeNode : PointNode
        {
            public readonly int Tile;
            public readonly Structure Building;
            public byte GroundEdges;
            public WildlifeNode(AstarPath engine,int tile,Structure building):base(engine)
            { Tile=tile;Building=building; }
        }
        sealed class Traversal : ITraversalProvider
        {
            readonly WorldData world;
            public Traversal(WorldData data){world=data;}
            public bool CanTraverse(Pathfinding.Path path,GraphNode node)=>DefaultITraversalProvider.CanTraverse(path,node);
            public bool CanTraverse(Pathfinding.Path path,GraphNode from,GraphNode to)
            {
                if(!CanTraverse(path,to)||!(from is WildlifeNode a)||!(to is WildlifeNode b))return false;
                for(int edge=0;edge<4;edge++)if(world.SphereNeighbor(a.Tile,edge)==b.Tile)return (a.GroundEdges&(1<<edge))!=0;
                return false;
            }
        }
        readonly Queue<Request> requests=new Queue<Request>();
        WorldData world;
        AstarPath engine;
        PointGraph graph;
        WildlifeNode[] nodes;
        readonly List<int> activeTiles=new List<int>();
        readonly List<int> missingTiles=new List<int>();
        uint[] eastWestCost;
        uint northSouthCost;
        const int LocalNodeBudget=256*1024;
        bool onDemand;
        Traversal traversal;
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
            // The animals choose destinations within eight steps. On large worlds only
            // materialize the actual spherical tiles around those decisions.
            onDemand=world.Count>1024*512;
            nodes=new WildlifeNode[world.Count];
            eastWestCost=new uint[world.Height];
            for(int y=0;y<world.Height;y++)
                eastWestCost[y]=ArcCost(y*world.Width,y*world.Width+1);
            northSouthCost=ArcCost(0,world.Width);
            traversal=new Traversal(world);
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
            if(revision==world.TileRevision)return false;
            revision=world.TileRevision;
            foreach(int tile in activeTiles)if(nodes[tile].Building!=world.Buildings[tile])return true;
            return false;
        }
        void Rebuild()
        {
            generation++;
            foreach(int tile in activeTiles)nodes[tile]=null;
            activeTiles.Clear();
            graph=FlatPlanetPathfinder.ReplacePointGraph(engine,graph,next=>
            {
                next.name="ORBIS wildlife sphere";
                next.optimizeForSparseGraph=onDemand;
                if(!onDemand)for(int i=0;i<world.Count;i++)AddTile(next,i);
            });
            facts=world.Facts;
            terrainRevision=world.TerrainRevision;revision=world.TileRevision;
        }
        uint ArcCost(int a,int b)=>(uint)Math.Max(1,Math.Round(1000*FlatPlanetPathfinder.ArcLength(world,a,b)));
        void AddTile(PointGraph target,int tile)
        {
            var d=FlatPlanetPathfinder.Direction(world,tile);
            var node=target.AddNode(new WildlifeNode(engine,tile,world.Buildings[tile]),
                (Int3)new Vector3((float)d.X*1000,(float)d.Y*1000,(float)d.Z*1000));
            nodes[tile]=node;activeTiles.Add(tile);
            node.Tag=FlatWildlifeSimulation.IsGroundTile(world,tile)?0u:1u;
            int row=tile/world.Width;
            for(int edge=0;edge<4;edge++)
            {
                int other=world.SphereNeighbor(tile,edge);
                var neighbour=nodes[other];
                if(neighbour==null)continue;
                GraphNode.Connect(node,neighbour,edge<2?eastWestCost[row]:northSouthCost);
                if(node.Tag!=0||neighbour.Tag!=0)continue;
                int reverse=edge>=2&&row==other/world.Width?edge:edge^1;
                if(FlatWildlifeSimulation.CanStep(world,tile,other,false))node.GroundEdges|=(byte)(1<<edge);
                if(FlatWildlifeSimulation.CanStep(world,other,tile,false))neighbour.GroundEdges|=(byte)(1<<reverse);
            }
        }
        void PrepareNeighbourhood(int tile)
        {
            int x=tile%world.Width,y=tile/world.Width,radius=FlatWildlifeSimulation.GoalSearchRadius;
            missingTiles.Clear();
            for(int dy=-radius;dy<=radius;dy++)
            {
                int row=y+dy,center=x;
                if(row<0){row=-1-row;center+=world.Width/2;}
                else if(row>=world.Height){row=2*world.Height-1-row;center+=world.Width/2;}
                for(int dx=-radius;dx<=radius;dx++)
                {
                    int candidate=world.Index(center+dx,row);
                    if(nodes[candidate]==null)missingTiles.Add(candidate);
                }
            }
            if(missingTiles.Count==0)return;
            engine.AddWorkItem(()=>{foreach(int candidate in missingTiles)AddTile(graph,candidate);});
            engine.FlushWorkItems();
        }
        void Update()
        {
            if(disposed||world==null||!engine)return;
            if(onDemand&&activeTiles.Count>=LocalNodeBudget&&PendingRequests>0)return;
            if(Changed()||onDemand&&activeTiles.Count>=LocalNodeBudget)Rebuild();
            for(int budget=0;budget<2&&PendingRequests<8&&requests.Count>0;budget++)Submit(requests.Dequeue());
        }
        void Submit(Request request)
        {
            if(!request.Flying&&(!FlatWildlifeSimulation.IsGroundTile(world,request.Start)||!FlatWildlifeSimulation.IsGroundTile(world,request.Goal)))
            {request.Complete(Array.Empty<int>());return;}
            if(onDemand)PrepareNeighbourhood(request.Start);
            int version=generation;
            var expectedStart=nodes[request.Start];var expectedEnd=nodes[request.Goal];
            if(expectedEnd==null){request.Complete(Array.Empty<int>());return;}
            PendingRequests++;
            var path=ABPath.Construct((Vector3)expectedStart.position,(Vector3)expectedEnd.position,completed=>
            {
                PendingRequests--;
                if(disposed||version!=generation){request.Complete(Array.Empty<int>());return;}
                if(completed.error||completed.path.Count==0||completed.path[0]!=expectedStart||completed.path[completed.path.Count-1]!=expectedEnd)
                {request.Complete(Array.Empty<int>());return;}
                var route=new int[completed.path.Count];
                for(int i=0;i<route.Length;i++)route[i]=((WildlifeNode)completed.path[i]).Tile;
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
