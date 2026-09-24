using System;
using Orbis.Flat;
public static class BathymetryRegression
{
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
 }
}
