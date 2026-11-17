#ifndef ORBIS_FLAT_PIXEL_DETAIL
#define ORBIS_FLAT_PIXEL_DETAIL
// Presentation only: 32 artwork pixels per authoritative Tile. No new simulation cells.
float _DetailWeight;
float PDBox(float2 p,float2 lo,float2 hi)
{return step(lo.x,p.x)*step(lo.y,p.y)*(1-step(hi.x,p.x))*(1-step(hi.y,p.y));}
float PDHash(float2 p)
{
 p.x=p.x-floor(p.x/_MapSize.x)*_MapSize.x;
 return frac(sin(dot(p,float2(12.9898,78.233)))*43758.5453);
}
float3 PDPaint(float3 col,float3 ink,float mask){return lerp(col,FlatColor(ink),saturate(mask));}
#include "FlatEcologyDetail.cginc"
#include "FlatVegetationArt.cginc"
#include "FlatAmbientLife.cginc"
bool PDTreeAvailable(float2 id,float4 placement,float4 info,float4 ecology)
{
 if(info.b>.5||(info.g>.5&&info.g<4.5))return false;
 float2 root=placement.xy/32,uv=(id+root)*_MapSize.zw;
 float4 field=FlatVisualSample(float4(uv,0,0));
 float density=saturate((field.a-.20)*1.25)*FlatTreeDensity(ecology.r);
 float2 canonical=id;canonical.x-=floor(canonical.x/_MapSize.x)*_MapSize.x;
 float occupancy=ecology.r<-.5?placement.w:PDHash(canonical+float2(39.17+ecology.a*17,87.23));
 if(occupancy>density||density<=0||field.g>-.18)return false;
 if(_RiverAtlasReady>.5)return FlatRiverAtlasSample(id+root).r>=.15;
 float2 centreUV=(id+.5)*_MapSize.zw,flow;
 float4 hydro=tex2Dlod(_HydroA,float4(centreUV,0,0));
 float4 river=tex2Dlod(_HydroB,float4(centreUV,0,0));
 return FlatRiverDistance(root,hydro,floor(river.a+.5),river.r,centreUV,flow)>=.15;
}
float PDBuildingSdf(float2 p)
{
 float2 roof=abs(p-float2(16,21))-float2(13,7);
 float2 wall=abs(p-float2(16.5,10.5))-float2(9.5,5.5);
 return min(max(roof.x,roof.y),max(wall.x,wall.y));
}
#include "FlatGroundCover.cginc"
// Evaluate neighbouring casters before painting ANY artwork. The receiver cell
// never clips a silhouette; the source cell owns occupancy, shape and sunlight.
float PixelPropShadow(float2 cell)
{
 float coverage=0;float2 id=floor(cell);
 [loop] [fastopt] for(int row=1;row>=-1;row--)
 [loop] [fastopt] for(int column=-1;column<=1;column++)
 {
  float2 root=id+float2(column,row);
  if(root.y<0||root.y>=_MapSize.y)continue;
  float2 uv=(root+.5)*_MapSize.zw;
  float scale=max(.02,cos((uv.y-.5)*3.141592654));
  float2 q=(cell-root)*32;
  // Building and tree projections cannot reach beyond this conservative bound.
  if(q.x< -31||q.x>63||q.y< -31||q.y>63)continue;
  float3 sun=normalize(_FlatSun+float3(0,1e-7,0));
  float3 localSun=FlatGeographicLight(uv,sun);
  float height=localSun.z;
  float daylight=smoothstep(-.04,.18,height);
  if(daylight<.001)continue;
  float2 direction=-localSun.xy/max(.25,height);
  float4 info=tex2Dlod(_InfoTex,float4(uv,0,0))*255;
  float mask=0;
  if(info.b>.5&&info.b<1.5)
  {
   q.y=16+(q.y-16)/scale;
   mask=(1-smoothstep(-.5,2.5,PDBuildingSdf(q-direction*4.5)))*.27;
  }
  else if(info.b<.5&&!(info.g>.5&&info.g<4.5))
  {
   float4 placement=PDTreePlacement(root);
   float4 ecology=FlatEcologyData(uv,info);placement.w=ecology.a;
   q.y=placement.y+(q.y-placement.y)/scale;
   float2 ground=(q-placement.xy)/placement.z;
   if(abs(ground.x)>37||abs(ground.y)>37)continue;
   if(!PDTreeAvailable(root,placement,info,ecology))continue;
   float shore=1-smoothstep(.2,1.2,-FlatVisualSample(float4(uv,0,0)).g);
   float species=PDTreeSpecies(ecology,info,shore);
   float foliage=FlatVegetationSeason(root+.5).y;
   mask=PDTreeCastShadow(ground,localSun,species,placement.w,foliage)*.30;
  }
  coverage=max(coverage,mask*daylight);
 }
 return coverage;
}
float4 PDTreeArtwork(float2 pixel,float2 id,float species,float seed,float leaf,float3 light,float3 localSun)
{
 float2 edge;float4 crown=PDTreeCrown(pixel,species,seed,leaf,edge);
 float trunk=PDTreeTrunk(pixel,species,seed);
 float3 foliage=PDTreeFoliage(crown.xyz,pixel,id,species,seed,localSun,edge);
 foliage=lerp(foliage*float3(1.30,.78,.45),foliage,leaf);
 float3 col=PDPaint(0,PDTreeBark(pixel,species,seed)*light,trunk);
 col=PDPaint(col,foliage*light,crown.a);
 return float4(col,saturate(crown.a+trunk));
}
float4 PDSettlementArtwork(float2 q,float3 light)
{
 float3 col=0;
 float wall=PDBox(q,float2(7,5),float2(26,16));
 float board=step(1,fmod(q.y,4));col=PDPaint(col,lerp(float3(.30,.20,.12),float3(.59,.43,.25),board)*light,wall);
 float2 roof=q-float2(16,21);float roofMask=step(abs(roof.x),13)*step(abs(roof.y),7);
 col=PDPaint(col,float3(.19,.16,.10)*light,roofMask);
 float interior=PDBox(q,float2(4,16),float2(28,27));
 float shingle=step(1,fmod(q.y,3))*step(1,fmod(q.x+floor(q.y/3)*3,6));
 col=PDPaint(col,lerp(float3(.42,.28,.12),float3(.69,.48,.21),shingle)*light,interior);
 col=PDPaint(col,float3(.82,.65,.32)*light,PDBox(q,float2(4,26),float2(28,28)));
 col=PDPaint(col,float3(.13,.12,.10)*light,PDBox(q,float2(14,5),float2(19,13)));
 col=PDPaint(col,float3(.92,.73,.33)*light,PDBox(q,float2(9,9),float2(12,12))+PDBox(q,float2(22,9),float2(25,12)));
 return float4(col,max(wall,roofMask));
}
float3 PixelProps(float3 col,float2 cell,float4 field,float waterMask,float structure,float3 light)
{
 col*=1-PixelPropShadow(cell);
 float2 id=floor(cell),q=floor(frac(cell)*32);
 // A longitude tile narrows with latitude. Fit artwork isotropically into that
 // footprint instead of stretching its height while squeezing its width.
 float artworkScale=max(.02,cos(((id.y+.5)*_MapSize.w-.5)*3.141592654));
 q.y=floor(16+(frac(cell.y)-.5)*32/artworkScale);
 float nearestRoot=1e10;
 [loop] [fastopt] for(int row=1;row>=-1;row--)
 [loop] [fastopt] for(int column=-1;column<=1;column++)
 {
  float2 treeId=id+float2(column,row);
  if(treeId.y<0||treeId.y>=_MapSize.y)continue;
  float4 placement=PDTreePlacement(treeId);
  float2 localPoint=(cell-treeId)*32;
  float localScale=max(.02,cos(((treeId.y+.5)*_MapSize.w-.5)*3.141592654));
  localPoint.y=placement.y+(localPoint.y-placement.y)/localScale;
  float2 treePoint=floor(localPoint-placement.xy)/placement.z;
  if(abs(treePoint.x)>14||treePoint.y< -3||treePoint.y>40)continue;
  float2 centreUV=(treeId+.5)*_MapSize.zw;
  float4 rootInfo=tex2Dlod(_InfoTex,float4(centreUV,0,0))*255;
  float4 ecology=FlatEcologyData(centreUV,rootInfo);placement.w=ecology.a;
  if(!PDTreeAvailable(treeId,placement,rootInfo,ecology))continue;
  float shore=1-smoothstep(.2,1.2,-FlatVisualSample(float4(centreUV,0,0)).g);
  float species=PDTreeSpecies(ecology,rootInfo,shore);
  float leaf=FlatVegetationSeason(treeId+.5).y;
  float rootDepth=treeId.y*32+placement.y;
  if(rootDepth>nearestRoot)continue;
  float3 localSun=FlatGeographicLight(centreUV,normalize(_FlatSun+float3(0,1e-7,0)));
  float4 art=PDTreeArtwork(treePoint,treeId,species,placement.w,leaf,light,localSun);
  if(art.a<.5)continue;
  nearestRoot=rootDepth;col=lerp(col,art.rgb,art.a);
 }
 if(structure>.5 && structure<1.5)
 {
  float4 art=PDSettlementArtwork(q,light);col=lerp(col,art.rgb,art.a);
 }
 else if(structure>1.5 && structure<2.5)
 {
  float plot=PDBox(q,float2(2,2),float2(30,30));
  col=PDPaint(col,lerp(float3(.28,.19,.105),float3(.43,.30,.16),step(1,fmod(q.y,5)))*light,plot);
  float crop=step(1,fmod(q.x,6))*(1-step(4,fmod(q.x,6)))*step(2,fmod(q.y,5))*(1-step(4,fmod(q.y,5)));
  col=PDPaint(col,float3(.36,.50,.16)*light,crop*plot);
  float fence=(PDBox(q,float2(1,1),float2(31,2))+PDBox(q,float2(1,29),float2(31,30)));
  float posts=(1-step(2,fmod(q.x,8)))*(PDBox(q,float2(0,0),float2(32,5))+PDBox(q,float2(0,27),float2(32,32)));
  col=PDPaint(col,float3(.65,.50,.27)*light,saturate(fence+posts));
 }
 else if(structure>2.5)
 {
  float2 uv=(id+.5)*_MapSize.zw;
  float left=tex2Dlod(_InfoTex,float4(uv-float2(_MapSize.z,0),0,0)).b*255;
  float right=tex2Dlod(_InfoTex,float4(uv+float2(_MapSize.z,0),0,0)).b*255;
  float up=tex2Dlod(_InfoTex,float4(uv+float2(0,_MapSize.w),0,0)).b*255;
  float down=tex2Dlod(_InfoTex,float4(uv-float2(0,_MapSize.w),0,0)).b*255;
  float west=step(2.5,left),east=step(2.5,right),north=step(2.5,up),south=step(2.5,down);
  float horizontal=max(west,east),vertical=max(north,south);
  float path=PDBox(q,float2(11,11),float2(21,21));
  path=max(path,PDBox(q,float2(0,11),float2(16,21))*west);
  path=max(path,PDBox(q,float2(16,11),float2(32,21))*east);
  path=max(path,PDBox(q,float2(11,16),float2(21,32))*north);
  path=max(path,PDBox(q,float2(11,0),float2(21,16))*south);
  if(horizontal+vertical<.5)path=PDBox(q,float2(11,0),float2(21,32));
  float plank=step(1,fmod(lerp(q.y,q.x,horizontal),4));
  float3 road=structure>3.5?lerp(float3(.29,.20,.12),float3(.62,.46,.25),plank):float3(.65,.48,.28)*(.9+PDHash(id+q*.02)*.15);
  col=PDPaint(col,road*light,path);
 }
 return col;
}
#endif
