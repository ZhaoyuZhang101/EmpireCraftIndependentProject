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
   for(int i=0;i<3000;i++) {var e=p.Frame.East;p.Move(0,10);Require(PlanetVector.Dot(e,p.Frame.East)>.999,"pole heading jump");checks++;}
   Require(Math.Abs(PlanetVector.Dot(p.Frame.Up,p.Frame.East))<1e-12,"orthogonal frame");checks++;
  }
  Console.WriteLine("PASS "+checks+" local projection checks (poles, date line, round trips, frame transport)");
 }
}
