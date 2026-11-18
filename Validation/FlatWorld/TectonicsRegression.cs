using System;
using System.Diagnostics;
using System.Linq;
using Orbis.Flat;

// Plate-tectonic generation and one-off fluvial erosion: determinism, margins,
// volcanoes, land share per preset, connected drainage and bounded lakes.
public static class TectonicsRegression
{
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static double LandShare(WorldData w){double land=0,all=0;for(int i=0;i<w.Count;i++){double a=Math.Cos(((i/w.Width+.5)/w.Height-.5)*Math.PI);all+=a;if(!FlatTerrainAnalysis.AreaWater(w.Water[i])||w.Water[i]==WaterKind.Lake)land+=a;}return land/all;}
 public static void Run()
 {
  foreach(TerrainPreset preset in Enum.GetValues(typeof(TerrainPreset)))
  {
   var clock=Stopwatch.StartNew();
   var w=FlatGenerator.Generate(256,128,"TECTONICS-REGRESSION",preset,default);long ms=clock.ElapsedMilliseconds;
   var again=FlatGenerator.Generate(256,128,"TECTONICS-REGRESSION",preset,default);
   Check(w.Elevation.SequenceEqual(again.Elevation),preset+": same seed must give the same bed");
   var t=w.Tectonics;Check(t!=null&&t.Plate.Length==w.Count&&t.Seismicity.Length==w.Count,preset+": tectonic record");
   Check(t.Plates.Length>=8&&t.Plates.All(p=>p.Tiles>0),preset+": every plate owns ground");
   Check(t.Volcanoes.Length>=10&&t.Volcanoes.All(v=>v.Tile>=0&&v.Tile<w.Count&&v.Activity>=0&&v.Activity<=1),preset+": volcano sites");
   Check(t.Volcanoes.Any(v=>v.Kind==VolcanoKind.Hotspot),preset+": hotspot chain");
   Check(t.Setting.Any(s=>s==TectonicSetting.Ridge)&&t.Setting.Any(s=>s==TectonicSetting.Collision||s==TectonicSetting.Subduction||s==TectonicSetting.IslandArc),preset+": spreading and converging margins");
   Check(t.Seismicity.All(s=>s>=0&&s<=1)&&t.Seismicity.Max()>.5f,preset+": seismic belts");
   double share=LandShare(w);double target=preset==TerrainPreset.Archipelago?.12:preset==TerrainPreset.GreatContinent?.33:preset==TerrainPreset.InlandSea?.40:.29;
   Check(Math.Abs(share-target)<.12,preset+": land share "+share.ToString("F3")+" far from "+target);
   int land=0,toSea=0,lakeTiles=0,high=0;
   for(int i=0;i<w.Count;i++)
   {
    if(w.Water[i]==WaterKind.Lake)lakeTiles++;
    if(w.Water[i]!=WaterKind.Land&&w.Water[i]!=WaterKind.River)continue;land++;if(w.Elevation[i]>=2000)high++;
    int n=i,guard=0;while(n>=0&&FlatTerrainAnalysis.AreaWater(w.Water[n])==false&&guard++<w.Count)n=w.Downstream[n];
    Check(n>=0,preset+": dry tile without an outlet");
    if(FlatTerrainAnalysis.Ocean(w.Water[n]))toSea++;
   }
   Check(toSea>land/2,preset+": most land must drain to the sea, got "+toSea+"/"+land);
   Check(lakeTiles<land/6,preset+": lakes cover too much land "+lakeTiles+"/"+land);
   Check(high>0,preset+": no high mountains or plateaus");
   double seam=0,inner=0;for(int y=0;y<w.Height;y++){seam+=Math.Abs(w.Elevation[y*w.Width]-w.Elevation[y*w.Width+w.Width-1]);for(int x=1;x<w.Width;x++)inner+=Math.Abs(w.Elevation[y*w.Width+x]-w.Elevation[y*w.Width+x-1]);}
   seam/=w.Height;inner/=w.Height*(w.Width-1.0);
   Check(seam<inner*2.5,preset+": dateline seam "+seam.ToString("F0")+" m vs interior "+inner.ToString("F0")+" m");
   Console.WriteLine($"{preset}: {ms} ms, land {share:P1}, plates {t.Plates.Length} ({t.Plates.Count(p=>p.Continental)} continental), volcanoes {t.Volcanoes.Length}, drain to sea {100.0*toSea/land:F0}%, lake tiles {lakeTiles}, >=2000 m {high}");
  }
  var clock2=Stopwatch.StartNew();var big=FlatGenerator.Generate(1024,512,"TECTONICS-REGRESSION",TerrainPreset.Continents,default);
  Console.WriteLine("1024x512 full generation "+clock2.ElapsedMilliseconds+" ms");
  Console.WriteLine("PASS tectonic generation, margins, volcanoes, hotspots, land shares, drainage and lakes");
 }
}
