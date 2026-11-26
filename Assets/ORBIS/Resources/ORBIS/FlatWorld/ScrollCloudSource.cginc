#ifndef ORBIS_SCROLL_CLOUD_SOURCE
#define ORBIS_SCROLL_CLOUD_SOURCE
#include "ScrollHeight.cginc"
#include "FlatCycloneWind.cginc"
#include "../OrbisHPVolumeCloud.cginc"
sampler2D _ScrollWeatherTex, _ScrollCloudWindTex;
UNITY_DECLARE_TEX2D(_ScrollHeightTex);
UNITY_DECLARE_TEX2D_NOSAMPLER(_ScrollUpperWindTex);
float4 _ScrollSourceSize;
float _ScrollWeatherTime,_ScrollUpperWindReady,_ScrollMonthBlend;
float4 _OrbisCycloneNorth,_OrbisCycloneSouth,_OrbisCyclonePhase;

float4 ScrollHeightAt(float2 uv)
{
 #ifdef UNITY_SEPARATE_TEXTURE_SAMPLER
 return UNITY_SAMPLE_TEX2D_LOD(_ScrollHeightTex,uv,0);
 #else
 return tex2Dlod(_ScrollHeightTex,float4(uv,0,0));
 #endif
}
float ScrollCloudGround(float2 uv)
{
 if(_ScrollHeightScale<=0)return 1;
 float2 pixel=uv*_ScrollSourceSize.xy-.5,base=floor(pixel),fraction=frac(pixel);
 float2 smoothFrac=fraction*fraction*(3.0-2.0*fraction);
 float2 texel=1/max(float2(1,1),_ScrollSourceSize.xy);
 float2 centre=(base+.5)*texel,dx=float2(texel.x,0),dy=float2(0,texel.y);
 return max(1.0,ScrollInterpolatedRadius(ScrollHeightAt(centre),ScrollHeightAt(centre+dx),
                                         ScrollHeightAt(centre+dy),ScrollHeightAt(centre+dx+dy),smoothFrac,uv,_ScrollSourceSize.y));
}
float ScrollCloudGroundFast(float2 uv)
{
 if(_ScrollHeightScale<=0)return 1;
 return max(1.0,ScrollRadius(ScrollSurfaceHeight(ScrollHeightAt(uv)),uv,_ScrollSourceSize.y));
}

// 3D Volumetric Tropical Cyclone (Typhoon / Hurricane) "Stadium Effect" Eyewall & Vortex Swirl:
// Carves a 40-deg outward-sloping amphitheater eyewall (narrow eye at sea level, flaring wide at 12.6 km)
// and winds the 3D Worley cumulus towers azimuthally around the vortex core!
void ApplyTyphoon3DStadium(float3 up,float z,float4 centre,float spin,float rotPhase,
                           inout float coverage,inout float highCov,inout float convection,inout float cdoSmooth,inout float3 a,inout float3 b)
{
 if(centre.w<=.20)return;
 float extent=FlatCycloneRadius(centre.w);
 float dist=length(up-centre.xyz);
 if(dist>=extent*1.52)return;
 float r=dist/extent;
 float maturity=smoothstep(.42,.74,centre.w);
 float altNorm=saturate((z-500.0)/11900.0);

 // Outward-sloping 3D Stadium Bowl: eye radius expands ~4x from sea surface to tropopause!
 float stadiumR=lerp(.022,.088,pow(altNorm,.78));
 float eyeClear=lerp(1.0,smoothstep(stadiumR*.50,stadiumR*1.10,r),maturity);
 float hubZone=(1.0-smoothstep(950.0,1750.0,z))*smoothstep(420.0,680.0,z)*(1.0-smoothstep(stadiumR*.32,stadiumR*.92,r))*maturity;
 coverage=max(coverage*eyeClear,hubZone*.54);
 highCov*=lerp(1.0,smoothstep(stadiumR*.70,stadiumR*1.24,r),maturity);

 float wallCollar=exp(-pow((r-(stadiumR+.038))/.046,2.0))*maturity*(1.0-smoothstep(12100.0,12980.0,z));
 coverage=max(coverage,wallCollar*.95*eyeClear);
 highCov=max(highCov,wallCollar*.56*smoothstep(7600.0,9400.0,z)*eyeClear);
 convection=lerp(max(convection,wallCollar*.98),.12,hubZone*.92);
 float denseSpiralBand=smoothstep(.42,.82,coverage);
 float stormCdo=(1.0-smoothstep(.22,.50,r))*smoothstep(.26,.68,centre.w)*eyeClear*denseSpiralBand;
 cdoSmooth=max(cdoSmooth,max(wallCollar*.88,stormCdo*.75));

 float swirlWeight=(1.0-smoothstep(.45,1.40,r))*smoothstep(.18,.62,centre.w);
 if(swirlWeight>.01)
 {
  float swirlAng=(spin*1.25*log(1.0+3.6*max(r,.02))-.50*rotPhase)*swirlWeight;
  float s,c;sincos(swirlAng,s,c);
  float3 axis=centre.xyz;
  a=normalize(a*c+cross(axis,a)*s+axis*dot(axis,a)*(1.0-c));
  b=normalize(b*c+cross(axis,b)*s+axis*dot(axis,b)*(1.0-c));
 }
}

// Fast 1-tap column existence query for clear-sky empty-space skipping (preserves exact cyclone eye/outflow extent).
bool ScrollColumnMayHaveCloud(float3 p)
{
 bool hasCyclone=(_OrbisCycloneNorth.w>.20)||(_OrbisCycloneSouth.w>.20);
 float3 up=normalize(p);
 if(hasCyclone)
 {
  if(_OrbisCycloneNorth.w>.20&&length(up-_OrbisCycloneNorth.xyz)<FlatCycloneRadius(_OrbisCycloneNorth.w)*1.55)return true;
  if(_OrbisCycloneSouth.w>.20&&length(up-_OrbisCycloneSouth.xyz)<FlatCycloneRadius(_OrbisCycloneSouth.w)*1.55)return true;
 }
 float4 w=tex2Dlod(_ScrollWeatherTex,float4(OrbisFastWeatherUV(up),0,0));
 return (w.r+w.g)>=.0045;
}

// Uses the SAME spherical coordinates on the globe and every repeated 2D sheet.
// PlanetVolumetricCloudsV3-style frequency separation & pixel/step footprint band-limiting:
//   - Broad macro (6.0) + meso (23.5) body noise builds connected, plush cumulus/Sc domes with zero high-freq holes.
//   - Detail volume (56.0) is strictly band-limited by max(pixelFootprint, rayFootprint) and only feathers outer rims.
//   - High cirrus (19.5) uses silky band-limited filaments without high-contrast corduroy Moire.
float2 ScrollClimateDensitiesMip(float3 p,bool simpleMode,float pixelFootprint,float rayFootprint)
{
 float rLen=length(p);
 float3 up=p/max(1e-7,rLen);
 float2 uv=OrbisFastWeatherUV(up);
 // Keep simpleMode (probe & LightField) on single-tap ScrollCloudGroundFast via [branch]
 // (avoids HLSL eager ternary evaluating both 1-tap and 4-tap height fetches per sample!)
 float fp=max(0.0,pixelFootprint);
 float stepFoot=max(0.0,rayFootprint);
 float ground;
 [branch] if(simpleMode)ground=ScrollCloudGroundFast(uv);
 else ground=ScrollCloudGround(uv);
 float z=(rLen-ground)/max(1e-7,_CloudHeightScale);
 if(z<400||z>13500)return 0;
 float4 weather=tex2Dlod(_ScrollWeatherTex,float4(uv,0,0));
 float coverage=weather.r,highWeather=weather.g,convection=weather.b;
 bool hasCyclone=(_OrbisCycloneNorth.w>.20)||(_OrbisCycloneSouth.w>.20);
 if(!hasCyclone&&(coverage+highWeather<.006))return 0;

 float3 localWind=tex2Dlod(_ScrollCloudWindTex,float4(uv,0,0)).xyz;
 float rawPhase=frac(_ScrollWeatherTime*.160);
 float phase=rawPhase*rawPhase*(3.0-2.0*rawPhase);
 float wSpeed3=length(localWind);
 float linCap=wSpeed3>.022?(.022/max(1e-5,wSpeed3)):1.0;
 float3 advWind=localWind*(2.18*linCap);
 float cycFlowKeep=1.0;
 if(hasCyclone)
 {
  if(_OrbisCycloneNorth.w>.20)
   cycFlowKeep*=smoothstep(.45,1.25,length(up-_OrbisCycloneNorth.xyz)/FlatCycloneRadius(_OrbisCycloneNorth.w));
  if(_OrbisCycloneSouth.w>.20)
   cycFlowKeep*=smoothstep(.45,1.25,length(up-_OrbisCycloneSouth.xyz)/FlatCycloneRadius(_OrbisCycloneSouth.w));
 }
 advWind*=cycFlowKeep;
 float3 a=normalize(up-advWind*rawPhase);
 float3 b=normalize(up+advWind*(1.0-rawPhase));

 float cdoSmooth=0;
 if(hasCyclone)
 {
  ApplyTyphoon3DStadium(up,z,_OrbisCycloneNorth,1.0,_OrbisCyclonePhase.x,coverage,highWeather,convection,cdoSmooth,a,b);
  ApplyTyphoon3DStadium(up,z,_OrbisCycloneSouth,-1.0,_OrbisCyclonePhase.y,coverage,highWeather,convection,cdoSmooth,a,b);
 }

 if(coverage+highWeather<.006)return 0;

 // Unified pixel + ray-step Nyquist band-limiting (active at ALL viewing angles including nadir 36-70% unfold!)
 float bodyFoot=max(fp*1.12,stepFoot*.34);
 float fineFoot=max(fp*1.38,stepFoot*.92);
 float macroMip=clamp(log2(max(1.0,bodyFoot*6.0*64.0)),0.0,4.5);
 float angularBodyMip=clamp(log2(max(1.0,fp*23.5*64.0)),0.0,5.0);
 float closeUp=1.0-smoothstep(.40,1.75,angularBodyMip);
 float bodyMip=clamp(log2(max(1.0,bodyFoot*23.5*64.0))+.05,0.0,5.0);
 float detailMip=clamp(log2(max(1.0,fineFoot*56.0*32.0))+.32,0.0,5.5);
 float cirrusMip=clamp(log2(max(1.0,fineFoot*19.5*32.0))+.45,0.0,4.5);

 float low=0,high=0;
 if(coverage>.004&&z<13100)
 {
  float2 span=OrbisCloudVerticalSpan(convection,saturate(coverage+.12));
  bool inMidBand=convection>.28&&convection<.76&&z>3800.0&&z<6150.0;
  if((z>span.x-100.0&&z<span.y*1.14+340.0)||inMidBand)
  {
   float altitude=z/7000;
   const float3x3 frame=float3x3(.36,.48,-.80,-.80,.60,0,.48,.64,.60);
   float3 radAxis=mul(frame,up);
   float anvilShear=(.72+1.05*smoothstep(.58,.96,convection)*smoothstep(1.05,1.65,altitude))*.42;
   float3 windShear=mul(frame,localWind)*(altitude*anvilShear);
   float3 q0=mul(frame,a);

   float shape=0,worleyMeso=.5,effCov=coverage,scCobbleGain=1.0;
   float4 erosion=float4(.68,.68,.46,.46);
   bool evalLow=false;
   float cdoEff=cdoSmooth*(1.0-.48*closeUp);
   [branch] if(simpleMode)
   {
    // Phase-consistent macro+meso simpleMode matching primary cloud body without +10% roof overhang:
    // eliminates binary sunTau=0 vs sunTau>3 confetti speckles in LightField while preserving probe recall!
    float3 qS=mul(frame,normalize(up-advWind*(rawPhase-.5)*.5));
    float3 useQ=cycFlowKeep<.05?q0:qS;
    float3 bPos=useQ*23.5+radAxis*(altitude*.38)+windShear+float3(.41,.19,.83);
    float4 body=tex3Dlod(_CloudNoise,float4(bPos,min(4.5,bodyMip+.18)));
    worleyMeso=body.g*lerp(.66,.54,closeUp)+body.b*lerp(.26,.38,closeUp)+body.a*.08;
    float baseS=body.r*.64+worleyMeso*.36;
    baseS=lerp(baseS,.68+.32*baseS,cdoEff*.28);
    worleyMeso=lerp(worleyMeso,.66+.34*worleyMeso,cdoEff*.26);

    // Match cellular marine Sc / open-cell meso structure cleanly without sharp max(0.68) roof cliffs
    float marineScS=1.0-smoothstep(.08,.18,convection);
    float openCellS=(1.0-smoothstep(.18,.48,convection))*(1.0-cdoSmooth);
    float scOvercastS=marineScS*smoothstep(.48,.78,coverage)*(1.0-cdoSmooth);
    float cellWallS=smoothstep(.30,.70,1.0-(body.b*.74+body.g*.26))*openCellS*(1.0-scOvercastS);
    baseS=lerp(baseS,max(baseS,max(.58*scOvercastS,.56*cellWallS)),.65);
    worleyMeso=lerp(worleyMeso,max(worleyMeso,max(.60*scOvercastS,.58*cellWallS)),.65);

    erosion=float4(.68,.68,.46,.46);
    float covActive=smoothstep(.04,.22,coverage);
    shape=saturate((baseS-(1.0-worleyMeso)*.16*(1.0-.36*cdoEff))/.84+coverage*.06+.11*smoothstep(.06,.52,coverage)+.03*covActive);
    effCov=saturate(coverage+.03*covActive);
    evalLow=true;
   }
   else
   {
    float invStd=lerp(1.0,rsqrt(max(.5,phase*phase+(1.0-phase)*(1.0-phase))),cycFlowKeep);
    float3 q1=mul(frame,b);

    float3 mPos0=q0*6.0+radAxis*(altitude*.13)+windShear*.38+float3(.13,.71,.37);
    float4 macroRaw=tex3Dlod(_CloudNoise,float4(mPos0,macroMip));
    [branch] if(cycFlowKeep>.01)
    {
     float3 mPos1=q1*6.0+radAxis*(altitude*.13)+windShear*.38+float3(.13,.71,.37);
     macroRaw=lerp(macroRaw,tex3Dlod(_CloudNoise,float4(mPos1,macroMip)),phase);
    }
    float4 macroBody=saturate(.5+(macroRaw-.5)*lerp(1.0,invStd,.60));

    float3 mesoWarp=(macroBody.gba-.5)*.055;
    float3 bPos0=q0*23.5+radAxis*(altitude*.38)+windShear+mesoWarp+float3(.41,.19,.83);
    float4 bodyRaw=tex3Dlod(_CloudNoise,float4(bPos0,bodyMip));
    [branch] if(cycFlowKeep>.01)
    {
     float3 bPos1=q1*23.5+radAxis*(altitude*.38)+windShear+mesoWarp+float3(.41,.19,.83);
     bodyRaw=lerp(bodyRaw,tex3Dlod(_CloudNoise,float4(bPos1,bodyMip)),phase);
    }
    float4 body=saturate(.5+(bodyRaw-.5)*lerp(1.0,invStd,.68));

    worleyMeso=body.g*lerp(.66,.54,closeUp)+body.b*lerp(.26,.38,closeUp)+body.a*.08;
    float basePW=macroBody.r*.26+body.r*.44+worleyMeso*.30;
    basePW=lerp(basePW,.68+.32*basePW,cdoEff*.28);
    worleyMeso=lerp(worleyMeso,.66+.34*worleyMeso,cdoEff*.26);
    float mistCohesion=.12*smoothstep(.06,.52,coverage);
    shape=saturate((basePW-(1.0-worleyMeso)*.16*(1.0-.36*cdoEff))/.84+coverage*.06+mistCohesion);

    float covBound=.16+.84*saturate(coverage+.16);
    if(pow(shape,.64)>.84-covBound||coverage>.50||(convection<.52&&coverage>.06)||inMidBand)
    {
     // Strictly separated meso cloud body: cellular Sc / open-wake / trade-Cu patterns use ONLY smooth
     // band-limited macroBody & body (ZERO high-frequency erosion injected into shape or worleyMeso!).
     float marineScMask=1.0-smoothstep(.08,.18,convection);
     float openWakeMask=smoothstep(.10,.17,convection)*(1.0-smoothstep(.20,.30,convection));
     float tradeCuMask=smoothstep(.17,.26,convection)*(1.0-smoothstep(.35,.52,convection));
     float cellSum=marineScMask+openWakeMask*.90+tradeCuMask*.55;
     float cellActive=saturate(cellSum)*(1.0-cdoSmooth);
     if(cellActive>.01)
     {
      float scOvercast=smoothstep(.48,.78,coverage);
      float wClosed=body.b*.72+body.g*.28;
      float cobbleFloor=lerp(.16,.64,scOvercast);
      float closedCobble=lerp(cobbleFloor,1.0,smoothstep(.24,.68,wClosed));
      float cellWall=1.0-(body.b*.72+body.g*.28);
      float openRing=smoothstep(.32,.72,cellWall)*(.68+.32*body.r);
      float tradeBillow=macroBody.r*.28+body.r*.46+body.g*.26;
      float finePopcorn=saturate(.22+.78*smoothstep(.20,.80,tradeBillow));

      float pocOpen=saturate(smoothstep(.30,.74,(1.0-coverage*.92)+(macroBody.g-.48)*.90))*(1.0-scOvercast);
      float scPattern=lerp(closedCobble,openRing,pocOpen);
      float wakePattern=lerp(lerp(openRing,closedCobble,scOvercast*.85),finePopcorn,.45);
      float tradePattern=lerp(finePopcorn,lerp(openRing,closedCobble,scOvercast*.80),.28);
      float targetPattern=(marineScMask*scPattern+openWakeMask*.90*wakePattern+tradeCuMask*.55*tradePattern)/max(1e-4,cellSum);

      float scDominance=marineScMask/max(1e-4,cellSum);
      float sculptW=saturate(marineScMask*.84+openWakeMask*.68+tradeCuMask*.38)*(1.0-smoothstep(.60,.90,coverage)*(1.0-marineScMask*.55));
      float baseKeep=lerp(.54,lerp(.34,.62,scOvercast),scDominance);
      float patGain=lerp(.54,lerp(.74,.50,scOvercast),scDominance);
      shape=lerp(shape,saturate(shape*baseKeep+targetPattern*patGain),sculptW*lerp(.64,.88,scDominance));
      worleyMeso=lerp(worleyMeso,targetPattern,sculptW*lerp(.56,.80,scDominance));
      scCobbleGain=lerp(1.0,1.24,marineScMask*(1.0-pocOpen*.55)*closedCobble);
     }

     // Evaluate fine detail volume ONLY when pixel & step footprints actually resolve it AND outside deep opaque cores
     float detailFade=1.0-smoothstep(1.35,2.75,detailMip);
     float4 eroNeutral=float4(.68,.68,.46,.46);
     if(detailFade>.02)
     {
      float3 curl=(body.gba-.5)*.16;
      float3 dPos0=q0*56.0+radAxis*(altitude*.88)+windShear*.55+curl;
      float4 eroRaw=tex3Dlod(_CloudDetailNoise,float4(dPos0,detailMip));
      [branch] if(cycFlowKeep>.01&&detailMip<1.85)
      {
       float3 dPos1=q1*56.0+radAxis*(altitude*.88)+windShear*.55+curl;
       eroRaw=lerp(eroRaw,tex3Dlod(_CloudDetailNoise,float4(dPos1,detailMip)),phase);
      }
      float eroBoost=lerp(1.0,invStd,.42*detailFade*(1.0-smoothstep(.45,1.40,detailMip)));
      erosion=lerp(eroNeutral,saturate(.5+(eroRaw-.5)*eroBoost),detailFade);
      erosion.a=lerp(erosion.a,.48,smoothstep(.10,.95,detailMip));
     }
     else
     {
      erosion=eroNeutral;
     }
     erosion=lerp(erosion,float4(.70,.70,.50,.50),cdoEff*.30);

     // Gentle outer-edge coverage feather only on broken cloud rims (never tears holes in >=50% cloud decks)
     float edgeInvW=1.0-(erosion.r*.66+erosion.g*.34);
     float edgeInvMean=lerp(edgeInvW,.44,smoothstep(.7,2.0,detailMip));
     float edgeTear=edgeInvMean*.085*(1.0-smoothstep(.48,.82,coverage))*smoothstep(.02,.20,coverage)*(1.0-.82*cdoSmooth);
     effCov=saturate(coverage-edgeTear);
     evalLow=true;
    }
   }
   if(evalLow)
   {
    float tower=smoothstep(.45,.96,convection);
    float mipBlur=smoothstep(.55,2.35,angularBodyMip);
    float stormProtect=saturate(cdoSmooth*1.4);
    float orbitM=mipBlur*(1.0-.90*stormProtect);
    float thinMist=lerp(.032,.23,smoothstep(.04,.48,effCov))*lerp(1.0,.68,orbitM*(1.0-smoothstep(.04,.28,effCov)));
    float coreExp=lerp(1.48,2.70,orbitM);
    float coreScale=lerp(1.20,.85,orbitM*(1.0-smoothstep(.45,.88,effCov)));
    float denseCore=coreScale*pow(smoothstep(.30,.92,effCov),coreExp);
    float scLow=1.0-smoothstep(.08,.20,convection);
    // PlanetVolumetricCloudsV3 balanced optical density: outer 2-3 steps stay plush cotton-soft
    // via silhouetteMask while plump cumulus cores reach natural opacity in 4-5 steps!
    float cuLwcBoost=lerp(1.0,lerp(2.25,1.42,tower)*(1.0-.36*scLow),closeUp*smoothstep(.18,.64,effCov));
    float opticalGain=(thinMist+denseCore)*lerp(.92,1.24,tower)*scCobbleGain*cuLwcBoost;
    low=OrbisHPLowDensityDetailed(z,.14+.86*effCov,convection,shape,erosion,.42+.58*worleyMeso)*opticalGain;
   }
  }
 }
 if(highWeather>.004&&z>6950)
 {
  float3 surfUnit=localWind*(1.0/.014);
  [branch] if(simpleMode)
  {
   // Fast single-tap high-cloud extinction for LightField & empty-space probe
   float3 hiMid=normalize(up-surfUnit*(cycFlowKeep*.015));
   float4 iceS=tex2Dlod(_HPHighDetail,float4(OrbisFastWeatherUV(hiMid),0,0));
   float highCoverS=highWeather*lerp(.68,.92,cdoSmooth*.62);
   high=OrbisHPHighDensity(z,.16+.74*highCoverS,.34,lerp(.58+.42*iceS.r,.84+.16*iceS.r,cdoSmooth*.60),iceS.g)*highCoverS*1.06;
  }
  else
  {
   float3 eastDir=normalize(float3(up.z,0,-up.x)+float3(1e-5,0,0));
   float3 upperUnit=surfUnit;
   if(_ScrollUpperWindReady>.5)
   {
    #ifdef UNITY_SEPARATE_TEXTURE_SAMPLER
    float4 uw=UNITY_SAMPLE_TEX2D_SAMPLER_LOD(_ScrollUpperWindTex,_ScrollHeightTex,uv,0);
    #else
    float4 uw=tex2Dlod(_ScrollUpperWindTex,float4(uv,0,0));
    #endif
    float2 uFlow=lerp(uw.rg,uw.ba,_ScrollMonthBlend)*(1.0/9.0);
    float3 northDir=normalize(cross(up,eastDir)+float3(0,1e-5,0));
    float3 jetUnit=eastDir*uFlow.x+northDir*uFlow.y;
    upperUnit=lerp(surfUnit,jetUnit,.55);
   }
   float3 hiUnit=upperUnit*cycFlowKeep;
   float hiLen=length(hiUnit);
   float wActive=smoothstep(.03,.24,hiLen);
   float hiDisp=hiLen*.030513;
   float hiCap=hiDisp>.065?(.065/max(1e-5,hiDisp)):1.0;
   float3 hiAdv=hiUnit*(.030513*hiCap)*wActive;
   float hiRaw=frac(_ScrollWeatherTime*.160+.37);
   float hiBlend=hiRaw*hiRaw*(3.0-2.0*hiRaw);
   float3 hiA=normalize(up-hiAdv*hiRaw);
   float3 hiB=normalize(up+hiAdv*(1.0-hiRaw));
   float4 iceA=tex2Dlod(_HPHighDetail,float4(OrbisFastWeatherUV(hiA),0,0));
   float4 iceB=tex2Dlod(_HPHighDetail,float4(OrbisFastWeatherUV(hiB),0,0));
   float4 ice=lerp(iceA,iceB,hiBlend);
   float fine=.56;
   float cirrusCell=ice.r;
   float cirrusWisp=ice.g;
   float hiDetailFade=1.0-smoothstep(1.45,2.90,cirrusMip);
   if(hiDetailFade>.02)
   {
    float3 dynAlong=hiLen>1e-4?(hiUnit/hiLen):eastDir;
    float3 dynCross=normalize(cross(up,dynAlong)+float3(1e-5,0,0));
    float3 axisAlong=normalize(lerp(eastDir,dynAlong,wActive));
    float3 axisCross=normalize(lerp(normalize(cross(up,eastDir)+float3(1e-5,0,0)),dynCross,wActive));
    float curve=(ice.b-.5)*.75+(ice.a-.5)*.45;
    float fallHook=(z-8400)*.0000022;
    float altZ=(z-7200.0)/5600.0;
    const float3x3 cFrame=float3x3(.48,.64,.60,-.80,.60,0,.36,.48,-.80);
    float3 cRotA=mul(cFrame,normalize(hiA+axisCross*(curve*.028)+axisAlong*fallHook));
    float4 tapA=tex3Dlod(_CloudDetailNoise,float4(cRotA*19.5+float3(0,0,altZ*.22)+(ice.b-.5)*.20,cirrusMip));
    [branch] if(cycFlowKeep>.01&&cirrusMip<2.0)
    {
     float3 cRotB=mul(cFrame,normalize(hiB+axisCross*(curve*.028)+axisAlong*fallHook));
     float4 tapB=tex3Dlod(_CloudDetailNoise,float4(cRotB*19.5+float3(0,0,altZ*.22)+(ice.b-.5)*.20,cirrusMip));
     tapA=lerp(tapA,tapB,hiBlend);
    }
    float streakFine=lerp(tapA.a,.5,smoothstep(.10,.95,cirrusMip));
    fine=lerp(.56,saturate(tapA.b*.48+tapA.g*.30+streakFine*.10+tapA.r*.12),hiDetailFade);
    float hiResBlend=closeUp*hiDetailFade*.58;
    cirrusCell=lerp(ice.r,saturate(tapA.r*.56+tapA.g*.44),hiResBlend);
    cirrusWisp=lerp(ice.g,saturate(tapA.b*.66+streakFine*.34),hiResBlend);
   }
   // Silky translucent cirrus modulation without high-contrast corduroy Moire
   float highMod=lerp(.58+.52*smoothstep(.20,.82,fine),.76+.30*fine,cdoSmooth*.62);
   float highCover=highWeather*highMod;
   float highUnderLowSoft=lerp(1.0,lerp(.70,1.0,cdoSmooth),closeUp*smoothstep(.28,.75,coverage));
   high=OrbisHPHighDensity(z,.16+.74*highCover,.34,lerp(.58+.42*cirrusCell,.84+.16*cirrusCell,cdoSmooth*.60),cirrusWisp)*highCover*1.06*highUnderLowSoft;
  }
 }
 return float2(low,high)*_Density*max(0,_Cover);
}
float2 ScrollClimateDensities(float3 p,bool simpleMode)
{
 return ScrollClimateDensitiesMip(p,simpleMode,0.0,0.0);
}
#endif
