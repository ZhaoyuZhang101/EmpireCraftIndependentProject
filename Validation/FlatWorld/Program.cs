using System;
using Orbis.Flat;
public static class Program {
 public static int Main(string[] args) {
  try {
   switch(args.Length == 0 ? "" : args[0]) {
    case "--climate-math": ClimateMathRegression.Run(); break;
    case "--climate-biomes": BiomeStatisticsRegression.Run(); break;
    case "--climate-observations": ClimateObservationRegression.Run(); break;
    case "--climate-regions": RegionalStatisticsRegression.Run(); break;
    case "--climate-saves": RegionalStatisticsRegression.CheckSaves(args); break;
    case "--tectonics": TectonicsRegression.Run(); break;
    case "--river-discharge": RiverDischargeRegression.Run(); break;
    case "--forest-stands": ForestStandRegression.Run(); break;
    case "--wildlife": WildlifeRegression.Run(); break;
    case "--bathymetry": BathymetryRegression.Run(); break;
    case "--weather": WeatherIntegrationRegression.Run(); break;
    case "--moisture": MoistureRegression(); break;
    case "--environment": EnvironmentRegression(); break;
    case "--hydrology": HydrologyRegression.Run(); break;
    case "--cyclone": CycloneRegression.Run(); break;
    default: Console.WriteLine(FlatSelfTests.Run()); break;
   }
   return 0;
  }
  catch(Exception e) { Console.Error.WriteLine(FlatSelfTests.LastOperation+"\n"+e); return 1; }
 }
 static void EnvironmentRegression() {
  ClimatologyRegression.MaritimeRainfall();
  ClimatologyRegression.FreshwaterBasin();
  ClimatologyRegression.FullGridCoverage();
  ClimatologyRegression.GridScale();
  ClimatologyRegression.EquatorialWinds();
  ClimatologyRegression.WeatherYears();
  ClimatologyRegression.RainShadow();
  var w=FlatAtlasTerrain.Read(System.IO.File.ReadAllBytes("Assets/ORBIS/Resources/ORBIS/WorldAtlas/Default.bytes"),"ORBIS-2D-2026",default);
  ClimatologyRegression.TerrainPresets(w);
  var counts=new int[256];int land=0;double rain=0;int ocean=0,deepFloor=0;
  for(int i=0;i<w.Count;i++){if(!FlatTerrainAnalysis.AreaWater(w.Water[i])){counts[(int)w.Biomes[i]]++;land++;rain+=w.Facts.AnnualRain[i];}else if(FlatTerrainAnalysis.Ocean(w.Water[i])){ocean++;if(w.Elevation[i]<=-3999)deepFloor++;}}
  foreach(Biome b in Enum.GetValues(typeof(Biome)))if(counts[(int)b]>0)Console.WriteLine(b+" "+counts[(int)b]+" ("+(100.0*counts[(int)b]/land).ToString("F1")+"%)");
  double bedMean=0,bedSq=0;int bedMin=0,bedMax=0;for(int i=0;i<w.Count;i++)if(FlatTerrainAnalysis.Ocean(w.Water[i])){int d=-w.Elevation[i];bedMean+=d;bedSq+=(double)d*d;bedMin=Math.Min(bedMin,w.Elevation[i]);bedMax=Math.Max(bedMax,w.Elevation[i]);}bedMean/=ocean;
  Console.WriteLine("Mean land rainfall="+(rain/land).ToString("F1")+"; ocean clipped floor="+deepFloor+"/"+ocean+"; bed range="+bedMin+".."+bedMax+" variance="+(bedSq/ocean-bedMean*bedMean).ToString("F0"));
  int activeCycloneMonths=0;for(int m=0;m<12;m++){var n=FlatCycloneSelection.Find(w,m,true);var s=FlatCycloneSelection.Find(w,m,false);if(n.Active||s.Active)activeCycloneMonths++;}
  if(activeCycloneMonths==0){double maxT=-100,maxR=0,maxProduct=0;for(int i=0;i<w.Count;i++){double lat=((i/w.Width+.5)/w.Height-.5)*180;if(Math.Abs(lat)<8||Math.Abs(lat)>38||!FlatTerrainAnalysis.Ocean(w.Water[i]))continue;double t=w.Facts.MeanTemperature[i],r=w.Facts.AnnualRain[i]/12;maxT=Math.Max(maxT,t);maxR=Math.Max(maxR,r);maxProduct=Math.Max(maxProduct,Math.Max(0,t-23)*Math.Max(0,r-105));}throw new Exception("Default world has no climate-eligible cyclone month; max annual T/R/product="+maxT+"/"+maxR+"/"+maxProduct);}
  Console.WriteLine("Climate-eligible cyclone months="+activeCycloneMonths+" current="+FlatCycloneSelection.Find(w,w.Month,true).Strength.ToString("F2")+"/"+FlatCycloneSelection.Find(w,w.Month,false).Strength.ToString("F2"));
  if(FlatTerrainAnalysis.ClassifyBiome(Koppen.BWh,30,24,100,10,25,10,true,false)!=Biome.Wetland)throw new Exception("Perennial saturated river margin must override regional desert");
  if(FlatTerrainAnalysis.ClassifyBiome(Koppen.BWh,30,24,100,0,25,10,false,false)!=Biome.Desert)throw new Exception("Dry land away from rivers must remain desert");
  if(FlatTerrainAnalysis.ClassifyBiome(Koppen.EF,30,-20,100,12,25,10,true,false)!=Biome.Snow)throw new Exception("Freshwater must not turn polar ice into wetland");
  Console.WriteLine("PASS environment regressions");
 }
 static void MoistureRegression() {
  var w=new WorldData(64,32,"moisture-budget",TerrainPreset.Continents);
  for(int i=0;i<w.Count;i++){w.Water[i]=i%64<24?WaterKind.DeepOcean:WaterKind.Land;w.Elevation[i]=(short)(i%64==38?1500:0);for(int m=0;m<12;m++)w.Temperature[i*12+m]=24;}
  var a=FlatMoisture.Build(w,default,1);var b=FlatMoisture.Build(w,default,4);var dry=FlatMoisture.Build(w,default,1,false);
  double wetRain=0,dryRain=0;float max=0;
  for(int i=0;i<a.Rain.Length;i++){
   if(a.Rain[i]!=b.Rain[i]||a.Humidity[i]!=b.Humidity[i])throw new Exception("Moisture depends on worker ordering");
   if(float.IsNaN(a.Rain[i])||a.Rain[i]<0||a.Humidity[i]<0||a.Humidity[i]>1)throw new Exception("Invalid moisture state");
   wetRain+=a.Rain[i];dryRain+=dry.Rain[i];max=Math.Max(max,a.Rain[i]);
  }
  foreach(double error in a.BudgetError)if(error>2e-5)throw new Exception("Water budget drift "+error);
  if(dryRain>wetRain*.05)throw new Exception("Atmosphere rains without water source");
  if(FlatMoisture.Capacity(30)<=FlatMoisture.Capacity(0)*3)throw new Exception("Temperature saturation capacity");
  float seamA=a.Sample(a.Humidity,-.00001f,.45f,0),seamB=a.Sample(a.Humidity,.99999f,.45f,0);if(Math.Abs(seamA-seamB)>1e-5)throw new Exception("Longitude seam");
  float windward=a.Rain[(16*64+38)*12],leeward=a.Rain[(16*64+35)*12];
  if(windward<=leeward*1.2f)throw new Exception("Missing mountain rain shadow: "+windward+" / "+leeward);
  FlatClimate.Build(w,default,1);if(w.Moisture==null)throw new Exception("Climate does not publish moisture state for clouds");
  Console.WriteLine("PASS water budgets, drying, positivity, serial/parallel equality, temperature saturation and dateline; max rain="+max+" dry/wet="+dryRain/wetRain+" budget="+a.BudgetError[0]);
  Console.WriteLine("PASS mountain windward rain="+windward+" leeward="+leeward);
 }
}
