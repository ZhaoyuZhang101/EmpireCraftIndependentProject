using System;

namespace Orbis.Planet
{
    // Canonical planet axes: +Y north, +Z longitude zero, +X longitude +90 degrees.
    // All distances use the caller's radius unit. Projection UV is an address, never a distance.
    public struct PlanetVector
    {
        public double X, Y, Z;
        public PlanetVector(double x, double y, double z) { X=x; Y=y; Z=z; }
        public static PlanetVector operator +(PlanetVector a, PlanetVector b) { return new PlanetVector(a.X+b.X,a.Y+b.Y,a.Z+b.Z); }
        public static PlanetVector operator -(PlanetVector a, PlanetVector b) { return new PlanetVector(a.X-b.X,a.Y-b.Y,a.Z-b.Z); }
        public static PlanetVector operator *(PlanetVector a, double b) { return new PlanetVector(a.X*b,a.Y*b,a.Z*b); }
        public static double Dot(PlanetVector a, PlanetVector b) { return a.X*b.X+a.Y*b.Y+a.Z*b.Z; }
    }

    public struct PlanetLocation
    {
        public double Longitude, Latitude, Altitude;
        public PlanetLocation(double longitude, double latitude, double altitude=0)
        { Longitude=longitude; Latitude=latitude; Altitude=altitude; }
    }

    public struct PlanetTangentFrame
    {
        public PlanetVector Origin, East, North, Up;
        // Subtract the double origin BEFORE handing the local result to a float renderer.
        public PlanetVector ToLocal(PlanetVector world)
        { return DirectionToLocal(world-Origin); }
        public PlanetVector ToWorld(PlanetVector local)
        { return Origin+DirectionToWorld(local); }
        public PlanetVector DirectionToLocal(PlanetVector direction)
        { return new PlanetVector(PlanetVector.Dot(direction,East),PlanetVector.Dot(direction,North),PlanetVector.Dot(direction,Up)); }
        public PlanetVector DirectionToWorld(PlanetVector local)
        { return East*local.X+North*local.Y+Up*local.Z; }
    }

    public static class PlanetCoordinateSystem
    {
        public const double Tau = Math.PI*2;
        public static double Wrap(double value, double period)
        { return value-Math.Floor(value/period)*period; }
        public static double NearestOffset(double delta, double period)
        { return Wrap(delta+period*.5,period)-period*.5; }
        public static PlanetLocation FromMapUV(double u, double v, double altitude=0)
        { return new PlanetLocation((Wrap(u,1)-.5)*Tau,(Math.Max(0,Math.Min(1,v))-.5)*Math.PI,altitude); }
        public static double MapU(PlanetLocation location) { return Wrap(location.Longitude/Tau+.5,1); }
        public static double MapV(PlanetLocation location) { return location.Latitude/Math.PI+.5; }
        public static PlanetVector SurfaceNormal(PlanetLocation location)
        {
            double c=Math.Cos(location.Latitude);
            return new PlanetVector(c*Math.Sin(location.Longitude),Math.Sin(location.Latitude),c*Math.Cos(location.Longitude));
        }
        public static PlanetVector ToWorld(PlanetLocation location, PlanetVector center, double radius)
        {
            if(!(radius>0))throw new ArgumentOutOfRangeException(nameof(radius));
            return center+SurfaceNormal(location)*(radius+location.Altitude);
        }
        public static PlanetLocation FromWorld(PlanetVector world, PlanetVector center, double radius)
        {
            if(!(radius>0))throw new ArgumentOutOfRangeException(nameof(radius));
            PlanetVector p=world-center;
            double horizontal=Math.Sqrt(p.X*p.X+p.Z*p.Z),length=Math.Sqrt(PlanetVector.Dot(p,p));
            if(!(length>0))throw new ArgumentException("Planet centre has no surface coordinate.",nameof(world));
            return new PlanetLocation(Math.Atan2(p.X,p.Z),Math.Atan2(p.Y,horizontal),length-radius);
        }
        public static PlanetTangentFrame LocalFrame(PlanetLocation location, PlanetVector center, double radius)
        {
            double s=Math.Sin(location.Longitude),c=Math.Cos(location.Longitude);
            double sl=Math.Sin(location.Latitude),cl=Math.Cos(location.Latitude);
            return new PlanetTangentFrame { Origin=ToWorld(location,center,radius),
                Up=SurfaceNormal(location),East=new PlanetVector(c,0,-s),North=new PlanetVector(-sl*s,cl,-sl*c) };
        }
    }

    // Periodic map adapter for existing saves. Rebase after each move in double precision;
    // GPU coordinates stay bounded even after many laps. No world/chunk reload at longitude zero.
    public struct PeriodicMapPosition
    {
        public double X, Y;
        public void Set(double x, double y, int width, int height) { X=x;Y=y;Normalize(width,height); }
        public void Move(double x, double y, int width, int height) { X+=x;Y+=y;Normalize(width,height); }
        public void Normalize(int width, int height)
        { X=PlanetCoordinateSystem.Wrap(X,width);Y=Math.Max(.0001,Math.Min(height-.0001,Y)); }
        public double RenderX(double anchorX, int width)
        { return anchorX+PlanetCoordinateSystem.NearestOffset(X-anchorX,width); }
    }
}
