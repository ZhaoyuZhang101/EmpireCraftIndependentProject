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
    case "--earth": EarthPresetRegression(); break;
    case "--world-sizes": WorldSizeRegression(); break;
    case "--large-presets": LargeProceduralPresets(); break;
    case "--river-discharge": RiverDischargeRegression.Run(); break;
    case "--river-map": RiverMapRegression.Run(); break;
    case "--forest-stands": ForestStandRegression.Run(); break;
    case "--wildlife": WildlifeRegression.Run(); break;
    case "--bathymetry": BathymetryRegression.Run(); break;
    case "--polar-terrain": PolarTerrainRegression.Run(); break;
    case "--spherical-relief": SphericalRelief(); break;
    case "--weather": WeatherIntegrationRegression.Run(); break;
    case "--moisture": MoistureRegression(); break;
    case "--seasonal-rainfall": SeasonalRainfallRegression.Run(); break;
    case "--seasonal-temperature": SeasonalTemperatureRegression.Run(); break;
    case "--east-asia-climate": ClimatologyRegression.EastAsia(args.Length>1?int.Parse(args[1]):256); break;
    case "--environment": EnvironmentRegression(); break;
    case "--hydrology": HydrologyRegression.Run(); break;
    case "--basin-water": BasinWaterRegression.Run(); break;
    case "--cyclone": CycloneRegression.Run(); break;
    default: Console.WriteLine(FlatSelfTests.Run()); break;
   }
   return 0;
  }
  catch(Exception e) { Console.Error.WriteLine(FlatSelfTests.LastOperation+"\n"+e); return 1; }
 }
 static void SphericalRelief() {
  const int width=256, height=128;
  double step=2*Math.PI/width, worstSlope=0, worstRelief=0;
  foreach(int row in new[]{0,6,21,64,106,121,127}) {
   int column=row%2==0?width-1:0;
   double latitude=(row+.5)*step-Math.PI/2, longitude=(column+.5)*step-Math.PI;
   foreach(bool east in new[]{true,false}) {
    var world=new WorldData(width,height,"SPHERICAL-SLOPE",TerrainPreset.Earth);
    for(int y=0;y<height;y++)for(int x=0;x<width;x++) {
     double lat=(y+.5)*step-Math.PI/2, delta=(x+.5)*step-Math.PI-longitude;
     // A plane in 3D tangent coordinates, rotated about the site normal.
     double dot=east?Math.Cos(lat)*Math.Sin(delta):
       Math.Sin(lat)*Math.Cos(latitude)-Math.Cos(lat)*Math.Sin(latitude)*Math.Cos(delta);
     world.Elevation[y*width+x]=(short)Math.Round(8000*dot);
     world.DrainOrder[y*width+x]=y*width+x;
    }
    FlatTerrainAnalysis.Build(world,default,false);
    int i=row*width+column;
    double slope=Math.Abs(world.Facts.Gradient[i]/(8000*Math.Sin(step))-1);
    double relief=Math.Abs(world.Facts.Relief[i]/(16000*Math.Sin(2*step))-1);
    worstSlope=Math.Max(worstSlope,slope);worstRelief=Math.Max(worstRelief,relief);
    Console.WriteLine($"lat {latitude*180/Math.PI,7:F2} {(east?"EW":"NS")} slope {world.Facts.Gradient[i],6:F1} m, relief {world.Facts.Relief[i],6:F1} m");
   }
  }
  if(worstSlope>.012||worstRelief>.012)throw new Exception($"Equal ground slopes differ by {worstSlope:P2}, relief by {worstRelief:P2}.");
  Console.WriteLine($"PASS spherical terrain diagnostics, maximum slope/relief errors {worstSlope:P2}/{worstRelief:P2}");
 }
 static void LargeProceduralPresets() {
  void Check(int width,TerrainPreset preset) {
   var timer=System.Diagnostics.Stopwatch.StartNew();
   var world=FlatGenerator.Generate(width,width/2,"SCALE-QA-"+width,preset,default);
   FlatSave.Validate(world);
   int habitable=0,marine=0;
   for(int i=0;i<world.Count;i++) {
    if(world.Habitable(i))habitable++;
    if(world.Water[i]>=WaterKind.DeepOcean&&world.Water[i]<=WaterKind.ShallowOcean)marine++;
   }
   if(world.Tectonics==null||world.Facts==null||world.Moisture==null||habitable<=0||marine<=0||
      world.Count!=(long)width*width/2||world.Temperature.Length!=world.Count*12)
    throw new Exception("Incomplete generated world: "+width+" "+preset);
   Console.WriteLine($"PASS {preset} {width}x{world.Height}: {world.Count:N0} validated tiles, {habitable:N0} habitable, {marine:N0} marine, {timer.Elapsed.TotalSeconds:F1}s");
  }
  foreach(int width in new[]{1536,2048})foreach(var preset in FlatGenerator.ProceduralPresets) {
   Check(width,preset);
   GC.Collect();
  }
 }
 static void WorldSizeRegression() {
  const string resources="Assets/ORBIS/Resources/ORBIS/FlatWorld/";
  var widths=Array.ConvertAll(System.IO.File.ReadAllLines(resources+"PlayerWidths.txt"),int.Parse);
  if(string.Join(",",widths)!="256,512,1024,1536,2048")throw new Exception("Unexpected playable widths.");
  foreach(int width in widths) {
   if(!WorldData.SupportsDimensions(width,width/2))throw new Exception("Menu size rejected by world constructor.");
   if(new System.IO.FileInfo(resources+"Earth"+width+".bytes").Length!=(long)width*width)
    throw new Exception("Earth resource dimensions disagree with playable width "+width);
  }
  int max=WorldData.MaxWidth;
  if(WorldData.SupportsDimensions(max,unchecked(int.MinValue+max/2))||WorldData.SupportsDimensions(max+8,max/2+4))
   throw new Exception("Accepted overflowing or unsupported world dimensions.");
  if(max!=2048 || FlatSave.MaxFileBytes < (long)max*(max/2)*130 + (long)max*(max/2)/16*4)
   throw new Exception("The largest playable world exceeds the save envelope.");
  // A valid checksum on a short payload must not allocate arrays for the advertised large world.
  var fixture=FlatGenerator.Generate(64,32,"SAVE-SIZE",TerrainPreset.Continents,default);
  byte[] file=FlatSave.Encode(fixture);
  int header="ORBIS-FLAT-01".Length+4+32;
  BitConverter.GetBytes(max).CopyTo(file,header+4);
  BitConverter.GetBytes(max/2).CopyTo(file,header+8);
  using(var sha=System.Security.Cryptography.SHA256.Create())sha.ComputeHash(file,header,file.Length-header).CopyTo(file,header-32);
  try {FlatSave.Decode(file);}
  catch(System.IO.InvalidDataException e) when(e.Message.Contains("tile payload")) {
   Console.WriteLine("PASS five player elevation grids, large save capacity and rejection before world allocation");return;
  }
  throw new Exception("Decoder did not reject an undersized grid before allocating the world.");
 }
 static void EarthPresetRegression() {
  const int width=256,height=128;
  const string folder="Assets/ORBIS/Resources/ORBIS/FlatWorld/";
  var raw=System.IO.File.ReadAllBytes(folder+"Earth256.bytes");
  var water=System.IO.File.ReadAllBytes(folder+"EarthWater256.bytes");
  var rawOnly=FlatGenerator.Generate(width,height,"EARTH-REGRESSION",TerrainPreset.Earth,default,raw);
  var world=FlatGenerator.Generate(width,height,"EARTH-REGRESSION",TerrainPreset.Earth,default,raw,water);
  FlatSave.Validate(world);
  var saved=FlatSave.Decode(FlatSave.Encode(world));
  if(saved.Preset!=TerrainPreset.Earth || world.Tectonics!=null)throw new Exception("Earth source was lost or procedurally regenerated.");
  int changed=0,largestCut=0,largestRise=0;
  for(int i=0;i<world.Count;i++) {
   int p=2*((height-1-i/width)*width+i%width);
   short expected=unchecked((short)(raw[p] | raw[p+1]<<8));
   if(rawOnly.Elevation[i]!=expected || saved.Elevation[i]!=world.Elevation[i] ||
      saved.WaterLevel[i]!=world.WaterLevel[i] || saved.Water[i]!=world.Water[i] ||
      !world.WaterReference.MatchesOriginalBed(i,world.Elevation[i]))
    throw new Exception($"Earth elevation, water or orientation changed at {i}.");
   if(world.Elevation[i]==expected)continue;
   int delta=world.Elevation[i]-expected;
   if(world.Water[i]!=WaterKind.Lake || world.Elevation[i]!=world.WaterReference.At(i) ||
      world.WaterLevel[i]!=world.Elevation[i] || delta>2)
    throw new Exception($"Invalid representative lake bed at {i}: change {delta} metres.");
   changed++;largestCut=Math.Max(largestCut,-delta);largestRise=Math.Max(largestRise,delta);
  }
  if(changed==0)throw new Exception("The preset did not apply its surveyed majority-lake beds.");
  void ExpectBadRaster(byte[] bytes) {
   try { FlatGenerator.Generate(width,height,"INVALID-DEM",TerrainPreset.Earth,default,bytes); }
   catch(ArgumentException) { return; }
   throw new Exception("Accepted an invalid Earth height grid.");
  }
  ExpectBadRaster(null);
  ExpectBadRaster(new byte[16]);
  raw[0]=0xff;raw[1]=0x7f;
  ExpectBadRaster(raw);
  Console.WriteLine($"PASS raw Earth DEM, all {world.Count} preset cells and saved levels; {changed} surveyed lake beds, " +
   $"largest cut {largestCut} m, largest rise {largestRise} m; missing/truncated/out-of-range DEM rejected");
 }
 static void EnvironmentRegression() {
  ClimatologyRegression.RotatedContinent();
  ClimatologyRegression.EasternCoastCirculation();
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
  // The main scene creates this seed with the plate generator. The baked atlas
  // above is a separate import fixture with a different ocean and climate.
  var defaultWorld=FlatGenerator.Generate(512,256,"ORBIS-2D-2026",TerrainPreset.Continents,default);
  int activeCycloneMonths=0;
  for(int month=0;month<12;month++)
  {
   var north=FlatCycloneSelection.Find(defaultWorld,month,true);
   var south=FlatCycloneSelection.Find(defaultWorld,month,false);
   if(north.Active||south.Active)activeCycloneMonths++;
  }
  if(activeCycloneMonths==0)throw new Exception("The main scene's default world has no climate-eligible cyclone month.");
  Console.WriteLine("Main scene climate-eligible cyclone months="+activeCycloneMonths);
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
