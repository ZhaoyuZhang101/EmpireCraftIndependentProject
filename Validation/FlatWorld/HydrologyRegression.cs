using System;
using System.IO;
using System.Linq;
using Orbis.Flat;
static class HydrologyRegression
{
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Validate(WorldData w)
 {
  var rank=new int[w.Count];double input=0,output=0;
  for(int k=0;k<w.Count;k++)rank[w.DrainOrder[k]]=k;
  for(int i=0;i<w.Count;i++)
  {
   int p=w.Downstream[i];bool ocean=FlatTerrainAnalysis.Ocean(w.Water[i]);
   if(!ocean)for(int m=0;m<12;m++)input+=w.Precipitation[i*12+m]/1200.0;
   if(p<0)output+=w.Runoff[i];
   else {Check(rank[p]<rank[i],"Drain cycle");Check(w.WaterLevel[p]<=w.WaterLevel[i],"Uphill water");}
   if(w.Water[i]==WaterKind.River)Check(p>=0&&w.Water[p]!=WaterKind.Land,"Disconnected river end");
   if(w.Water[i]==WaterKind.Lake&&p>=0)Check(w.Water[p]!=WaterKind.Land,"Disconnected lake outlet");
  }
  Check(Math.Abs(input-output)<Math.Max(0.01,input*1e-5),"Runoff not conserved");
 }
 public static void Run()
 {
  var atlas=FlatAtlasTerrain.Read(File.ReadAllBytes("Assets/ORBIS/Resources/ORBIS/WorldAtlas/Default.bytes"),"ORBIS-2D-2026",default);
  Validate(atlas);
  Console.WriteLine($"Atlas: rivers={atlas.Water.Count(x=>x==WaterKind.River)}, lakes={atlas.Water.Count(x=>x==WaterKind.Lake)}, mouths={atlas.Facts.MouthCount}, max runoff={atlas.Runoff.Max():F2}");
  // A level mesa with ocean on both sides: runoff must use the nearest real spill,
  // independent of heap cell ID. The old flood-parent routing crosses the mesa.
  var flat=new WorldData(64,32,"FLAT-SPILL",TerrainPreset.Continents);
  for(int i=0;i<flat.Count;i++)
  {int x=i%64;bool sea=x<8||x>=56;flat.Elevation[i]=(short)(sea?-100:100);flat.Water[i]=sea?WaterKind.ShallowOcean:WaterKind.Land;for(int m=0;m<12;m++)flat.Precipitation[i*12+m]=100;}
  FlatHydrology.Build(flat,default);Validate(flat);
  int longestExcess=0;
  for(int y=2;y<30;y++)for(int x=8;x<56;x++)
  {int steps=0,p=y*64+x;while(!FlatTerrainAnalysis.Ocean(flat.Water[p])&&steps++<flat.Count)p=flat.Downstream[p];longestExcess=Math.Max(longestExcess,steps-Math.Min(x-7,56-x));}
  Console.WriteLine("Flat spill excess steps="+longestExcess);
  Check(longestExcess==0,"Flat drainage must follow shortest spill distance, not heap insertion order");
  // All-land, all-water, dateline and polar parents remain acyclic and conservative.
  foreach(TerrainPreset p in Enum.GetValues(typeof(TerrainPreset)))Validate(FlatGenerator.Generate(64,32,"ATLAS-ROUTING-REGRESSION",p,default));
  foreach(bool ocean in new[]{false,true})
  {
   var edge=new WorldData(64,32,"NO-COAST",TerrainPreset.Continents);
   for(int i=0;i<edge.Count;i++){edge.Elevation[i]=(short)(ocean?-100:100);edge.Water[i]=ocean?WaterKind.ShallowOcean:WaterKind.Land;for(int m=0;m<12;m++)edge.Precipitation[i*12+m]=100;}
   FlatHydrology.Build(edge,default);Validate(edge);
  }
  var repeat=FlatAtlasTerrain.Read(File.ReadAllBytes("Assets/ORBIS/Resources/ORBIS/WorldAtlas/Default.bytes"),"ORBIS-2D-2026",default);
  Check(atlas.Downstream.SequenceEqual(repeat.Downstream)&&atlas.Water.SequenceEqual(repeat.Water),"Nondeterministic hydrology");
  Console.WriteLine("PASS nearest spill, connected river exits, conservation, drainage topology and repeatability");
 }
}
