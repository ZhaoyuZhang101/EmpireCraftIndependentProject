using System;

namespace Orbis.Planet
{
    /// <summary>Double precision local maps. Radius units belong to the caller.</summary>
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
            var unit=Unit(direction);
            double denominator=1+PlanetVector.Dot(unit,Frame.Up);
            if(denominator>1e-8)
            {
                double scale=2*Radius/denominator;
                return new PlanetVector(PlanetVector.Dot(unit,Frame.East)*scale,PlanetVector.Dot(unit,Frame.North)*scale,0);
            }
            var point=Log(direction);
            double distance=Math.Sqrt(point.X*point.X+point.Y*point.Y);
            if(distance<1e-14)return new PlanetVector();
            // Bound the antipodal singularity to finite renderer coordinates.
            double angle=Math.Min(Math.PI-1e-6,distance/Radius);
            double factor=2*Radius*Math.Tan(angle*.5)/distance;
            return new PlanetVector(point.X*factor,point.Y*factor,0);
        }
        // Screen radius s (in planet radii) has one angular distance throughout the unfold.
        // Its derivative (1-t)+t/(1+s²/4) is positive on the entire visible disk.
        static double CentralAngle(double s,double blend)
        {return (1-blend)*s+blend*2*Math.Atan(s*.5);}

        public PlanetVector LocalExp(double x,double y,double blend)
        {
            if(blend<=0)return Exp(x,y);
            if(blend>=1)return StereographicExp(x,y);
            double distance=Math.Sqrt(x*x+y*y);
            if(distance<1e-14)return Frame.Up;
            double angle=CentralAngle(distance/Radius,blend);
            return Unit(Frame.Up*Math.Cos(angle)+(Frame.East*x+Frame.North*y)*(Math.Sin(angle)/distance));
        }

        public PlanetVector LocalLog(PlanetVector direction,double blend)
        {
            if(blend>=1)return StereographicLog(direction);
            var point=Log(direction);
            if(blend<=0)return point;
            double distance=Math.Sqrt(point.X*point.X+point.Y*point.Y);
            if(distance<1e-14)return point;
            double angle=distance/Radius;
            // The equidistant and stereographic radii bracket the unique inverse.
            // The linear term also bounds the radius for a point at the antipode.
            double lower=angle,upper=Math.Min(2*Math.Tan(Math.Min(angle,Math.PI)*.5),angle/(1-blend));
            double s=lower;
            for(int i=0;i<40;i++)
            {
                double error=CentralAngle(s,blend)-angle;
                if(Math.Abs(error)<2e-14)break;
                if(error<0)lower=s;else upper=s;
                double next=s-error/(1-blend+blend/(1+s*s*.25));
                s=next>lower&&next<upper?next:(lower+upper)*.5;
            }
            return point*(s/angle);
        }

        // Equal-angular geographic address chart; its coordinates describe stored cells.
        public PlanetVector GeographicExp(double x,double y)
        {
            var center=Center;
            return PlanetCoordinateSystem.SurfaceNormal(new PlanetLocation(center.Longitude+x/Radius,center.Latitude+y/Radius));
        }
        public PlanetVector GeographicLog(PlanetVector direction)
        {
            var center=Center;
            var target=PlanetCoordinateSystem.FromWorld(direction,new PlanetVector(),1);
            double x=PlanetCoordinateSystem.NearestOffset(target.Longitude-center.Longitude,PlanetCoordinateSystem.Tau);
            double y=target.Latitude-center.Latitude;
            var point=new PlanetVector(x*Radius,y*Radius,0);
            // The nearest reflected copy keeps routes and anchors continuous over a pole.
            double reflectedX=PlanetCoordinateSystem.NearestOffset(x+Math.PI,PlanetCoordinateSystem.Tau);
            for(int copy=0;copy<2;copy++)
            {
                double latitude=(copy==0?Math.PI:-Math.PI)-target.Latitude;
                var candidate=new PlanetVector(reflectedX*Radius,(latitude-center.Latitude)*Radius,0);
                if(PlanetVector.Dot(candidate,candidate)<PlanetVector.Dot(point,point))point=candidate;
            }
            return point;
        }
        public bool TryAnchorGeographicNorthUp(PlanetVector anchor,double x,double y)
        {
            var center=Center;
            var before=GeographicLog(anchor);
            double latitude=center.Latitude+(before.Y-y)/Radius;
            if(Math.Abs(latitude)>Math.PI*.5+1e-12)return false;
            latitude=Math.Max(-Math.PI*.5,Math.Min(Math.PI*.5,latitude));
            var location=new PlanetLocation(center.Longitude+(before.X-x)/Radius,latitude);
            Frame=PlanetCoordinateSystem.LocalFrame(location,new PlanetVector(),Radius);
            return true;
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

        // Preserve the whole tangent frame under the shortest rotation from one direction to another.
        static PlanetVector Rotate(PlanetVector point,PlanetVector axis,double cosine)
        {
            var cross=Cross(axis,point);
            return point+cross+Cross(axis,cross)*(1/(1+cosine));
        }
        void Transport(PlanetVector from,PlanetVector to)
        {
            var axis=Cross(from,to);
            double cosine=PlanetVector.Dot(from,to);
            PlanetVector up,right;
            if(cosine<-.999999)
            {
                // Two well-conditioned rotations also define an exact half-turn.
                var middle=Cross(axis,from);
                if(PlanetVector.Dot(middle,middle)<1e-24)
                    middle=Math.Abs(from.Y)<.9?Cross(from,new PlanetVector(0,1,0)):Cross(from,new PlanetVector(1,0,0));
                middle=Unit(middle);
                var first=Cross(from,middle);var second=Cross(middle,to);
                double remaining=PlanetVector.Dot(middle,to);
                up=Rotate(Rotate(Frame.Up,first,0),second,remaining);
                right=Rotate(Rotate(Frame.East,first,0),second,remaining);
            }
            else
            {
                up=Rotate(Frame.Up,axis,cosine);
                right=Rotate(Frame.East,axis,cosine);
            }
            up=Unit(up);
            right=Unit(right-up*PlanetVector.Dot(right,up));
            Frame=new PlanetTangentFrame{Up=up,East=right,North=Unit(Cross(up,right)),Origin=up*Radius};
        }
        // The immediate polar cap has no preferred meridian. From 2° to 8° away
        // from either pole, progressively allow the camera to turn toward geographic north.
        public double NorthAlignmentWeight
        {
            get
            {
                double horizontal=Math.Sqrt(Frame.Up.X*Frame.Up.X+Frame.Up.Z*Frame.Up.Z);
                double t=Math.Max(0,Math.Min(1,(horizontal-.0348994967)/(.139173101-.0348994967)));
                return t*t*(3-2*t);
            }
        }
        double NorthTurn()
        {
            var east=new PlanetVector(Frame.Up.Z,0,-Frame.Up.X);
            double sine=PlanetVector.Dot(east,Frame.North),cosine=PlanetVector.Dot(east,Frame.East);
            return Math.Abs(sine)<1e-14&&cosine<0?Math.PI:Math.Atan2(sine,cosine);
        }
        public bool AlignNorth(double maximumTurn)
        {
            double limit=maximumTurn*NorthAlignmentWeight;
            if(limit<=0)return false;
            double turn=Math.Max(-limit,Math.Min(limit,NorthTurn()));
            if(Math.Abs(turn)<1e-12)return false;
            AlignEast(Frame.East*Math.Cos(turn)+Frame.North*Math.Sin(turn));
            return true;
        }
        public void AnchorStereographic(PlanetVector anchor,double x,double y,bool preserveHeading=false)
        {
            anchor=Unit(anchor);
            // Most latitudes have an exact north-up solution connected to the current
            // one. At the polar branch boundary, rotate the frame and anchor together.
            if(!preserveHeading&&NorthAlignmentWeight>=1&&Math.Abs(NorthTurn())<1e-8)
            {
                var before=Frame;
                if(TryAnchorStereographicNorthUp(anchor,x,y)&&NorthAlignmentWeight>=1&&
                    PlanetVector.Dot(before.East,Frame.East)>.95)return;
                Frame=before;
            }
            Transport(StereographicExp(x,y),anchor);
        }
        // Pure parallel transport remains available to callers following a geodesic.
        public void Move(double east,double north){Transport(Frame.Up,Exp(east,north));}
        public void MoveChart(double x,double y,double blend)
        {
            var direction=LocalExp(x,y,blend);
            double arc=Distance(Frame.Up,direction,1);
            Transport(Frame.Up,direction);
            AlignNorth(Math.Min(Math.PI/12,12*arc));
        }
        public PlanetLocation Center {get{return PlanetCoordinateSystem.FromWorld(Frame.Up,new PlanetVector(),1);}}
        public static double Distance(PlanetVector a,PlanetVector b,double radius)
        {
            a=Unit(a);b=Unit(b);var cross=Cross(a,b);
            return radius*Math.Atan2(Math.Sqrt(PlanetVector.Dot(cross,cross)),PlanetVector.Dot(a,b));
        }
    }

    // Shared pure navigation math for FlatScrollNavigator and standalone CPU regressions.
    public static class PlanetScrollNavigationMath
    {
        // Telephoto external-viewpoint rig (18 deg vs legacy 42 deg wide-angle, HalfTan-invariant):
        // expands visible globe hemisphere from 71.2 deg to 82.0 deg (and unfold threshold from
        // 51.3 deg to 67.2 deg), cutting perspective limb/corner distortion by 2.42x while keeping
        // identical screen planet size (88.8%), EntryPixels, FoldZoom/HomeZoom ratio, and cloud parity.
        public const double Fov = 18.0, Start = 2.58, EndGap = .5332, ZoomRate = .075, ZoomOmega = 18, MaxAltitude = 13.35, HomeAltitude = 6.18;
        public const double MaxLocalRadius = Math.PI - .0001;

        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static double Clamp(double value, double min, double max) => value < min ? min : (value > max ? max : value);
        static double SmoothStep(double edge0, double edge1, double x)
        {
            double t = Clamp((x - edge0) / (edge1 - edge0), 0, 1);
            return t * t * (3 - 2 * t);
        }

        public static double ZoomFor(double altitude) => Math.Log(altitude / EndGap) / ZoomRate;
        public static double Altitude(double zoom) => EndGap * Math.Exp(zoom * ZoomRate);
        public static double MorphForAltitude(double altitude)
        {
            if (altitude >= Start - 1) return 0;
            if (altitude <= EndGap) return 1;
            return (Start - 1 - altitude) / (Start - 1 - EndGap);
        }

        // Keep eye-to-surface distance equal to gap throughout the unfold so perspective/ortho
        // projection blending maintains exact central scale and never stalls at morph -> 0.
        public static double CameraDistanceForUnfold(double distance, double altitude, double morph)
        {
            if (morph <= 0) return distance;
            return 1 + Math.Min(altitude, Start - 1);
        }

        // Covers widescreen viewports (16:9, 21:9, 32:9, 5.33:1), maximum relief displacement/shrink,
        // and off-axis Focus() transitions, while preserving the full sphere silhouette at morph == 0.
        public static double ComputeLocalRadius(double orthographicSize, double aspect, double morph,
            double localHeightSpan, double reliefTilt, double reliefCameraHeight, double offAxisRadians = 0)
        {
            double m = Finite(morph) ? Clamp(morph, 0, 1) : 0;
            if (m <= 0) return MaxLocalRadius;
            double safeOrtho = Finite(orthographicSize) ? Math.Max(.002, orthographicSize) : .085;
            double safeAspect = Finite(aspect) ? Clamp(aspect, .25, 16) : 16.0 / 9.0;
            double safeOffAxis = Finite(offAxisRadians) ? Clamp(offAxisRadians, 0, Math.PI) : 0;
            double corner = safeOrtho * Math.Sqrt(1 + safeAspect * safeAspect);
            double reliefPad = localHeightSpan * (reliefTilt + corner / Math.Max(.02, reliefCameraHeight));
            double flatRequired = Math.Max(.04, (corner + reliefPad) * 1.35 + safeOffAxis * 1.25);
            // Retain dome coverage until the orthographic projection blend starts at morph == 0.62.
            double shrink = SmoothStep(.62, 1, m);
            double blended = MaxLocalRadius * (1 - shrink) + Math.Min(MaxLocalRadius, flatRequired) * shrink;
            return Clamp(Math.Max(blended, flatRequired), .04, MaxLocalRadius);
        }

        // Fast wheel direction reversal resets opposing spring velocity so rapid zoom gestures
        // never overshoot outward back into the circular globe silhouette or jitter at limits.
        public static double ApplyWheelTarget(double zoom, double targetZoom, ref double velocity,
            double wheel, double minZoom, double maxZoom)
        {
            double clampedWheel = Clamp(wheel, -24, 24);
            bool reversingTarget = (clampedWheel > 0 && targetZoom < zoom) || (clampedWheel < 0 && targetZoom > zoom);
            bool reversingVelocity = (clampedWheel > 0 && velocity < 0) || (clampedWheel < 0 && velocity > 0);
            if (reversingTarget || reversingVelocity) velocity = 0;
            double from = reversingTarget ? zoom : targetZoom;
            return Clamp(from + clampedWheel, minZoom, maxZoom);
        }

        public static void StepZoomSpring(ref double zoom, ref double velocity, double targetZoom,
            double minZoom, double maxZoom, double dt)
        {
            if (dt <= 0) return;
            double target = Clamp(targetZoom, minZoom, maxZoom);
            double delta = zoom - target;
            double temp = (velocity + ZoomOmega * delta) * dt;
            double decay = Math.Exp(-ZoomOmega * dt);
            velocity = (velocity - ZoomOmega * temp) * decay;
            zoom = target + (delta + temp) * decay;
            if (zoom < minZoom) { zoom = minZoom; velocity = 0; }
            else if (zoom > maxZoom) { zoom = maxZoom; velocity = 0; }
            if (Math.Abs(zoom - target) < .002 && Math.Abs(velocity) < .05) { zoom = target; velocity = 0; }
        }
    }
}
