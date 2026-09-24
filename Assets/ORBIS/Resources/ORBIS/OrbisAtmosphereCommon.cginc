#ifndef ORBIS_ATMOSPHERE_INTEGRATION
#define ORBIS_ATMOSPHERE_INTEGRATION
#include "FlatWorld/Markley.cginc"
float _MarkleyMap;
#include "UnityCG.cginc"
#include "OrbisOptics.cginc"
sampler2D _MainTex;
UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
float4 _MainTex_TexelSize;
#include "OrbisAtmosphereView.cginc"
#include "OrbisHPVolumeCloud.cginc"
int _CloudSteps,_AirSteps,_LightSteps,_CloudDebug;
float _CloudMS,_ScatteringStrength,_MapVolume;
float _ScrollVolumeWeight;

float3 _MapBL,_MapBR,_MapTL,_MapTR;
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
float2 LightField(float3 p,float3 sun,float density)
{
 if(OrbisSunVisible(p,sun)<.5)return float2(0,0);
 float lengthToExit=max(0.0,OrbisSphere(p,sun,ORBIS_CLOUD_TOP).y);
 float2 inner=OrbisSphere(p,sun,ORBIS_CLOUD_BASE);
 if(inner.x>0)lengthToExit=min(lengthToExit,inner.x);
 if(lengthToExit<=0.0)return float2(1,0);
 // The loop cap and quadrature divisor MUST use the same bounded count.
 int lightSteps=clamp(_LightSteps,1,8);
 float ds=lengthToExit/lightSteps;
 float4 tauFirst=0;float4 tauLast=0;float totalTau=0;
 [loop] [fastopt] for(int j=0;j<8;j++)
 {
  if(j>=lightSteps)break;
  float2 segment=OrbisLightSegment(j,lightSteps,lengthToExit);
  float t=(segment.x+segment.y)*.5;
  float sampleTau=OrbisCloudDensity(p+sun*t)*ORBIS_EXTINCTION*(segment.y-segment.x);
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
 float direct=exp(-min(totalTau*1.6,30.0));
 if(_CloudMS<.5)return float2(direct,0);
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
 float thinSuppression=1-exp(-totalTau*1.2);
 diffuse=OrbisHPCompressDiffuse(diffuse)*thinSuppression;
 return float2(direct,diffuse);
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
void OrbisIntegrateSample(float3 p,float ds,float2 cloudLayers,float3 sun,float2 phaseAir,
                          float phaseCloud,float phaseHigh,float2 phaseMoon,float phaseMoonCloud,
                          inout float3 light,inout float3 transmission)
{
 float3 rho=AirDensities(p);
 float cloudDensity=cloudLayers.x+cloudLayers.y;
 float3 sigma=AirExtinction(rho)+cloudDensity*ORBIS_EXTINCTION;
 if(max(sigma.r,max(sigma.g,sigma.b))<1e-6)return;
 float3 sunT=AirSunTransmittance(p,sun);
 // Both uses previously inlined the entire direct-shadow fallback independently.
 // Evaluate the SAME shadow once, only when at least one consumer needs it.
 float shadowT=1.0;
 if(_GodRays>.5||(cloudDensity>.0005&&_OrbisShadowReady>.5))
     shadowT=OrbisCloudShadow(p,sun);
 float airShadow=_GodRays>.5?lerp(1.0,shadowT,_ShadowStrength):1.0;
 float3 source=OrbisGLSLSource(rho,phaseAir,sunT)*airShadow;
 float nightTau=OrbisNightWeight(p,sun)>1e-5?OrbisNightColumnTau(p):0;
 float3 nightAmbient=OrbisNightAmbient(p,sun,nightTau);
 float3 moonT=OrbisMoonIncident(p,sun,nightTau);
 float3 airglow=OrbisAirglowEmission(p,sun);
 // All new terms enter LINEAR radiance before the existing Beer accumulation.
 // Weak isotropic night sky + directional moon scattering + true emissive airglow.
 float3 moonSource=(rho.x*_AtmoBetaRayleigh.xyz*phaseMoon.x+
     rho.y*_AtmoBetaMie.xyz*max(0,_Haze)*phaseMoon.y)*moonT*1.8;
 source+=AirExtinction(rho)*nightAmbient*.35+moonSource+airglow;
 if(cloudDensity>.0005)
 {
  float2 fields;
  if(_OrbisShadowReady>.5)
      fields=float2(shadowT,OrbisCachedPhi(-log(max(shadowT,1e-6)),cloudDensity));
  else fields=LightField(p,sun,cloudDensity);
  float directT=fields.x;
  float highWeight=cloudLayers.y/max(cloudDensity,1e-6);
  float3 direct=directT*lerp(phaseCloud,phaseHigh,highWeight)*.70*sunT;
  float3 multiple=fields.y*.70*_CloudMS*_ScatteringStrength*sunT;
  float denseGate=smoothstep(.025,.18,cloudDensity);
  multiple*=denseGate;
  float3 moonScatter=moonT*phaseMoonCloud*.65*exp(-cloudDensity*1.4);
  // Self-occlusion proxy, not a second LightField/raymarch. Solar energy scales
  // ONLY the existing solar terms, not moonlight, night sky, airglow or lightning.
  float3 nightDiffuse=nightAmbient*lerp(.65,1.35,exp(-cloudDensity*1.2));
  // HanPi upward ambient occlusion, reusing light optical depth (no extra rays).
  float lightDepth=-log(max(directT,1e-6));
  float upwardAO=exp(-lightDepth*max(.05,dot(normalize(p),sun))*.8);
  float3 radiance=(direct+multiple+OrbisDaySkyFill(p,sun)*lerp(.20,1.05,upwardAO))*max(.1,_SolarEnergy)+
      nightDiffuse+moonScatter+OrbisLightningLight(p)*3.5;
  if(_Night<.5)radiance=max(radiance,float3(.20,.23,.27));
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
OrbisVolumeResult OrbisMarchRay(float3 ro,float3 rd,float endDistance,float sceneDepth)
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
 float phaseCloud=min(4.0,.83*HG(mu,.72)+.17*HG(mu,-.33));
 float phaseHigh=min(3.0,.85*HG(mu,.48)+.15*HG(mu,-.12));
 float moonMu=dot(rd,_OrbisMoon.xyz);
 float2 phaseMoon=OrbisGLSLPhase(moonMu);
 float phaseMoonCloud=min(2.0,.85*HG(moonMu,.45)+.15*HG(moonMu,-.15));
 float3 light=0,transmission=1;
 int nextInterval=0,sampleIndex=0,sampleCount=0;
 float segmentBegin=0,segmentEnd=0,cursor=0,ds=0;
 bool segmentHasCloud=false;
 // HP adaptive traversal: at most four fine steps per original cloud sample,
 // five ordered shell intervals, one rolled loop and bounded early extinction.
 [loop] [fastopt] for(int traversal=0;traversal<1285;traversal++)
 {
  if(max(transmission.r,max(transmission.g,transmission.b))<.002)break;
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
   sampleCount=cloud?sampleCount*4:sampleCount;
   sampleIndex=0;segmentBegin=interval.x;segmentEnd=interval.y;cursor=segmentBegin;segmentHasCloud=cloud;
  }
  float stepSize=min(ds,segmentEnd-cursor);float2 density=0;
  if(segmentHasCloud)
  {
   // HP: cheap no-erosion probe, fine steps inside clouds and large empty steps.
   // Preserve an exact interval end even when the bounded iteration budget is tight.
   float slab=ORBIS_CLOUD_TOP-ORBIS_CLOUD_BASE;
   float cap=min(max(cursor/8,.00004),lerp(slab/16,slab/2,pow(saturate((cursor-segmentBegin)/.08),2)));
   float large=min(ds,cap);
   float2 probe=OrbisCloudDensities(ro+rd*(cursor+large*.5),true);
   stepSize=large*((probe.x+probe.y)>.0005?.25:1);
   stepSize=max(stepSize,(segmentEnd-cursor)/max(1,sampleCount-sampleIndex));
   stepSize=min(stepSize,segmentEnd-cursor);
   density=OrbisCloudDensities(ro+rd*(cursor+stepSize*.5),false);
  }
  float3 p=ro+rd*(cursor+stepSize*.5);
  OrbisIntegrateSample(p,stepSize,density,sun,phaseAir,phaseCloud,phaseHigh,phaseMoon,phaseMoonCloud,light,transmission);
  cursor=min(segmentEnd,cursor+stepSize);
  sampleIndex++;
 }
 float volumeWeight=_ScrollCloudSource>.5?saturate(_ScrollVolumeWeight):1;
 result.light=light*volumeWeight;result.transmission=lerp(float3(1,1,1),transmission,volumeWeight);
 return result;
}

OrbisVolumeResult OrbisMarch(float2 uv)
{
 float3 ray=OrbisViewRay(uv);
 float depth=SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture,OrbisDepthUV(uv));
 float eye=LinearEyeDepth(depth),distance=eye*length(ray);
 float3 ro=_CameraLocal,rd=normalize(ray);
 float2 planet=OrbisSphere(ro,rd,1.0);
 if(planet.x>0)distance=min(distance,planet.x);
 return OrbisMarchRay(ro,rd,distance,eye);
}
OrbisVolumeResult OrbisMarchMap(float2 uv)
{
 // Atlas semantics: the radial atmospheric column at each longitude/latitude.
 // Not a flat plane shoved into the sphere-ray intersection equations.
 float3 p=lerp(lerp(_MapBL,_MapBR,uv.x),lerp(_MapTL,_MapTR,uv.x),uv.y);
 OrbisVolumeResult empty;empty.light=0;empty.transmission=1;empty.sceneDepth=100;
 if(_MarkleyMap<.5&&((_ScrollCloudSource<.5&&abs(p.x)>ORBIS_PI)||abs(p.y)>ORBIS_PI*.5))return empty;
 float lon=-p.x,lat=p.y;
 float3 up=float3(sin(lon)*cos(lat),sin(lat),cos(lon)*cos(lat));
 if(_MarkleyMap>.5)up=MarkleyDirection(p.xy);
 float ground=clamp(tex2Dlod(_GroundField,float4(OrbisWeatherUV(up),0,0)).r,1.0,1.085);
 #ifdef ORBIS_SHARED_SCROLL_WEATHER
 if(_ScrollCloudSource>.5)ground=ScrollCloudGround(OrbisWeatherUV(up));
 #endif
 return OrbisMarchRay(up*ORBIS_AIR_RADIUS,-up,ORBIS_AIR_RADIUS-ground,8);
}
float3 OrbisTone(float3 color){return OrbisDisplay(color);}
#endif
