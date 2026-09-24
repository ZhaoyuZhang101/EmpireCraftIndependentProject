#include <cmath>
#include <stdexcept>
#include <iostream>
struct PlanetVector{PlanetVector():X(0),Y(0),Z(0){}

        double X, Y, Z;
        PlanetVector(double x, double y, double z) { X=x; Y=y; Z=z; }
        friend PlanetVector operator +(PlanetVector a, PlanetVector b) { return PlanetVector(a.X+b.X,a.Y+b.Y,a.Z+b.Z); }
        friend PlanetVector operator -(PlanetVector a, PlanetVector b) { return PlanetVector(a.X-b.X,a.Y-b.Y,a.Z-b.Z); }
        friend PlanetVector operator *(PlanetVector a, double b) { return PlanetVector(a.X*b,a.Y*b,a.Z*b); }
        static double Dot(PlanetVector a, PlanetVector b) { return a.X*b.X+a.Y*b.Y+a.Z*b.Z; }
    };
struct PlanetLocation{
        double Longitude, Latitude, Altitude;
        PlanetLocation(double longitude, double latitude, double altitude=0)
        { Longitude=longitude; Latitude=latitude; Altitude=altitude; }
    };
struct PlanetTangentFrame{
        PlanetVector Origin, East, North, Up;
        // Subtract the double origin BEFORE handing the local result to a float renderer.
        PlanetVector ToLocal(PlanetVector world)
        { return DirectionToLocal(world-Origin); }
        PlanetVector ToWorld(PlanetVector local)
        { return Origin+DirectionToWorld(local); }
        PlanetVector DirectionToLocal(PlanetVector direction)
        { return PlanetVector(PlanetVector::Dot(direction,East),PlanetVector::Dot(direction,North),PlanetVector::Dot(direction,Up)); }
        PlanetVector DirectionToWorld(PlanetVector local)
        { return East*local.X+North*local.Y+Up*local.Z; }
    };
struct PlanetCoordinateSystem{
        static constexpr double Tau = 3.1415926535897932384626433832795*2;
        static double Wrap(double value, double period)
        { return value-std::floor(value/period)*period; }
        static double NearestOffset(double delta, double period)
        { return Wrap(delta+period*.5,period)-period*.5; }
        static PlanetLocation FromMapUV(double u, double v, double altitude=0)
        { return PlanetLocation((Wrap(u,1)-.5)*Tau,(std::fmax(0,std::fmin(1,v))-.5)*3.1415926535897932384626433832795,altitude); }
        static double MapU(PlanetLocation location) { return Wrap(location.Longitude/Tau+.5,1); }
        static double MapV(PlanetLocation location) { return location.Latitude/3.1415926535897932384626433832795+.5; }
        static PlanetVector SurfaceNormal(PlanetLocation location)
        {
            double c=std::cos(location.Latitude);
            return PlanetVector(c*std::sin(location.Longitude),std::sin(location.Latitude),c*std::cos(location.Longitude));
        }
        static PlanetVector ToWorld(PlanetLocation location, PlanetVector center, double radius)
        {
            if(!(radius>0))throw std::runtime_error("invalid coordinate");
            return center+SurfaceNormal(location)*(radius+location.Altitude);
        }
        static PlanetLocation FromWorld(PlanetVector world, PlanetVector center, double radius)
        {
            if(!(radius>0))throw std::runtime_error("invalid coordinate");
            PlanetVector p=world-center;
            double horizontal=std::sqrt(p.X*p.X+p.Z*p.Z),length=std::sqrt(PlanetVector::Dot(p,p));
            if(!(length>0))throw std::runtime_error("invalid coordinate");
            return PlanetLocation(std::atan2(p.X,p.Z),std::atan2(p.Y,horizontal),length-radius);
        }
        static PlanetTangentFrame LocalFrame(PlanetLocation location, PlanetVector center, double radius)
        {
            double s=std::sin(location.Longitude),c=std::cos(location.Longitude);
            double sl=std::sin(location.Latitude),cl=std::cos(location.Latitude);
            PlanetTangentFrame frame;frame.Origin=ToWorld(location,center,radius);frame.Up=SurfaceNormal(location);frame.East=PlanetVector(c,0,-s);frame.North=PlanetVector(-sl*s,cl,-sl*c);return frame;
        }
    };
struct PeriodicMapPosition{
        double X, Y;
        void Set(double x, double y, int width, int height) { X=x;Y=y;Normalize(width,height); }
        void Move(double x, double y, int width, int height) { X+=x;Y+=y;Normalize(width,height); }
        void Normalize(int width, int height)
        { X=PlanetCoordinateSystem::Wrap(X,width);Y=std::fmax(.0001,std::fmin(height-.0001,Y)); }
        double RenderX(double anchorX, int width)
        { return anchorX+PlanetCoordinateSystem::NearestOffset(X-anchorX,width); }
    };
using C=PlanetCoordinateSystem;
static int checks=0;
void require(bool v){++checks;if(!v)throw std::runtime_error("coordinate assertion "+std::to_string(checks));}
double error(PlanetVector a,PlanetVector b){auto d=a-b;return std::sqrt(PlanetVector::Dot(d,d));}
int main(){
 for(double lon : {-180.,-179.999,-90.,0.,90.,179.999,180.})
 for(double lat : {-90.,-89.999,-70.,0.,70.,89.999,90.}){
  PlanetLocation loc(lon*C::Tau/360,lat*C::Tau/360,3200);
  for(double far : {0.,1e12}){
   PlanetVector center(far,far,-far);auto frame=C::LocalFrame(loc,center,6371000);
   require(std::abs(PlanetVector::Dot(frame.East,frame.North))<1e-12);
   require(std::abs(PlanetVector::Dot(frame.Up,frame.North))<1e-12);
   require(std::abs(PlanetVector::Dot(frame.Up,frame.East))<1e-12);
   require(std::abs(PlanetVector::Dot(frame.Up,frame.Up)-1)<1e-12);
   auto inv=C::FromWorld(frame.Origin,center,6371000);
   require(error(C::ToWorld(inv,center,6371000),frame.Origin)<.001);
   PlanetVector local(120.345,-678.123,42.456);
   require(error(frame.ToLocal(frame.ToWorld(local)),local)<.001);
   require(error(frame.DirectionToLocal(frame.DirectionToWorld(local)),local)<1e-9);
   for(double laps : {-100.,-1.,0.,1.,100.}){
    auto uv=C::FromMapUV(C::MapU(loc)+laps,C::MapV(loc));
    require(error(C::SurfaceNormal(uv),C::SurfaceNormal(loc))<1e-12);
   }
  }
 }
 for(int w : {64,256,512,1024}){
  PeriodicMapPosition pos;pos.Set(w-.25,w/4,w,w/2);
  for(int i=0;i<100000;i++)pos.Move(.000003,0,w,w/2);
  require(std::abs(pos.X-.05)<1e-8); // Float accumulation loses these small steps at W=1024.
  for(int i=0;i<10000;i++){
   pos.Move(w+.125,0,w,w/2);
   double x=pos.RenderX(w*.67,w);
   require(std::abs(C::NearestOffset(x-pos.X,w))<1e-10);
   require(x>=w*.17-1e-10&&x<w*1.17+1e-10);
  }
 }
 bool rejected=false;try{C::FromWorld(PlanetVector(),PlanetVector(),1);}catch(...){rejected=true;}
 require(rejected);std::cout<<checks;
}
