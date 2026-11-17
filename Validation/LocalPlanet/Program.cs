using System;
using Orbis.Planet;
class Program
{
 static void Require(bool result,string message){if(!result)throw new Exception(message);}
 static void Main()
 {
  int checks=0;
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
  Console.WriteLine("PASS "+checks+" local projection checks (square geographic grid, poles, date line, round trips, frame transport)");
 }
}
