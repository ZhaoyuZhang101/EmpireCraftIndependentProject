using System;

namespace Orbis.Planet
{
    /// <summary>Double precision geodesic and stereographic maps. Radius units belong to the caller.</summary>
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
        public PlanetVector StereographicExp(double x,double y)
        {
            x/=Radius;
            y/=Radius;
            double squared=(x*x+y*y)*.25;
            return Unit((Frame.Up*(1-squared)+Frame.East*x+Frame.North*y)*(1/(1+squared)));
        }
        public PlanetVector StereographicLog(PlanetVector direction)
        {
            var point=Log(direction);
            double distance=Math.Sqrt(point.X*point.X+point.Y*point.Y);
            if(distance<1e-14)return new PlanetVector();
            // Bound the antipodal singularity to finite renderer coordinates.
            double angle=Math.Min(Math.PI-1e-6,distance/Radius);
            double factor=2*Radius*Math.Tan(angle*.5)/distance;
            return new PlanetVector(point.X*factor,point.Y*factor,0);
        }
        // Recenter the connected north-up solution for a geographic screen anchor.
        public bool TryAnchorStereographicNorthUp(PlanetVector anchor,double x,double y)
        {
            anchor=Unit(anchor);
            x/=Radius;
            y/=Radius;
            var reference=Center;
            double squared=(x*x+y*y)*.25;
            double radial=1-squared,denominator=1+squared;
            double meridian=Math.Sqrt(radial*radial+y*y);
            double latitude=reference.Latitude;
            if(meridian<1e-12)
            {
                if(Math.Abs(anchor.Y)>1e-12)return false;
            }
            else
            {
                double argument=denominator*anchor.Y/meridian;
                if(Math.Abs(argument)>1+1e-12)return false;
                double angle=Math.Asin(Math.Max(-1,Math.Min(1,argument)));
                var before=StereographicLog(anchor);
                double beforeX=before.X/Radius,beforeY=before.Y/Radius;
                // The meridian derivative identifies the continuous latitude branch.
                double branch=(1-(beforeX*beforeX+beforeY*beforeY)*.25)*Math.Cos(reference.Latitude)
                    -beforeY*Math.Sin(reference.Latitude);
                if(branch<0)angle=Math.PI-angle;
                latitude=PlanetCoordinateSystem.NearestOffset(angle-Math.Atan2(y,radial),PlanetCoordinateSystem.Tau);
                if(Math.Abs(latitude)>Math.PI*.5+1e-10)return false;
                latitude=Math.Max(-Math.PI*.5,Math.Min(Math.PI*.5,latitude));
            }
            double longitude=reference.Longitude;
            if(anchor.X*anchor.X+anchor.Z*anchor.Z>1e-24)
            {
                double horizontal=radial*Math.Cos(latitude)-y*Math.Sin(latitude);
                longitude=Math.Atan2(anchor.X,anchor.Z)-Math.Atan2(x,horizontal);
            }
            Frame=PlanetCoordinateSystem.LocalFrame(new PlanetLocation(longitude,latitude),new PlanetVector(),Radius);
            return true;
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
