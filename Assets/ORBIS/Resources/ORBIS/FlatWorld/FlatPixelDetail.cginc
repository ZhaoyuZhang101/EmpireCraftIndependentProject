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
float PDLeafSdf(float2 p)
{
 float shape=min(length((p-float2(-4,-1))*float2(1,1.08))-7,
                 length((p-float2(4,0))*float2(1,1.10))-7);
 shape=min(shape,length((p-float2(-1,5))*float2(1.04,1))-7);
 return min(shape,length((p-float2(0,-4))*float2(1,.9))-7);
}
float PDConiferSdf(float2 p)
{return max(abs(p.x)-min(10.5,max(0,8-p.y*.35)),abs(p.y-1)-13);}
float PDBuildingSdf(float2 p)
{
 float2 roof=abs(p-float2(16,21))-float2(13,7);
 float2 wall=abs(p-float2(16.5,10.5))-float2(9.5,5.5);
 return min(max(roof.x,roof.y),max(wall.x,wall.y));
}
float3 PixelGround(float3 baseColor,float2 cell,float4 field,float waterMask)
{
 float2 id=floor(cell),q=floor(frac(cell)*32),uv=(id+.5)*_MapSize.zw;
 float3 artwork=FlatDetailPosition(cell);
 float rand=PDHash(id),grain=FlatDetailNoise(artwork*(32.0/3));
 float3 col=floor(baseColor*28+.5)/28*(.90+.17*grain);
 float green=step(.26,field.a)*step(0,field.r);
 float blades=step(.88,grain)*step(1,fmod(q.y,5))*step(fmod(q.x,5),1);
 col=PDPaint(col,float3(.35,.43,.19),blades*green*.4);
 // Continuous height terraces: no cell-edge bars or needles. Only steep,
 // south-facing slopes expose a rock face; water later cuts through the face.
 float2 dx=float2(_MapSize.z,0),dy=float2(0,_MapSize.w),at=cell*_MapSize.zw;
 float gx=(tex2Dlod(_VisualTex,float4(at+dx,0,0)).r-tex2Dlod(_VisualTex,float4(at-dx,0,0)).r)*.5;
 float gy=(tex2Dlod(_VisualTex,float4(at+dy,0,0)).r-tex2Dlod(_VisualTex,float4(at-dy,0,0)).r)*.5;
 float slope=length(float2(gx,gy));
 float band=frac(max(0,field.r)/180);
 float face=smoothstep(110,230,slope)*smoothstep(35,110,gy)*(1-smoothstep(.10,.27,band));
 face*=step(.01,field.r)*(1-waterMask)*step(.20,-field.g);
 float strata=step(.42,FlatDetailNoise(artwork*24));
 float3 rock=lerp(float3(.31,.29,.23),float3(.48,.44,.34),strata*.5+band);
 col=PDPaint(col,rock,face*.85);
 float shore=step(-.16,field.g)*(1-step(.22,field.g))*(1-waterMask);
 col=PDPaint(col,lerp(float3(.47,.36,.21),float3(.66,.53,.30),step(.45,grain)),shore*.8);
 return col;
}
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
  if(q.x< -23||q.x>55||q.y< -23||q.y>55)continue;
  float lon=(uv.x-.5)*6.283185307,lat=(uv.y-.5)*3.141592654;
  float3 sun=normalize(_FlatSun+float3(0,1e-7,0));
  float height=dot(FlatRadial(uv),sun);
  float daylight=smoothstep(-.04,.18,height);
  if(daylight<.001)continue;
  float2 direction=-float2(dot(float3(cos(lon),0,-sin(lon)),sun),
   dot(float3(-sin(lat)*sin(lon),cos(lat),-sin(lat)*cos(lon)),sun))/max(.25,height);
  float4 info=tex2Dlod(_InfoTex,float4(uv,0,0))*255;
  float mask=0;
  if(info.b>.5&&info.b<1.5)
  {
   q.y=16+(q.y-16)/scale;
   mask=(1-smoothstep(-.5,2.5,PDBuildingSdf(q-direction*4.5)))*.27;
  }
  else if(info.b<.5&&!(info.g>.5&&info.g<4.5))
  {
   float seed=PDHash(root),size=.86+.15*PDHash(root+float2(29,13));
   q.y=8+(q.y-8)/scale;
   float2 crown=q-float2(13+floor(seed*7),20+floor(PDHash(root+71)*6));
   float2 silhouetteUV=(crown-direction*3.5)/size;
   if(abs(silhouetteUV.x)>14||abs(silhouetteUV.y)>17)continue;
   float4 field=tex2Dlod(_VisualTex,float4((root+float2(.5,.25))*_MapSize.zw,0,0));
   if(seed>saturate((field.a-.20)*1.25)||field.g>-.18)continue;
   float4 hydro=tex2Dlod(_HydroA,float4(uv,0,0));
   float4 river=tex2Dlod(_HydroB,float4(uv,0,0));float2 flow;
   if(FlatRiverDistance(float2(.5,.25),hydro,floor(river.a+.5),river.r,uv,flow)<.15)continue;
   float conifer=step(16.5,info.a)*step(info.a,28.5);
   float shape=lerp(PDLeafSdf(silhouetteUV),PDConiferSdf(silhouetteUV),conifer);
   mask=(1-smoothstep(-.4,2.2,shape))*.30;
  }
  coverage=max(coverage,mask*daylight);
 }
 return coverage;
}
float3 PixelProps(float3 col,float2 cell,float4 field,float waterMask,float structure,float3 light)
{
 col*=1-PixelPropShadow(cell);
 float2 id=floor(cell),q=floor(frac(cell)*32);
 // A longitude tile narrows with latitude. Fit artwork isotropically into that
 // footprint instead of stretching its height while squeezing its width.
 float artworkScale=max(.02,cos(((id.y+.5)*_MapSize.w-.5)*3.141592654));
 q.y=floor(16+(frac(cell.y)-.5)*32/artworkScale);
 [loop] [fastopt] for(int row=1;row>=-1;row--)
 [loop] [fastopt] for(int column=-1;column<=1;column++)
 {
  float2 treeId=id+float2(column,row),rootUV=(treeId+float2(.5,.25))*_MapSize.zw;
  if(treeId.y<0||treeId.y>=_MapSize.y)continue;
  float2 localPoint=(cell-treeId)*32;
  float localScale=max(.02,cos(((treeId.y+.5)*_MapSize.w-.5)*3.141592654));
  localPoint.y=8+(localPoint.y-8)/localScale;
  if(localPoint.x<0||localPoint.x>32||localPoint.y<4||localPoint.y>41)continue;
  float2 centreUV=(treeId+.5)*_MapSize.zw;
  float4 rootField=tex2Dlod(_VisualTex,float4(rootUV,0,0));
  float4 rootHydro=tex2Dlod(_HydroA,float4(centreUV,0,0));
  float4 rootMixing=tex2Dlod(_HydroB,float4(centreUV,0,0));
  float4 rootInfo=tex2Dlod(_InfoTex,float4(centreUV,0,0))*255;
  float rootStructure=rootInfo.b;
  float seed=PDHash(treeId);
  float2 rootFlow;
  float rootRiver=FlatRiverDistance(float2(.5,.25),rootHydro,floor(rootMixing.a+.5),rootMixing.r,centreUV,rootFlow);
  float occupied=step(seed,saturate((rootField.a-.20)*1.25))*step(.18,-rootField.g)*step(.15,rootRiver);
  if(rootStructure>.5||occupied<.5||(rootInfo.g>.5&&rootInfo.g<4.5))continue;
  float2 tq=floor((cell-treeId)*32);
  float rootScale=max(.02,cos(((treeId.y+.5)*_MapSize.w-.5)*3.141592654));
  tq.y=floor(8+((cell.y-treeId.y)*32-8)/rootScale);
  float2 crown=tq-float2(13+floor(seed*7),20+floor(PDHash(treeId+71)*6));
  float size=.86+.15*PDHash(treeId+float2(29,13));
  float2 leafPoint=crown/size;
  // The same silhouette drives both artwork and projected shadow.
  float conifer=step(16.5,rootInfo.a)*step(rootInfo.a,28.5);
  float shape=lerp(PDLeafSdf(leafPoint),PDConiferSdf(leafPoint),conifer);
  float crownMask=1-step(.1,shape),inner=1-step(-1.2,shape);
  float trunk=PDBox(tq,float2(14,5),float2(18,17));
  col=PDPaint(col,float3(.23,.16,.105)*light,trunk);
  col=PDPaint(col,float3(.44,.30,.16)*light,PDBox(tq,float2(15,6),float2(17,16)));
  float cluster=PDHash(treeId+floor(tq/3)*.07);
  float sunward=saturate(.48+dot(leafPoint,float2(-.035,.045)));
  float3 dark=lerp(float3(.10,.20,.10),float3(.13,.23,.14),seed);
  float3 bright=lerp(float3(.34,.45,.17),float3(.27,.43,.22),seed);
  float3 foliage=lerp(dark,bright,floor(sunward*4)/4)*(.88+.20*step(.38,cluster));
  // Continental cold climates use layered conifer silhouettes.
  if(conifer>.5)
  {
   float coniferEdgeDistance=PDConiferSdf(leafPoint);
   crownMask=1-step(0,coniferEdgeDistance);inner=1-step(-1,coniferEdgeDistance);
   float tier=frac((leafPoint.y+14)/7);
   foliage=lerp(float3(.09,.19,.15),float3(.24,.37,.23),tier*.6+sunward*.4);
  }
  col=PDPaint(col,float3(.055,.115,.065)*light,crownMask);
  col=PDPaint(col,foliage*light,inner);

 }
 if(structure>.5 && structure<1.5)
 {
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
