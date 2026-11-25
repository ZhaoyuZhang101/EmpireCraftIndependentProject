#ifndef ORBIS_FLAT_PIXEL_DETAIL
#define ORBIS_FLAT_PIXEL_DETAIL
// Presentation only: 32 artwork pixels per authoritative Tile. No new simulation cells.
float _DetailWeight;
float PDBox(float2 p,float2 lo,float2 hi)
{return step(lo.x,p.x)*step(lo.y,p.y)*(1-step(hi.x,p.x))*(1-step(hi.y,p.y));}
#include "FlatTreePlacement.cginc"
UNITY_DECLARE_TEX2D_NOSAMPLER(_EarthTreeBanks);
float2 FlatCachedTreeBank(float2 id,float gridShore)
{
 if(_EarthStraitReady+_EarthLakeReady<.5)return float2(gridShore,0);
#ifdef UNITY_SEPARATE_TEXTURE_SAMPLER
 id.x-=floor(id.x/_MapSize.x)*_MapSize.x;
 float2 bank=_EarthTreeBanks.Load(int3(id,0)).rg;
#else
 float2 bank=FlatPresentationSample(_EarthTreeBanks,(id+.5)*_MapSize.zw,0).rg;
#endif
 return bank.y>0?bank:float2(gridShore,0);
}
float3 PDPaint(float3 col,float3 ink,float mask){return lerp(col,FlatColor(ink),saturate(mask));}
#include "FlatEcologyDetail.cginc"
#include "FlatVegetationWind.cginc"
#include "FlatVegetationArt.cginc"
#include "FlatPlantProjection.cginc"
float3 FlatVegetation(float3 col,float2 cell,float canopy,float pixelsPerTile,PDPlantView view)
{
 if(_Vegetation<.5)return col;
 float4 plant=FlatCanopyPlacement(cell);
 float scale=lerp(1,FlatArtworkLatitudeScale(plant.y*_MapSize.w),_LocalPhysicalArtwork);
 float2 offset=PDPlantPixel(view,cell,plant.xy,scale)/16;
 float edge=FlatPlantRiverEdge(plant.xy,pixelsPerTile);
 return FlatCanopyColor(col,cell,canopy,pixelsPerTile,plant.zw,offset,edge);
}
#include "FlatAmbientLife.cginc"
float PDTreeSizeAt(float2 id,float4 placement,float4 info,float4 ecology,bool directProbe)
{
 if(info.b>.5||(info.g>.5&&info.g<4.5))return 0;
 float2 root=placement.xy/32,uv=(id+root)*_MapSize.zw;
 float4 field=FlatVisualSample(float4(uv,0,0));
 float density=saturate((field.a-.20)*1.25)*FlatTreeDensity(ecology.r);
 float2 canonical=id;canonical.x-=floor(canonical.x/_MapSize.x)*_MapSize.x;
 float occupancy=ecology.r<-.5?placement.w:PDHash(canonical+float2(39.17+ecology.a*17,87.23));
 if(occupancy>density||density<=0||field.g>-.18)return 0;
 // A root is a geographic point. Its surveyed bank must stay fixed while a
 // crown, shadow or reflection crosses pixels with different screen footprints.
 float2 bankSurvey;
#ifdef ORBIS_DIRECT_TREE_BANK_TEST
 // Synthetic editor controls can put a root at an arbitrary point within the cell.
 if(directProbe)bankSurvey=FlatEarthTreeBank(uv,info.g,field.g);
 else
#endif
 bankSurvey=FlatCachedTreeBank(id,field.g);
 float shore=bankSurvey.x;
 if(shore>=0)return 0;
 float size=placement.z;
 float surveyed=bankSurvey.y;
 if(surveyed>0)
 {
  // The 32-pixel tree is a map symbol. Allow 1.5 km of coastal overhang,
  // then scale its full 21 x 40-pixel outer extent about the geographic foot.
  float cellKm=40030.2*_MapSize.z;
  float pixelKm=cellKm*FlatTreeLatitudeScale(id,placement.y)/32;
  float coastalSize=(1.5-shore*cellKm)/(length(float2(21,40))*pixelKm);
  size=lerp(size,min(size,coastalSize),surveyed);
 }
 float bank=FlatPlantRiverEdge(id+root,_PixelsPerTile);
 if(bank<.15)return 0;
 // Cap the whole map symbol at the geographic bank. The same sized tree
 // supplies its upright artwork, cast shadow and reflection.
 float extent=length(float2(21,40))*FlatTreeLatitudeScale(id,placement.y)/32;
 return min(size,bank/max(.0001,extent));
}
float PDTreeSize(float2 id,float4 placement,float4 info,float4 ecology)
{return PDTreeSizeAt(id,placement,info,ecology,false);}
bool PDTreeAvailable(float2 id,float4 placement,float4 info,float4 ecology)
{return PDTreeSize(id,placement,info,ecology)>0;}
static const float PDSettlementHeightProjection=.46947156;
struct PDSettlementState
{
 float wall,roof,eave,awning,pole,flag;
 float2 roofQ;
 float flagWave;
};
// Foundation & wall (y<=15) stay strictly pinned; roof eaves, side awning and gable flag flutter with published wind.
// Uses a single sincos pair per evaluation to keep D3D11 fragment compile time and ALU compact.
PDSettlementState PDSettlementEvaluate(float2 q,float2 wind,float seed)
{
 PDSettlementState s=(PDSettlementState)0;
 s.wall=PDBox(q,float2(7,5),float2(26,16));
 float windMag=length(wind);
 float breeze=smoothstep(.15,.75,windMag);
 float storm=smoothstep(.55,1.85,windMag);
 float phase=_SkyClock*(4.2+6.8*storm)+seed*6.283185307;
 float sPhase,cPhase;
 sincos(phase,sPhase,cPhase);
 float roofLift=saturate((q.y-15.5)/12.5);
 float eaveOsc=(sPhase*.30+cPhase*.20)*storm;
 float2 roofShift=float2(clamp(wind.x*.22*storm+eaveOsc,-.95,.95),0)*roofLift;
 float2 rq=q-roofShift;
 s.roofQ=rq;
 float2 roof=rq-float2(16,21);
 s.roof=step(abs(roof.x),13)*step(abs(roof.y),6.8);
 float eaveFlutter=floor(sPhase*storm*.75+.5)*storm;
 s.eave=PDBox(float2(q.x,q.y-eaveFlutter*.4),float2(4,14),float2(28,16))*(1-s.wall);
 float awningSpan=saturate((q.x-25)*.25);
 float clothWave=lerp(sPhase,cPhase,awningSpan)*(breeze*.38+storm*.72);
 float awningDrop=lerp(.55,-.45,saturate(windMag*.45))+clothWave*awningSpan;
 float2 aq=float2(q.x-clamp(wind.x*.28*awningSpan,-.6,.9),q.y-awningDrop*awningSpan);
 s.awning=PDBox(aq,float2(25,11),float2(29.2,15))*(1-s.wall);
 s.pole=PDBox(q,float2(22.0,26),float2(23.1,30.6));
 float dirX=windMag>.08?(wind.x>=0?1:-1):(seed<.5?1:-1);
 float flagLen=lerp(1.8,5.4,saturate(breeze*.45+storm*.75));
 float along=(q.x-22.5)*dirX;
 float alongNorm=saturate(along/max(1.0,flagLen));
 float flagWave=lerp(sPhase,cPhase,alongNorm)*(breeze*.35+storm*.70)*alongNorm;
 float droop=lerp(-.95,.25*clamp(wind.y,-1.1,1.1),saturate(breeze*.55+storm*.65))*alongNorm;
 float fy=q.y-(29.3+droop+flagWave);
 float halfHeight=lerp(1.15,.50,alongNorm);
 s.flag=step(.2,along)*step(along,flagLen)*step(abs(fy),halfHeight);
 s.flagWave=flagWave;
 return s;
}
struct PDSettlementShadowWind
{
 float2 roofShift;
 float2 awningShift;
 float dirX,flagLen,flagTipOffset;
};
// Precompute per-settlement shadow wind once outside the 6-slice shadow loop (zero trig inside shadow taps).
PDSettlementShadowWind PDSettlementPrecomputeShadowWind(float2 wind,float seed)
{
 PDSettlementShadowWind p=(PDSettlementShadowWind)0;
 float windMag=length(wind);
 float breeze=smoothstep(.15,.75,windMag);
 float storm=smoothstep(.55,1.85,windMag);
 float phase=_SkyClock*(4.2+6.8*storm)+seed*6.283185307;
 float sPhase,cPhase;
 sincos(phase,sPhase,cPhase);
 p.roofShift=float2(clamp(wind.x*.22*storm+(sPhase*.30+cPhase*.20)*storm,-.95,.95),clamp(wind.y*.10*storm,-.35,.35));
 p.awningShift=float2(clamp(wind.x*.22,-.5,.8),lerp(.45,-.35,saturate(windMag*.45))+sPhase*(breeze*.38+storm*.72)*.75);
 p.dirX=windMag>.08?(wind.x>=0?1:-1):(seed<.5?1:-1);
 p.flagLen=lerp(1.8,5.4,saturate(breeze*.45+storm*.75));
 p.flagTipOffset=lerp(-1.3,.30*clamp(wind.y,-1.1,1.1),saturate(breeze*.55+storm*.65))-cPhase*(breeze*.38+storm*.95);
 return p;
}
float PDBoxAA(float2 p,float2 lo,float2 hi)
{
 float2 a=saturate((p-lo+.35)/.70)*saturate((hi-p+.35)/.70);
 return a.x*a.y;
}
float2 PDSettlementShadowBox(float2 q,float2 roofShift)
{
 float wall=PDBoxAA(q,float2(7,5),float2(26,16));
 float2 rq=q-roofShift*saturate((q.y-15.5)/12.5);
 float roof=PDBoxAA(rq,float2(3,14),float2(29,28));
 return float2(wall,roof);
}
float PDSettlementShadowExtras(float2 q,PDSettlementShadowWind p)
{
 float awning=PDBox(q-p.awningShift*saturate((q.x-25)*.25),float2(25,11),float2(29.2,15));
 float pole=PDBox(q,float2(22.0,26),float2(23.1,30.6));
 float along=(q.x-22.5)*p.dirX;
 float alongNorm=saturate(along/max(1.0,p.flagLen));
 float flag=step(.2,along)*step(along,p.flagLen)*step(abs(q.y-(28.8+p.flagTipOffset*alongNorm)),lerp(1.15,.50,alongNorm));
 return max(awning,max(pole,flag));
}
float2 PDSettlementGeometry(float2 q,float2 wind,float seed)
{
 PDSettlementState s=PDSettlementEvaluate(q,wind,seed);
 return float2(s.wall,max(max(s.roof,s.eave),max(s.awning,max(s.pole,s.flag))));
}
float2 PDSettlementGeometry(float2 q)
{return PDSettlementGeometry(q,0,0);}
float PDSettlementCastShadow(float2 ground,float2 direction,float2 wind,float seed)
{
 PDSettlementShadowWind wp=PDSettlementPrecomputeShadowWind(wind,seed);
 float coverage=0;
 [loop] [fastopt] for(int slice=0;slice<6;slice++)
 {
  float height=slice*1.5;
  float2 samplePoint=ground-direction*height;
  float2 geometry=PDSettlementShadowBox(samplePoint,wp.roofShift);
  float extras=PDSettlementShadowExtras(samplePoint,wp);
  coverage=max(coverage,max(geometry.x*step(height,4.5),max(geometry.y,extras)));
 }
 return coverage;
}
float PDSettlementCastShadow(float2 ground,float2 direction)
{return PDSettlementCastShadow(ground,direction,0,0);}
#include "FlatGroundCover.cginc"
// Evaluate neighbouring casters before painting ANY artwork. The receiver cell
// never clips a silhouette; the source cell owns occupancy, shape and sunlight.
float PixelPropShadow(float2 cell)
{
 float coverage=0;float2 id=floor(cell);
 float3 sun=normalize(_FlatSun+float3(0,1e-7,0));
 float2 nearEdge=abs(frac(cell)-.5);
 if(max(nearEdge.x,nearEdge.y)>.36)
 {
  // Only low-angle shadows can arrive from two tiles away. Shift the same
  // nine-source window towards the sun at a receiving tile's outer edge.
  float2 uv=(id+.5)*_MapSize.zw;
  float3 receiverSun=FlatGeographicLight(uv,sun);
  id=floor(cell+.14*receiverSun.xy*float2(1,FlatArtworkLatitudeScale(uv.y)));
 }
 [loop] [fastopt] for(int row=1;row>=-1;row--)
 [loop] [fastopt] for(int column=-1;column<=1;column++)
 {
  float2 root=id+float2(column,row);
  if(root.y<0||root.y>=_MapSize.y)continue;
  float2 uv=(root+.5)*_MapSize.zw;
  float scale=FlatArtworkLatitudeScale(uv.y);
  float2 q=(cell-root)*32;
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
   float2 canonicalRoot=root;canonicalRoot.x-=floor(canonicalRoot.x/_MapSize.x)*_MapSize.x;
   float sSeed=PDHash(canonicalRoot+float2(17.3,53.7));
   float sStorm;
   float2 sWind=PDSettlementWind(canonicalRoot,sSeed,sStorm);
   mask=PDSettlementCastShadow(q,direction,sWind,sSeed)*.27;
  }
  else if(info.b<.5&&!(info.g>.5&&info.g<4.5))
  {
   float4 placement=PDTreePlacement(root);
   scale=FlatTreeLatitudeScale(root,placement.y);
   float tree;
   float4 ecology=FlatEcologyData(uv,info,tree);placement.w=ecology.a;
   q.y=placement.y+(q.y-placement.y)/scale;
   float2 offset=q-placement.xy;
   if(any(abs(offset)>42*placement.z))continue;
   float size=PDTreeSize(root,placement,info,ecology);
   if(size<=0)continue;
   float2 ground=offset/size;
   if(any(abs(ground)>42))continue;
   float shore=1-smoothstep(.2,1.2,-FlatVisualSample(float4(uv,0,0)).g);
   float species=PDTreeSpecies(ecology,info,shore,tree);
   float foliage=FlatVegetationSeason(root+.5).y;
   float2 wind=PDVegetationWind(root,placement.w);
   mask=PDTreeCastShadow(ground,localSun,species,placement.w,foliage,wind)*.30;
  }
  coverage=max(coverage,mask*daylight);
 }
 return coverage;
}
float4 PDTreeArtwork(float2 pixel,float2 id,float species,float seed,float leaf,float3 light,float3 localSun)
{
 id.x-=floor(id.x/_MapSize.x)*_MapSize.x;
 float2 wind=PDVegetationWind(id,seed);
 float2 edge;float3 surface;
 float4 crown=PDTreeCrown(pixel,species,seed,leaf,wind,edge,surface);
 float trunk=PDTreeTrunk(pixel,species,seed,wind);
 float3 foliage=PDTreeFoliage(crown.xyz,surface,species,seed,localSun,edge);
 foliage=lerp(foliage*float3(1.30,.78,.45),foliage,leaf);
 float3 col=PDPaint(0,PDTreeBark(pixel,species,seed,wind)*light,trunk);
 col=PDPaint(col,foliage*light,crown.a);
 return float4(col,saturate(crown.a+trunk));
}
float4 PDSettlementArtwork(float2 q,float3 light,float2 wind,float seed)
{
 float3 col=0;
 PDSettlementState s=PDSettlementEvaluate(q,wind,seed);
 float wall=s.wall;
 float board=step(1,fmod(q.y,4));col=PDPaint(col,lerp(float3(.30,.20,.12),float3(.59,.43,.25),board)*light,wall);
 float awningStripe=step(1,fmod(floor(q.x+q.y*.25),2));
 col=PDPaint(col,lerp(float3(.70,.24,.18),float3(.90,.82,.66),awningStripe)*light,s.awning);
 col=PDPaint(col,float3(.26,.19,.12)*light,s.eave);
 float roofMask=s.roof;
 float2 rq=s.roofQ;
 col=PDPaint(col,float3(.19,.16,.10)*light,roofMask);
 float interior=PDBox(rq,float2(4,16),float2(28,27));
 float shingle=step(1,fmod(rq.y,3))*step(1,fmod(rq.x+floor(rq.y/3)*3,6));
 col=PDPaint(col,lerp(float3(.42,.28,.12),float3(.69,.48,.21),shingle)*light,interior);
 col=PDPaint(col,float3(.82,.65,.32)*light,PDBox(rq,float2(4,26),float2(28,28)));
 col=PDPaint(col,float3(.13,.12,.10)*light,PDBox(q,float2(14,5),float2(19,13)));
 col=PDPaint(col,float3(.92,.73,.33)*light,PDBox(q,float2(9,9),float2(12,12))+PDBox(q,float2(22,9),float2(25,12)));
 col=PDPaint(col,float3(.38,.31,.22)*light,s.pole);
 float3 flagCol=lerp(float3(.78,.20,.17),float3(.92,.72,.26),step(.5,frac(seed*5.3)))*(.88+.18*saturate(s.flagWave+.5));
 col=PDPaint(col,flagCol*light,s.flag);
 float alpha=max(max(wall,roofMask),max(max(s.eave,s.awning),max(s.pole,s.flag)));
 return float4(col,alpha);
}
float4 PDSettlementArtwork(float2 q,float3 light)
{return PDSettlementArtwork(q,light,0,0);}
float3 PixelProps(float3 col,float2 cell,float4 field,float waterMask,float structure,float3 light,PDPlantView plantView)
{
 col*=1-PixelPropShadow(cell);
 float2 id=floor(cell),q=floor(frac(cell)*32);
 // A source is anchored in geographic cells. Its two art axes have equal ground units.
 float artworkScale=FlatArtworkLatitudeScale((id.y+.5)*_MapSize.w);
 q.y=floor(16+(frac(cell.y)-.5)*32/artworkScale);
 float nearestRoot=1e10;
 float2 first=id-1,last=id+1;
 if(plantView.weight>0)
 {
  float2 dx,dy;FlatCellScreenDerivatives(cell,dx,dy);
  float extent=48*PDPlantArtSize(plantView,artworkScale)/plantView.chartPerPixel;
  float2 reach=min(3,sqrt(dx*dx+dy*dy)*extent);
  // PDTreePlacement keeps the foot between these fractions of its owning tile.
  first=min(first,ceil(cell-reach-float2(.75,.64)));
  last=max(last,floor(cell+reach-float2(.25,.18)));
 }
 [loop] [fastopt] for(int row=(int)last.y;row>=(int)first.y;row--)
 [loop] [fastopt] for(int column=(int)first.x;column<=(int)last.x;column++)
 {
  float2 treeId=float2(column,row);
  if(treeId.y<0||treeId.y>=_MapSize.y)continue;
  float4 placement=PDTreePlacement(treeId);
  float localScale=FlatTreeLatitudeScale(treeId,placement.y);
  float2 offset=floor(PDPlantPixel(plantView,cell,treeId+placement.xy/32,localScale));
  if(!PDTreeInDrawBounds(offset/placement.z))continue;
  float2 centreUV=(treeId+.5)*_MapSize.zw;
  float4 rootInfo=tex2Dlod(_InfoTex,float4(centreUV,0,0))*255;
  float tree;
  float4 ecology=FlatEcologyData(centreUV,rootInfo,tree);placement.w=ecology.a;
  float size=PDTreeSize(treeId,placement,rootInfo,ecology);
  if(size<=0)continue;
  float2 treePoint=offset/size;
  if(!PDTreeInDrawBounds(treePoint))continue;
  float shore=1-smoothstep(.2,1.2,-FlatVisualSample(float4(centreUV,0,0)).g);
  float species=PDTreeSpecies(ecology,rootInfo,shore,tree);
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
  float2 canonicalId=id;canonicalId.x-=floor(canonicalId.x/_MapSize.x)*_MapSize.x;
  float sSeed=PDHash(canonicalId+float2(17.3,53.7));
  float sStorm;
  float2 sWind=PDSettlementWind(canonicalId,sSeed,sStorm);
  float4 art=PDSettlementArtwork(q,light,sWind,sSeed);col=lerp(col,art.rgb,art.a);
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
  // Linear roads and bridges reach the geographic edge of the adjacent tile.
  // Their cross-section and planks still use the physical artwork metric.
  float roadY=lerp(q.y,floor(frac(cell.y)*32),_LocalPhysicalArtwork);
  float path=PDBox(q,float2(11,11),float2(21,21));
  path=max(path,PDBox(q,float2(0,11),float2(16,21))*west);
  path=max(path,PDBox(q,float2(16,11),float2(32,21))*east);
  path=max(path,PDBox(float2(q.x,roadY),float2(11,16),float2(21,32))*north);
  path=max(path,PDBox(float2(q.x,roadY),float2(11,0),float2(21,16))*south);
  if(horizontal+vertical<.5)path=PDBox(float2(q.x,roadY),float2(11,0),float2(21,32));
  float plank=step(1,fmod(lerp(q.y,q.x,horizontal),4));
  float3 road=structure>3.5?lerp(float3(.29,.20,.12),float3(.62,.46,.25),plank):float3(.65,.48,.28)*(.9+PDHash(id+q*.02)*.15);
  col=PDPaint(col,road*light,path);
 }
 return col;
}
float3 PixelProps(float3 col,float2 cell,float4 field,float waterMask,float structure,float3 light)
{return PixelProps(col,cell,field,waterMask,structure,light,PDGroundView());}
#endif
