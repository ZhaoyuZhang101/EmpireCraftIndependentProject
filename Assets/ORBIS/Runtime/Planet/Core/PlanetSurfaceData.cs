using System;
using Orbis.Flat;

namespace Orbis.Planet
{
    public struct PlanetWaterSample
    {
        public int Tile;
        public double GroundHeight, SurfaceHeight, Depth, Coverage, OceanWeight, Discharge;
        public PlanetVector Position, FlowVelocity;
        public bool Walkable;
    }

    // Read-only physical adapter: one WorldData, metres for position/height/velocity.
    // This is distinct from the legacy unit-sphere artistic height exaggeration.
    public sealed class PlanetSurfaceData
    {
        const double MinimumRadiusMetres=2048;
        public readonly WorldData World;
        public readonly PlanetVector Center;
        public readonly double Radius;
        float[] heights;int revision=int.MinValue;
        double southHeight,northHeight;
        public PlanetSurfaceData(WorldData world,double radius,PlanetVector center)
        {
            if(world==null||world.Facts==null)throw new ArgumentException("Analysed WorldData is required.");
            if(!(radius>MinimumRadiusMetres)||double.IsInfinity(radius))throw new ArgumentOutOfRangeException(nameof(radius));
            World=world;Radius=radius;Center=center;Refresh();
        }
        public void Refresh()
        {
            if(revision==World.TerrainRevision)return;
            if(heights==null||heights.Length!=World.Count)heights=new float[World.Count];
            southHeight=northHeight=0;
            for(int i=0;i<World.Count;i++)heights[i]=World.Elevation[i];
            for(int x=0;x<World.Width;x++){southHeight+=heights[x];northHeight+=heights[(World.Height-1)*World.Width+x];}
            southHeight/=World.Width;northHeight/=World.Width;revision=World.TerrainRevision;
        }
        public PlanetLocation TileLocation(int tile)
        {
            if(tile<0||tile>=World.Count)throw new ArgumentOutOfRangeException(nameof(tile));
            return PlanetCoordinateSystem.FromMapUV((tile%World.Width+.5)/World.Width,(tile/World.Width+.5)/World.Height);
        }
        public PlanetLocation AtLocal(PlanetTangentFrame frame,double east,double north)
        {
            // Ray from the centre through the tangent plane; works across the date line and poles.
            return PlanetCoordinateSystem.FromWorld(Center+frame.Up*Radius+frame.East*east+frame.North*north,Center,Radius);
        }
        public PlanetTangentFrame FrameAt(PlanetLocation location)
        {
            location.Altitude=Sample(location).GroundHeight;
            return PlanetCoordinateSystem.LocalFrame(location,Center,Radius);
        }
        public PlanetWaterSample Sample(PlanetVector position)
        { return Sample(PlanetCoordinateSystem.FromWorld(position,Center,Radius)); }
        public PlanetWaterSample Sample(PlanetLocation location)
        {
            Refresh();
            double u=PlanetCoordinateSystem.MapU(location),v=Math.Max(0,Math.Min(1,PlanetCoordinateSystem.MapV(location)));
            float x=(float)(u*World.Width),y=(float)Math.Min(World.Height-.0001,v*World.Height);
            int tile=World.Index((int)Math.Floor(x),(int)Math.Floor(y));
            double height=FlatSurfaceSampling.Sample(heights,World.Width,World.Height,x,y);
            double polar=FlatSurfaceSampling.PolarRingWeight(v,World.Height);
            height=(v<.5?southHeight:northHeight)*(1-polar)+height*polar;
            float depth;double coverage=FlatWaterGeometry.Coverage(World,x,y,out depth);
            bool area=FlatTerrainAnalysis.AreaWater(World.Water[tile]);
            // Preserve ocean/lake levels. Narrow channels use the same signed coverage as rendering.
            double surface=coverage>.001?(area?Math.Max(height,World.WaterLevel[tile]):height+Math.Max(0,depth)):height;
            double ocean=FlatTerrainAnalysis.Ocean(World.Water[tile])?1:0;
            int parent=World.Downstream[tile];
            PlanetVector velocity=new PlanetVector();
            if(coverage>.001&&parent>=0&&parent<World.Count)
            {
                var frame=PlanetCoordinateSystem.LocalFrame(location,Center,Radius);
                var target=PlanetCoordinateSystem.ToWorld(TileLocation(parent),Center,Radius);
                var delta=frame.DirectionToLocal(target-PlanetCoordinateSystem.ToWorld(location,Center,Radius));
                double length=Math.Sqrt(delta.X*delta.X+delta.Y*delta.Y);
                if(length>1e-6)
                {
                    double slope=Math.Max(0,(World.WaterLevel[tile]-World.WaterLevel[parent])/length);
                    double speed=Math.Min(5,Math.Sqrt(9.81*Math.Max(.01,depth)*slope));
                    velocity=frame.DirectionToWorld(new PlanetVector(delta.X/length*speed,delta.Y/length*speed,0));
                }
                if(FlatTerrainAnalysis.Ocean(World.Water[parent]))ocean=.5;
            }
            location.Altitude=surface;
            bool bridge=World.Buildings[tile]==Structure.Bridge;
            return new PlanetWaterSample { Tile=tile,GroundHeight=height,SurfaceHeight=surface,Depth=Math.Max(0,surface-height),
                Coverage=coverage,OceanWeight=ocean,Discharge=World.Runoff[tile],FlowVelocity=velocity,
                Position=PlanetCoordinateSystem.ToWorld(location,Center,Radius),
                Walkable=bridge||(World.Walkable(tile)&&(coverage<.5||surface-height<=.6)) };
        }
    }
}
