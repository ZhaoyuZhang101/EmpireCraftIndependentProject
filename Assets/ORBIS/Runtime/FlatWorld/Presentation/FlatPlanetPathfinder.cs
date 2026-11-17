using System;
using System.Collections.Generic;
using UnityEngine;
using Pathfinding;
using Orbis.Planet;
namespace Orbis.Flat
{
    /// <summary>A* Pro point graph in sphere space, never in the moving render projection.</summary>
    public sealed class FlatPlanetPathfinder : MonoBehaviour
    {
        AstarPath engine;PointGraph graph;WorldData source;int revision=-1;
        PointNode[] nodes;readonly Dictionary<GraphNode,int> ids=new Dictionary<GraphNode,int>();
        public static AstarPath SharedEngine(Transform owner)
        {
            if(AstarPath.active)return AstarPath.active;
            var go=new GameObject("ORBIS spherical AStar workers");go.SetActive(false);go.transform.SetParent(owner.root,false);
            var engine=go.AddComponent<AstarPath>();engine.scanOnStartup=false;engine.showNavGraphs=false;
            engine.threadCount=ThreadCount.AutomaticLowLoad;
            engine.logPathResults=PathLog.None;
            go.SetActive(true);return engine;
        }
        static PlanetVector Direction(WorldData w,int i)=>PlanetCoordinateSystem.SurfaceNormal(PlanetCoordinateSystem.FromMapUV((i%w.Width+.5)/w.Width,(i/w.Width+.5)/w.Height));
        public int[] Find(WorldData world,int start,int goal)
        {
            if(!world.Walkable(start)||!world.Walkable(goal))return new int[0];
            if(!engine)engine=SharedEngine(transform);
            if(source!=world||revision!=world.Revision)
            {
                engine.AddWorkItem(()=>
                {
                    if(graph!=null)engine.data.RemoveGraph(graph);graph=engine.data.AddGraph(typeof(PointGraph)) as PointGraph;
                    nodes=new PointNode[world.Count];ids.Clear();
                    for(int i=0;i<world.Count;i++)
                    {
                        if(!world.Walkable(i))continue;var d=Direction(world,i);
                        nodes[i]=graph.AddNode((Int3)new Vector3((float)d.X*1000,(float)d.Y*1000,(float)d.Z*1000));ids[nodes[i]]=i;
                    }
                    for(int i=0;i<world.Count;i++)if(nodes[i]!=null)for(int edge=0;edge<4;edge++)
                    {
                        int j=world.SphereNeighbor(i,edge);if(j<=i||nodes[j]==null)continue;
                        double arc=PlanetLocalProjection.Distance(Direction(world,i),Direction(world,j),1000);
                        double weight=1+Math.Abs(world.Elevation[i]-world.Elevation[j])/600.0;
                        if(world.Water[i]==WaterKind.River||world.Water[j]==WaterKind.River)weight+=2.5;
                        if((world.Buildings[i]==Structure.Road||world.Buildings[i]==Structure.Bridge)&&(world.Buildings[j]==Structure.Road||world.Buildings[j]==Structure.Bridge))weight=.5;
                        GraphNode.Connect(nodes[i],nodes[j],(uint)Math.Max(1,Math.Round(arc*weight*1000)));
                    }
                    graph.RebuildNodeLookup();
                });
                engine.FlushWorkItems();source=world;revision=world.Revision;
            }
            var path=ABPath.Construct((Vector3)nodes[start].position,(Vector3)nodes[goal].position);
            path.nnConstraint.graphMask=GraphMask.FromGraph(graph);
            path.heuristic=Heuristic.None; // Roads may cost less than chord distance: no overestimating heuristic.
            AstarPath.StartPath(path);path.BlockUntilCalculated();
            if(path.error)return new int[0];var result=new int[path.path.Count];for(int i=0;i<result.Length;i++)result[i]=ids[path.path[i]];return result;
        }
        void OnDestroy()
        {
            var removed=graph;graph=null;
            if(engine&&engine==AstarPath.active&&removed!=null)engine.AddWorkItem(()=>engine.data.RemoveGraph(removed));
        }
    }
}
