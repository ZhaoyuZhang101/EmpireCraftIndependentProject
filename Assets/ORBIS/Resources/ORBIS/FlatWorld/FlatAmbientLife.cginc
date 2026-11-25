#ifndef ORBIS_FLAT_AMBIENT_LIFE
#define ORBIS_FLAT_AMBIENT_LIFE

// Scenic artwork uses one possible resident per roughly six-by-six tile patch.
// An integer patch count closes the longitude seam even on odd-sized maps.
// Home position, facing and animation phase never depend on screen coordinates.
float4 PDAmbientHome(float2 cell)
{
 float2 count=max(float2(1,1),floor(_MapSize.xy/6));
 float2 span=_MapSize.xy/count;
 cell.x-=floor(cell.x/_MapSize.x)*_MapSize.x;
 cell.y=clamp(cell.y,0,_MapSize.y-.0001);
 float2 origin=min(count-1,floor(cell/span))*span;
 float seed=PDHash(origin+float2(311,127));
 float2 jitter=float2(PDHash(origin+float2(53,211)),PDHash(origin+float2(197,41)));
 float2 home=origin+span*(.35+jitter*.30);
 float latitudeScale=FlatArtworkLatitudeScale(home.y*_MapSize.w);
 return float4(home,seed,latitudeScale);
}

// XY is the moving geographic position. Z is the east/west art orientation,
// and W is a stable local animation phase driven by the existing paused clock.
float4 PDAmbientPose(float4 home,float clock)
{
 float phase=home.z*6.283185307;
 float eastPhase=clock*.17+phase;
 float northPhase=clock*.13+phase*1.7;
 float2 wander=float2(sin(eastPhase)*.24,sin(northPhase)*.18*home.w);
 float facing=cos(eastPhase)<0?-1:1;
 return float4(home.xy+wander,facing,clock+phase);
}
float4 PDAmbientPose(float4 home)
{
 return PDAmbientPose(home,_WaterClock);
}

// Four live particles reconstruct their emission positions from the resident's
// path. A shared paused clock keeps motion, emission and lifetime in step.
float4 PDAmbientParticles(float2 pixel,float4 home,float species,float wetness)
{
 bool ground=species<2.5;
 float interval=ground?.32:.24,lifetime=ground?1.25:.90;
 float tick=floor(_WaterClock/interval+home.z);
 float4 current=PDAmbientPose(home);
 float opacity=0;
 [loop] [fastopt] for(int particle=0;particle<4;particle++)
 {
  float emission=(tick-particle-home.z)*interval;
  float age=_WaterClock-emission;
  if(emission<0||age>=lifetime)continue;
  float phase=home.z*6.283185307;
  float2 velocity=float2(.24*.17*cos(emission*.17+phase),.18*.13*cos(emission*.13+phase*1.7));
  float moving=smoothstep(.006,.035,length(velocity));
  float seed=PDHash(home.xy+float2((tick-particle)*.731,83+species*17));
  float4 previous=PDAmbientPose(home,emission);
  float2 centre=(previous.xy-current.xy)*float2(32,32/home.w);
  float side=seed<.5?-1:1;
  if(ground)centre+=float2(side*2+(seed-.5)*age*2,-4+age*2);
  else centre+=float2(-previous.z*(3+age*1.8),side*(2+age));
  float2 offset=floor(pixel)-floor(centre);
  float radius=ground?.70+age*.75:.75;
  float shape=1-step(radius,max(abs(offset.x),abs(offset.y)));
  float life=age/lifetime;
  float fade=smoothstep(0,.09,age)*(1-life)*(1-life);
  float strength=ground?lerp(.45,.16,saturate(wetness)):.24;
  opacity=max(opacity,shape*fade*moving*strength);
 }
 float3 color=ground?lerp(float3(.65,.58,.43),float3(.43,.43,.35),saturate(wetness)):float3(.69,.73,.72);
 if(species>3.5)color=float3(.80,.74,.48);
 return float4(color,opacity);
}

// Species IDs are rabbit, deer, bird and butterfly. The habitat remains a
// categorical home-cell choice while wetness and the saved world variant vary
// its residents. Legacy saves receive a woodland choice until ecology is ready.
float PDAmbientSpecies(float4 ecology,float seed)
{
 float cover=floor(ecology.r+.5);
 if(ecology.r<-.5)cover=6;
 if(cover<.5||cover>12.5)return 0;
 float choice=frac(seed+ecology.a*.618033989);
 if(cover<1.5)return choice<.76?1:4;
 if(cover<2.5)return 1;
 if(cover<3.5)return choice<.68?2:3;
 if(cover<4.5)return choice<.70?1:3;
 if(cover<6.5)return choice<.64?2:4;
 if(cover<7.5)return choice<.70?2:3;
 if(cover<8.5)return choice<.82?2:1;
 if(cover<9.5)return choice<lerp(.55,.75,saturate(ecology.b))?4:3;
 if(cover<10.5)return choice<.75?3:4;
 if(cover<11.5)return choice<.78?1:3;
 return 1;
}

bool PDAmbientHomeClear(float4 home,bool ground)
{
 // The .82-tile disc contains the complete .24-tile wander and every animal
 // pixel. Check all semantic cells it touches so a neighbouring building or
 // river cannot cut a resident in half as it moves across a tile boundary.
 float2 root=floor(home.xy);
 bool clear=true;
 [loop] [fastopt] for(int row=-1;row<=1;row++)
 [loop] [fastopt] for(int column=-1;column<=1;column++)
 {
  float2 tile=root+float2(column,row);
  float2 nearest=max(abs(tile+.5-home.xy)-.5,0);
  nearest.y/=home.w;
  if(dot(nearest,nearest)>.82*.82)continue;
  if(tile.y<0||tile.y>=_MapSize.y){clear=false;continue;}
  tile.x-=floor(tile.x/_MapSize.x)*_MapSize.x;
  float2 uv=(tile+.5)*_MapSize.zw;
  float4 semantic=floor(tex2Dlod(_InfoTex,float4(uv,0,0))*255+.5);
  clear=clear&&semantic.b<.5&&semantic.a<29.5&&(!ground||semantic.g<.5);
 }
 if(ground&&clear)
 {
  // These are the same reconstructed field and river atlas as the visible
  // surface, including sub-cell shores and rivers passing through land cells.
  float4 field=FlatField(home.xy);
  clear=field.r>=0&&field.g<=-.82&&FlatRiverAtlasClear(home.xy,.82);
 }
 return clear;
}

#include "FlatWildlifeArt.cginc"

float _WildlifeActors;
float3 PixelAmbientLife(float3 col,float2 cell,float4 field,float waterMask,float4 info,float3 light)
{
 if(_WildlifeActors>.5||_DetailWeight<=0||info.b>.5||info.a>29.5)return col;
 float4 home=PDAmbientHome(cell);
 if(home.z>.40)return col;
 float4 pose=PDAmbientPose(home);
 float2 delta=cell-pose.xy;
 delta.x-=floor(delta.x/_MapSize.x+.5)*_MapSize.x;
 float2 pixel=delta*float2(32,32/home.w);
 float2 homeUV=home.xy*_MapSize.zw;
 float3 sun=FlatGeographicLight(homeUV,normalize(_FlatSun+float3(0,1e-7,0)));
 float daylight=smoothstep(-.06,.18,sun.z);
 float2 shadowDirection=clamp(-sun.xy/max(.35,sun.z),-2.6,2.6);
 float2 longestShadow=shadowDirection*4*step(.001,daylight);
 float along=saturate(dot(pixel,longestShadow)/max(.0001,dot(longestShadow,longestShadow)));
 // Cheap world-space bounds precede every semantic, ecology and field read.
 // The segment encloses the complete range of animal-specific cast heights.
 if(any(abs(pixel)>14)&&any(abs(pixel-longestShadow*along)>11))return col;

 float2 homeCentre=(floor(home.xy)+.5)*_MapSize.zw;
 float4 homeInfo=floor(tex2Dlod(_InfoTex,float4(homeCentre,0,0))*255+.5);
 float4 ecology=FlatEcologyData(homeCentre,homeInfo);
 if(ecology.r<-.5)
 {
  // The legacy texture has climate and canopy but no categorical cover.
  // Keep its deserts empty and choose an appropriate broad habitat elsewhere.
  float climate=homeInfo.a;
  ecology.r=6;
  if(climate>29.5)ecology.r=13;
  else if(climate>28.5)ecology.r=12;
  else if(climate>16.5)ecology.r=8;
  else if(climate>5.5&&climate<7.5)ecology.r=2;
  else if(climate>3.5&&climate<5.5)ecology.r=0;
  else if(climate>2.5&&climate<3.5)ecology.r=3;
  else if(climate>.5&&climate<2.5)ecology.r=9;
  else if(climate<.5&&FlatVisualSample(float4(homeCentre,0,0)).a<.12)ecology.r=0;
 }
 float species=PDAmbientSpecies(ecology,home.z);
 if(species<.5)return col;
 bool ground=species<2.5;
 if(ground&&(waterMask>.01||field.g>-.05))return col;
 float visibility=species>2.5?daylight:(species<1.5?.85-.20*daylight:.55+.45*daylight);
 if(visibility<.001)return col;
 float height=species<1.5?1.5:species<2.5?2.5:4;
 float2 cast=shadowDirection*height;
 float4 art=0;float shadow=0;
 // Evaluate body and cast-shadow sprite masks through one rolled PDAmbientArt call site.
 [loop] [fastopt] for(int artPass=0;artPass<2;artPass++)
 {
  float4 sampleArt=PDAmbientArt(artPass==0?pixel:(pixel-cast),species,pose,ecology.a);
  if(artPass==0)art=sampleArt;
  else shadow=sampleArt.a*daylight;
 }
 float4 particles=PDAmbientParticles(pixel,home,species,ecology.b);
 if(art.a+shadow+particles.a<.001)return col;
 if(!PDAmbientHomeClear(home,ground))return col;
 // Black, translucent cast silhouettes follow the same animated art mask and
 // local geographic sun. Low flight leaves a compact shadow under flying art.
 col*=1-shadow*.25*visibility*lerp(1,.45,waterMask);
 col=lerp(col,FlatColor(particles.rgb)*light,particles.a*visibility);
 return lerp(col,FlatColor(art.rgb)*light,art.a*visibility);
}
#endif
