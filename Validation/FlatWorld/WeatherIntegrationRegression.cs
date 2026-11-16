using System;
using System.IO;
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
  if(FlatWeatherSimulation.SynopticLift(20,.88,-6,true,3)<.15||FlatWeatherSimulation.SynopticLift(20,.25,28,false,3)>.05)
   throw new Exception("Frontal/marine lift must depend on moisture and thermal contrast");
  var w=new WorldData(64,32,"weather-integration",TerrainPreset.Continents);
  w.Moisture=new FlatMoistureState(64,32);
  for(int i=0;i<w.Count;i++)
  {
   w.Water[i]=WaterKind.DeepOcean;w.Elevation[i]=-1800;
   for(int m=0;m<12;m++){int j=i*12+m;w.Temperature[j]=28;w.Precipitation[j]=220;w.Moisture.Humidity[j]=.85f;w.Moisture.East[j]=.6f;w.Moisture.Condensation[j]=2;}
  }
  var a=new FlatWeatherSimulation(w,1);var b=new FlatWeatherSimulation(w,4);
  // A constant mixing ratio cannot change when the dry-air layer masses are fixed.
  // Divergent horizontal flow must have a compensating vertical/pressure response.
  var tracer=new FlatWeatherSimulation(w,1).Model;
  for(int k=0;k<OrbisAtmosphereCore.Layers;k++)for(int i=0;i<tracer.Count;i++)
  {
   tracer.Water[0][k*tracer.Count+i]=OrbisAtmosphereCore.AirMass[k]*.004;
   var p=tracer.Position[i];
   tracer.Wind[k*tracer.Count+i]=new AtmosVector(0,.02*(1-k/5.5),0).Tangent(p);
  }
  double tracerBefore=tracer.TotalWater();
  tracer.HorizontalTransport(.1);
  double tracerError=0;
  for(int k=0;k<OrbisAtmosphereCore.Layers;k++)for(int i=0;i<tracer.Count;i++)
   tracerError=Math.Max(tracerError,Math.Abs(tracer.Water[0][k*tracer.Count+i]/OrbisAtmosphereCore.AirMass[k]-.004));
  if(tracerError>1e-8)throw new Exception("Fixed dry-air continuity violates uniform tracer: "+tracerError);
  if(Math.Abs(tracer.TotalWater()-tracerBefore)>1e-7)throw new Exception("Continuity correction lost water");
  double before=a.Model.TotalWater(),change=0;var initial=(double[])a.Model.Water[1].Clone();
  for(int n=0;n<30;n++){a.Step(.1,n/1200.0);b.Step(.1,n/1200.0);}
  for(int i=0;i<initial.Length;i++)
  {change+=Math.Abs(initial[i]-a.Model.Water[1][i]);if(a.Model.Water[1][i]!=b.Model.Water[1][i])throw new Exception("Weather worker nondeterminism");}
  AssertSameState(a.Model,b.Model,"serial/parallel");
  var resumed=new FlatWeatherSimulation(w,4);resumed.Model.Restore(a.Model.Save());
  a.Step(.1,.05);resumed.Step(.1,.05);AssertSameState(a.Model,resumed.Model,"save/resume");
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
  var atlas=FlatAtlasTerrain.Read(File.ReadAllBytes("Assets/ORBIS/Resources/ORBIS/WorldAtlas/Default.bytes"),"ORBIS-2D-2026",default);
  var planet=new FlatWeatherSimulation(atlas,4);
  double[] lastBands=null;
  for(int n=0;n<3600;n++)
  {
   planet.Step(.1,atlas.Day+n/1200.0);
   if((n+1)%300!=0)continue;
   var snapshot=planet.Capture();var bands=new double[8];int cloudy=0,clear=0;
   for(int i=0;i<planet.Model.Count;i++){double c=snapshot.Clouds[i*4];bands[(i/64)/4]+=c/256; if(c>.08)cloudy++;if(c<.02)clear++;}
   Console.WriteLine("ATLAS t="+planet.Model.Time.ToString("F0")+" bands="+string.Join(",",Array.ConvertAll(bands,v=>v.ToString("F3")))+" cloudy="+cloudy+" clear="+clear);
   int populated=0;foreach(double c in bands)if(c>.015)populated++;
   if(populated<6||bands[1]<.015||bands[6]<.015)throw new Exception("Persistent atlas cloud belts/polar gaps after "+planet.Model.Time);
   if(cloudy<150||clear<150)throw new Exception("Atlas must retain cloud systems and clear areas");
   double zonalVariance=0;
   for(int y=0;y<FlatWeatherSimulation.Height;y++)
   {
    double row=0;for(int x=0;x<64;x++)row+=snapshot.Clouds[(y*64+x)*4]/64;
    for(int x=0;x<64;x++){double d=snapshot.Clouds[(y*64+x)*4]-row;zonalVariance+=d*d/planet.Model.Count;}
   }
   if(zonalVariance<.003)throw new Exception("Atlas clouds collapsed into longitude-invariant rings");
   if(lastBands!=null){double evolving=0;for(int q=0;q<8;q++)evolving+=Math.Abs(bands[q]-lastBands[q]);if(evolving<.004)throw new Exception("Atlas cloud field froze");}
   lastBands=bands;
   if(Math.Abs(planet.Model.WaterBudgetError())/planet.Model.InitialWater>1e-8)throw new Exception("Long-run atlas water budget");
   foreach(var species in planet.Model.Water)foreach(double q in species)if(!OrbisAtmosphereCore.Finite(q)||q<0)throw new Exception("Nonfinite/negative atlas water");
  }
  var polar=planet.Capture();double south=0,north=0;
  for(int y=3;y<=7;y++)for(int x=0;x<FlatWeatherSimulation.Width;x++)south+=polar.Clouds[(y*FlatWeatherSimulation.Width+x)*4];
  for(int y=24;y<=28;y++)for(int x=0;x<FlatWeatherSimulation.Width;x++)north+=polar.Clouds[(y*FlatWeatherSimulation.Width+x)*4];
  south/=5*FlatWeatherSimulation.Width;north/=5*FlatWeatherSimulation.Width;
  if(south<.02||north<.02)throw new Exception("Mid-high latitude polar cloud gap south/north="+south+"/"+north);
  Console.WriteLine("PASS uniform tracer, 360 s atlas distribution, dry-air dissipation, rain/lightning gate, serial/parallel and save/resume agreement; cloud/rain="+cloudMax+"/"+rainMax+" budget="+a.Model.WaterBudgetError()+" atlasBudget="+planet.Model.WaterBudgetError()+" condensate "+condensateBefore+" -> "+condensateAfter);
 }
 static void AssertSameState(OrbisAtmosphereCore a,OrbisAtmosphereCore b,string label)
 {
  for(int s=0;s<OrbisAtmosphereCore.Species;s++)for(int j=0;j<a.Water[s].Length;j++)
   if(a.Water[s][j]!=b.Water[s][j])throw new Exception(label+" water differs");
  for(int j=0;j<a.Temperature.Length;j++)if(a.Temperature[j]!=b.Temperature[j])throw new Exception(label+" temperature differs");
 }
}
