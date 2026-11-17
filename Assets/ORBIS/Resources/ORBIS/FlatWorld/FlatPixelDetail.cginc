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
// An orthographic 62-degree art view. Tree volumes are evaluated at pixel centres,
// so the top faces, short trunk and cast shadow share one small 3D representation.
static const float3 PDTreeView=float3(0,-.46947156,.88294759);
static const float3 PDTreeUp=float3(0,.88294759,.46947156);
float4 PDTreePlacement(float2 id)
{
 float seed=PDHash(id);
 float2 anchor=float2(16+(seed-.5)*16,13+(PDHash(id+71)-.5)*14);
 return float4(floor(anchor)+.5,.78+.30*PDHash(id+float2(29,13)),seed);
}
float4 PDTreeLobe(int index,float conifer,float seed)
{
 float4 broadleaf=float4(0,0,15.5,6.8),pine=float4(0,0,7,10.3);
 if(index==1){broadleaf=float4(-5,-1,12,6.3);pine=float4(0,0,13,8);}
 if(index==2){broadleaf=float4(5,0,13.5,6.5);pine=float4(0,0,18,5.5);}
 if(index==3){broadleaf=float4(-1,5,15.5,6.8);pine=float4(0,0,22,3.6);}
 if(index==4){broadleaf=float4(1,-4.5,11,6.4);pine=float4(0,0,25,1.8);}
 // Alternate crown width and lobe height without changing a tree across frames.
 broadleaf.x*=.86+seed*.28;
 broadleaf.z+=(seed-.5)*index*.8;
 float angle=floor(seed*4)*1.570796327;
 float sine=sin(angle),cosine=cos(angle);
 broadleaf.xy=float2(cosine*broadleaf.x-sine*broadleaf.y,sine*broadleaf.x+cosine*broadleaf.y);
 pine.x=(seed-.5)*index*.5;
 float4 volume=lerp(broadleaf,pine,conifer);
 volume.z+=12;
 return volume;
}
bool PDTreeAvailable(float2 id,float4 placement,float4 info)
{
 if(info.b>.5||(info.g>.5&&info.g<4.5))return false;
 float2 root=placement.xy/32,uv=(id+root)*_MapSize.zw;
 float4 field=tex2Dlod(_VisualTex,float4(uv,0,0));
 if(placement.w>saturate((field.a-.20)*1.25)||field.g>-.18)return false;
 float2 centreUV=(id+.5)*_MapSize.zw,flow;
 float4 hydro=tex2Dlod(_HydroA,float4(centreUV,0,0));
 float4 river=tex2Dlod(_HydroB,float4(centreUV,0,0));
 return FlatRiverDistance(root,hydro,floor(river.a+.5),river.r,centreUV,flow)>=.15;
}
float4 PDTreeCrown(float2 pixel,float conifer,float seed,out float2 edge)
{
 float3 origin=float3(pixel.x,0,0)+PDTreeUp*pixel.y;
 float front=-100;float3 normal=0;edge=-100;
 [unroll] for(int lobe=0;lobe<5;lobe++)
 {
  float4 volume=PDTreeLobe(lobe,conifer,seed);
  float3 relative=origin-volume.xyz;
  float depth=-dot(relative,PDTreeView);
  float3 closest=relative+PDTreeView*depth;
  float distanceSquared=dot(closest,closest);
  float radiusSquared=volume.w*volume.w;
  float lobeEdge=volume.w-sqrt(distanceSquared);
  edge.x=max(edge.x,lobeEdge);
  if(distanceSquared<radiusSquared)
  {
   float surface=sqrt(radiusSquared-distanceSquared);
   float visibleDepth=depth+surface;
   if(visibleDepth>front)
   {
    front=visibleDepth;
    normal=(closest+PDTreeView*surface)/volume.w;
    edge.y=lobeEdge;
   }
  }
 }
 return float4(normal,step(-99,front));
}
float PDTreeCastShadow(float2 pixel,float3 sun,float conifer,float seed)
{
 float3 origin=float3(pixel.x,pixel.y/PDTreeUp.y,0);
 // Foreshorten cast heights for the art view and bound low-sun silhouettes inside
 // the neighbouring cells. The actual crown lobes determine every shadow edge.
 float3 ray=normalize(float3(sun.xy*.40,max(.55,sun.z)));
 float mask=0;
 [unroll] for(int lobe=0;lobe<5;lobe++)
 {
  float4 volume=PDTreeLobe(lobe,conifer,seed);
  float3 relative=volume.xyz-origin;
  float depth=dot(relative,ray);
  float distanceToRay=length(relative-ray*max(0,depth));
  mask=max(mask,1-smoothstep(volume.w-.7,volume.w+1.5,distanceToRay));
 }
 return mask;
}
float3 PDTreeFoliage(float3 normal,float2 pixel,float2 id,float conifer,float seed,float3 sun,float2 edge)
{
 float daylight=smoothstep(-.06,.15,sun.z);
 float3 key=normalize(lerp(float3(-.3,.4,1),sun,daylight));
 // Broad pixel clusters sit on the volume, with a limited stepped palette.
 float cluster=PDHash(id+floor(pixel/2)*.071);
 float3 facet=normalize(floor(normal*4+.5)/4+float3(0,0,.001));
 float diffuse=saturate(dot(facet,key)*.70+facet.z*.10+.10);
 float tone=floor(saturate(diffuse+(cluster-.5)*.08)*4+.15)/4;
 float3 dark=lerp(float3(.115,.23,.13),float3(.10,.23,.20),conifer);
 float3 middle=lerp(float3(.24,.39,.18),float3(.20,.36,.28),conifer);
 float3 bright=lerp(float3(.43,.57,.26),float3(.38,.52,.36),conifer);
 float3 foliage=lerp(lerp(dark,middle,saturate(tone*2)),bright,saturate(tone*2-1));
 foliage*=lerp(float3(1.05,.99,.91),float3(.91,1.03,1.02),seed);
 float crease=(1-smoothstep(.6,2.2,edge.y))*step(1.4,edge.x);
 foliage*=1-crease*.19;
 float rim=1-step(.65,edge.x);
 return lerp(foliage,dark*.85,rim*.65);
}
float PDBuildingSdf(float2 p)
{
 float2 roof=abs(p-float2(16,21))-float2(13,7);
 float2 wall=abs(p-float2(16.5,10.5))-float2(9.5,5.5);
 return min(max(roof.x,roof.y),max(wall.x,wall.y));
}
float3 PixelGround(float3 baseColor,float2 cell,float4 field,float waterMask,float2 heightGradient)
{
 float2 id=floor(cell),q=floor(frac(cell)*32),uv=(id+.5)*_MapSize.zw;
 q.y=floor(16+(frac(cell.y)-.5)*32/max(.02,cos((uv.y-.5)*3.141592654)));
 float3 artwork=FlatDetailPosition(cell);
 float grain=FlatDetailNoise(artwork*23);
 float grainFoot=max(length(ddx(artwork*23)),length(ddy(artwork*23)));
 float grainWeight=1-smoothstep(.45,1.4,grainFoot);
 float3 col=floor(baseColor*64+.5)/64*(1+(grain-.5)*.055*grainWeight);
 float green=step(.26,field.a)*step(0,field.r);
 float tuft=PDHash(id+floor(q/4)*.13);
 float blades=step(.86,tuft)*step(1,frac(q.y*.25)*4)*step(frac((q.x+floor(q.y/4))*.25)*4,1);
 col=PDPaint(col,lerp(float3(.28,.39,.16),float3(.46,.54,.26),grain),blades*green*.28*grainWeight);
 // Continuous height terraces: no cell-edge bars or needles. Only steep,
 // south-facing slopes expose a rock face; water later cuts through the face.
 float gx=heightGradient.x,gy=heightGradient.y;
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
   q.y=placement.y+(q.y-placement.y)/scale;
   float2 ground=(q-placement.xy)/placement.z;
   if(abs(ground.x)>37||abs(ground.y)>37)continue;
   if(!PDTreeAvailable(root,placement,info))continue;
   float conifer=step(16.5,info.a)*step(info.a,28.5);
   mask=PDTreeCastShadow(ground,localSun,conifer,placement.w)*.30;
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
  if(abs(treePoint.x)>14||treePoint.y< -3||treePoint.y>25)continue;
  float2 centreUV=(treeId+.5)*_MapSize.zw;
  float4 rootInfo=tex2Dlod(_InfoTex,float4(centreUV,0,0))*255;
  if(!PDTreeAvailable(treeId,placement,rootInfo))continue;
  float conifer=step(16.5,rootInfo.a)*step(rootInfo.a,28.5);
  float2 edge;
  float4 crown=PDTreeCrown(treePoint,conifer,placement.w,edge);
  float trunk=PDBox(treePoint,float2(-1.5,-1.5),float2(1.5,9));
  float rootDepth=treeId.y*32+placement.y;
  if(crown.a+trunk<.5||rootDepth>nearestRoot)continue;
  nearestRoot=rootDepth;
  float3 localSun=FlatGeographicLight(centreUV,normalize(_FlatSun+float3(0,1e-7,0)));
  float3 foliage=PDTreeFoliage(crown.xyz,treePoint,treeId,conifer,placement.w,localSun,edge);
  float3 bark=lerp(float3(.28,.22,.14),float3(.47,.35,.19),step(0,treePoint.x));
  col=PDPaint(col,bark*light,trunk);
  col=PDPaint(col,foliage*light,crown.a);

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
