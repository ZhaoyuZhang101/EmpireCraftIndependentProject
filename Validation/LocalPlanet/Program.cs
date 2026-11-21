using System;
using Orbis.Planet;
class Program
{
 static void Require(bool result,string message){if(!result)throw new Exception(message);}
 static void Main()
 {
  int checks=0;
  checks+=DisplayedGroundMetrics();
  checks+=UnfoldRoundTrips();
  checks+=TransportedAnchors();
  checks+=MinimapProjection();
  foreach(double lat in new[]{-Math.PI/2,-1.0,0,1.0,Math.PI/2})
  foreach(double lon in new[]{-Math.PI,0,Math.PI-.00001})
  {
   var p=new PlanetLocalProjection(new PlanetLocation(lon,lat),6371);
   var right=p.Frame.East*.6+p.Frame.North*.8+ p.Frame.Up*.15;
   p.AlignEast(right);
   Require(Math.Abs(PlanetVector.Dot(p.Frame.East,p.Frame.Up))<1e-12,"view aligned frame tangent");checks++;
   for(int i=0;i<100;i++)
   {
    double x=Math.Sin(i*1.73)*1500,y=Math.Cos(i*2.31)*1500;var s=p.Exp(x,y);var q=p.Log(s);
    Require(Math.Abs(q.X-x)<1e-7&&Math.Abs(q.Y-y)<1e-7,"log/exp round trip");
    Require(Math.Abs(PlanetLocalProjection.Distance(p.Frame.Up,s,6371)-Math.Sqrt(x*x+y*y))<1e-7,"geodesic distance");checks+=2;
   }
   for(int i=0;i<100;i++)
   {
    double x=Math.Sin(i*1.73)*3000,y=Math.Cos(i*2.31)*3000,step=.01;
    var direction=p.StereographicExp(x,y);var point=p.StereographicLog(direction);
    Require(Math.Abs(point.X-x)<1e-7&&Math.Abs(point.Y-y)<1e-7,"stereographic round trip");
    var east=p.StereographicExp(x+step,y)-p.StereographicExp(x-step,y);
    var north=p.StereographicExp(x,y+step)-p.StereographicExp(x,y-step);
    double eastLength=Math.Sqrt(PlanetVector.Dot(east,east)),northLength=Math.Sqrt(PlanetVector.Dot(north,north));
    Require(Math.Abs(eastLength/northLength-1)<1e-8,"local chart preserves equal directional scale");
    Require(Math.Abs(PlanetVector.Dot(east,north)/(eastLength*northLength))<1e-8,"local chart preserves right angles");checks+=3;
    foreach(double scale in new[]{.92,1.08})
    {
     var anchored=new PlanetLocalProjection(p.Center,6371);
     var anchor=anchored.StereographicExp(x,y);
     var original=anchored.Frame.Up;
     bool recentered=anchored.TryAnchorStereographicNorthUp(anchor,x*scale,y*scale);
     if(Math.Abs(lat)<1.5&&scale<1)Require(recentered,"north-up inward anchor has a chart centre");
     if(recentered)Require(PlanetLocalProjection.Distance(anchor,anchored.StereographicExp(x*scale,y*scale),6371)<1e-6,"geographic zoom anchor is preserved");
     else Require(PlanetLocalProjection.Distance(original,anchored.Frame.Up,6371)<1e-9,"polar branch boundary retains a stable chart centre");
     checks+=2;
    }
   }
   var far=p.StereographicLog(p.Frame.Up*-1);
   Require(!double.IsNaN(far.X+far.Y)&&!double.IsInfinity(far.X+far.Y),"antipodal renderer coordinates remain finite");checks++;
   for(int i=0;i<3000;i++) {var e=p.Frame.East;p.Move(0,10);Require(PlanetVector.Dot(e,p.Frame.East)>.999,"pole heading jump");checks++;}
   Require(Math.Abs(PlanetVector.Dot(p.Frame.Up,p.Frame.East))<1e-12,"orthogonal frame");checks++;
  }
  foreach(double latitude in new[]{-90.0,-89.6484375,-80,-60,0,60,80,89.6484375,90})
  foreach(double longitude in new[]{-180.0,-.01,0,179.99})
  {
   var chart=new PlanetLocalProjection(new PlanetLocation(longitude*Math.PI/180,latitude*Math.PI/180),6371);
   for(int sample=0;sample<100;sample++)
   {
    double x=Math.Sin(sample*1.73)*1000,y=Math.Cos(sample*2.31)*1000;
    var direction=chart.GeographicExp(x,y);var point=chart.GeographicLog(direction);
    Require(PlanetLocalProjection.Distance(direction,chart.GeographicExp(point.X,point.Y),6371)<1e-7,"geographic address round trip across date line and polar copies");checks++;
    var expected=new PlanetLocation(chart.Center.Longitude+x/6371,chart.Center.Latitude+y/6371);
    Require(PlanetLocalProjection.Distance(direction,PlanetCoordinateSystem.SurfaceNormal(expected),6371)<1e-7,"equal display steps address equal angular cell steps");checks++;
    foreach(double scale in new[]{.92,1.08})
    {
     var anchored=new PlanetLocalProjection(chart.Center,6371);
     bool moved=anchored.TryAnchorGeographicNorthUp(direction,point.X*scale,point.Y*scale);
     if(moved)Require(PlanetLocalProjection.Distance(direction,anchored.GeographicExp(point.X*scale,point.Y*scale),6371)<1e-7,"rectified wheel anchor stays at the same geography");
     else Require(PlanetLocalProjection.Distance(chart.Frame.Up,anchored.Frame.Up,6371)<1e-9,"polar anchor boundary leaves the chart stable");
     checks++;
    }
   }
  }
  Console.WriteLine("PASS "+checks+" local projection checks (ground shape, polar navigation, date line, round trips, geographic addresses)");
 }

 static int MinimapProjection()
 {
  int checks=0;
  foreach(double lon in new[]{-180d,-179.999,-78,0,121.5,179.999,180})
  foreach(double lat in new[]{-90d,-89.99,-80,-23,0,60,80,89.99,90})
  {
   double u=lon/360+.5,v=lat/180+.5;
   HammerProjection.Project(u,v,out double x,out double y);
   Require(HammerProjection.TryUnproject(x,y,out var location),"minimap must accept its projected footprint");
   var target=PlanetCoordinateSystem.SurfaceNormal(PlanetCoordinateSystem.FromMapUV(u,v));
   Require(PlanetLocalProjection.Distance(target,PlanetCoordinateSystem.SurfaceNormal(location),1)<3e-8,"click and displayed geography disagree");
   checks+=2;
  }
  Require(!HammerProjection.TryUnproject(.01,.99,out _)&&!HammerProjection.TryUnproject(1,0,out _),"outside ellipse is empty");
  Require(!HammerProjection.TryUnproject(double.NaN,.5,out _)&&!HammerProjection.TryUnproject(.5,double.PositiveInfinity,out _),"nonfinite minimap hits are rejected");
  HammerProjection.Project(0,.5,out var west,out _);
  HammerProjection.Project(1,.5,out var east,out _);
  Require(west==0&&east==1,"date line has endpoints on both edges");
  return checks+3;
 }

 static int DisplayedGroundMetrics()
 {
  int checks=0;
  foreach(double latitude in new[]{-90d,-89.99,-85,-80,-60,0,45,60,80,85,89.99,90})
  foreach(double longitude in new[]{-180d,-100,15,179.999})
  {
   var chart=new PlanetLocalProjection(new PlanetLocation(longitude*Math.PI/180,latitude*Math.PI/180));
   foreach(double x in new[]{-.4,0,.4})foreach(double y in new[]{-.2,0,.2})
   {
    const double step=.00005;
    var east=chart.LocalExp(x+step,y,1)-chart.LocalExp(x-step,y,1);
    var north=chart.LocalExp(x,y+step,1)-chart.LocalExp(x,y-step,1);
    double xx=PlanetVector.Dot(east,east),yy=PlanetVector.Dot(north,north),xy=PlanetVector.Dot(east,north);
    double spread=Math.Sqrt((xx-yy)*(xx-yy)+4*xy*xy),trace=xx+yy;
    double axes=Math.Sqrt((trace+spread)/(trace-spread));
    Require(axes<=1.00001,"equal ground distances in the displayed local map at latitude "+latitude+" have axis ratio "+axes);
    double expectedScale=1/(1+(x*x+y*y)*.25);
    Require(Math.Abs(Math.Sqrt(xx)/(2*step)-expectedScale)<1e-8,"stereographic ground scale away from the map centre");
    checks+=2;
   }
  }
  return checks;
 }

 static int UnfoldRoundTrips()
 {
  int checks=0;
  foreach(double latitude in new[]{-90d,-85,-60,0,60,85,90})
  foreach(double longitude in new[]{-180d,0,179.999})
  {
   const double radius=6371;
   var chart=new PlanetLocalProjection(new PlanetLocation(longitude*Math.PI/180,latitude*Math.PI/180),radius);
   chart.AlignEast(chart.Frame.East*.6+chart.Frame.North*.8);
   foreach(double blend in new[]{0d,.00001,.2,.5,.9,.99999,1})
   foreach(double bearing in new[]{0d,.5,1.8,3.1,4.5,5.9})
   {
    double previous=-1;
    for(int i=0;i<=32;i++)
    {
     double s=(Math.PI-.0001)*i/32,x=radius*s*Math.Cos(bearing),y=radius*s*Math.Sin(bearing);
     var geographic=chart.LocalExp(x,y,blend);
     var point=chart.LocalLog(geographic,blend);
     double angle=PlanetLocalProjection.Distance(chart.Frame.Up,geographic,1);
     Require(angle>previous,"displayed radius is strictly monotone during the unfold");
     Require(Math.Abs(angle-((1-blend)*s+2*blend*Math.Atan(s*.5)))<1e-12,"displayed radius follows the blended central angle");
     Require(Math.Abs(x-point.X)<1e-6&&Math.Abs(y-point.Y)<1e-6,"one radial inverse recovers route and pick positions throughout the unfold");
     previous=angle;checks+=3;
    }
   }
  }
  return checks;
 }

 static int TransportedAnchors()
 {
  int checks=0;
  foreach(double latitude in new[]{-90d,-89.99,-85,-80,0,80,85,89.99,90})
  foreach(double longitude in new[]{-180d,.1,179.999})
  foreach(double heading in new[]{0d,.7,2.8})
  foreach(double scale in new[]{.92,1.08})
  {
   var chart=new PlanetLocalProjection(new PlanetLocation(longitude*Math.PI/180,latitude*Math.PI/180));
   chart.AlignEast(chart.Frame.East*Math.Cos(heading)+chart.Frame.North*Math.Sin(heading));
   foreach(double x in new[]{-.4,0,.4})foreach(double y in new[]{-.25,.25})
   {
    var anchor=chart.LocalExp(x,y,1);
    var previousEast=chart.Frame.East;
    chart.AnchorStereographic(anchor,x*scale,y*scale);
    Require(PlanetLocalProjection.Distance(anchor,chart.LocalExp(x*scale,y*scale,1),1)<1e-10,"wheel anchor retained with transported and north-up headings");
    Require(PlanetVector.Dot(previousEast,chart.Frame.East)>.94,"off-centre wheel never flips heading at the polar branch");checks+=2;
   }
  }
  foreach(double sign in new[]{-1d,1})
  {
   var chart=new PlanetLocalProjection(new PlanetLocation(Math.PI-.001,sign*89*Math.PI/180));
   for(int step=0;step<12;step++)
   {
    var previous=chart.Frame.East;
    var expected=chart.LocalExp(0,sign*.003,1);
    chart.MoveChart(0,sign*.003,1);
    Require(PlanetLocalProjection.Distance(expected,chart.Frame.Up,1)<1e-12,"drag recentres on the displayed ground");
    Require(PlanetVector.Dot(previous,chart.Frame.East)>.999,"continuous heading while dragging across the pole");checks+=2;
   }
   Require(Math.Abs(PlanetCoordinateSystem.NearestOffset(chart.Center.Longitude-(Math.PI-.001),2*Math.PI))>3,"drag crosses the physical pole");checks++;
   chart.MoveChart(0,sign*.35,1);
   for(int i=0;i<120;i++)chart.AlignNorth(Math.PI/120);
   var north=PlanetCoordinateSystem.LocalFrame(chart.Center,new PlanetVector(),1).North;
   Require(PlanetVector.Dot(north,chart.Frame.North)>1-1e-10,"geographic north restored after leaving the polar cap");checks++;
  }
  return checks;
 }
}
