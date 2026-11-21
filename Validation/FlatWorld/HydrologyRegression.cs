using System;
using System.IO;
using System.Linq;
using Orbis.Flat;
static class HydrologyRegression
{
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void ReportNetwork(WorldData world,string name)
 {
  var incoming=new int[world.Count];
  int reaches=0,diagonals=0,rightAngleTurns=0,turns=0;
  for(int i=0;i<world.Count;i++)
  {
   int p=world.Downstream[i];
   if(world.Water[i]!=WaterKind.River||p<0||world.Water[p]!=WaterKind.River)continue;
   incoming[p]++;reaches++;
   int dx=FlatTerrainAnalysis.WrappedDX(p%world.Width,i%world.Width,world.Width),dy=p/world.Width-i/world.Width;
   if(Math.Abs(dx)==1&&Math.Abs(dy)==1)diagonals++;
   int n=world.Downstream[p];
   if(n<0||world.Water[n]!=WaterKind.River||Math.Abs(dx)>1)continue;
   int tx=FlatTerrainAnalysis.WrappedDX(n%world.Width,p%world.Width,world.Width),ty=n/world.Width-p/world.Width;
   if(Math.Abs(tx)>1)continue;
   turns++;
   if(dx*tx+dy*ty==0)rightAngleTurns++;
  }
  int heads=0,junctions=0;
  for(int i=0;i<world.Count;i++)if(world.Water[i]==WaterKind.River)
  {if(incoming[i]==0)heads++;if(incoming[i]>1)junctions++;}
  Console.WriteLine($"{name} {world.Width}x{world.Height}: river tiles={world.Water.Count(k=>k==WaterKind.River)}, river-to-river diagonals={diagonals}/{reaches}, 90 degree turns={rightAngleTurns}/{turns}, source reaches={heads}, confluences={junctions}");
 }
 static void DiagonalValley()
 {
  var world=new WorldData(64,32,"DATELINE-VALLEY",TerrainPreset.Continents);
  for(int i=0;i<world.Count;i++)
  {
   int x=i%world.Width,y=i/world.Width,axis=FMath.Wrap(60+y-8,world.Width);
   int cross=Math.Abs(FlatTerrainAnalysis.WrappedDX(x,axis,world.Width));
   world.Elevation[i]=(short)(y>=25?-100:100+(24-y)*20+cross*8);
   world.Water[i]=y>=25?WaterKind.ShallowOcean:WaterKind.Land;
   for(int month=0;month<12;month++){world.Precipitation[i*12+month]=100;world.Temperature[i*12+month]=12;}
  }
  FlatHydrology.Build(world,default);
  int follows=0;
  for(int y=8;y<24;y++)
  {
   int x=60+y-8;
   if(world.Downstream[world.Index(x,y)]==world.Index(x+1,y+1))follows++;
  }
  Console.WriteLine($"Diagonal valley across longitude seam: {follows}/16 links follow its bed");
  Check(follows==16,"The low centerline of a diagonal valley must not become a cardinal staircase");
  Validate(world);

  var isolated=world.Clone();
  Array.Clear(isolated.Precipitation,0,isolated.Precipitation.Length);
  int spring=isolated.Index(62,10);
  for(int m=0;m<12;m++)isolated.Precipitation[spring*12+m]=100;
  FlatHydrology.Build(isolated,default);
  float springFlow=isolated.Runoff[spring];var path=new bool[isolated.Count];
  Check(springFlow>0,"The isolated warm, wet source must supply runoff");
  for(int n=spring;n>=0;n=isolated.Downstream[n])path[n]=true;
  for(int i=0;i<isolated.Count;i++)Check(isolated.Runoff[i]==(path[i]?springFlow:0),
   "A single source keeps exactly its discharge through the seam, with no water in a neighbouring catchment");

  int source=world.Index(63,11),receiver=world.Index(0,12);
  world.Elevation[world.Index(0,11)]=world.Elevation[world.Index(63,12)]=1800;
  FlatHydrology.Build(world,default);
  Check(world.Downstream[source]!=receiver,"A lower diagonal beyond two high banks is not a spill");
  Validate(world);
  int wetBank=world.Index(63,12);
  world.Elevation[wetBank]=(short)(world.Elevation[receiver]-25);
  world.Elevation[world.Index(0,11)]=(short)(world.Elevation[source]+25);
  FlatHydrology.Build(world,default);
  Check(world.Water[wetBank]==WaterKind.Lake&&world.Downstream[source]!=receiver,
   "A dry-looking diagonal may not bypass an intervening standing lake at a shared corner");
 }
 static void LatitudeAndPole()
 {
  foreach(int y in new[]{1,16})
  {
   var w=new WorldData(64,32,"METRIC-SLOPE",TerrainPreset.Continents);
   for(int i=0;i<w.Count;i++){w.Elevation[i]=-100;w.Water[i]=WaterKind.ShallowOcean;}
   int at=w.Index(16,y),east=w.Index(17,y),north=w.Index(16,y+1);
   for(int dx=-1;dx<=1;dx++)for(int dy=-1;dy<=1;dy++)
   {int n=w.Index(16+dx,y+dy);w.Elevation[n]=110;w.Water[n]=WaterKind.Land;}
   w.Elevation[at]=100;w.Elevation[east]=92;w.Elevation[north]=84;
   FlatHydrology.BuildDrainage(w,default);
   Check(w.Downstream[at]==(y==1?east:north),"Use physical grade per ground distance at each latitude");
  }
  var polar=new WorldData(64,32,"POLAR-SPILL",TerrainPreset.Continents);
  for(int i=0;i<polar.Count;i++){polar.Elevation[i]=-100;polar.Water[i]=WaterKind.ShallowOcean;}
  int origin=polar.Index(0,31),opposite=polar.Index(32,31);
  for(int y=30;y<=31;y++)for(int dx=-1;dx<=1;dx++)
  {int n=polar.Index(dx,y);polar.Elevation[n]=300;polar.Water[n]=WaterKind.Land;}
  polar.Elevation[origin]=100;
  FlatHydrology.BuildDrainage(polar,default);
  Check(polar.Downstream[origin]==opposite,"A real polar outlet crosses to the opposite meridian of the same pole");
 }
 static void TributaryValleys()
 {
  var w=new WorldData(128,64,"TWIN-VALLEYS",TerrainPreset.Continents);
  const int outletX=64,junctionY=32;
  for(int i=0;i<w.Count;i++)
  {
   int x=i%w.Width,y=i/w.Width,span=Math.Max(0,junctionY-y);
   int cross=Math.Min(Math.Abs(FlatTerrainAnalysis.WrappedDX(x,outletX-span,w.Width)),
                      Math.Abs(FlatTerrainAnalysis.WrappedDX(x,outletX+span,w.Width)));
   bool ocean=y<=8||y>=54;
   w.Elevation[i]=(short)(ocean?-100:100+(54-y)*20+cross*8);
   w.Water[i]=ocean?WaterKind.ShallowOcean:WaterKind.Land;
   for(int m=0;m<12;m++){w.Precipitation[i*12+m]=120;w.Temperature[i*12+m]=10;}
  }
  FlatHydrology.Build(w,default);
  int confluence=w.Index(outletX,junctionY);
  foreach(int side in new[]{-1,1})
  {
   int first=junctionY;
   for(int y=10;y<junctionY;y++)
    if(w.Water[w.Index(outletX+side*(junctionY-y),y)]==WaterKind.River){first=y;break;}
   Console.WriteLine($"Twin valleys, side {side}: channel begins on row {first}");
   Check(first<=16,"Both sustained tributaries must initiate in the upper third of the wet inland valley");
   int at=w.Index(outletX+side*(junctionY-first),first);
   while(at!=confluence&&w.Water[at]==WaterKind.River&&at/w.Width<junctionY)at=w.Downstream[at];
   Check(at==confluence,"Both tributaries must join the same downstream trunk");
  }
  Validate(w);
  // Identical rainfall on a tilted plane has no convergent catchment or
  // incised banks. A long raster-aligned path alone cannot initiate a stream.
  for(int i=0;i<w.Count;i++)
  {
   int y=i/w.Width;
   if(!FlatTerrainAnalysis.Ocean(w.Water[i]))w.Elevation[i]=(short)(100+(54-y)*20);
  }
  FlatHydrology.Build(w,default);
  for(int y=15;y<=48;y++)for(int x=0;x<w.Width;x++)
   Check(w.Water[w.Index(x,y)]==WaterKind.Land,"A broad planar slope must not turn into parallel rivers");
 }
 static void ShallowPassThroughs()
 {
  var w=new WorldData(64,32,"SMALL-SPILL",TerrainPreset.Continents);
  for(int i=0;i<w.Count;i++)
  {
   int x=i%w.Width,y=i/w.Width;
   w.Water[i]=x>=55?WaterKind.ShallowOcean:WaterKind.Land;
   w.Elevation[i]=(short)(x>=55?-100:100+5*(54-x)+30*Math.Abs(y-16));
   for(int m=0;m<12;m++){w.Precipitation[i*12+m]=120;w.Temperature[i*12+m]=10;}
  }
  int first=w.Index(39,16),second=w.Index(40,16);
  w.Elevation[first]=163;w.Elevation[second]=164;
  FlatHydrology.Build(w,default);
  Check(w.Water[first]==WaterKind.River&&w.Water[second]==WaterKind.River,
   "A well-fed shallow pocket must carry the main channel continuously over its spill");
  Check(w.WaterLevel[first]==165&&w.Elevation[first]==163&&w.Downstream[first]==second,
   "Routing a shallow throughflow must preserve its surveyed bed, actual sill and downstream direction");
  var dry=w.Clone();Array.Clear(dry.Precipitation,0,dry.Precipitation.Length);
  FlatHydrology.Build(dry,default);
  Check(dry.Water[first]==WaterKind.Land&&dry.Water[second]==WaterKind.Land,
   "A small filled depression alone cannot create a flowing river without a water supply");
  var deep=w.Clone();deep.Elevation[first]=125;
  FlatHydrology.Build(deep,default);
  Check(deep.Water[first]==WaterKind.Lake,"A deep pocket remains standing water even with a large inlet");
  var wide=w.Clone();wide.Elevation[first]=157;wide.Elevation[second]=158;wide.Elevation[wide.Index(41,16)]=159;
  FlatHydrology.Build(wide,default);
  Check(wide.Water[first]==WaterKind.Lake&&wide.Water[wide.Index(41,16)]==WaterKind.Lake,
   "A mapped lake extending over several cells keeps its basin");
  var belowSea=w.Clone();
  for(int i=0;i<w.Count;i++)if(!FlatTerrainAnalysis.Ocean(w.Water[i]))belowSea.Elevation[i]-=160;
  belowSea.Elevation[first]=-1;belowSea.Elevation[second]=-2;
  FlatGenerator.Rebuild(belowSea,default);
  Check(belowSea.Water[first]==WaterKind.Lake&&belowSea.WaterLevel[first]==5,
   "Inland freshwater below sea level retains the actual sill that separates it from the ocean");
  Validate(w);ValidateLakeOutlets(wide);
 }
 static void ValidateLakeOutlets(WorldData world)
 {
  var visited=new bool[world.Count];var queue=new int[world.Count];int lakes=0,multiple=0;
  for(int start=0;start<world.Count;start++)
  {
   if(visited[start]||world.Water[start]!=WaterKind.Lake)continue;
   int head=0,tail=1,exits=0;queue[0]=start;visited[start]=true;lakes++;
   while(head<tail)
   {
    int cell=queue[head++],parent=world.Downstream[cell];
    if(parent>=0&&(world.Water[parent]!=WaterKind.Lake||world.WaterLevel[parent]!=world.WaterLevel[cell]))exits++;
    for(int direction=0;direction<4;direction++)
    {
     int next=world.SphereNeighbor(cell,direction);
     if(visited[next]||world.Water[next]!=WaterKind.Lake||world.WaterLevel[next]!=world.WaterLevel[start])continue;
     visited[next]=true;queue[tail++]=next;
    }
   }
   if(exits>1)multiple++;
  }
  Console.WriteLine($"Lake basins={lakes}, basins with multiple outlets={multiple}");
  Check(multiple==0,"Connected lake surface must share one main spill outlet");
 }
 static void Validate(WorldData w)
 {
  var rank=new int[w.Count];var local=new float[w.Count];var incoming=new double[w.Count];double input=0,output=0;
  FlatRiverDischarge.BuildSources(w,default,local);
  for(int k=0;k<w.Count;k++)rank[w.DrainOrder[k]]=k;
  for(int i=0;i<w.Count;i++)
  {
   int p=w.Downstream[i];input+=local[i];
   if(p<0)output+=w.Runoff[i];
   else
   {
    incoming[p]+=w.Runoff[i];Check(rank[p]<rank[i],"Drain cycle");Check(w.WaterLevel[p]<=w.WaterLevel[i],"Uphill water");
    Check(FlatHydrology.IsDrainageNeighbor(w,i,p),"Drainage edge crosses nonadjacent terrain");
    int dx=FlatTerrainAnalysis.WrappedDX(p%w.Width,i%w.Width,w.Width),dy=p/w.Width-i/w.Width;
    if(Math.Abs(dx)==1&&Math.Abs(dy)==1)
    {
     int left=w.Index(i%w.Width+dx,i/w.Width),right=w.Index(i%w.Width,i/w.Width+dy);
     Check(!FlatTerrainAnalysis.AreaWater(w.Water[left])&&!FlatTerrainAnalysis.AreaWater(w.Water[right]),"Diagonal skipped a standing water body");
     int sideSum=w.Elevation[left]+w.Elevation[right];
     Check(sideSum>=2*w.Elevation[p]&&sideSum<=2*w.Elevation[i],"Diagonal route cuts across a ridge or trough");
    }
   }
   if(w.Water[i]==WaterKind.River)
   {
    if(p>=0)Check(w.Water[p]!=WaterKind.Land,"Disconnected river end");
    else for(int d=0;d<4;d++)Check(w.WaterLevel[w.SphereNeighbor(i,d)]>=w.WaterLevel[i],
     "A terminal inland river stopped above a lower exit");
   }
   if(w.Water[i]==WaterKind.Lake&&p>=0&&w.Runoff[i]>0)Check(w.Water[p]!=WaterKind.Land,"Disconnected lake outlet");
  }
  Check(Math.Abs(input-output)<Math.Max(0.01,input*1e-5),"Runoff not conserved");
  for(int i=0;i<w.Count;i++)
   Check(Math.Abs(w.Runoff[i]-incoming[i]-local[i])<.00001*Math.Max(1,w.Runoff[i]),"A junction lost or duplicated annual discharge");
 }
 static void ValidateProceduralMouths(WorldData world)
 {
  var terminal=new WaterKind[world.Count];
  int sea=0,lake=0,inland=0,overlap=0;
  foreach(int i in world.DrainOrder)
  {
   if(world.Water[i]!=WaterKind.River)continue;
   int next=world.Downstream[i];
   if(next<0)
   {
    for(int d=0;d<4;d++) Check(world.WaterLevel[world.SphereNeighbor(i,d)]>=world.WaterLevel[i],
     "An internal river terminal must be a local minimum");
    terminal[i]=WaterKind.River;
   }
   else if(FlatTerrainAnalysis.AreaWater(world.Water[next]))
   {
    terminal[i]=world.Water[next];
    if(world.Facts.HalfWidth[i]>=.01f)
    {
     float x=next%world.Width+.5f,y=next/world.Width+.5f;
     var curves=world.Facts.RiverCurves;
     bool joins=false;
     for(int s=0;s<curves.TileSegmentCount(next);s++)
     {
      var segment=curves.TileSegment(next,s);
      float dx=segment.B.X-x;
      dx-=world.Width*(float)Math.Round(dx/world.Width);
      if(segment.Reach==i && Math.Abs(dx)<.0001f && Math.Abs(segment.B.Y-y)<.0001f)
      { joins=true; break; }
     }
     Check(joins && curves.Sample(x,y).Distance<0, $"River {i} misses receiving water {next} in {world.Seed}");
     overlap++;
    }
   }
   else
   {
    Check(world.Water[next]==WaterKind.River && terminal[next]!=WaterKind.Land,"River stops on land or cycles");
    terminal[i]=terminal[next];
   }
   if(FlatTerrainAnalysis.Ocean(terminal[i]))sea++;
   else if(terminal[i]==WaterKind.Lake)lake++;
   else inland++;
  }
  Console.WriteLine($"{world.Preset} {world.Seed}: {sea} river cells reach sea, {lake} reach a lake, {inland} internal, {overlap} rendered mouth overlaps");
 }

 public static void Run()
 {
  var atlas=FlatAtlasTerrain.Read(File.ReadAllBytes("Assets/ORBIS/Resources/ORBIS/WorldAtlas/Default.bytes"),"ORBIS-2D-2026",default);
  Validate(atlas);
  ReportNetwork(atlas,"Default atlas");
  var seeded=FlatGenerator.Generate(256,128,"ATLAS-ROUTING-REGRESSION",TerrainPreset.Continents,default);
  ReportNetwork(seeded,"Seeded continents");
  ValidateProceduralMouths(seeded);
  DiagonalValley();LatitudeAndPole();ShallowPassThroughs();TributaryValleys();
  Console.WriteLine($"Atlas: rivers={atlas.Water.Count(x=>x==WaterKind.River)}, lakes={atlas.Water.Count(x=>x==WaterKind.Lake)}, mouths={atlas.Facts.MouthCount}, max runoff={atlas.Runoff.Max():F2}");
  float leastSediment=1,mostSediment=0;
  for(int i=0;i<atlas.Count;i++)if(atlas.Water[i]==WaterKind.Lake){leastSediment=Math.Min(leastSediment,atlas.Facts.Sediment[i]);mostSediment=Math.Max(mostSediment,atlas.Facts.Sediment[i]);}
  Check(mostSediment-leastSediment>.02f,"Lake clarity should respond to incoming sediment instead of a uniform tint");
  Console.WriteLine($"Lake sediment range={leastSediment:F3}..{mostSediment:F3}");
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
  foreach(TerrainPreset p in FlatGenerator.ProceduralPresets)
  {
   var generated=FlatGenerator.Generate(64,32,"ATLAS-ROUTING-REGRESSION",p,default);
   Validate(generated);ValidateLakeOutlets(generated);
   ValidateProceduralMouths(generated);
   ValidateProceduralMouths(FlatGenerator.Generate(128,64,"MOUTH-SECOND-SEED",p,default));
   var loaded=FlatSave.Decode(FlatSave.Encode(generated));
   Check(generated.Downstream.SequenceEqual(loaded.Downstream),"Save changed lake drainage");
  }
  foreach(bool ocean in new[]{false,true})
  {
   var edge=new WorldData(64,32,"NO-COAST",TerrainPreset.Continents);
   for(int i=0;i<edge.Count;i++){edge.Elevation[i]=(short)(ocean?-100:100);edge.Water[i]=ocean?WaterKind.ShallowOcean:WaterKind.Land;for(int m=0;m<12;m++)edge.Precipitation[i*12+m]=100;}
   FlatHydrology.Build(edge,default);Validate(edge);
  }
  var repeat=FlatAtlasTerrain.Read(File.ReadAllBytes("Assets/ORBIS/Resources/ORBIS/WorldAtlas/Default.bytes"),"ORBIS-2D-2026",default);
  Check(atlas.Downstream.SequenceEqual(repeat.Downstream)&&atlas.Water.SequenceEqual(repeat.Water),"Nondeterministic hydrology");
  ValidateLakeOutlets(atlas);
  Console.WriteLine("PASS nearest spill, connected river exits, conservation, drainage topology and repeatability");
 }
}
