#ifndef ORBIS_ATMOSPHERE_INTEGRATION
#define ORBIS_ATMOSPHERE_INTEGRATION
#include "FlatWorld/Markley.cginc"
float _MarkleyMap;
#include "UnityCG.cginc"
#include "OrbisOptics.cginc"
sampler2D _MainTex;
UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
float4 _MainTex_TexelSize,_VolumeTexelSize;
#include "OrbisAtmosphereView.cginc"
#include "OrbisHPVolumeCloud.cginc"
int _CloudSteps,_AirSteps,_LightSteps,_CloudDebug;
float _CloudRefinement,_CloudLightSimple;
float _CloudMS,_ScatteringStrength,_MapVolume;
float _ScrollVolumeWeight;
float _LocalCloudMap,_CloudCameraHeight,_CloudViewBlend,_CloudProjectionBlend;
float _CloudTemporalJitter,_CloudTemporalActive;
float3 _CloudLocalUp,_CloudLocalEast,_CloudLocalNorth;

float3 _MapBL,_MapBR,_MapTL,_MapTR;

// ============================================================================
// CARBON + V3 PORT ISOLATED HELPERS (Light Cone, Shadow Fast-Path, Adaptive Step)
// ============================================================================
// Compile-time simpleMode=true helper for LightField & empty-space probe so HLSL
// dead-code-eliminates dual-phase detail erosion inside the inner light loop.
float2 OrbisCarbonLightDensities(float3 p,float pixelFootprint,float rayFootprint)
{
 if(_Clouds<.5||length(p)<ORBIS_CLOUD_BASE||length(p)>ORBIS_CLOUD_TOP)return 0;
 #ifdef ORBIS_SHARED_SCROLL_WEATHER
 if(_ScrollCloudSource>.5)return ScrollClimateDensitiesMip(p,true,pixelFootprint,rayFootprint);
 #endif
 return OrbisCloudDensitiesMip(p,true,pixelFootprint,rayFootprint);
}

// Fast legacy shadow-atlas lookup without inlining the 32-step OrbisCloudShadowDirect megakernel
// inside the main regional-weather view-ray loop (cuts MAP_VOLUME / MRT_VOLUME DXBC size ~40%).
float OrbisCarbonLegacyCloudShadow(float3 p,float3 sun)
{
 if(_Clouds<.5||OrbisSunVisible(p,sun)<.5)return 1.0;
 if(_OrbisShadowReady>=.5)
 {
  float2 xy=float2(dot(p,_OrbisShadowRight),dot(p,_OrbisShadowUp));
  float impact2=dot(xy,xy),outer2=ORBIS_CLOUD_TOP*ORBIS_CLOUD_TOP;
  if(impact2>=outer2)return 1.0;
  float top=sqrt(max(0.0,outer2-impact2));
  float inner2=ORBIS_CLOUD_BASE*ORBIS_CLOUD_BASE;
  float bottom=impact2<inner2?sqrt(max(0.0,inner2-impact2)):-top;
  float z=dot(p,_OrbisShadowSun);
  if(z>=top)return 1.0;
  bool farSide=z<0.0&&impact2<inner2&&length(p)>ORBIS_CLOUD_BASE;
  if(!farSide)
  {
   float layer=saturate((top-z)/max(1e-5,top-bottom))*7.0;
   float lo=floor(layer),hi=min(7.0,lo+1.0);
   float2 uv=xy/(2.0*ORBIS_CLOUD_TOP)+.5;
   float tau=lerp(OrbisShadowSlice(uv,lo),OrbisShadowSlice(uv,hi),layer-lo);
   return exp(-min(max(0.0,tau),16.0));
  }
 }
 #ifdef ORBIS_SHARED_SCROLL_WEATHER
 if(_ScrollCloudSource>.5)return 1.0;
 #endif
 return OrbisCloudShadowDirect(p,sun);
}

#ifdef ORBIS_SHARED_SCROLL_WEATHER
UNITY_DECLARE_TEX2D_NOSAMPLER(_ScrollCloudColumn);
float4 _ScrollCloudColumn_TexelSize;

float2 ScrollColumnTau(float3 p,int deck,float broadLod)
{
 float2 uv=OrbisFastWeatherUV(p);
 #ifdef UNITY_SEPARATE_TEXTURE_SAMPLER
 float2 opacity=UNITY_SAMPLE_TEX2D_SAMPLER_LOD(_ScrollCloudColumn,_ScrollHeightTex,uv,0).rg;
 float2 nearby=UNITY_SAMPLE_TEX2D_SAMPLER_LOD(_ScrollCloudColumn,_ScrollHeightTex,uv,broadLod).rg;
 #else
 float2 opacity=tex2Dlod(_ScrollCloudColumn,float4(uv,0,0)).rg;
 float2 nearby=tex2Dlod(_ScrollCloudColumn,float4(uv,0,broadLod)).rg;
 #endif
 return -log(max(1.0/255.0,1.0-(deck==0?float2(opacity.r,nearby.r):float2(opacity.g,nearby.g))));
}
float2 ScrollCloudSunTransmission(float3 p,float3 sun)
{
 float z=dot(p,sun);
 float3 axis=p-z*sun;
 float impact2=dot(axis,axis);
 float outer=1+4000*max(0,_ScrollHeightScale)+13500*_CloudHeightScale;
 if(impact2+max(z,0)*max(z,0)>=outer*outer)return 1;
 float reference=1+6000*_CloudHeightScale;
 float halfChord=sqrt(max(0,reference*reference-impact2));
 float broadLod=clamp(log2(max(1,_ScrollCloudColumn_TexelSize.z/80)),0,4);
 float2 tau=0;
 [loop] for(int side=z<0?-1:1;side<=1;side+=2)
 {
  float sampleZ=max(z,side*halfChord);
  float2 columnUV=OrbisFastWeatherUV(axis+sampleZ*sun);
  float4 weather=tex2Dlod(_ScrollWeatherTex,float4(columnUV,0,0));
  if(weather.r+weather.g>.006)
  {
   float base=ScrollCloudGroundFast(columnUV);
   float2 lowSpan=OrbisCloudVerticalSpan(weather.b,weather.r);
   float iceCover=pow(.24+.63*saturate(weather.g),.65);
   float4 layerHeights=float4(lowSpan.x,max(lowSpan.y,weather.b>.30&&weather.b<.74?5950.0:lowSpan.y),
                              8350-1150*iceCover,9100+4050*iceCover);
   float4 radii=base+layerHeights*_CloudHeightScale;
   float4 chord=side*sqrt(max(0,radii*radii-impact2));
   [unroll] for(int deck=0;deck<2;deck++)
   {
    float deckCov=deck==0?weather.r:weather.g;
    float2 ends=deck==0?chord.xy:chord.zw;
    float start=max(z,min(ends.x,ends.y)),span=max(ends.x,ends.y)-start;
    if(span>0&&deckCov>.005)
    {
     float3 mid=axis+(start+.5*span)*sun;
     float2 column;
     if(span>.035&&_CloudLightSimple<.5)
     {
      float3 offset=(.288675*span)*sun;
      column=.5*(ScrollColumnTau(mid-offset,deck,broadLod)+ScrollColumnTau(mid+offset,deck,broadLod));
     }
     else column=ScrollColumnTau(mid,deck,broadLod);
     float thickness=deck==0?layerHeights.y-layerHeights.x:layerHeights.w-layerHeights.z;
     tau+=column*span/max(1e-6,thickness*_CloudHeightScale);
    }
   }
  }
 }
 return exp(-min(16,tau*max(0,_Density*_Cover)));
}
float ScrollMoistAerosol(float3 p,float cloudDensity,float litOpening)
{
 float2 uv=OrbisFastWeatherUV(p);
 float ground=ScrollRadius(ScrollSurfaceHeight(ScrollHeightAt(uv)),uv,_ScrollSourceSize.y);
 float height=max(0,length(p)-ground);
 float4 weather=tex2Dlod(_ScrollWeatherTex,float4(uv,0,0));
 float moisture=smoothstep(.10,.64,weather.r)*(.5+.5*weather.b);
 return max(moisture,litOpening)*exp2(-height/max(1e-6,5200*_CloudHeightScale))*(1-smoothstep(.005,.14,cloudDensity));
}
#endif

float HG(float cosine,float g)
{
 return (1-g*g)/pow(max(.06,1+g*g-2*g*cosine),1.5);
}

// PlanetVolumetricCloudsV3 + HanPi cone-sampled single-loop LightField:
//   - Smooth volumetric cone footprint (lightFoot + segRayFoot) eliminates binary direct-light confetti dots!
//   - Avoids hard OrbisSunVisible binary shelf on twilight cloud tops (AirSunTransmittance handles limb extinction smoothly).
//   - Adaptively reduces light steps deep inside opaque foreground clouds (maxTrans < 0.16) to cut dense-cloud P95 cost.
//   - phi_fwd (fields.y) strictly contributes additive diffuse radiance; never writes to density or alpha.
float2 LightField(float3 p,float3 sun,float density,float baseFootprint,float localCloudTop,float viewTrans,float sunPeak)
{
 // Immediate physical planet-shadow & night-side skip (prevents twilight terminator leak and halves globe/coast light cost!)
 if(sunPeak<4e-4||OrbisSunVisible(p,sun)<.5)return float2(30.0,0);
 float rP=length(p);
 float sinSun=dot(p/max(1e-6,rP),sun);

 float lightTop=ORBIS_CLOUD_TOP;
 float actualSlab=max(.012,13500.0*max(1e-7,_CloudHeightScale));
 #ifdef ORBIS_SHARED_SCROLL_WEATHER
 if(_ScrollCloudSource>.5)
  lightTop=min(ORBIS_CLOUD_TOP,max(localCloudTop,rP+5200.0*max(1e-7,_CloudHeightScale)));
 #endif
 float lengthToExit=max(0.0,OrbisSphere(p,sun,lightTop).y);
 float2 inner=OrbisSphere(p,sun,ORBIS_CLOUD_BASE);
 if(inner.x<=inner.y&&inner.x>0)lengthToExit=min(lengthToExit,inner.x);
 #ifdef ORBIS_SHARED_SCROLL_WEATHER
 if(_ScrollCloudSource>.5)
  lengthToExit=min(lengthToExit,actualSlab*1.35);
 #endif
 if(lengthToExit<=0.0)return float2(0.0,0);

 bool allowEarlyOut=_CloudLightSimple>.5;
 int lightSteps=clamp(_LightSteps,1,8);
 if(allowEarlyOut&&_CloudDebug==0)
 {
  // Cone-filtered sun march resolves smooth 3D self-shadowing in <=3 steps at Balanced q=1, tapering inside dense cores
  if(lightSteps<=4)lightSteps=min(lightSteps,3);
  if(viewTrans<.22)lightSteps=1;
  else if(viewTrans<.52)lightSteps=min(lightSteps,2);
 }

 float growth=lerp(2.50,1.95,saturate((lightSteps-3.0)/5.0));
 float denom=max(1e-4,pow(growth,lightSteps)-1.0);
 float width=lengthToExit*(growth-1.0)/denom;
 float entry=0,totalTau=0,diffuse=0;
 bool multiple=_CloudMS>=.5;
 float boundary=multiple?OrbisHPBoundaryConfidence(density)*saturate((sinSun+.35)/1.35):0;

 [loop] [fastopt] for(int j=0;j<8;j++)
 {
  if(j>=lightSteps)break;
  float tail=allowEarlyOut?(1.0-smoothstep(2.6,4.2,totalTau)):1.0;
  if(tail<=1e-3)break;
  float t=entry+width*.5;
  // Wide volumetric sun-cone footprint (~850m near-field start) eliminates residual direct-light mottling/dots!
  // IMPORTANT: pass allowEarlyOut directly (never use HLSL ?: which eagerly evaluates both simple & full 3D paths!)
  float lightFoot=max(baseFootprint*1.75,.00128+t*.52);
  float segRayFoot=width*.90;
  float2 lightDensity=OrbisCloudDensitiesMip(p+sun*t,allowEarlyOut,lightFoot,segRayFoot);
  float sampleTau=(lightDensity.x+lightDensity.y)*ORBIS_EXTINCTION*width*tail;
  if(multiple)
   diffuse+=OrbisHPDiffuseStep(totalTau,sampleTau,t,width,boundary);
  totalTau+=sampleTau;
  entry+=width;
  width*=growth;
 }
 if(!multiple)return float2(totalTau,0);
 float thinSuppression=1-exp(-totalTau*1.10);
 diffuse=OrbisHPCompressDiffuse(diffuse)*thinSuppression;
 return float2(totalTau,diffuse);
}

float OrbisCachedPhi(float opticalDepth,float density)
{
 if(_CloudMS<.5)return 0;
 float tau=clamp(opticalDepth,0,24),ds=tau/6,phi=0;
 float boundary=OrbisHPBoundaryConfidence(density);
 [loop] [fastopt] for(int i=0;i<6;i++)
 {
  float distance=(i+.5)*.025;
  phi+=OrbisHPDiffuseStep(i*ds,ds,distance,.025,boundary);
 }
 float thin=1-exp(-tau*1.2);
 return OrbisHPCompressDiffuse(phi)*thin;
}

struct OrbisVolumeResult {float3 light;float3 transmission;float sceneDepth;float cloudDepth;};
float2 OrbisDepthUV(float2 uv)
{
 #if UNITY_UV_STARTS_AT_TOP
 if(_MainTex_TexelSize.y<0)uv.y=1-uv.y;
 #endif
 return uv;
}

void OrbisIntegrateSample(float3 p,float3 rd,float ds,float2 cloudLayers,float3 sun,float mu,float2 phaseAir,float moistPhase,
                          float phaseCloud,float phaseMid,float phaseHigh,float2 phaseMoon,float phaseMoonCloud,float footprint,float shaftJitter,float localCloudTop,float localGroundR,bool skipShaft,
                          inout float3 light,inout float3 transmission)
{
 float3 rho=AirDensities(p);
 float cloudDensity=cloudLayers.x+cloudLayers.y;
 float3 sigma=AirExtinction(rho)+cloudDensity*ORBIS_EXTINCTION;
 if(max(sigma.r,max(sigma.g,sigma.b))<1e-6)return;
 float3 sunT=AirSunTransmittance(p,sun);
 float maxT=max(transmission.r,max(transmission.g,transmission.b));
 bool fastDense=_CloudLightSimple>.5;
 float rLen=length(p);

 float shadowT=1.0;
 float3 moistScatter=0;
 #ifdef ORBIS_SHARED_SCROLL_WEATHER
 if(_ScrollCloudSource>.5)
 {
  // Skip 2D shaft evaluation in upper stratosphere (>localCloudTop) and verified open cloudless ocean
  bool inShaftEnvelope=(!skipShaft)&&((!fastDense)||(rLen<localCloudTop-.0010&&ds<.0095&&maxT>.085));
  [branch] if(_GodRays>.5&&_Clouds>.5&&any(sunT>1e-5)&&inShaftEnvelope)
  {
   float shaftStep=min(ds,.0018)*(1.0-smoothstep(.0035,.0095,ds));
   float3 pShaft=(cloudDensity<.0005)?(p+rd*(shaftStep*(shaftJitter-.5)*.75)):p;
   float2 paths=ScrollCloudSunTransmission(pShaft,sun);
   shadowT=paths.x;
   float clearAir=1.0-smoothstep(.001,.026,cloudDensity);
   float opening=smoothstep(.014,.38,paths.x-paths.y)*smoothstep(.06,.76,paths.x);
   float forwardBeam=lerp(.65,1.55,smoothstep(.15,.86,mu));
   if(rLen<1.045&&clearAir>.01&&(opening>.004||!fastDense))
    moistScatter=ScrollMoistAerosol(pShaft,cloudDensity,opening)*clearAir*_AtmoBetaMie.rgb*
                ((.12+30.0*opening*forwardBeam)*max(0,_Haze)*max(_AirDensity,.32*saturate(_Atmosphere))*
                 saturate(_Atmosphere)*saturate(_ShadowStrength)*
                 smoothstep(.001,.08,max(sunT.r,max(sunT.g,sunT.b))));
  }
 }
 else
 #endif
 if(_GodRays>.5||(cloudDensity>.0005&&_OrbisShadowReady>.5))shadowT=OrbisCarbonLegacyCloudShadow(p,sun);

 float airShadow=_GodRays>.5?lerp(1.0,.18+.82*shadowT,_ShadowStrength):1.0;
 float3 source=OrbisGLSLSource(rho,phaseAir,sunT)*airShadow;
 sigma+=moistScatter*.28;
 float3 moistLight=moistPhase*sunT*(shadowT*max(0,_AtmoParameters.z)*max(0,_SolarEnergy));
 float moistPeak=max(moistLight.r,max(moistLight.g,moistLight.b));
 source+=moistScatter*moistLight/(1+.12*moistPeak);

 float nightW=OrbisNightWeight(p,sun);
 float nightTau=0;
 float3 moonT=0;
 if(nightW>1e-5)
 {
  nightTau=OrbisNightColumnTau(p);
  float3 nightAmbient=OrbisNightAmbient(p,sun,nightTau);
  moonT=OrbisMoonIncident(p,sun,nightTau);
  float3 airglow=OrbisAirglowEmission(p,sun);
  float3 moonSource=(rho.x*_AtmoBetaRayleigh.xyz*phaseMoon.x+
      rho.y*_AtmoBetaMie.xyz*max(0,_Haze)*phaseMoon.y)*moonT*1.8;
  source+=AirExtinction(rho)*nightAmbient*.35+moonSource+airglow;
 }

 if(cloudDensity>.0005)
 {
  float sunPeak=max(sunT.r,max(sunT.g,sunT.b));
  float2 fields;
  bool useCachedShadow=_OrbisShadowReady>.5;
  if(useCachedShadow)
  {
   float cachedTau=-log(max(shadowT,1e-6));
   fields=float2(cachedTau,OrbisCachedPhi(cachedTau,cloudDensity));
  }
  else if(fastDense&&_CloudDebug==0&&cloudDensity<.0045)
  {
   // Ultra-tenuous outer vapor veil (<0.3% step opacity): analytical self-extinction skips 3D LightField texture fetches!
   fields=float2(sunPeak<4e-4?30.0:(cloudDensity*26.0),0.0);
  }
  else
   fields=LightField(p,sun,cloudDensity,footprint,localCloudTop,maxT,sunPeak);

  float sunTau=fields.x;
  float sinElev=dot(p/max(1e-6,rLen),sun);
  #ifdef ORBIS_SHARED_SCROLL_WEATHER
  if(_ScrollCloudSource>.5&&_GodRays>.5&&!useCachedShadow)
  {
   float lowSunCast=1.0-smoothstep(.03,.18,sinElev);
   sunTau+=(1.0-shadowT)*saturate(_ShadowStrength)*lowSunCast*.95;
  }
  #endif

  // PlanetVolumetricCloudsV3 continuous Crown -> Mid-Body -> Base vertical height fraction:
  // Normalizes low/cumulus height across the actual 600-3,800 m convective deck above local terrain
  // (instead of 13,200 m sea-level ceiling, which previously compressed low clouds into a flat 0.04..0.18 band!).
  float highWeight=cloudLayers.y/max(cloudDensity,1e-6);
  float altAboveGround=max(0.0,rLen-localGroundR)/max(1e-7,_CloudHeightScale);
  float lowHFrac=saturate((altAboveGround-620.0)/3100.0);
  float highHFrac=saturate((altAboveGround-7400.0)/5200.0);
  float hFrac=lerp(lowHFrac,max(lowHFrac,highHFrac),highWeight);

  // Soft 3-octave multi-scattering Beer-Powder attenuation (PlanetVolumetricCloudsV3 / Hillaire):
  // preserves 5+ continuous tonal levels from snowy sunlit crowns -> creamy mid-body -> cool slate-blue bases!
  float phase0=lerp(phaseCloud,phaseHigh,highWeight);
  float t0=exp(-min(sunTau*.68,30.0));
  float t1=exp(-min(sunTau*.23,30.0));
  float t2=exp(-min(sunTau*.08,30.0));
  float heightLift=lerp(.82,1.14,smoothstep(.08,.88,hFrac));
  float directScatter=(t0*phase0*.56+t1*phaseMid*.29+t2*.64*.15)*heightLift;

  // Gentle angle-gated PowderEffect: soft volumetric cushion on sunlit edges while keeping backlit silver-linings bright
  float powderDark=saturate((1.0-exp(-cloudDensity*2.6))*1.45);
  float powder=lerp(1.0,lerp(1.0,powderDark,smoothstep(.45,-.55,mu)),.24*(1.0-highWeight*.75));
  float crownGlow=1.0+.14*smoothstep(.25,.90,hFrac)*smoothstep(.24,.84,t0);
  float3 direct=directScatter*(1.04*powder*crownGlow)*sunT;

  // Continuous vertical & optical AO + cool slate-blue shadow tint (PlanetVolumetricCloudsV3 _CloudTopAmbient / _CloudBottomAmbient)
  float opticalAO=exp(-min(sunTau,10.0)*max(.24,sinElev)*.26);
  float heightAO=lerp(.58,1.0,smoothstep(.02,.78,hFrac));
  float cloudAO=lerp(.46,1.0,saturate(opticalAO*.62+heightAO*.38));
  float litBlend=smoothstep(.04,.64,t0*.58+t1*.42);
  float3 shadowTint=lerp(float3(.64,.78,1.05),float3(1.0,1.0,1.0),litBlend);

  // HanPi phi_fwd isotropic diffuse field (strictly additive to radiance ONLY!)
  float depthPhi=lerp(.68,1.18,smoothstep(.04,.82,hFrac));
  float denseGate=smoothstep(.003,.055,cloudDensity);
  float3 multiple=fields.y*_CloudMS*_ScatteringStrength*1.08*cloudAO*depthPhi*shadowTint*sunT*denseGate;

  float3 nightCloud=0;
  if(nightW>1e-5)
  {
   float3 moonScatter=moonT*phaseMoonCloud*.65*(.25+.75*exp(-cloudDensity*.75));
   float3 nightDiffuse=OrbisNightAmbient(p,sun,0)*(.45+.55*exp(-nightTau*.10))*
       lerp(.70,1.30,exp(-cloudDensity*.75));
   nightCloud=nightDiffuse+moonScatter;
  }

  float3 verticalAmbient=lerp(float3(.66,.77,.96),float3(1.08,1.05,1.01),smoothstep(.04,.85,hFrac));
  float3 skyBounce=OrbisDaySkyFill(p,sun)*(1.10*cloudAO*shadowTint*verticalAmbient);
  float3 radiance=(direct+multiple+skyBounce)*max(.1,_SolarEnergy)+
      nightCloud+OrbisLightningLight(p)*3.5;

  float dayHemi=saturate(sinElev*2.2+.20);
  float3 ambientFloor=lerp(float3(.028,.044,.074),float3(.068,.084,.112),smoothstep(.04,.82,hFrac))*dayHemi;
  if(_Night<.5)radiance=max(radiance,ambientFloor);

  // Smooth highlight shoulder starting at 1.08 preserves silky snow-white crown gradation without flat clipping
  float peakR=max(radiance.r,max(radiance.g,radiance.b));
  float knee=peakR>1.08?(1.08+(peakR-1.08)/(1.0+(peakR-1.08)*.52))/peakR:1.0;
  radiance*=knee;

  if(_CloudDebug==1)radiance=cloudDensity.xxx;
  if(_CloudDebug==2)radiance=direct;
  if(_CloudDebug==3)radiance=multiple;
  if(_CloudDebug==4)radiance=sunT;
  if(_CloudDebug==5)radiance=float3(1-exp(-cloudDensity*3),cloudDensity*.3,.1);
  source+=min(float3(5,5,5),radiance)*cloudDensity*ORBIS_EXTINCTION;
 }
 OrbisGLSLAccumulate(source,sigma,ds,light,transmission);
}

float4 OrbisSortCuts4(float4 v)
{
 float4 pairs=float4(min(v.x,v.y),max(v.x,v.y),min(v.z,v.w),max(v.z,v.w));
 float4 crossed=float4(min(pairs.x,pairs.z),min(pairs.y,pairs.w),max(pairs.x,pairs.z),max(pairs.y,pairs.w));
 return float4(crossed.x,min(crossed.y,crossed.z),max(crossed.y,crossed.z),crossed.w);
}
float2 OrbisInterval(int index,float begin,float end,float4 cuts)
{
 float a=index==0?begin:index==1?cuts.x:index==2?cuts.y:index==3?cuts.z:cuts.w;
 float b=index==0?cuts.x:index==1?cuts.y:index==2?cuts.z:index==3?cuts.w:end;
 return float2(a,b);
}
float OrbisIGN(float2 pixelPos)
{
 return frac(52.9829189*frac(dot(floor(pixelPos),float2(0.06711056,0.00583715))));
}

OrbisVolumeResult OrbisMarchRayFootprintEx(float3 ro,float3 rd,float endDistance,float sceneDepth,float jitter,float pixelAngle,float baseFoot)
{
 OrbisVolumeResult result;result.light=0;result.transmission=1;result.sceneDepth=sceneDepth;result.cloudDepth=endDistance;
 float2 visibleRange=OrbisGLSLViewInterval(ro,rd,endDistance);
 float begin=visibleRange.x,end=visibleRange.y;
 if(end<=begin)return result;
 result.cloudDepth=end;
 float2 cloudOuter=OrbisSphere(ro,rd,ORBIS_CLOUD_TOP),cloudInner=OrbisSphere(ro,rd,ORBIS_CLOUD_BASE);
 float4 cuts=OrbisSortCuts4(clamp(float4(cloudOuter,cloudInner),begin,end));
 float cloudLength=0,airLength=0;
 [loop] [fastopt] for(int k=0;k<5;k++)
 {
  float2 interval=OrbisInterval(k,begin,end,cuts);
  float middle=length(ro+rd*((interval.x+interval.y)*.5));
  bool cloud=middle>ORBIS_CLOUD_BASE&&middle<ORBIS_CLOUD_TOP&&_Clouds>.5;
  if(cloud)cloudLength+=interval.y-interval.x;else airLength+=interval.y-interval.x;
 }
 float3 sun=normalize(_Sun);float mu=dot(rd,sun);
 float2 phaseAir=OrbisGLSLPhase(mu);
 float moistPhase=lerp(phaseAir.y,HG(mu,.76)/(4*ORBIS_PI),.76);
 float rawCloudPhase=.52*HG(mu,.78)+.26*HG(mu,.36)+.22*HG(mu,-.32)+.16;
 float phaseCloud=rawCloudPhase/(1.0+rawCloudPhase*.14);
 float phaseMid=min(2.4,.56*HG(mu,.36)+.44);
 float rawHighPhase=.56*HG(mu,.60)+.24*HG(mu,-.30)+.20;
 float phaseHigh=rawHighPhase/(1.0+rawHighPhase*.16);
 float moonMu=dot(rd,_OrbisMoon.xyz);
 float2 phaseMoon=OrbisGLSLPhase(moonMu);
 float phaseMoonCloud=min(2.0,.85*HG(moonMu,.45)+.15*HG(moonMu,-.15));

 float3 light=0,transmission=1;
 float cloudWeightSum=0,cloudDistSum=0;
 int nextInterval=0,sampleIndex=0,sampleCount=0;
 float segmentBegin=0,segmentEnd=0,cursor=0,ds=0,crustStart=0,grazing=0,nearCam=0,insideShell=0,closeCam=0,baseDescent=0;
 float localCloudTop=ORBIS_CLOUD_TOP,localGroundR=1.0,lastProbe=-1.0;
 bool segmentHasCloud=false,prevHit=false,skipShaft=false;
 float actualSlab=max(.012,13500.0*max(1e-7,_CloudHeightScale));
 float temporalPhase=frac(jitter+_CloudTemporalJitter);

 [loop] [fastopt] for(int traversal=0;traversal<1285;traversal++)
 {
  float maxTrans=max(transmission.r,max(transmission.g,transmission.b));
  if(maxTrans<(_CloudLightSimple>.5?.045:.002))break;
  if(sampleIndex>=sampleCount||cursor>=segmentEnd)
  {
   if(nextInterval>=5)break;
   float2 interval=OrbisInterval(nextInterval,begin,end,cuts);
   nextInterval++;
   float len=interval.y-interval.x;
   if(len<1e-7)continue;
   float middle=length(ro+rd*((interval.x+interval.y)*.5));
   bool cloud=middle>ORBIS_CLOUD_BASE&&middle<ORBIS_CLOUD_TOP&&_Clouds>.5;
   if(!cloud&&_Atmosphere<.5)continue;

   float camR=length(ro);
   float lowOrbit=1.0-smoothstep(1.38,1.72,camR);
   float unfoldColumn=smoothstep(.24,.68,_CloudViewBlend);
   nearCam=cloud?(1.0-smoothstep(1.08,1.44,camR)):0.0;
   closeCam=cloud?(1.0-smoothstep(1.32,1.90,camR)):0.0;
   // Restore fine insideShell below 3,000 m (camR < 1.0045) during wheel cloud-base descent so LCL exit never straddles!
   baseDescent=1.0-smoothstep(1.0022,1.0052,camR);
   insideShell=cloud?(1.0-smoothstep(1.04,1.12,camR))*lerp(1.0-unfoldColumn*.95,1.0,baseDescent):0.0;
   grazing=cloud?max(smoothstep(.058,.088,len)*lowOrbit,nearCam*(1.0-unfoldColumn*.75)):0.0;

   int budget=clamp(cloud?_CloudSteps:_AirSteps,1,64);
   if(cloud&&_CloudLightSimple>.5)
   {
    float orbitScale=lerp(.84,1.0,max(insideShell,grazing));
    float colRatio=clamp(len/max(1e-5,actualSlab*1.15),.44,.74);
    budget=max(10,(int)ceil(budget*lerp(orbitScale,colRatio,unfoldColumn)));
   }
   float total=cloud?cloudLength:airLength;
   sampleCount=clamp((int)ceil(budget*len/max(total,1e-6)),cloud?1:6,64);
   ds=len/sampleCount;
   float effRefine=cloud?(_CloudLightSimple>.5?lerp(lerp(1.28,1.15,max(unfoldColumn,1.0-closeCam)),clamp(_CloudRefinement,1,4),insideShell):clamp(_CloudRefinement,1,4)):1.0;
   sampleCount=cloud?max(sampleCount,(int)ceil(sampleCount*effRefine)):sampleCount;
   sampleIndex=0;segmentBegin=interval.x;segmentEnd=interval.y;cursor=segmentBegin;segmentHasCloud=cloud;skipShaft=!cloud;
   prevHit=false;crustStart=segmentBegin;lastProbe=-1.0;
   localCloudTop=ORBIS_CLOUD_TOP;
   localGroundR=1.0;
   #ifdef ORBIS_SHARED_SCROLL_WEATHER
   if(cloud&&_ScrollCloudSource>.5)
   {
    if(_ScrollHeightScale>0)
    {
     float gB=ScrollCloudGroundFast(OrbisFastWeatherUV(ro+rd*segmentEnd));
     float gMax=gB;
     [branch] if(len>.08)
     {
      float gA=ScrollCloudGroundFast(OrbisFastWeatherUV(ro+rd*segmentBegin));
      float gM=ScrollCloudGroundFast(OrbisFastWeatherUV(ro+rd*((segmentBegin+segmentEnd)*.5)));
      gMax=max(gMax,max(gA,gM));
     }
     localGroundR=gB;
     localCloudTop=min(ORBIS_CLOUD_TOP,gMax+actualSlab+.0015);
    }
    else localCloudTop=min(ORBIS_CLOUD_TOP,1.0+actualSlab+.0015);

    // One-time segment-entry stratosphere analytic step + 4-tap tropospheric clear-sky bypass:
    if(_CloudLightSimple>.5&&insideShell<.05)
    {
     float tClosest=clamp(-dot(ro,rd),segmentBegin,segmentEnd);
     if(length(ro+rd*tClosest)>localCloudTop+.0008)
     {
      segmentHasCloud=false;
      skipShaft=true;
      sampleCount=min(sampleCount,3);
      ds=len/max(1,sampleCount);
     }
     else
     {
      float tTropo=segmentBegin;
      if(length(ro+rd*segmentBegin)>localCloudTop+.0006)
      {
       float2 th=OrbisSphere(ro,rd,localCloudTop+.0004);
       tTropo=(th.x<=th.y)?clamp(th.x,segmentBegin,segmentEnd):segmentEnd;
      }
      float tropoLen=segmentEnd-tTropo;
      if(tropoLen<=.065)
      {
       bool hasC=(tropoLen>1e-5)&&(
        ScrollColumnMayHaveCloud(ro+rd*(tTropo+tropoLen*.15))||
        ScrollColumnMayHaveCloud(ro+rd*(tTropo+tropoLen*.40))||
        ScrollColumnMayHaveCloud(ro+rd*(tTropo+tropoLen*.66))||
        ScrollColumnMayHaveCloud(ro+rd*(tTropo+tropoLen*.90)));
       if(!hasC)
       {
        segmentHasCloud=false;
        bool hasSunC=(_GodRays>.5)&&ScrollColumnMayHaveCloud(ro+rd*(tTropo+tropoLen*.60)+sun*.014);
        skipShaft=!hasSunC;
        sampleCount=min(sampleCount,hasSunC?5:3);
        ds=len/max(1,sampleCount);
       }
      }
      // Integrate empty upper stratosphere [segmentBegin, tTropo] (>13.5 km) in 1 midpoint air step!
      if(segmentHasCloud&&(tTropo-segmentBegin)>.0035)
      {
       float stratLen=tTropo-segmentBegin;
       float stratMid=segmentBegin+stratLen*.5;
       OrbisIntegrateSample(ro+rd*stratMid,rd,stratLen,0,sun,mu,phaseAir,moistPhase,phaseCloud,phaseMid,phaseHigh,phaseMoon,phaseMoonCloud,baseFoot+stratMid*pixelAngle,jitter,localCloudTop,localGroundR,true,light,transmission);
       segmentBegin=tTropo;cursor=tTropo;crustStart=tTropo;
       sampleCount=max(6,(int)ceil(sampleCount*clamp((tropoLen/max(1e-5,len))*1.45,.42,.85)));
      }
     }
    }
   }
   #endif
  }

  float stepSize=min(ds,segmentEnd-cursor);float2 density=0;
  float sampleDistOverride=-1.0;
  if(segmentHasCloud)
  {
   float slab=ORBIS_CLOUD_TOP-ORBIS_CLOUD_BASE;
   float remSpan=segmentEnd-cursor;
   float minStep=remSpan/max(1,sampleCount-sampleIndex);
   // Keep minCap >= actualSlab/14 during top-down unfold (where cursor starts near 0 inside ORBIS_CLOUD_TOP!)
   float unfoldCol=smoothstep(.24,.68,_CloudViewBlend);
   float minCap=max(lerp(.00004,actualSlab/28.0,nearCam),unfoldCol*(actualSlab*.14));
   float cap=min(max(cursor*.125,minCap),lerp(slab/16,slab/2,pow(saturate((cursor-crustStart)/.08),2)));
   float3 pCur=ro+rd*cursor;
   float rProbe=length(pCur);
   float hiWeight=prevHit?0.0:smoothstep(localCloudTop-.0012,localCloudTop+.0045,rProbe);
   float lowBand=1.0-smoothstep(localCloudTop-actualSlab*.70,localCloudTop-actualSlab*.56,rProbe);
   float emptyBase=(grazing>.01&&!prevHit)?min(ds,actualSlab*lerp(.18,.085,grazing)):min(ds,actualSlab*.14);
   if(_CloudLightSimple>.5&&!prevHit&&insideShell<.1)
    emptyBase=max(emptyBase,min(actualSlab*.062,ds*1.6));
   float lowEmpty=lerp(emptyBase,min(emptyBase,actualSlab*.048),lowBand);
   if(!prevHit&&nearCam>.10&&_GodRays>.5)
    lowEmpty=lerp(lowEmpty,min(lowEmpty,actualSlab*.078),1.0-smoothstep(localCloudTop-.008,localCloudTop,rProbe));
   float emptyCap=lerp(lowEmpty,min(.012,max(ds*3.0,minStep)),hiWeight);
   float emptyTail=smoothstep((float)sampleCount-14.0,(float)sampleCount-6.0,(float)sampleIndex);
   float emptyFloor=lerp(lerp(minStep,min(minStep,actualSlab*.048),max(insideShell,lowBand*(1.0-emptyTail))),minStep,emptyTail);
   float large=min(remSpan,max(lerp(min(emptyCap,cap),emptyCap,hiWeight),emptyFloor));

   bool aboveCeiling=!prevHit&&rProbe>localCloudTop+.0006&&length(ro+rd*(cursor+large))>localCloudTop+.0006;
   bool hitProbe=false;
   float probeSum=0.0,probeDist=cursor+large*.5;
   if(!aboveCeiling)
   {
    float probeOffset=(!prevHit&&large<=actualSlab*.16)?large:(large*.5);
    probeDist=cursor+probeOffset;
    if(prevHit)probeDist=max(probeDist,min(segmentEnd,lastProbe+.00008));
    lastProbe=probeDist;
    float probeFoot=baseFoot+probeDist*pixelAngle;
    float2 probe=OrbisCarbonLightDensities(ro+rd*probeDist,probeFoot,large*.65);
    probeSum=probe.x+probe.y;
    hitProbe=probeSum>(_CloudLightSimple>.5?.0015:.0005);
   }
   if(hitProbe)
   {
    if(!prevHit)
    {
     crustStart=lerp(cursor,probeDist,saturate(1.0-probeSum*5.5));
     prevHit=true;
    }
    float refine=lerp(clamp(_CloudRefinement,1,4),1.25,unfoldCol);
    if(_CloudLightSimple>.5)refine=lerp(lerp(1.0,refine,insideShell*.5),refine,smoothstep(.12,.64,maxTrans));
    float baseFine=min(ds,cap)*rcp(refine);
    float nearWeight=saturate(max(grazing,closeCam*.65)*(1.0-unfoldCol*.65));
    float refineTier=saturate((_CloudRefinement-1.0)+.45);
    float tropoDiv=lerp(15.0,lerp(18.0,24.0,nearWeight),refineTier);
    float tropoFineCap=actualSlab/tropoDiv;
    float crustBoost=max(grazing,nearWeight*.45)*smoothstep(.20,.82,maxTrans);
    float crustCap=lerp(actualSlab/24.0,actualSlab/6.0,pow(saturate((cursor-crustStart)/.032),1.35));
    float fineStep=min(min(baseFine,tropoFineCap),lerp(baseFine,crustCap,crustBoost));

    if(_CloudLightSimple>.5)
    {
     float denseGrow=lerp(2.25,1.0,smoothstep(.08,.48,maxTrans));
     float maxAllowed=lerp(ds*1.85,ds,baseDescent);
     fineStep=min(maxAllowed,max(fineStep*denseGrow,min(ds,actualSlab*lerp(.026,.008,insideShell))));
    }
    float fineTail=smoothstep((float)sampleCount-14.0,(float)sampleCount-4.0,(float)sampleIndex);
    float allowFine=saturate(max(smoothstep(.02,.15,crustBoost),max(insideShell,closeCam*.55*(1.0-unfoldCol*.80))));
    float safeMinStep=minStep*lerp(1.0,fineTail,allowFine);
    stepSize=min(remSpan,max(fineStep,safeMinStep));

    float phaseOffset=_CloudTemporalActive>.5?((temporalPhase-.5)*.45):0.0;
    float sampleDistance=clamp(cursor+stepSize*(.5+phaseOffset),cursor+.05*stepSize,segmentEnd-.05*stepSize);
    sampleDistOverride=sampleDistance;
    float sampleFoot=baseFoot+sampleDistance*pixelAngle;
    float3 samplePos=ro+rd*sampleDistance;
    float radialCos=dot(normalize(samplePos),rd);
    float isoStride=stepSize*sqrt(1.0+.36*radialCos*radialCos);
    float opaqueBlur=(1.0-smoothstep(.12,.42,maxTrans))*actualSlab*.09;
    float rayFoot=min(actualSlab*.20,1.08*isoStride+opaqueBlur);
    density=OrbisCloudDensitiesMip(samplePos,false,sampleFoot,rayFoot);
   }
   else
   {
    prevHit=false;
    lastProbe=-1.0;
    stepSize=large;
    density=0;
   }
  }
  float sampleDist=sampleDistOverride>0.0?sampleDistOverride:(cursor+stepSize*.5);
  float3 p=ro+rd*sampleDist;
  float prevMaxT=max(transmission.r,max(transmission.g,transmission.b));
  OrbisIntegrateSample(p,rd,stepSize,density,sun,mu,phaseAir,moistPhase,phaseCloud,phaseMid,phaseHigh,phaseMoon,phaseMoonCloud,baseFoot+sampleDist*pixelAngle,jitter,localCloudTop,localGroundR,skipShaft,light,transmission);
  if(density.x+density.y>.0005)
  {
   float newMaxT=max(transmission.r,max(transmission.g,transmission.b));
   float wCloud=max(0.0,prevMaxT-newMaxT);
   cloudWeightSum+=wCloud;
   cloudDistSum+=wCloud*sampleDist;
  }
  cursor=min(segmentEnd,cursor+stepSize);
  sampleIndex++;
 }
 float volumeWeight=_ScrollCloudSource>.5?saturate(_ScrollVolumeWeight):1;
 result.light=light*volumeWeight;result.transmission=lerp(float3(1,1,1),transmission,volumeWeight);
 if(cloudWeightSum>1e-4)result.cloudDepth=cloudDistSum/cloudWeightSum;
 return result;
}
OrbisVolumeResult OrbisMarchRayFootprint(float3 ro,float3 rd,float endDistance,float sceneDepth,float jitter,float pixelAngle)
{
 return OrbisMarchRayFootprintEx(ro,rd,endDistance,sceneDepth,jitter,pixelAngle,0.0);
}
OrbisVolumeResult OrbisMarchRayJittered(float3 ro,float3 rd,float endDistance,float sceneDepth,float jitter)
{
 return OrbisMarchRayFootprintEx(ro,rd,endDistance,sceneDepth,jitter,0.0,0.0);
}
OrbisVolumeResult OrbisMarchRay(float3 ro,float3 rd,float endDistance,float sceneDepth)
{
 return OrbisMarchRayFootprintEx(ro,rd,endDistance,sceneDepth,.5,0.0,0.0);
}

float3 OrbisLocalCloudDirection(float2 uv)
{
 float2 map=lerp(lerp(_MapBL.xy,_MapBR.xy,uv.x),lerp(_MapTL.xy,_MapTR.xy,uv.x),uv.y);
 float2 xy=map*float2(2*ORBIS_PI,ORBIS_PI);
 return OrbisLocalDirection(_CloudLocalUp,_CloudLocalEast,_CloudLocalNorth,xy,_CloudProjectionBlend);
}
OrbisVolumeResult OrbisMarchPixel(float2 uv,float2 pixelPos)
{
 float3 ray=OrbisViewRay(uv);
 float depth=SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture,OrbisDepthUV(uv));
 float eye=OrbisEyeDepth(depth),distance=eye*length(ray);
 float3 ro=OrbisViewOrigin(uv),rd=normalize(ray);
 float2 planet=OrbisSphere(ro,rd,1.0);
 if(planet.x<=planet.y&&planet.x>0)distance=min(distance,planet.x);
 float2 texel=_VolumeTexelSize.x>1e-7?_VolumeTexelSize.xy:max(float2(1e-4,1e-4),_MainTex_TexelSize.xy);
 float3 rayDx=OrbisViewRay(uv+float2(texel.x,0))-ray;
 float3 rayDy=OrbisViewRay(uv+float2(0,texel.y))-ray;
 float pixelAngle=max(length(rayDx),length(rayDy))/max(1e-5,length(ray));
 float baseFoot=max(length(_OrbisRayOriginRight)*(2.0*texel.x),length(_OrbisRayOriginUp)*(2.0*texel.y));
 #ifdef ORBIS_SHARED_SCROLL_WEATHER
 if(_LocalCloudMap>.5&&_CloudViewBlend>0)
 {
  float perspDist=distance;
  float3 up=OrbisLocalCloudDirection(uv);
  float3 upDx=OrbisLocalCloudDirection(uv+float2(texel.x,0))-up;
  float3 upDy=OrbisLocalCloudDirection(uv+float2(0,texel.y))-up;
  float localFoot=max(length(upDx),length(upDy));
  float ground=ScrollCloudGround(OrbisWeatherUV(up));
  float3 end=lerp(ro+rd*distance,up*ground,_CloudViewBlend);
  ro=lerp(ro,up*(ground+max(0,_CloudCameraHeight)*_CloudHeightScale),_CloudViewBlend);
  float3 delta=end-ro;distance=length(delta);rd=delta/max(1e-7,distance);
  float foot0=lerp(baseFoot,localFoot,_CloudViewBlend);
  float foot1=lerp(baseFoot+perspDist*pixelAngle,localFoot,_CloudViewBlend);
  baseFoot=foot0;
  pixelAngle=max(0.0,(foot1-foot0)/max(1e-5,distance));
 }
 #endif
 return OrbisMarchRayFootprintEx(ro,rd,distance,eye,OrbisIGN(pixelPos),pixelAngle,baseFoot);
}
OrbisVolumeResult OrbisMarch(float2 uv)
{
 return OrbisMarchPixel(uv,uv*max(float2(1,1),_MainTex_TexelSize.zw));
}
OrbisVolumeResult OrbisMarchMap(float2 uv)
{
 float3 p=lerp(lerp(_MapBL,_MapBR,uv.x),lerp(_MapTL,_MapTR,uv.x),uv.y);
 OrbisVolumeResult empty;empty.light=0;empty.transmission=1;empty.sceneDepth=100;empty.cloudDepth=100;
 if(_LocalCloudMap<.5&&_MarkleyMap<.5&&((_ScrollCloudSource<.5&&abs(p.x)>ORBIS_PI)||abs(p.y)>ORBIS_PI*.5))return empty;
 float lon=-p.x,lat=p.y;
 float3 up=float3(sin(lon)*cos(lat),sin(lat),cos(lon)*cos(lat));
 if(_MarkleyMap>.5)up=MarkleyDirection(p.xy);
 if(_LocalCloudMap>.5)
  up=OrbisLocalCloudDirection(uv);
 float ground=clamp(tex2Dlod(_GroundField,float4(OrbisWeatherUV(up),0,0)).r,1.0,1.085);
 #ifdef ORBIS_SHARED_SCROLL_WEATHER
 if(_ScrollCloudSource>.5)ground=ScrollCloudGround(OrbisWeatherUV(up));
 #endif
 float mapBaseFoot=0.0;
 if(_VolumeTexelSize.x>1e-7)
 {
  if(_LocalCloudMap>.5)
  {
   float3 uDx=OrbisLocalCloudDirection(uv+float2(_VolumeTexelSize.x,0))-up;
   float3 uDy=OrbisLocalCloudDirection(uv+float2(0,_VolumeTexelSize.y))-up;
   mapBaseFoot=max(length(uDx),length(uDy));
  }
  else
  {
   float3 dpX=(_MapBR-_MapBL)*_VolumeTexelSize.x;
   float3 dpY=(_MapTL-_MapBL)*_VolumeTexelSize.y;
   mapBaseFoot=max(length(dpX),length(dpY));
  }
 }
 float altitude=_LocalCloudMap>.5?max(0,_CloudCameraHeight)*_CloudHeightScale:max(0,ORBIS_AIR_RADIUS-ground);
 return OrbisMarchRayFootprintEx(up*(ground+altitude),-up,altitude,8,.5,0.0,mapBaseFoot);
}
float3 OrbisTone(float3 color){return OrbisDisplay(color);}
#endif
