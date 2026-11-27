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
  checks+=ZoomUnfoldAndWidescreenCoverage();
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

 static int ZoomUnfoldAndWidescreenCoverage()
 {
  int checks=0;
  const double fov=PlanetScrollNavigationMath.Fov,localHeightSpan=21000.0*(2.5/6371008.8),reliefTilt=.72,reliefCameraHeight=.18;
  double halfTan=Math.Tan(fov*Math.PI/360.0);
  double homeCapDeg=Math.Acos(1.0/(1.0+PlanetScrollNavigationMath.HomeAltitude))*180.0/Math.PI;
  double unfoldCapDeg=Math.Acos(1.0/PlanetScrollNavigationMath.Start)*180.0/Math.PI;
  Require(fov<=20.0&&homeCapDeg>81.0&&unfoldCapDeg>65.0,"telephoto globe distortion correction must expose >81 deg hemisphere at home and >65 deg at unfold");
  checks++;
  double foldZoom=PlanetScrollNavigationMath.ZoomFor(PlanetScrollNavigationMath.Start-1);
  double maxZoom=PlanetScrollNavigationMath.ZoomFor(PlanetScrollNavigationMath.MaxAltitude);
  double minZoom=PlanetScrollNavigationMath.ZoomFor(PlanetScrollNavigationMath.EndGap/4.0);

  // 1. Globe retains full spherical silhouette radius at morph == 0.
  foreach(double aspect in new[]{1.0,16.0/9.0,2.003,21.0/9.0,32.0/9.0,3840.0/720.0})
  {
   double globeRadius=PlanetScrollNavigationMath.ComputeLocalRadius(.23,aspect,0,localHeightSpan,reliefTilt,reliefCameraHeight,0);
   Require(Math.Abs(globeRadius-PlanetScrollNavigationMath.MaxLocalRadius)<1e-12,"globe must retain full spherical silhouette");
   checks++;
  }

  // 2. Unfold camera distance stays locked to 1 + gap (zero central scale lag/hump) and continuous at endpoints.
  double prevDist=double.PositiveInfinity;
  foreach(double m in new[]{0.0,0.01,0.10,0.36,0.50,0.62,0.85,0.95,1.0})
  {
   double altitude=PlanetScrollNavigationMath.EndGap+(PlanetScrollNavigationMath.Start-1-PlanetScrollNavigationMath.EndGap)*(1-m);
   double rawDistance=m<=0?1+altitude:PlanetScrollNavigationMath.Start+(1.08-PlanetScrollNavigationMath.Start)*m;
   double camDist=PlanetScrollNavigationMath.CameraDistanceForUnfold(rawDistance,altitude,m);
   Require(Math.Abs(camDist-(1+altitude))<1e-12,"camera unfold distance matches 1+gap at morph="+m);
   Require(camDist<=prevDist+1e-12,"camera distance strictly decreases as morph increases");
   prevDist=camDist;checks+=2;
  }

  // 3. Widescreen coverage across 16:9, 2.003 (screenshot viewport), 21:9, 32:9, 5.33:1 at 36%, 85%, 100% and all unfold steps.
  foreach(double aspect in new[]{4.0/3.0,16.0/9.0,2.003,21.0/9.0,32.0/9.0,3840.0/720.0})
  {
   for(int step=1;step<=100;step++)
   {
    double m=step/100.0;
    double gap=PlanetScrollNavigationMath.EndGap+(PlanetScrollNavigationMath.Start-1-PlanetScrollNavigationMath.EndGap)*(1-m);
    double camDist=1+gap;
    double ortho=gap*halfTan;
    double diag=ortho*Math.Sqrt(1+aspect*aspect);
    double t=m<=.62?0:m>=1?1:((m-.62)/.38)*((m-.62)/.38)*(3-2*((m-.62)/.38));
    double radius=PlanetScrollNavigationMath.ComputeLocalRadius(ortho,aspect,m,localHeightSpan,reliefTilt,reliefCameraHeight,0);
    double maxNdcAll=0,maxNdcInR=0;
    for(int i=1;i<=314;i++)
    {
     double s=PlanetScrollNavigationMath.MaxLocalRadius*i/314.0;
     double angle=(1-m)*s+2*m*Math.Atan(s*.5);
     double x=(1-m)*Math.Sin(angle)+m*s;
     double z=(1-m)*Math.Cos(angle)+m;
     double dEye=camDist-z;
     if(dEye<=.001)continue;
     double clipW=(1-t)*(dEye/gap)+t;
     double ndc=x/(diag*clipW);
     if(ndc>maxNdcAll)maxNdcAll=ndc;
     if(s<=radius&&ndc>maxNdcInR)maxNdcInR=ndc;
    }
    if(maxNdcAll>=1.0) Require(maxNdcInR>=1.0,"local radius must fill widescreen corners; aspect="+aspect+" morph="+m+" ndc="+maxNdcInR);
    else Require(maxNdcInR>=maxNdcAll-1e-3,"local radius must not clip visible dome limb; aspect="+aspect+" morph="+m);
    checks++;
   }
   // Flat zoomed-in & entry states cover worst-case relief-displaced corners with >= 1.30x margin.
   foreach(double ortho in new[]{.004,.015,.04,PlanetScrollNavigationMath.EndGap*halfTan})
   {
    double radius=PlanetScrollNavigationMath.ComputeLocalRadius(ortho,aspect,1.0,localHeightSpan,reliefTilt,reliefCameraHeight,0);
    double corner=ortho*Math.Sqrt(1+aspect*aspect);
    double needed=corner*(1+localHeightSpan/reliefCameraHeight)+localHeightSpan*reliefTilt;
    Require(radius>=needed*1.30,"flat radius covers relief-displaced widescreen corners; aspect="+aspect+" ortho="+ortho);
    checks++;
   }
   // Off-axis Focus() transition keeps patch edge beyond shifted view axis.
   double focusRadius=PlanetScrollNavigationMath.ComputeLocalRadius(.084,aspect,.85,localHeightSpan,reliefTilt,reliefCameraHeight,.35);
   Require(focusRadius>.80,"off-axis Focus() expands local patch to prevent exposed circular rim");
   checks++;
  }

  // 4. Real wheel zoom-in / zoom-out sequences (including 36%, 85%, 100%) and fast direction reversal without overshoot.
  double zoom=PlanetScrollNavigationMath.ZoomFor(PlanetScrollNavigationMath.HomeAltitude),target=zoom,vel=0;
  bool saw36=false,saw85=false,saw100=false;
  for(int notch=0;notch<20;notch++)
  {
   target=PlanetScrollNavigationMath.ApplyWheelTarget(zoom,target,ref vel,-3,minZoom,maxZoom);
   for(int frame=0;frame<12;frame++)
   {
    double before=zoom;
    PlanetScrollNavigationMath.StepZoomSpring(ref zoom,ref vel,target,minZoom,maxZoom,1.0/60.0);
    Require(zoom<=before+1e-9,"inward wheel never rebounds outward");
    double m=PlanetScrollNavigationMath.MorphForAltitude(PlanetScrollNavigationMath.Altitude(zoom));
    if(m>=.25&&m<=.48)saw36=true;
    if(m>=.78&&m<=.92)saw85=true;
    if(m>=1.0)saw100=true;
   }
   checks++;
  }
  Require(saw36&&saw85&&saw100,"inward wheel passes smoothly through ~36%, ~85% and 100%");
  // Now simulate fast outward flick followed immediately by inward reversal near the fold threshold:
  // previously stale outward velocity overshot past FoldZoom into morph == 0 (circular globe).
  zoom=foldZoom-1.2;target=zoom;vel=0;
  target=PlanetScrollNavigationMath.ApplyWheelTarget(zoom,target,ref vel,+6,minZoom,maxZoom);
  for(int frame=0;frame<3;frame++)PlanetScrollNavigationMath.StepZoomSpring(ref zoom,ref vel,target,minZoom,maxZoom,1.0/60.0);
  Require(vel>5,"outward flick builds significant outward spring velocity");
  double atReversal=zoom;
  target=PlanetScrollNavigationMath.ApplyWheelTarget(zoom,target,ref vel,-3,minZoom,maxZoom);
  Require(vel==0,"reversing wheel clears opposing spring velocity");
  for(int frame=0;frame<30;frame++)
  {
   PlanetScrollNavigationMath.StepZoomSpring(ref zoom,ref vel,target,minZoom,maxZoom,1.0/60.0);
   Require(zoom<=atReversal+1e-9,"inward reversal never overshoots outward into circular globe");
  }
  // Zoom all the way back out to globe and verify clean clamp at MaxZoom.
  for(int notch=0;notch<25;notch++)
  {
   target=PlanetScrollNavigationMath.ApplyWheelTarget(zoom,target,ref vel,+3,minZoom,maxZoom);
   for(int frame=0;frame<12;frame++)PlanetScrollNavigationMath.StepZoomSpring(ref zoom,ref vel,target,minZoom,maxZoom,1.0/60.0);
  }
  Require(PlanetScrollNavigationMath.MorphForAltitude(PlanetScrollNavigationMath.Altitude(zoom))==0&&zoom<=maxZoom+1e-9,"outward wheel returns cleanly to circular globe");
  checks+=6;
  return checks;
 }
}
