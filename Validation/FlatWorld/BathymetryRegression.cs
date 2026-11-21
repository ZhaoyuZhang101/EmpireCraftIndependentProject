using System;
using Orbis.Flat;
public static class BathymetryRegression
{
 static int[] Quantiles(WorldData world,bool marine)
 {
  int bound=marine?-HeightBands.MinimumBed:HeightBands.MaximumBed;
  var weight=new double[bound+1];double area=0;
  for(int i=0;i<world.Count;i++)
  {
   bool ocean=FlatTerrainAnalysis.Ocean(world.Water[i]);
   int metres=marine?-world.Elevation[i]:world.Elevation[i];
   if(ocean!=marine || metres<0 || (!marine&&world.Water[i]==WaterKind.Lake))continue;
   double cell=FlatRiverDischarge.RelativeArea(i/world.Width,world.Height);
   weight[metres]+=cell;area+=cell;
  }
  if(area<=0)throw new Exception("Terrain distribution empty.");
  double sum=0;double[] fractions={.10,.50,.90,.99};int q=0;var result=new int[fractions.Length];
  for(int metres=0;metres<=bound && q<result.Length;metres++){
   sum+=weight[metres];
   while(q<result.Length && sum>=area*fractions[q])result[q++]=metres;
  }
  return result;
 }

 static void EarthRelief()
 {
  const int width=256;
  const string assets="Assets/ORBIS/Resources/ORBIS/FlatWorld/";
  var earth=FlatGenerator.Generate(width,width/2,"RELIEF-CONTROL",TerrainPreset.Earth,default,
   System.IO.File.ReadAllBytes(assets+"Earth256.bytes"),System.IO.File.ReadAllBytes(assets+"EarthWater256.bytes"));
  var reference=Quantiles(earth,true);
  Console.WriteLine($"Earth area-weighted depth P10/50/90/99 m: {string.Join("/",reference)}; land: {string.Join("/",Quantiles(earth,false))}");
  foreach(var preset in FlatGenerator.ProceduralPresets)
  {
   var w=FlatGenerator.Generate(width,width/2,"RELIEF-TRANSFER",preset,default);
   var depth=Quantiles(w,true);var land=Quantiles(w,false);
   Console.WriteLine($"{preset} depth P10/50/90/99 m: {string.Join("/",depth)}; land: {string.Join("/",land)}");
   if(Math.Abs(depth[2]-reference[2])>1250 || depth[3]<=4000 || depth[3]<=depth[2] || land[3]<=land[1])
    throw new Exception($"{preset}: clipped seabed or missing terrestrial relief on a generated world.");
  }
 }
 public static void Run()
 {
  float shelf=FlatGenerator.OceanDepth(-.01f,0,0),slope=FlatGenerator.OceanDepth(-.10f,0,0),abyss=FlatGenerator.OceanDepth(-.35f,0,0);
  float ridge=FlatGenerator.OceanDepth(-.35f,1,0),trench=FlatGenerator.OceanDepth(-.35f,0,1);
  if(!(shelf>0&&shelf<350&&slope>shelf+500&&abyss>slope+300&&ridge<abyss-100&&trench>abyss+100&&trench<4000))throw new Exception("Ocean shelf/slope/ridge/trench profile");
  var w=FlatGenerator.Generate(128,64,"bathymetry-regression",TerrainPreset.Continents,default);int ocean=0;double mean=0,variance=0;
  foreach(short h in w.Elevation)if(h<0){ocean++;mean-=h;variance+=(double)h*h;}
  mean/=ocean;variance=variance/ocean-mean*mean;
  if(ocean<100||variance<100000)throw new Exception("Generated ocean floor lacks relief: "+variance);
  Console.WriteLine("PASS shelf="+shelf+" slope="+slope+" abyss="+abyss+" ridge="+ridge+" trench="+trench+" variance="+variance);
  EarthRelief();
 }
}
