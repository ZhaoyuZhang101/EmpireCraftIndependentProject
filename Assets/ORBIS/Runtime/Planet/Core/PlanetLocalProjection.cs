using System;

namespace Orbis.Planet
{
    /// <summary>Double precision geodesic log/exp map. Radius units belong to the caller.</summary>
    public sealed class PlanetLocalProjection
    {
        public PlanetTangentFrame Frame { get; private set; }
        public double Radius { get; private set; }
        public PlanetLocalProjection(PlanetLocation center, double radius=1)
        {
            if (!(radius>0)) throw new ArgumentOutOfRangeException(nameof(radius));
            Radius=radius;Frame=PlanetCoordinateSystem.LocalFrame(center,new PlanetVector(),radius);
        }
        static PlanetVector Cross(PlanetVector a,PlanetVector b)
        {return new PlanetVector(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);}
        static PlanetVector Unit(PlanetVector p)
        {return p*(1/Math.Sqrt(PlanetVector.Dot(p,p)));}
        // Align the local chart with the viewport, including transported camera roll.
        public void AlignEast(PlanetVector viewportRight)
        {
            var up=Frame.Up;var right=viewportRight-up*PlanetVector.Dot(viewportRight,up);
            if(PlanetVector.Dot(right,right)<1e-20)return;
            right=Unit(right);
            Frame=new PlanetTangentFrame{Up=up,East=right,North=Unit(Cross(up,right)),Origin=up*Radius};
        }
        public PlanetVector Log(PlanetVector direction)
        {
            var p=Unit(direction);double c=Math.Max(-1,Math.Min(1,PlanetVector.Dot(p,Frame.Up)));
            var t=p-Frame.Up*c;double s=Math.Sqrt(PlanetVector.Dot(t,t));
            if(s<1e-12) return c>0?new PlanetVector():new PlanetVector(Math.PI*Radius,0,0);
            double factor=Radius*Math.Atan2(s,c)/s;
            return new PlanetVector(PlanetVector.Dot(t,Frame.East)*factor,PlanetVector.Dot(t,Frame.North)*factor,0);
        }
        public PlanetVector Exp(double x,double y)
        {
            double r=Math.Sqrt(x*x+y*y),a=r/Radius;
            if(r<1e-14)return Frame.Up;
            return Unit(Frame.Up*Math.Cos(a)+(Frame.East*x+Frame.North*y)*(Math.Sin(a)/r));
        }
        // Parallel transport avoids a longitude-based frame flip when traversing a pole.
        public void Move(double east,double north)
        {
            var old=Frame;var up=Exp(east,north);double c=PlanetVector.Dot(old.Up,up);
            var axis=Cross(old.Up,up);
            var right=old.East+Cross(axis,old.East)+Cross(axis,Cross(axis,old.East))*(1/Math.Max(1e-12,1+c));
            right=Unit(right-up*PlanetVector.Dot(right,up));
            Frame=new PlanetTangentFrame{Up=up,East=right,North=Unit(Cross(up,right)),Origin=up*Radius};
        }
        public PlanetLocation Center {get{return PlanetCoordinateSystem.FromWorld(Frame.Up,new PlanetVector(),1);}}
        public static double Distance(PlanetVector a,PlanetVector b,double radius)
        {
            a=Unit(a);b=Unit(b);var cross=Cross(a,b);
            return radius*Math.Atan2(Math.Sqrt(PlanetVector.Dot(cross,cross)),PlanetVector.Dot(a,b));
        }
    }
}
