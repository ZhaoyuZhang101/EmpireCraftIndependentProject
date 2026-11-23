using System;
using UnityEngine;
using Pathfinding;
using Orbis.Planet;
namespace Orbis.Flat
{
    /// <summary>A* Pro point graph in sphere space, never in the moving render projection.</summary>
    public sealed class FlatPlanetPathfinder : MonoBehaviour
    {
        sealed class RouteNode : PointNode
        {
            public readonly int Tile;
            public RouteNode(AstarPath engine,int tile):base(engine){Tile=tile;}
        }
        AstarPath engine;PointGraph graph;WorldData source;int tileRevision=-1,terrainRevision=-1;
        RouteNode[] nodes;
        public static AstarPath SharedEngine(Transform owner)
        {
            if(AstarPath.active)return AstarPath.active;
            var go=new GameObject("ORBIS spherical AStar workers");go.SetActive(false);go.transform.SetParent(owner.root,false);
            var engine=go.AddComponent<AstarPath>();engine.scanOnStartup=false;engine.showNavGraphs=false;
            engine.threadCount=ThreadCount.AutomaticLowLoad;
            engine.logPathResults=PathLog.None;
            go.SetActive(true);return engine;
        }
        internal static PointGraph ReplacePointGraph(AstarPath engine,PointGraph previous,Action<PointGraph> populate)
        {
            PointGraph next=null;
            try
            {
                engine.AddWorkItem(()=>
                {
                    next=(PointGraph)engine.data.AddGraph(typeof(PointGraph));
                    populate(next);
                    next.RebuildNodeLookup();
                });
                engine.FlushWorkItems();
                // Astar 5 allows adding a graph in a work item. Removing one
                // takes its own pathfinding lock and must run outside that item.
                ReleasePointGraph(engine,previous);
                return next;
            }
            catch{ReleasePointGraph(engine,next);throw;}
        }
        internal static void ReleasePointGraph(AstarPath engine,PointGraph graph)
        {
            if(engine&&engine==AstarPath.active&&graph!=null&&graph.active==engine)engine.data.RemoveGraph(graph);
        }
        internal static PlanetVector Direction(WorldData w,int i)=>PlanetCoordinateSystem.SurfaceNormal(PlanetCoordinateSystem.FromMapUV((i%w.Width+.5)/w.Width,(i/w.Width+.5)/w.Height));
        internal static double ArcLength(WorldData w,int a,int b)=>PlanetLocalProjection.Distance(Direction(w,a),Direction(w,b),1000);
        public int[] Find(WorldData world,int start,int goal)
        {
            if(!world.Walkable(start)||!world.Walkable(goal))return new int[0];
            if(!engine)engine=SharedEngine(transform);
            if(source!=world||tileRevision!=world.TileRevision||terrainRevision!=world.TerrainRevision)
            {
                var nextNodes=new RouteNode[world.Count];
                // Horizontal arc length is constant along a parallel. A polar crossing
                // has the same arc as one ordinary meridian step on this cell-centred grid.
                double northSouth=ArcLength(world,0,world.Width);
                var eastWest=new double[world.Height];
                for(int y=0;y<world.Height;y++)eastWest[y]=ArcLength(world,y*world.Width,y*world.Width+1);
                graph=ReplacePointGraph(engine,graph,next=>
                {
                    for(int i=0;i<world.Count;i++)
                    {
                        if(!world.Walkable(i))continue;var d=Direction(world,i);
                        nextNodes[i]=next.AddNode(new RouteNode(engine,i),(Int3)new Vector3((float)d.X*1000,(float)d.Y*1000,(float)d.Z*1000));
                    }
                    for(int i=0;i<world.Count;i++)if(nextNodes[i]!=null)for(int edge=0;edge<4;edge++)
                    {
                        int j=world.SphereNeighbor(i,edge);if(j<=i||nextNodes[j]==null||!FlatGeographicSurface.GroundEdge(world,i,j))continue;
                        double arc=edge<2?eastWest[i/world.Width]:northSouth;
                        double weight=1+Math.Abs(world.Elevation[i]-world.Elevation[j])/600.0;
                        if(world.Water[i]==WaterKind.River||world.Water[j]==WaterKind.River)weight+=2.5;
                        if((world.Buildings[i]==Structure.Road||world.Buildings[i]==Structure.Bridge)&&(world.Buildings[j]==Structure.Road||world.Buildings[j]==Structure.Bridge))weight=.5;
                        GraphNode.Connect(nextNodes[i],nextNodes[j],(uint)Math.Max(1,Math.Round(arc*weight*1000)));
                    }
                });
                nodes=nextNodes;source=world;tileRevision=world.TileRevision;terrainRevision=world.TerrainRevision;
            }
            var path=ABPath.Construct((Vector3)nodes[start].position,(Vector3)nodes[goal].position);
            path.nnConstraint.graphMask=GraphMask.FromGraph(graph);
            path.heuristic=Heuristic.None; // Roads may cost less than chord distance: no overestimating heuristic.
            AstarPath.StartPath(path);path.BlockUntilCalculated();
            if(path.error)return new int[0];var result=new int[path.path.Count];for(int i=0;i<result.Length;i++)result[i]=((RouteNode)path.path[i]).Tile;return result;
        }
        void OnDestroy()
        {
            ReleasePointGraph(engine,graph);graph=null;
        }
    }
}
