#ifndef ORBIS_SCROLL_CLOUD_SOURCE
#define ORBIS_SCROLL_CLOUD_SOURCE
#include "ScrollHeight.cginc"
#include "FlatCycloneWind.cginc"
#include "../OrbisHPVolumeCloud.cginc"
sampler2D _ScrollWeatherTex, _ScrollCloudWindTex;
UNITY_DECLARE_TEX2D(_ScrollHeightTex);
float4 _ScrollSourceSize;
float _ScrollWeatherTime;
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
 float2 texel=1/max(float2(1,1),_ScrollSourceSize.xy);
 float2 centre=(base+.5)*texel,dx=float2(texel.x,0),dy=float2(0,texel.y);
 return ScrollInterpolatedRadius(ScrollHeightAt(centre),ScrollHeightAt(centre+dx),
                                 ScrollHeightAt(centre+dy),ScrollHeightAt(centre+dx+dy),fraction,uv,_ScrollSourceSize.y);
}
float ScrollCloudGroundFast(float2 uv)
{
 if(_ScrollHeightScale<=0)return 1;
 return ScrollRadius(ScrollSurfaceHeight(ScrollHeightAt(uv)),uv,_ScrollSourceSize.y);
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
float2 ScrollClimateDensitiesMip(float3 p,bool simpleMode,float pixelFootprint)
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
 float phase=frac(_ScrollWeatherTime*.016);
 // Soft-cap advection speed and damp linear dual-phase split inside active cyclones (where
 // ApplyTyphoon3DStadium already rotates 3D clouds via rotPhase) so primary & simpleMode
 // LightField stay 100% phase-locked with zero double-exposure ghosting or phantom gray craters!
 float3 advWind=localWind*rsqrt(1.0+dot(localWind,localWind)*200000.0);
 float cycFlowKeep=1.0;
 if(hasCyclone)
 {
  if(_OrbisCycloneNorth.w>.20)
   cycFlowKeep*=smoothstep(.45,1.25,length(up-_OrbisCycloneNorth.xyz)/FlatCycloneRadius(_OrbisCycloneNorth.w));
  if(_OrbisCycloneSouth.w>.20)
   cycFlowKeep*=smoothstep(.45,1.25,length(up-_OrbisCycloneSouth.xyz)/FlatCycloneRadius(_OrbisCycloneSouth.w));
 }
 advWind*=cycFlowKeep;
 float3 a=normalize(up-advWind*phase*8);
 float3 b=normalize(up+advWind*(1-phase)*8);

 float cdoSmooth=0;
 if(hasCyclone)
 {
  ApplyTyphoon3DStadium(up,z,_OrbisCycloneNorth,1.0,_OrbisCyclonePhase.x,coverage,highWeather,convection,cdoSmooth,a,b);
  ApplyTyphoon3DStadium(up,z,_OrbisCycloneSouth,-1.0,_OrbisCyclonePhase.y,coverage,highWeather,convection,cdoSmooth,a,b);
 }

 if(coverage+highWeather<.006)return 0;
 // Physically matched trilinear 3D mip levels from view-ray pixel footprint (prevents Nyquist grain & moving moire).
 float fp=max(0.0,pixelFootprint);
 float macroMip=fp>1e-7?clamp(log2(max(1.0,fp*6.4*128.0))-.45,0.0,4.5):0.0;
 float bodyMip=fp>1e-7?clamp(log2(max(1.0,fp*30.0*128.0))-.45,0.0,5.0):0.0;
 float detailMip=fp>1e-7?clamp(log2(max(1.0,fp*112.0*64.0)),0.0,5.5):0.0;
 float cirrusMip=fp>1e-7?clamp(log2(max(1.0,fp*17.5*64.0))-.30,0.0,4.5):0.0;

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
   // Gentle downwind anvil shear (clamped so strong cyclone winds never smear 3D cauliflower tops into broom streaks!)
   float anvilShear=.80+1.15*smoothstep(.58,.96,convection)*smoothstep(1.05,1.65,altitude);
   float3 windShear=mul(frame,localWind)*(altitude*anvilShear);
   float3 q0=mul(frame,a);

   float shape=0,worleyMeso=.5,effCov=coverage,scCobbleGain=1.0;
   float4 erosion=float4(.68,.68,.42,.42);
   bool evalLow=false;
   if(simpleMode)
   {
    // Phase-locked single-fetch simpleMode with trilinear mip filtering (matches primary advection without extra 3D fetch cost).
    float3 qS=mul(frame,normalize(up-advWind*(phase-.5)*4.0));
    float3 bPos=(cycFlowKeep<.05?q0:qS)*30.0+radAxis*(altitude*1.12)+windShear+float3(.41,.19,.83);
    float4 body=tex3Dlod(_CloudNoise,float4(bPos,min(5.5,bodyMip+.65)));
    worleyMeso=body.g*.56+body.b*.34+body.a*.10;
    float baseS=body.r*.68+worleyMeso*.32;
    baseS=lerp(baseS,.68+.32*baseS,cdoSmooth*.44);
    worleyMeso=lerp(worleyMeso,.66+.34*worleyMeso,cdoSmooth*.42);
    erosion=float4(.68,.68,.44,.44);
    shape=saturate((baseS-(1.0-worleyMeso)*.16*(1.0-.40*cdoSmooth))/.84+coverage*.06+.10*smoothstep(.06,.52,coverage));
    effCov=saturate(coverage+.06);
    evalLow=true;
   }
   else
   {
    // Variance-preserving dual-phase flow blend: keeps crisp cauliflower tops & unbroken slender curved bands!
    float invStd=lerp(1.0,rsqrt(max(.5,phase*phase+(1.0-phase)*(1.0-phase))),cycFlowKeep);
    float3 q1=mul(frame,b);

    float3 mPos0=q0*6.4+radAxis*(altitude*.34)+windShear*.40+float3(.13,.71,.37);
    float4 macroRaw=tex3Dlod(_CloudNoise,float4(mPos0,macroMip));
    [branch] if(cycFlowKeep>.01)
    {
     float3 mPos1=q1*6.4+radAxis*(altitude*.34)+windShear*.40+float3(.13,.71,.37);
     macroRaw=lerp(macroRaw,tex3Dlod(_CloudNoise,float4(mPos1,macroMip)),phase);
    }
    float4 macroBody=saturate(.5+(macroRaw-.5)*lerp(1.0,invStd,.65));

    float3 mesoWarp=(macroBody.gba-.5)*.06;
    float3 bPos0=q0*30.0+radAxis*(altitude*1.12)+windShear+mesoWarp+float3(.41,.19,.83);
    float4 bodyRaw=tex3Dlod(_CloudNoise,float4(bPos0,bodyMip));
    [branch] if(cycFlowKeep>.01)
    {
     float3 bPos1=q1*30.0+radAxis*(altitude*1.12)+windShear+mesoWarp+float3(.41,.19,.83);
     bodyRaw=lerp(bodyRaw,tex3Dlod(_CloudNoise,float4(bPos1,bodyMip)),phase);
    }
    float4 body=saturate(.5+(bodyRaw-.5)*lerp(1.0,invStd,.78));

    worleyMeso=body.g*.56+body.b*.34+body.a*.10;
    float basePW=macroBody.r*.28+body.r*.44+worleyMeso*.28;
    // Keep inner eyewall smooth and creamy while letting outer eyewall & rainbands express puffy 3D billows
    basePW=lerp(basePW,.68+.32*basePW,cdoSmooth*.44);
    worleyMeso=lerp(worleyMeso,.66+.34*worleyMeso,cdoSmooth*.42);
    // Keep soft synoptic mist envelopes connected while 3D fine detail sculpts lacy micro-structure
    float mistCohesion=.12*smoothstep(.06,.52,coverage);
    shape=saturate((basePW-(1.0-worleyMeso)*.16*(1.0-.40*cdoSmooth))/.84+coverage*.06+mistCohesion);

    float covBound=.16+.84*saturate(coverage+.16);
    if(pow(shape,.68)>.86-covBound||coverage>.52||inMidBand)
    {
     float detailFade=1.0-smoothstep(2.2,3.8,detailMip);
     float4 eroNeutral=float4(.60,.60,.50,.50);
     if(detailFade>.02)
     {
      float3 curl=(body.gba-.5)*.22;
      float3 dPos0=q0*112.0+radAxis*(altitude*2.60)+windShear*.62+curl;
      float4 eroRaw=tex3Dlod(_CloudDetailNoise,float4(dPos0,detailMip));
      [branch] if(cycFlowKeep>.01&&detailMip<2.6)
      {
       float3 dPos1=q1*112.0+radAxis*(altitude*2.60)+windShear*.62+curl;
       eroRaw=lerp(eroRaw,tex3Dlod(_CloudDetailNoise,float4(dPos1,detailMip)),phase);
      }
      // Damp contrast amplification as mip rises so sub-pixel erosion never aliases into grain/moire
      float eroBoost=lerp(1.0,invStd,.85*detailFade);
      erosion=lerp(eroNeutral,saturate(.5+(eroRaw-.5)*eroBoost),detailFade);
     }
     else
     {
      erosion=eroNeutral;
     }

     // True 3D Voronoi Marine Stratocumulus (Closed-Cell Cobblestone Sheets + Lacy Open-Cell Actiniform Rings!):
     // Smooth C1 weighted blend across marine Sc, post-frontal cold-air open cells, and trade cumulus (ZERO seams!)
     float marineScMask=1.0-smoothstep(.08,.18,convection);
     float openWakeMask=smoothstep(.10,.17,convection)*(1.0-smoothstep(.20,.30,convection));
     float tradeCuMask=smoothstep(.17,.26,convection)*(1.0-smoothstep(.35,.52,convection));
     float cellSum=marineScMask+openWakeMask*.90+tradeCuMask*.55;
     float cellActive=saturate(cellSum)*(1.0-cdoSmooth);
     if(cellActive>.01)
     {
      float wClosed=body.b+(erosion.r-.5)*.20;
      float closedCobble=smoothstep(.34,.66,wClosed)*(.76+.24*smoothstep(.18,.72,erosion.g));
      float cellWall=1.0-(body.b*.76+body.g*.24);
      float openRing=smoothstep(.36,.72,cellWall*.73+erosion.g*.41)*(.54+.46*smoothstep(.18,.68,erosion.r*.58+erosion.g*.42));
      float finePopcorn=saturate(.12+.88*smoothstep(.26,.74,erosion.r*.52+erosion.g*.48));

      float pocOpen=saturate(smoothstep(.24,.70,(1.0-coverage*.80)+(macroBody.g-.46)*1.22));
      float scPattern=lerp(closedCobble,openRing,pocOpen);
      float wakePattern=lerp(openRing,finePopcorn,.40);
      float tradePattern=lerp(finePopcorn,openRing,.36);
      float targetPattern=(marineScMask*scPattern+openWakeMask*.90*wakePattern+tradeCuMask*.55*tradePattern)/max(1e-4,cellSum);

      float scDominance=marineScMask/max(1e-4,cellSum);
      float sculptW=saturate(marineScMask*.94+openWakeMask*.80+tradeCuMask*.54)*(1.0-smoothstep(.72,.96,coverage)*(1.0-marineScMask*.82));
      float baseKeep=lerp(.40,.22,scDominance);
      float patGain=lerp(.72,.88,scDominance);
      shape=lerp(shape,saturate(shape*baseKeep+targetPattern*patGain),sculptW*lerp(.76,1.0,scDominance));
      worleyMeso=lerp(worleyMeso,targetPattern,sculptW*lerp(.72,.92,scDominance));
      scCobbleGain=lerp(1.0,1.52,marineScMask*(1.0-pocOpen*.55)*closedCobble);
     }
     erosion=lerp(erosion,float4(.68,.68,.52,.52),cdoSmooth*.28);

     float edgeInvW=1.0-(erosion.r*.62+erosion.g*.38);
     // Mean-preserving mip attenuation: removes sub-pixel noise at orbit without inflating cloud area into clear gyres!
     float edgeInvMean=lerp(edgeInvW,.44,smoothstep(.9,2.4,detailMip));
     float edgeTear=edgeInvMean*.138*(1.0-smoothstep(.58,.90,coverage))*smoothstep(.02,.22,coverage)*(1.0-.78*cdoSmooth);
     effCov=saturate(coverage-edgeTear);
     evalLow=true;
    }
   }
   if(evalLow)
   {
    float tower=smoothstep(.45,.96,convection);
    // Volumetric optical depth curve + Cahalan sub-pixel clumping curve for orbital mipmaps:
    // close-up (bodyMip < 0.55) stays 100% untouched; at orbital distances where 3D mipmaps smooth
    // broken cumulus puffs into their mean, Jensen's inequality (E[exp(-tau)] > exp(-E[tau])) requires
    // delaying denseCore saturation on mid-coverage broken clouds (effCov 0.34..0.72) while keeping
    // clear gyres open and thin-cloud veils luminous (dense storm cores protected via stormProtect).
    float mipBlur=smoothstep(.55,2.35,bodyMip);
    float stormProtect=saturate(cdoSmooth*1.4);
    float orbitM=mipBlur*(1.0-.90*stormProtect);
    float thinMist=lerp(.030,.24,smoothstep(.04,.48,effCov))*lerp(1.0,.66,orbitM*(1.0-smoothstep(.04,.28,effCov)));
    float coreExp=lerp(1.40,2.95,orbitM);
    float coreScale=lerp(1.24,.84,orbitM*(1.0-smoothstep(.45,.88,effCov)));
    float denseCore=coreScale*pow(smoothstep(.34,.94,effCov),coreExp);
    float opticalGain=(thinMist+denseCore)*lerp(.87,1.22,tower)*scCobbleGain;
    low=OrbisHPLowDensityDetailed(z,.14+.86*effCov,convection,shape,erosion,.42+.58*worleyMeso)*opticalGain;
   }
  }
 }
 if(highWeather>.004&&z>6950)
 {
  float4 ice=tex2Dlod(_HPHighDetail,float4(uv,0,0));
  float fine=.52;
  if(!simpleMode)
  {
   float s,c;sincos(_ScrollWeatherTime*.0018,s,c);
   float3 upper=float3(c*up.x-s*up.z,up.y,s*up.x+c*up.z);
   float wLen=length(localWind);
   float wActive=smoothstep(.0005,.004,wLen);
   float3 dynAlong=wLen>1e-5?(localWind/wLen):float3(.84,.25,.48);
   float3 dynCross=normalize(cross(up,dynAlong)+float3(1e-5,0,0));
   float3 axisAlong=normalize(lerp(float3(.84,.25,.48),dynAlong,wActive));
   float3 axisCross=normalize(lerp(float3(-.50,.05,.86),dynCross,wActive));
   float curve=(ice.b-.5)*.85+(ice.a-.5)*.55;
   float fallHook=(z-8400)*.00020;
   float altZ=(z-7200.0)/5600.0;
   float3 uSwirl=normalize(upper+axisCross*(curve*.032)+axisAlong*fallHook);
   float3 uLic=normalize(uSwirl+axisAlong*.016);
   const float3x3 cFrame=float3x3(.48,.64,.60,-.80,.60,0,.36,.48,-.80);
   float3 cRot0=mul(cFrame,uSwirl);
   float3 cRot1=mul(cFrame,uLic);
   float4 tapA=tex3Dlod(_CloudDetailNoise,float4(cRot0*17.5+float3(0,0,altZ*.92)+(ice.b-.5)*.28,cirrusMip));
   float4 tapB=tex3Dlod(_CloudDetailNoise,float4(cRot1*17.5+float3(0,0,altZ*.92)+(ice.b-.5)*.28,cirrusMip));
   float streakFine=(tapA.a+tapB.a)*.5;
   float streakA=(tapA.b+tapB.b)*.5;
   float streakB=(tapA.g+tapB.g)*.5;
   fine=saturate(streakA*.48+streakB*.28+streakFine*.16+tapA.r*.08);
  }
  float highMod=lerp(.26+.94*smoothstep(.15,.84,fine),.72+.36*fine,cdoSmooth*.62);
  float highCover=highWeather*highMod;
  high=OrbisHPHighDensity(z,.16+.74*highCover,.36,lerp(.56+.44*ice.r,.83+.17*ice.r,cdoSmooth*.60),ice.g)*highCover*1.16;
 }
 return float2(low,high)*_Density*max(0,_Cover);
}
float2 ScrollClimateDensities(float3 p,bool simpleMode)
{
 return ScrollClimateDensitiesMip(p,simpleMode,0.0);
}
#endif
