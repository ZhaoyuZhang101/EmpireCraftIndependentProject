using System;
using Orbis;
using Orbis.Flat;
public static class WeatherIntegrationRegression
{
 public static void Run()
 {
  // A changed geographic pressure field must influence the evolving atmosphere.
  var points=new[]{new AtmosVector(1,0,0),new AtmosVector(0,0,1),new AtmosVector(-1,0,0),new AtmosVector(0,0,-1)};
  var areas=new[]{1.0,1.0,1.0,1.0};
  var low=new OrbisAtmosphereCore(points,areas,Array.Empty<AtmosEdge>(),17);
  var high=new OrbisAtmosphereCore(points,areas,Array.Empty<AtmosEdge>(),17);
  for(int i=0;i<4;i++)
  {
   low.SetForcing(i,290,.5,.5,true,new AtmosVector(),1013,0,0,0);
   high.SetForcing(i,290,.5,.5,true,new AtmosVector(),i==0?990:1013,0,0,0);
  }
  low.Initialize();high.Initialize();low.Tick(.1);high.Tick(.1);
  if(Math.Abs(low.PressureAnomaly[0]-high.PressureAnomaly[0])<.001)
   throw new Exception("Geographic pressure forcing does not affect weather dynamics");
  if(FlatMoisture.ConvergenceLatitude(6,0,0)<25||FlatMoisture.ConvergenceLatitude(6,1,0)>10)
   throw new Exception("Land and sea ITCZ excursions are indistinguishable");
  if(FlatMoisture.ConvergenceLatitude(6,0,4)<=FlatMoisture.ConvergenceLatitude(6,0,0))
   throw new Exception("Thermal anomaly does not bend the convergence zone");
  var w=new WorldData(64,32,"weather-integration",TerrainPreset.Continents);
  w.Moisture=new FlatMoistureState(64,32);
  for(int i=0;i<w.Count;i++)
  {
   w.Water[i]=WaterKind.DeepOcean;w.Elevation[i]=-1800;
   for(int m=0;m<12;m++){int j=i*12+m;w.Temperature[j]=28;w.Precipitation[j]=220;w.Moisture.Humidity[j]=.85f;w.Moisture.East[j]=.6f;w.Moisture.Condensation[j]=2;}
  }
  var a=new FlatWeatherSimulation(w,1);var b=new FlatWeatherSimulation(w,4);
  double before=a.Model.TotalWater(),change=0;var initial=(double[])a.Model.Water[1].Clone();
  for(int n=0;n<30;n++){a.Step(.1,n/1200.0);b.Step(.1,n/1200.0);}
  for(int i=0;i<initial.Length;i++)
  {change+=Math.Abs(initial[i]-a.Model.Water[1][i]);if(a.Model.Water[1][i]!=b.Model.Water[1][i])throw new Exception("Weather worker nondeterminism");}
  if(change<.01)throw new Exception("Cloud condensate did not evolve");
  if(Math.Abs(a.Model.WaterBudgetError())/before>1e-6)throw new Exception("Weather water budget");
  var frame=a.Capture();foreach(float f in frame.Clouds)if(!FMath.Finite(f)||f<0||f>1)throw new Exception("Invalid cloud snapshot");
  float cloudMax=0,rainMax=0;for(int i=0;i<a.Model.Count;i++){cloudMax=Math.Max(cloudMax,frame.Clouds[i*4]);rainMax=Math.Max(rainMax,frame.Effects[i*4]);}
  if(cloudMax<.05f||rainMax<.005f)throw new Exception("Weather never generated clouds/rain "+cloudMax+"/"+rainMax);
  var dry=new FlatWeatherSimulation(w,1);dry.Model.SurfaceExchangeEnabled=false;
  for(int s=0;s<6;s++)Array.Clear(dry.Model.Water[s],0,dry.Model.Water[s].Length);
  for(int n=0;n<10;n++)dry.Step(.1,n/1200.0);
  foreach(double rain in dry.Model.RainRate)if(rain>1e-8)throw new Exception("Rain without atmospheric water");
  if(dry.Model.Lightning.Count!=0)throw new Exception("Dry lightning");
  var decay=new FlatWeatherSimulation(w,1);for(int n=0;n<30;n++)decay.Step(.1,n/1200.0);
  double condensateBefore=0,condensateAfter=0;foreach(double q in decay.Model.Water[1])condensateBefore+=q;
  decay.Model.SurfaceExchangeEnabled=false;Array.Clear(decay.Model.Water[0],0,decay.Model.Water[0].Length);
  for(int n=0;n<20;n++)decay.Step(.1,(30+n)/1200.0);
  foreach(double q in decay.Model.Water[1])condensateAfter+=q;
  if(condensateBefore<1||condensateAfter>=condensateBefore*.95)throw new Exception("Clouds do not dissipate in dry air: "+condensateBefore+" -> "+condensateAfter);
  Console.WriteLine("PASS cloud transport, dry-air dissipation, rain/lightning gate, serial/parallel agreement; cloud/rain="+cloudMax+"/"+rainMax+" budget="+a.Model.WaterBudgetError()+" condensate "+condensateBefore+" -> "+condensateAfter);
 }
}
