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
 // Subtle low-level mesovortex "hub cloud" stratocumulus swirl at the bottom of the 12 km stadium bowl
 // (seen inside Category-5 Hurricane Isabel's eye from the ISS — gives instant vertical depth scale!)
 float hubZone=(1.0-smoothstep(950.0,1750.0,z))*smoothstep(420.0,680.0,z)*(1.0-smoothstep(stadiumR*.32,stadiumR*.92,r))*maturity;
 coverage=max(coverage*eyeClear,hubZone*.54);
 highCov*=lerp(1.0,smoothstep(stadiumR*.70,stadiumR*1.24,r),maturity);

 // Tight 12.6 km stadium eyewall collar (strictly r < 0.21 — NEVER overwrites inner spiral moats with a round disk!)
 // + Coverage-gated CDO cohesion (preserves 79e6fa2 zero-gray-spot creamy tops on bright spiral bands & inner CDO
 // while keeping inter-band spiral moats razor-clear!)
 float wallCollar=exp(-pow((r-(stadiumR+.038))/.046,2.0))*maturity*(1.0-smoothstep(12100.0,12980.0,z));
 coverage=max(coverage,wallCollar*.95*eyeClear);
 highCov=max(highCov,wallCollar*.56*smoothstep(7600.0,9400.0,z)*eyeClear);
 convection=lerp(max(convection,wallCollar*.98),.12,hubZone*.92);
 float denseSpiralBand=smoothstep(.42,.82,coverage);
 // Keep inner eyewall smooth and cohesive while letting outer eyewall & rainband mid-sections show soft 3D billows.
 float stormCdo=(1.0-smoothstep(.22,.50,r))*smoothstep(.26,.68,centre.w)*eyeClear*denseSpiralBand;
 cdoSmooth=max(cdoSmooth,max(wallCollar*.88,stormCdo*.75));

 // Smooth logarithmic azimuthal cyclonic swirl of 3D volumetric clouds along spiral rainbands
 // (gentle shear gradient 1.25 prevents comb-teeth sawtooth while preserving 3D billowy spiral ridges in both NH & SH!)
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

// Uses the SAME spherical coordinates on the globe and every repeated 2D sheet.
// pixelFootprint = projected world-space arc length per rendered pixel (0 = full-res mip 0).
float2 ScrollClimateDensitiesMip(float3 p,bool simpleMode,float pixelFootprint,float rayFootprint)
{
 float rLen=length(p);
 float3 up=p/max(1e-7,rLen);
 float2 uv=OrbisFastWeatherUV(up);
 float ground=simpleMode?ScrollCloudGroundFast(uv):ScrollCloudGround(uv);
 float z=(rLen-ground)/max(1e-7,_CloudHeightScale);
 if(z<400||z>13500)return 0;
 float4 weather=tex2Dlod(_ScrollWeatherTex,float4(uv,0,0));
 float coverage=weather.r,highWeather=weather.g,convection=weather.b;
 bool hasCyclone=(_OrbisCycloneNorth.w>.20)||(_OrbisCycloneSouth.w>.20);
 if(!hasCyclone&&(coverage+highWeather<.006))return 0;

 float3 localWind=tex2Dlod(_ScrollCloudWindTex,float4(uv,0,0)).xyz;
 // Calendar-locked dual-phase 3D puff advection (_ScrollWeatherTime = PublishedCalendarDay * 25.0):
 // 4.0 cycles/day (6h lifecycle) with linear wind proportionality matches FlatSkyBake's exact physical
 // surface transport (0.12205 rad/day per wind unit, i.e., 8.72 rad/day per localWind) across weak/medium/strong winds
 // while keeping |b - a| <= 0.025 rad (< 0.75 Worley cell, zero ghosting or wallpaper slip)!
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
 // Physically matched trilinear 3D mip levels from true screen pixel footprint & isotropic ray stride:
 // prevents sub-pixel Value(16)/Value(32) grain and wheel-scroll moire while keeping close-up cumulus plump!
 float fp=max(0.0,pixelFootprint);
 float macroMip=clamp(log2(max(1.0,fp*6.4*128.0))-.20,0.0,4.5);
 float angularBodyMip=clamp(log2(max(1.0,fp*30.0*128.0))-.15,0.0,5.0);
 float closeUp=1.0-smoothstep(.35,1.75,angularBodyMip);
 float isoRayFoot=rayFootprint*lerp(.55,.28,closeUp);
 float filteredFoot=max(fp,isoRayFoot);
 float bodyMip=clamp(log2(max(1.0,filteredFoot*30.0*128.0))-.10,0.0,5.0);
 float detailMip=clamp(log2(max(1.0,filteredFoot*84.0*64.0))+.55,0.0,5.5);
 float cirrusMip=clamp(log2(max(1.0,filteredFoot*34.0*64.0))+.10,0.0,4.5);

 float low=0,high=0;
 if(coverage>.004&&z<13100)
 {
  float2 span=OrbisCloudVerticalSpan(convection,saturate(coverage+.12));
  bool inMidBand=convection>.28&&convection<.76&&z>3800.0&&z<6150.0;
  if((z>span.x-90.0&&z<span.y*1.14+300.0)||inMidBand)
  {
   float altitude=z/7000;
   const float3x3 frame=float3x3(.36,.48,-.80,-.80,.60,0,.48,.64,.60);
   float3 radAxis=mul(frame,up);
   // Scale downwind anvil shear proportionally to isotropic vertical scale so cyclone billows stay plump, never smeared
   float anvilShear=(.80+1.15*smoothstep(.58,.96,convection)*smoothstep(1.05,1.65,altitude))*.45;
   float3 windShear=mul(frame,localWind)*(altitude*anvilShear);
   float3 q0=mul(frame,a);

   float shape=0,worleyMeso=.5,effCov=coverage,scCobbleGain=1.0;
   float4 erosion=float4(.80,.80,.24,.24);
   bool evalLow=false;
   float cdoEff=cdoSmooth*(1.0-.52*closeUp);
   if(simpleMode)
   {
    // Phase-locked single-fetch simpleMode with conservative erosion bound matching primary pass & LightField:
    // avoids false misses on thin wispy rims while keeping LightField self-shadowing responsive inside billows.
    float3 qS=mul(frame,normalize(up-advWind*(rawPhase-.5)*.5));
    float3 bPos=(cycFlowKeep<.05?q0:qS)*30.0+radAxis*(altitude*.42)+windShear+float3(.41,.19,.83);
    float4 body=tex3Dlod(_CloudNoise,float4(bPos,min(4.5,bodyMip+.20)));
    worleyMeso=body.g*lerp(.66,.54,closeUp)+body.b*lerp(.26,.38,closeUp)+body.a*.08;
    float baseS=body.r*.62+worleyMeso*.38;
    baseS=lerp(baseS,.68+.32*baseS,cdoEff*.28);
    worleyMeso=lerp(worleyMeso,.66+.34*worleyMeso,cdoEff*.26);
    float scOvercastS=(1.0-smoothstep(.08,.18,convection))*smoothstep(.48,.78,coverage)*(1.0-cdoSmooth);
    // Conservative bound for open-cell / wake / trade-Cu rings whose bright walls use 1 - (body.b, body.g):
    // prevents simpleMode probe from missing thin ring rims and punching cookie-cutter holes in top-down views.
    float openCellS=(1.0-smoothstep(.18,.48,convection))*(1.0-cdoSmooth)*(1.0-scOvercastS);
    float openWallS=smoothstep(.30,.68,1.0-(body.b*.76+body.g*.24))*openCellS;
    baseS=max(baseS,max(.64*scOvercastS,.68*openWallS));
    worleyMeso=max(worleyMeso,max(.66*scOvercastS,.70*openWallS));
    erosion=float4(.80,.80,.24,.24);
    float covActive=smoothstep(.04,.20,coverage);
    shape=saturate((baseS-(1.0-worleyMeso)*.16*(1.0-.36*cdoEff))/.84+coverage*.06+.12*smoothstep(.06,.52,coverage)+.07*covActive);
    effCov=saturate(coverage+.10*covActive);
    evalLow=true;
   }
   else
   {
    // Variance-preserving dual-phase flow blend with isotropic 1:1 spherical 3D cumulus billows (HPVolumeCloud style):
    // eliminates 3.56x pancake squashing & oblique Nyquist fingerprint moire while preserving full-disk horizontal frequency.
    float invStd=lerp(1.0,rsqrt(max(.5,phase*phase+(1.0-phase)*(1.0-phase))),cycFlowKeep);
    float3 q1=mul(frame,b);

    float3 mPos0=q0*6.4+radAxis*(altitude*.14)+windShear*.40+float3(.13,.71,.37);
    float4 macroRaw=tex3Dlod(_CloudNoise,float4(mPos0,macroMip));
    [branch] if(cycFlowKeep>.01)
    {
     float3 mPos1=q1*6.4+radAxis*(altitude*.14)+windShear*.40+float3(.13,.71,.37);
     macroRaw=lerp(macroRaw,tex3Dlod(_CloudNoise,float4(mPos1,macroMip)),phase);
    }
    float4 macroBody=saturate(.5+(macroRaw-.5)*lerp(1.0,invStd,.65));

    float3 mesoWarp=(macroBody.gba-.5)*.06;
    float3 bPos0=q0*30.0+radAxis*(altitude*.42)+windShear+mesoWarp+float3(.41,.19,.83);
    float4 bodyRaw=tex3Dlod(_CloudNoise,float4(bPos0,bodyMip));
    [branch] if(cycFlowKeep>.01)
    {
     float3 bPos1=q1*30.0+radAxis*(altitude*.42)+windShear+mesoWarp+float3(.41,.19,.83);
     bodyRaw=lerp(bodyRaw,tex3Dlod(_CloudNoise,float4(bPos1,bodyMip)),phase);
    }
    float4 body=saturate(.5+(bodyRaw-.5)*lerp(1.0,invStd,.74));

    worleyMeso=body.g*lerp(.66,.54,closeUp)+body.b*lerp(.26,.38,closeUp)+body.a*.08;
    float basePW=macroBody.r*.22+body.r*.44+worleyMeso*.34;
    // Keep inner eyewall smooth from orbit while letting near-eye turrets & rainbands express plump 3D cauliflower billows
    basePW=lerp(basePW,.68+.32*basePW,cdoEff*.28);
    worleyMeso=lerp(worleyMeso,.66+.34*worleyMeso,cdoEff*.26);
    // Keep soft synoptic mist envelopes connected while 3D fine detail sculpts lacy micro-structure
    float mistCohesion=.12*smoothstep(.06,.52,coverage);
    shape=saturate((basePW-(1.0-worleyMeso)*.18*(1.0-.36*cdoEff))/.82+coverage*.06+mistCohesion);

    float covBound=.16+.84*saturate(coverage+.16);
    // Marine Sc / open-wake / trade-Cu regimes (convection < 0.52) sculpt shape via targetPattern, so do not
    // early-cull them on raw pre-sculpt shape (which caused hard cookie-cutter cliffs at coverage <= 0.52).
    if(pow(shape,.58)>.86-covBound||coverage>.52||(convection<.52&&coverage>.06)||inMidBand)
    {
     float detailFade=1.0-smoothstep(1.35,3.25,detailMip);
     float4 eroNeutral=float4(.64,.64,.46,.46);
     if(detailFade>.02)
     {
      float3 curl=(body.gba-.5)*.20;
      // 2.8x ratio above body (84 vs 30) closes the spectral gap between meso billows and fine erosion
      // so 36-55% unfold stays fluffy and coherent instead of breaking into isolated confetti dots.
      float3 dPos0=q0*84.0+radAxis*(altitude*.96)+windShear*.62+curl;
      float4 eroRaw=tex3Dlod(_CloudDetailNoise,float4(dPos0,detailMip));
      [branch] if(cycFlowKeep>.01&&detailMip<2.2)
      {
       float3 dPos1=q1*84.0+radAxis*(altitude*.96)+windShear*.62+curl;
       eroRaw=lerp(eroRaw,tex3Dlod(_CloudDetailNoise,float4(dPos1,detailMip)),phase);
      }
      // Damp contrast amplification and Value(32) channel A above mip 0.35 so sub-pixel detail never shimmers!
      float eroBoost=lerp(1.0,invStd,.55*detailFade*(1.0-smoothstep(.50,1.60,detailMip)));
      erosion=lerp(eroNeutral,saturate(.5+(eroRaw-.5)*eroBoost),detailFade);
      erosion.a=lerp(erosion.a,.48,smoothstep(.35,1.35,detailMip));
     }
     else
     {
      erosion=eroNeutral;
     }

     // True 3D Voronoi Marine Stratocumulus (Closed-Cell Cobblestone Sheets + Lacy Open-Cell Actiniform Rings):
     // Gate open-cell POC cutouts cleanly off high-coverage overcast decks (>0.52) so thick Sc sheets form
     // continuous plump cobblestone pillows instead of Swiss-cheese pinholes, while preserving natural open-cell Sc!
     float marineScMask=1.0-smoothstep(.08,.18,convection);
     float openWakeMask=smoothstep(.10,.17,convection)*(1.0-smoothstep(.20,.30,convection));
     float tradeCuMask=smoothstep(.17,.26,convection)*(1.0-smoothstep(.35,.52,convection));
     float cellSum=marineScMask+openWakeMask*.90+tradeCuMask*.55;
     float cellActive=saturate(cellSum)*(1.0-cdoSmooth);
     if(cellActive>.01)
     {
      float scOvercast=smoothstep(.48,.78,coverage);
      float wClosed=body.b+(erosion.r-.5)*.16;
      float cobbleFloor=lerp(.08,.58,scOvercast);
      float closedCobble=lerp(cobbleFloor,1.0,smoothstep(.28,.66,wClosed))*(.82+.18*smoothstep(.18,.72,erosion.g));
      float cellWall=1.0-(body.b*.76+body.g*.24);
      float openRing=smoothstep(.36,.72,cellWall*.74+erosion.g*.38)*(.56+.44*smoothstep(.18,.68,erosion.r*.62+erosion.g*.38));
      // Coherent billowy trade-cumulus clusters anchored on 3D body noise (eliminates polka-dot Worley spotting!)
      float tradeBillow=body.r*.44+body.g*.36+(erosion.r*.65+erosion.g*.35)*.20;
      float finePopcorn=saturate(.18+.82*smoothstep(.22,.80,tradeBillow));

      float pocOpen=saturate(smoothstep(.28,.74,(1.0-coverage*.92)+(macroBody.g-.48)*.95))*(1.0-scOvercast);
      float scPattern=lerp(closedCobble,openRing,pocOpen);
      float wakePattern=lerp(lerp(openRing,closedCobble,scOvercast*.85),finePopcorn,.42);
      float tradePattern=lerp(finePopcorn,lerp(openRing,closedCobble,scOvercast*.80),.30);
      float targetPattern=(marineScMask*scPattern+openWakeMask*.90*wakePattern+tradeCuMask*.55*tradePattern)/max(1e-4,cellSum);

      float scDominance=marineScMask/max(1e-4,cellSum);
      float sculptW=saturate(marineScMask*.92+openWakeMask*.76+tradeCuMask*.44)*(1.0-smoothstep(.60,.90,coverage)*(1.0-marineScMask*.60));
      float baseKeep=lerp(.48,lerp(.26,.56,scOvercast),scDominance);
      float patGain=lerp(.62,lerp(.84,.58,scOvercast),scDominance);
      shape=lerp(shape,saturate(shape*baseKeep+targetPattern*patGain),sculptW*lerp(.70,.96,scDominance));
      worleyMeso=lerp(worleyMeso,targetPattern,sculptW*lerp(.64,.88,scDominance));
      scCobbleGain=lerp(1.0,1.42,marineScMask*(1.0-pocOpen*.55)*closedCobble);
     }
     // Keep detail noise strictly subtractive on outer silhouettes (HanPi style) instead of injecting additive
     // Worley polka-dots into shape; gently modulate meso dome relief strictly inside existing cloud cores.
     float puffSculpt=closeUp*detailFade*(1.0-marineScMask*.75)*smoothstep(.24,.64,shape);
     if(puffSculpt>.01)
     {
      float microPuff=erosion.r*.66+erosion.g*.34;
      worleyMeso=lerp(worleyMeso,saturate(worleyMeso*.84+microPuff*.20),puffSculpt*.32);
     }
     erosion=lerp(erosion,float4(.68,.68,.52,.52),cdoEff*.26);

     float edgeInvW=1.0-(erosion.r*.66+erosion.g*.34);
     // Mean-preserving mip attenuation: removes sub-pixel noise at orbit without inflating cloud area into clear gyres!
     float edgeInvMean=lerp(edgeInvW,.44,smoothstep(.9,2.4,detailMip));
     float edgeTear=edgeInvMean*.125*(1.0-smoothstep(.58,.90,coverage))*smoothstep(.02,.22,coverage)*(1.0-.78*cdoSmooth);
     effCov=saturate(coverage-edgeTear);
     evalLow=true;
    }
   }
   if(evalLow)
   {
    float tower=smoothstep(.45,.96,convection);
    // Smooth zoom-continuous optical depth curve: preserves orbital full-disk albedo while letting
    // close-up cumulus build volumetric thickness across 5-8 steps (eliminates the 3.4x posterization cliff
    // and violent wheel-scroll density pop at 36-55% unfold!).
    float mipBlur=smoothstep(.55,2.35,angularBodyMip);
    float stormProtect=saturate(cdoSmooth*1.4);
    float orbitM=mipBlur*(1.0-.90*stormProtect);
    float thinMist=lerp(.032,.23,smoothstep(.04,.48,effCov))*lerp(1.0,.66,orbitM*(1.0-smoothstep(.04,.28,effCov)));
    float coreExp=lerp(1.68,2.85,orbitM);
    float coreScale=lerp(1.16,.86,orbitM*(1.0-smoothstep(.45,.88,effCov)));
    float denseCore=coreScale*pow(smoothstep(.34,.94,effCov),coreExp);
    float scLow=1.0-smoothstep(.08,.20,convection);
    float cuLwcBoost=lerp(1.0,lerp(1.78,1.22,tower)*(1.0-.38*scLow),closeUp*smoothstep(.20,.68,effCov));
    float opticalGain=(thinMist+denseCore)*lerp(.88,1.20,tower)*scCobbleGain*cuLwcBoost;
    low=OrbisHPLowDensityDetailed(z,.14+.86*effCov,convection,shape,erosion,.42+.58*worleyMeso)*opticalGain;
   }
  }
 }
 if(highWeather>.004&&z>6950)
 {
  // Separate high-cloud wind direction, speed, and time calibration matching FlatSkyBake 1:1:
  // reads physical m/s _ScrollUpperWindTex / 9.0 ONLY on high-altitude steps (z > 6950 m, zero low-cloud cost!)
  // sharing _ScrollHeightTex's sampler so D3D11 sampler budget stays unchanged.
  float3 eastDir=normalize(float3(up.z,0,-up.x)+float3(1e-5,0,0));
  float3 surfUnit=localWind*(1.0/.014);
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
  // 4.0 cycles/day (6h period): 0.122053 rad/day * 0.25 day = 0.030513 rad/cycle per 9 m/s unit
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
  if(!simpleMode)
  {
   float3 dynAlong=hiLen>1e-4?(hiUnit/hiLen):eastDir;
   float3 dynCross=normalize(cross(up,dynAlong)+float3(1e-5,0,0));
   float3 axisAlong=normalize(lerp(eastDir,dynAlong,wActive));
   float3 axisCross=normalize(lerp(normalize(cross(up,eastDir)+float3(1e-5,0,0)),dynCross,wActive));
   float curve=(ice.b-.5)*.85+(ice.a-.5)*.55;
   // Physically realistic cirrus virga fallstreak (~15 km horizontal hook across 4 km altitude, NOT 0.92 rad / 53 deg!):
   // eliminates 8.9 m vertical Nyquist thread-striping in oblique grazing, upward views, and typhoon canopy rims!
   float fallHook=(z-8400)*.0000025;
   float altZ=(z-7200.0)/5600.0;
   const float3x3 cFrame=float3x3(.48,.64,.60,-.80,.60,0,.36,.48,-.80);
   float3 uSwirlA=normalize(hiA+axisCross*(curve*.032)+axisAlong*fallHook);
   float3 uSwirlB=normalize(hiB+axisCross*(curve*.032)+axisAlong*fallHook);
   float3 cRotA=mul(cFrame,uSwirlA);
   float3 cRotB=mul(cFrame,uSwirlB);
   float3 cLicA=mul(cFrame,normalize(uSwirlA+axisAlong*.010));
   // Resolved 3D flow-combed cirrus filaments (34.0 vs legacy 17.5) eliminate coarse 256x128 _HPHighDetail
   // low-res cloud blotches at 36-55% unfold while keeping zero extra texture fetches or samplers.
   float4 tapA0=tex3Dlod(_CloudDetailNoise,float4(cRotA*34.0+float3(0,0,altZ*.26)+(ice.b-.5)*.26,cirrusMip));
   float4 tapA1=tex3Dlod(_CloudDetailNoise,float4(cLicA*34.0+float3(0,0,altZ*.26)+(ice.b-.5)*.26,cirrusMip));
   float4 tapB0=tex3Dlod(_CloudDetailNoise,float4(cRotB*34.0+float3(0,0,altZ*.26)+(ice.b-.5)*.26,cirrusMip));
   float4 tapA=lerp(tapA0,tapB0,hiBlend);
   float4 tapB=lerp(tapA1,tapB0,hiBlend*.5);
   float streakFine=lerp((tapA.a+tapB.a)*.5,.5,smoothstep(.45,1.45,cirrusMip));
   float streakA=(tapA.b+tapB.b)*.5;
   float streakB=(tapA.g+tapB.g)*.5;
   float streakBase=(tapA.r+tapB.r)*.5;
   fine=saturate(streakA*.46+streakB*.30+streakFine*.12+streakBase*.12);
   float hiDetailFade=1.0-smoothstep(1.8,3.6,cirrusMip);
   float hiResBlend=closeUp*hiDetailFade*.85;
   cirrusCell=lerp(ice.r,saturate(streakBase*.58+streakB*.42),hiResBlend);
   cirrusWisp=lerp(ice.g,saturate(streakA*.68+streakFine*.32),hiResBlend);
  }
  float highMod=lerp(.24+.96*smoothstep(.18,.84,fine),.72+.36*fine,cdoSmooth*.62);
  float highCover=highWeather*highMod;
  float highUnderLowSoft=lerp(1.0,lerp(.72,1.0,cdoSmooth),closeUp*smoothstep(.28,.75,coverage));
  high=OrbisHPHighDensity(z,.16+.74*highCover,.36,lerp(.56+.44*cirrusCell,.83+.17*cirrusCell,cdoSmooth*.60),cirrusWisp)*highCover*1.14*highUnderLowSoft;
 }
 return float2(low,high)*_Density*max(0,_Cover);
}
float2 ScrollClimateDensities(float3 p,bool simpleMode)
{
 return ScrollClimateDensitiesMip(p,simpleMode,0.0,0.0);
}
#endif
