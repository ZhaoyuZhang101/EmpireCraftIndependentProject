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
float3 _CloudLocalUp,_CloudLocalEast,_CloudLocalNorth;

float3 _MapBL,_MapBR,_MapTL,_MapTR;
#ifdef ORBIS_SHARED_SCROLL_WEATHER
// This equirectangular cache and the terrain height use the same bilinear
// longitude-wrap / latitude-clamp addressing. Retain the full pass's sampler budget.
UNITY_DECLARE_TEX2D_NOSAMPLER(_ScrollCloudColumn);
float4 _ScrollCloudColumn_TexelSize;

// The published HP cache stores 1-exp(-tau) vertically through each cloud deck.
// Reproject each segment of the sunlight path, including the distant half of
// a grazing chord. The observer can lie anywhere along this path.
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
  float base=ScrollCloudGroundFast(columnUV);
  float4 weather=tex2Dlod(_ScrollWeatherTex,float4(columnUV,0,0));
  if(weather.r+weather.g>.006)
  {
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
// Moist boundary-layer air scatters more sunlight near a bank. Enforce its
// altitude falloff and exclude dense HP cloud, which has its own lighting.
float ScrollMoistAerosol(float3 p,float cloudDensity,float litOpening)
{
 float2 uv=OrbisFastWeatherUV(p);
 float ground=ScrollRadius(ScrollSurfaceHeight(ScrollHeightAt(uv)),uv,_ScrollSourceSize.y);
 float height=max(0,length(p)-ground);
 // Regional moisture remains present between the fine HP cloud shapes:
 // droplets there let an unblocked sunlight path show up inside a cloud gap.
 float4 weather=tex2Dlod(_ScrollWeatherTex,float4(uv,0,0));
 float moisture=smoothstep(.10,.64,weather.r)*(.5+.5*weather.b);
 return max(moisture,litOpening)*exp2(-height/max(1e-6,5200*_CloudHeightScale))*(1-smoothstep(.005,.14,cloudDensity));
}
#endif
   float HG(float cosine,float g)
   {
    return (1-g*g)/pow(max(.06,1+g*g-2*g*cosine),1.5);
   }
// Eight scalar slots stored in vectors avoid dynamically indexed temporary arrays.
// Every select uses a literal component; the enclosing sampling loops remain rolled.
// fastopt prevents expensive loop-unroll analysis; it does not disable shader-wide optimization.
float OrbisLightTau(int index,float4 first,float4 last)
{
 return index==0?first.x:index==1?first.y:index==2?first.z:index==3?first.w:index==4?last.x:index==5?last.y:index==6?last.z:last.w;
}
float2 OrbisLightSegment(float index,float count,float span)
{
 return OrbisHPLightSegment(index,count,span);
}
// Returns float2(sunOpticalDepth, phiFwdDiffuse).
// phi_fwd (fields.y) strictly contributes additive diffuse radiance; it never writes to density or alpha.
// Based on HanPi Volume Cloud © AshenOneArt (https://github.com/AshenOneArt/HPVolumeCloud), MIT License.
float2 LightField(float3 p,float3 sun,float density,float baseFootprint)
{
 if(OrbisSunVisible(p,sun)<.5)return float2(30.0,0);
 // Bound light-march exit tightly to the actual 13.5 km troposphere ceiling (not 40 km stratosphere!)
 // so Balanced q=1 4-step sun marching resolves genuine 3D self-shadowing inside every cumulus puff!
 float lightTop=ORBIS_CLOUD_TOP;
 float actualSlab=max(.012,13500.0*max(1e-7,_CloudHeightScale));
 #ifdef ORBIS_SHARED_SCROLL_WEATHER
 if(_ScrollCloudSource>.5)
 {
  float localGround=ScrollCloudGroundFast(OrbisFastWeatherUV(p));
  lightTop=min(ORBIS_CLOUD_TOP,max(localGround+actualSlab+.0006,length(p)+3200.0*max(1e-7,_CloudHeightScale)));
 }
 #endif
 float lengthToExit=max(0.0,OrbisSphere(p,sun,lightTop).y);
 float2 inner=OrbisSphere(p,sun,ORBIS_CLOUD_BASE);
 if(inner.x>0)lengthToExit=min(lengthToExit,inner.x);
 #ifdef ORBIS_SHARED_SCROLL_WEATHER
 if(_ScrollCloudSource>.5)
  lengthToExit=min(lengthToExit,actualSlab*1.35);
 #endif
 if(lengthToExit<=0.0)return float2(0.0,0);
 // The loop cap and quadrature divisor MUST use the same bounded count.
 bool allowEarlyOut=_CloudLightSimple>.5;
 int lightSteps=clamp(_LightSteps,1,8);
 float4 tauFirst=0;float4 tauLast=0;float totalTau=0;
 [loop] [fastopt] for(int j=0;j<8;j++)
 {
  if(j>=lightSteps)break;
  float2 segment=OrbisLightSegment(j,lightSteps,lengthToExit);
  float sampleTau=0;
  // Skip remaining light steps once Beer-Lambert, 3-octave MS, and HanPi diffuse are saturated (totalTau >= 5.5)
  [branch] if(!allowEarlyOut||totalTau<5.5)
  {
   float t=(segment.x+segment.y)*.5;
   // Progressive mip footprint along sun light ray: sharp near-field self-shadowing inside puffs, soft far shadows
   float lightFoot=max(baseFootprint*1.10,t*.22);
   float2 lightDensity=OrbisCloudDensitiesMip(p+sun*t,allowEarlyOut,lightFoot);
   sampleTau=(lightDensity.x+lightDensity.y)*ORBIS_EXTINCTION*(segment.y-segment.x);
  }
  if(j==0)tauFirst.x=sampleTau;
  else if(j==1)tauFirst.y=sampleTau;
  else if(j==2)tauFirst.z=sampleTau;
  else if(j==3)tauFirst.w=sampleTau;
  else if(j==4)tauLast.x=sampleTau;
  else if(j==5)tauLast.y=sampleTau;
  else if(j==6)tauLast.z=sampleTau;
  else tauLast.w=sampleTau;
  totalTau+=sampleTau;
 }
 if(_CloudMS<.5)return float2(totalTau,0);
 // HanPi phi_fwd source accumulation, adapted to ORBIS's bounded eight-step ray.
 // Direct anisotropic sunlight remains separate from this isotropic field.
 float diffuse=0,travelTau=0;
 float boundary=OrbisHPBoundaryConfidence(density)*saturate((dot(normalize(p),sun)+.35)/1.35);
 [loop] [fastopt] for(int k=0;k<8;k++)
 {
  if(k>=lightSteps)break;
  float localTau=OrbisLightTau(k,tauFirst,tauLast);
  float2 segment=OrbisLightSegment(k,lightSteps,lengthToExit);
  diffuse+=OrbisHPDiffuseStep(travelTau,localTau,(segment.x+segment.y)*.5,
                              segment.y-segment.x,boundary);
  travelTau+=localTau;
 }
 float thinSuppression=1-exp(-totalTau*1.15);
 diffuse=OrbisHPCompressDiffuse(diffuse)*thinSuppression;
 return float2(totalTau,diffuse);
}

// Same phi_fwd source integral for the cached shadow path. The atlas stores total
// optical depth, so its six segments approximate the unavailable density profile.
// No new density fetches or view-ray steps are introduced.
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
struct OrbisVolumeResult {float3 light;float3 transmission;float sceneDepth;};
float2 OrbisDepthUV(float2 uv)
{
 #if UNITY_UV_STARTS_AT_TOP
 if(_MainTex_TexelSize.y<0)uv.y=1-uv.y;
 #endif
 return uv;
}
// V4.2 Repair IPC fix: one sample only; no view loop and no inout result struct.
// The caller owns the flat view traversal. Density/light quadratures stay rolled.
void OrbisIntegrateSample(float3 p,float3 rd,float ds,float2 cloudLayers,float3 sun,float mu,float2 phaseAir,float moistPhase,
                          float phaseCloud,float phaseMid,float phaseHigh,float2 phaseMoon,float phaseMoonCloud,float footprint,float shaftJitter,
                          inout float3 light,inout float3 transmission)
{
 float3 rho=AirDensities(p);
 float cloudDensity=cloudLayers.x+cloudLayers.y;
 float3 sigma=AirExtinction(rho)+cloudDensity*ORBIS_EXTINCTION;
 if(max(sigma.r,max(sigma.g,sigma.b))<1e-6)return;
 float3 sunT=AirSunTransmittance(p,sun);
 float maxT=max(transmission.r,max(transmission.g,transmission.b));
 bool fastDense=_CloudLightSimple>.5;
 // Both uses previously inlined the entire direct-shadow fallback independently.
 // Evaluate the SAME shadow once, only when at least one consumer needs it.
 float shadowT=1.0;
 float3 moistScatter=0;
 #ifdef ORBIS_SHARED_SCROLL_WEATHER
 if(_ScrollCloudSource>.5)
 {
  [branch] if(_GodRays>.5&&_Clouds>.5&&any(sunT>1e-5)&&(!fastDense||maxT>.055))
  {
   // Sub-step dither strictly on clear-air smooth 2D shaft lookup; fade out on long horizon steps (ds > 0.0035)
   // to eliminate diagonal IGN cross-hatch mesh along the sky/horizon while keeping near-cloud shafts silky smooth.
   float shaftStep=min(ds,.0018)*(1.0-smoothstep(.0035,.0095,ds));
   float3 pShaft=(cloudDensity<.0005)?(p+rd*(shaftStep*(shaftJitter-.5)*.75)):p;
   float2 paths=ScrollCloudSunTransmission(pShaft,sun);
   shadowT=paths.x;
   // Volumetric cloud-gap opening: crisp golden beams piercing broken clouds, zero glare in open clear sky,
   // and cleanly faded out before solid cloud walls so shafts never paint bright bands onto cloud faces.
   float clearAir=1.0-smoothstep(.001,.026,cloudDensity);
   float opening=smoothstep(.014,.38,paths.x-paths.y)*smoothstep(.06,.76,paths.x);
   float forwardBeam=lerp(.65,1.55,smoothstep(.15,.86,mu));
   if(dot(p,p)<1.045*1.045&&clearAir>.01&&(opening>.004||!fastDense))
    moistScatter=ScrollMoistAerosol(pShaft,cloudDensity,opening)*clearAir*_AtmoBetaMie.rgb*
                ((.12+30.0*opening*forwardBeam)*max(0,_Haze)*max(_AirDensity,.32*saturate(_Atmosphere))*
                 saturate(_Atmosphere)*saturate(_ShadowStrength)*
                 smoothstep(.001,.08,max(sunT.r,max(sunT.g,sunT.b))));
  }
 }
 else
 #endif
 if(_GodRays>.5||(cloudDensity>.0005&&_OrbisShadowReady>.5))shadowT=OrbisCloudShadow(p,sun);
 // Preserve cool atmospheric air readability in cloud shadow while letting sunlit cloud-gap beams stand out clearly
 float airShadow=_GodRays>.5?lerp(1.0,.18+.82*shadowT,_ShadowStrength):1.0;
 float3 source=OrbisGLSLSource(rho,phaseAir,sunT)*airShadow;
 // Moist cloud-gap droplets are strongly forward-scattering (high albedo): modest extinction lets
 // genuine sunlit crepuscular shafts add crisp positive luminance inside cloud openings without glare washout.
 sigma+=moistScatter*.28;
 float3 moistLight=moistPhase*sunT*(shadowT*max(0,_AtmoParameters.z)*max(0,_SolarEnergy));
 float moistPeak=max(moistLight.r,max(moistLight.g,moistLight.b));
 source+=moistScatter*moistLight/(1+.12*moistPeak);
 // Skip night-sky, lunar LUT transmittance & airglow completely on the sunlit daytime hemisphere
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
  float2 fields;
  // Never hard-switch from 3D LightField to 2D shadow cache mid-cloud: eliminates both
  // cloud-wall contour ribbons when godRays=on and concentric arches during cloud passage!
  bool useCachedShadow=_OrbisShadowReady>.5;
  if(useCachedShadow)
  {
   float cachedTau=-log(max(shadowT,1e-6));
   fields=float2(cachedTau,OrbisCachedPhi(cachedTau,cloudDensity));
  }
  else
      fields=LightField(p,sun,cloudDensity,footprint);
  float sunTau=fields.x;
  float rLen=length(p);
  float sinElev=dot(p/max(1e-6,rLen),sun);
  #ifdef ORBIS_SHARED_SCROLL_WEATHER
  if(_ScrollCloudSource>.5&&_GodRays>.5&&!useCachedShadow)
  {
   // Gentle distant cast-shadow optical depth only at true twilight (< 11 deg) so 3D LightField
   // owns cumulus self-shadowing & backlit silver linings without crushing 3D clouds into dark solid walls!
   float lowSunCast=1.0-smoothstep(.04,.20,sinElev);
   sunTau+=(1.0-shadowT)*saturate(_ShadowStrength)*lowSunCast*1.45;
  }
  #endif
  float highWeight=cloudLayers.y/max(cloudDensity,1e-6);
  float cloudAlt=saturate((rLen-ORBIS_CLOUD_BASE)/max(1e-5,13200.0*max(1e-7,_CloudHeightScale)));
  // Three-octave Hillaire multi-scattering (PlanetCloudDemo / HPVolumeCloud style):
  // creamy sunlit crowns, silky backlit silver linings, and deep cool blue-grey shadow valleys.
  float phase0=lerp(phaseCloud,phaseHigh,highWeight);
  float t0=exp(-min(sunTau*1.28,30.0));
  float t1=exp(-min(sunTau*.46,30.0));
  float t2=exp(-min(sunTau*.16,30.0));
  float directScatter=t0*phase0*.62+t1*phaseMid*.26+t2*.58*.12;
  // Angle-gated PowderEffect (HPVolumeCloud): preserves luminous backlit silver-lining toward sun (mu > 0.45)
  float powderDark=saturate((1.0-exp(-cloudDensity*3.8))*1.55);
  float powder=lerp(1.0,lerp(1.0,powderDark,smoothstep(.45,-.55,mu)),.30*(1.0-highWeight*.75));
  float3 direct=directScatter*(1.02*powder)*sunT;
  // Sculpted 3D upwardAO driven by actual sunTau: keeps sunlit crowns bright snow-white at any altitude
  // while giving shadowed cumulus undersides and crevices authentic cool slate-blue depth.
  float upwardAO=exp(-min(sunTau,12.0)*max(.30,sinElev)*.42);
  float cloudAO=lerp(.44,1.0,upwardAO);
  float3 shadowTint=lerp(float3(.64,.80,1.12),float3(1,1,1),smoothstep(.06,.62,t0*.62+t1*.38));
  // HanPi phi_fwd isotropic diffuse field with gentle vertical depth bias (strictly additive to radiance ONLY!)
  float depthPhi=lerp(.62,1.18,smoothstep(.04,.76,cloudAlt));
  float denseGate=smoothstep(.003,.065,cloudDensity);
  float3 multiple=fields.y*_CloudMS*_ScatteringStrength*1.02*cloudAO*depthPhi*shadowTint*sunT*denseGate;
  float3 nightCloud=0;
  if(nightW>1e-5)
  {
   float3 moonScatter=moonT*phaseMoonCloud*.65*(.25+.75*exp(-cloudDensity*.75));
   float3 nightDiffuse=OrbisNightAmbient(p,sun,0)*(.45+.55*exp(-nightTau*.10))*
       lerp(.70,1.30,exp(-cloudDensity*.75));
   nightCloud=nightDiffuse+moonScatter;
  }
  float3 verticalAmbient=lerp(float3(.80,.90,1.10),float3(1.06,1.04,1.00),smoothstep(.06,.78,cloudAlt));
  float3 skyBounce=OrbisDaySkyFill(p,sun)*(.96*cloudAO*shadowTint*verticalAmbient);
  float3 radiance=(direct+multiple+skyBounce)*max(.1,_SolarEnergy)+
      nightCloud+OrbisLightningLight(p)*3.5;
  float dayHemi=saturate(sinElev*2.4+.22);
  float3 ambientFloor=lerp(float3(.032,.050,.084),float3(.074,.090,.120),smoothstep(.04,.76,cloudAlt))*dayHemi;
  if(_Night<.5)radiance=max(radiance,ambientFloor);
  // Higher highlight knee preserves creamy sunlit cumulus crown gradation without flat posterized capping.
  float peakR=max(radiance.r,max(radiance.g,radiance.b));
  float knee=peakR>1.22?(1.22+(peakR-1.22)/(1.0+(peakR-1.22)*.48))/peakR:1.0;
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
// The atmosphere entry/exit already bound the four cloud intersections.
// Sort only those four values with a five-comparator network: no sorting loop,
// no dependent loop initializer (b=a+1), no dynamically indexed cuts[] array.
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
 OrbisVolumeResult result;result.light=0;result.transmission=1;result.sceneDepth=sceneDepth;
 float2 visibleRange=OrbisGLSLViewInterval(ro,rd,endDistance);
 float begin=visibleRange.x,end=visibleRange.y;
 if(end<=begin)return result;
 float2 cloudOuter=OrbisSphere(ro,rd,ORBIS_CLOUD_TOP),cloudInner=OrbisSphere(ro,rd,ORBIS_CLOUD_BASE);
 float4 cuts=OrbisSortCuts4(clamp(float4(cloudOuter,cloudInner),begin,end));
 float cloudLength=0,airLength=0;
 // This lightweight length prepass contains NO density or lighting evaluation.
 [loop] [fastopt] for(int k=0;k<5;k++)
 {
  float2 interval=OrbisInterval(k,begin,end,cuts);
  float middle=length(ro+rd*((interval.x+interval.y)*.5));
  bool cloud=middle>ORBIS_CLOUD_BASE&&middle<ORBIS_CLOUD_TOP&&_Clouds>.5;
  if(cloud)cloudLength+=interval.y-interval.x;else airLength+=interval.y-interval.x;
 }
 float3 sun=normalize(_Sun);float mu=dot(rd,sun);
 float2 phaseAir=OrbisGLSLPhase(mu);
 // Smooth dual-lobe Mie phase: luminous silver-lining halo toward sun without a hard min(4.2) clip ring!
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
 int nextInterval=0,sampleIndex=0,sampleCount=0;
 float segmentBegin=0,segmentEnd=0,cursor=0,ds=0,crustStart=0,grazing=0,nearCam=0,insideShell=0,closeCam=0;
 float localCloudTop=ORBIS_CLOUD_TOP;
 bool segmentHasCloud=false,prevHit=false;
 float actualSlab=max(.012,13500.0*max(1e-7,_CloudHeightScale));
 // Smooth coherent raymarching (single probe + single full-density call site, ZERO cloud-density IGN stipple):
 // rapid stratosphere stride above localCloudTop reallocates budget into the real 13.5 km troposphere so
 // 36-55% unfold and low-angle cumulus get 5-8 smooth volumetric steps per puff in q=1 Balanced mode.
 [loop] [fastopt] for(int traversal=0;traversal<1285;traversal++)
 {
  float maxTrans=max(transmission.r,max(transmission.g,transmission.b));
  if(maxTrans<(_CloudLightSimple>.5?.014:.002))break;
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
   int budget=clamp(cloud?_CloudSteps:_AirSteps,1,64);
   float total=cloud?cloudLength:airLength;
   sampleCount=clamp((int)ceil(budget*len/max(total,1e-6)),cloud?1:6,64);
   ds=len/sampleCount;
   sampleCount=cloud?sampleCount*(int)clamp(_CloudRefinement,1,4):sampleCount;
   sampleIndex=0;segmentBegin=interval.x;segmentEnd=interval.y;cursor=segmentBegin;segmentHasCloud=cloud;
   prevHit=false;crustStart=segmentBegin;
   float camR=length(ro);
   float lowOrbit=1.0-smoothstep(1.38,1.72,camR);
   nearCam=cloud?(1.0-smoothstep(1.08,1.44,camR)):0.0;
   closeCam=cloud?(1.0-smoothstep(1.32,1.90,camR)):0.0;
   // Strictly inside/adjacent to the cloud shell (0.0 for all orbital/low-orbit views R >= 1.12):
   // prevents long tangent-chord minStep from snapping inside typhoon eyes & near-cloud views into horizontal layer rings.
   insideShell=cloud?(1.0-smoothstep(1.04,1.12,camR)):0.0;
   grazing=cloud?max(smoothstep(.058,.088,len)*lowOrbit,nearCam):0.0;
   localCloudTop=ORBIS_CLOUD_TOP;
   #ifdef ORBIS_SHARED_SCROLL_WEATHER
   if(cloud&&_ScrollCloudSource>.5)
   {
    float gA=ScrollCloudGroundFast(OrbisFastWeatherUV(ro+rd*segmentBegin));
    float gM=ScrollCloudGroundFast(OrbisFastWeatherUV(ro+rd*((segmentBegin+segmentEnd)*.5)));
    float gB=ScrollCloudGroundFast(OrbisFastWeatherUV(ro+rd*segmentEnd));
    localCloudTop=min(ORBIS_CLOUD_TOP,max(gA,max(gM,gB))+actualSlab+.0015);
   }
   #endif
  }
  float stepSize=min(ds,segmentEnd-cursor);float2 density=0;
  if(segmentHasCloud)
  {
   float slab=ORBIS_CLOUD_TOP-ORBIS_CLOUD_BASE;
   float minStep=(segmentEnd-cursor)/max(1,sampleCount-sampleIndex);
   // Prevent step collapse at cursor=0 inside/near clouds while keeping smooth stride growth from actual cloud entry
   float minCap=lerp(.00004,actualSlab/28.0,nearCam);
   float cap=min(max(cursor*.125,minCap),lerp(slab/16,slab/2,pow(saturate((cursor-crustStart)/.08),2)));
   float rProbe=length(ro+rd*cursor);
   // Smooth stratosphere transition across localCloudTop (no hard step cliffs or concave-up sky arcs)
   float hiWeight=prevHit?0.0:smoothstep(localCloudTop-.0015,localCloudTop+.0055,rProbe);
   float lowEmpty=(grazing>.01&&!prevHit)?min(ds,actualSlab*lerp(.20,.085,grazing)):min(ds,actualSlab*.16);
   if(!prevHit&&nearCam>.10&&_GodRays>.5)
    lowEmpty=lerp(lowEmpty,min(lowEmpty,actualSlab*.075),1.0-smoothstep(localCloudTop-.008,localCloudTop,rProbe));
   float emptyCap=lerp(lowEmpty,min(.0075,max(ds*2.2,minStep)),hiWeight);
   // Smooth budget tail instead of a hard step cliff at sampleCount-10; probe far-edge on empty
   // spans so upward/oblique rays never skip half a step past the LCL cloud base into concentric arcs.
   float emptyTail=smoothstep((float)sampleCount-14.0,(float)sampleCount-6.0,(float)sampleIndex);
   float emptyFloor=lerp(lerp(minStep,min(minStep,actualSlab*.085),insideShell),minStep,emptyTail);
   float large=min(segmentEnd-cursor,max(lerp(min(emptyCap,cap),emptyCap,hiWeight),emptyFloor));
   float probeOffset=(!prevHit&&large<=actualSlab*.16)?large:(large*.5);
   float probeFoot=baseFoot+(cursor+probeOffset)*pixelAngle;
   float2 probe=OrbisCloudDensitiesMip(ro+rd*(cursor+probeOffset),true,probeFoot);
   bool hitProbe=(probe.x+probe.y)>.0005;
   if(hitProbe)
   {
    if(!prevHit){crustStart=cursor;prevHit=true;}
    float refine=clamp(_CloudRefinement,1,4);
    if(_CloudLightSimple>.5)refine=lerp(lerp(1.25,refine,insideShell*.5),refine,smoothstep(.04,.48,maxTrans));
    float baseFine=min(ds,cap)*rcp(refine);
    // Bound fine steps to the ACTUAL 13.5 km troposphere (not 40 km ORBIS_CLOUD_TOP), scaled cleanly by
    // _CloudRefinement so q=0 stays fast while q=1 Balanced resolves plump 36-55% unfold cumulus smoothly.
    float nearWeight=saturate(max(grazing,closeCam));
    float refineTier=saturate((_CloudRefinement-1.0)+.45);
    float tropoDiv=lerp(16.0,lerp(22.0,38.0,nearWeight),refineTier);
    float tropoFineCap=actualSlab/tropoDiv;
    float crustBoost=max(grazing,nearWeight*.72)*smoothstep(.18,.82,maxTrans);
    float crustCap=lerp(actualSlab/34.0,actualSlab/10.0,pow(saturate((cursor-crustStart)/.030),1.35));
    float fineStep=min(min(baseFine,tropoFineCap),lerp(baseFine,crustCap,crustBoost));
    float fineTail=smoothstep((float)sampleCount-14.0,(float)sampleCount-4.0,(float)sampleIndex);
    float allowFine=saturate(max(smoothstep(.02,.15,crustBoost),max(insideShell,closeCam*.85)));
    float safeMinStep=minStep*lerp(1.0,fineTail,allowFine);
    stepSize=min(segmentEnd-cursor,max(fineStep,safeMinStep));
    // Keep primary view-ray density model continuous (never flip to simpleMode mid-cloud).
    float sampleDistance=cursor+stepSize*.5;
    float sampleFoot=baseFoot+sampleDistance*pixelAngle;
    // 3D billow noise is isotropic (~1.18:1, not legacy 3.56x pancake); band-limit strictly by actual stepSize
    // so foreground/lower-viewport clouds stay fluffy instead of over-blurring into low-res MIP-3 smudges.
    float3 samplePos=ro+rd*sampleDistance;
    float radialCos=dot(normalize(samplePos),rd);
    float isoStride=stepSize*sqrt(1.0+.40*radialCos*radialCos);
    float rayFoot=min(actualSlab*.16,1.15*isoStride)*smoothstep(.15,.75,grazing);
    density=OrbisCloudDensitiesMip(samplePos,false,sampleFoot,rayFoot);
   }
   else
   {
    prevHit=false;
    stepSize=large;
    density=0;
   }
  }
  float sampleDist=cursor+stepSize*.5;
  float3 p=ro+rd*sampleDist;
  OrbisIntegrateSample(p,rd,stepSize,density,sun,mu,phaseAir,moistPhase,phaseCloud,phaseMid,phaseHigh,phaseMoon,phaseMoonCloud,baseFoot+sampleDist*pixelAngle,jitter,light,transmission);
  cursor=min(segmentEnd,cursor+stepSize);
  sampleIndex++;
 }
 float volumeWeight=_ScrollCloudSource>.5?saturate(_ScrollVolumeWeight):1;
 result.light=light*volumeWeight;result.transmission=lerp(float3(1,1,1),transmission,volumeWeight);
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
 if(planet.x>0)distance=min(distance,planet.x);
 float2 texel=_VolumeTexelSize.x>1e-7?_VolumeTexelSize.xy:max(float2(1e-4,1e-4),_MainTex_TexelSize.xy);
 float3 rayDx=OrbisViewRay(uv+float2(texel.x,0))-ray;
 float3 rayDy=OrbisViewRay(uv+float2(0,texel.y))-ray;
 float pixelAngle=max(length(rayDx),length(rayDy))/max(1e-5,length(ray));
 // Account for orthographic origin shift during mixed projection (morph > 0.62) so mip footprint never collapses to 0!
 float baseFoot=max(length(_OrbisRayOriginRight)*(2.0*texel.x),length(_OrbisRayOriginUp)*(2.0*texel.y));
 #ifdef ORBIS_SHARED_SCROLL_WEATHER
 if(_LocalCloudMap>.5&&_CloudViewBlend>0)
 {
  // Continuously transport the existing view ray into the local geographic
  // column. Never fade clouds out to expose their shadow, then enable a second
  // cloud pass at a mode threshold: that produced the black flash at 46-50%.
  float perspDist=distance;
  float3 up=OrbisLocalCloudDirection(uv);
  float3 upDx=OrbisLocalCloudDirection(uv+float2(texel.x,0))-up;
  float3 upDy=OrbisLocalCloudDirection(uv+float2(0,texel.y))-up;
  float localFoot=max(length(upDx),length(upDy));
  float ground=ScrollCloudGround(OrbisWeatherUV(up));
  float3 end=lerp(ro+rd*distance,up*ground,_CloudViewBlend);
  ro=lerp(ro,up*(ground+max(0,_CloudCameraHeight)*_CloudHeightScale),_CloudViewBlend);
  float3 delta=end-ro;distance=length(delta);rd=delta/max(1e-7,distance);
  // Keep pixel footprint continuous across 12%-78% unfold (no mip collapse or scroll shimmer).
  float foot0=lerp(baseFoot,localFoot,_CloudViewBlend);
  float foot1=lerp(baseFoot+perspDist*pixelAngle,localFoot,_CloudViewBlend);
  baseFoot=foot0;
  pixelAngle=max(0.0,(foot1-foot0)/max(1e-5,distance));
 }
 #endif
 // Pass OrbisIGN strictly for clear-air 2D shaft sub-step smoothing (never used on 3D cloud ray steps!)
 return OrbisMarchRayFootprintEx(ro,rd,distance,eye,OrbisIGN(pixelPos),pixelAngle,baseFoot);
}
OrbisVolumeResult OrbisMarch(float2 uv)
{
 return OrbisMarchPixel(uv,uv*max(float2(1,1),_MainTex_TexelSize.zw));
}
OrbisVolumeResult OrbisMarchMap(float2 uv)
{
 // Atlas semantics: the radial atmospheric column at each longitude/latitude.
 // Not a flat plane shoved into the sphere-ray intersection equations.
 float3 p=lerp(lerp(_MapBL,_MapBR,uv.x),lerp(_MapTL,_MapTR,uv.x),uv.y);
 OrbisVolumeResult empty;empty.light=0;empty.transmission=1;empty.sceneDepth=100;
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
 // Single call site avoids duplicating the entire raymarcher inside MAP_VOLUME / MRT_MAP_VOLUME.
 float altitude=_LocalCloudMap>.5?max(0,_CloudCameraHeight)*_CloudHeightScale:max(0,ORBIS_AIR_RADIUS-ground);
 return OrbisMarchRayFootprintEx(up*(ground+altitude),-up,altitude,8,.5,0.0,mapBaseFoot);
}
float3 OrbisTone(float3 color){return OrbisDisplay(color);}
#endif
