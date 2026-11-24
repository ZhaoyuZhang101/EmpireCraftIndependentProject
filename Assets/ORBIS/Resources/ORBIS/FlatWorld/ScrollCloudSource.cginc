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
                           inout float coverage,inout float highCov,inout float convection,inout float3 a,inout float3 b)
{
 if(centre.w<=.20)return;
 float extent=FlatCycloneRadius(centre.w);
 float dist=length(up-centre.xyz);
 if(dist>=extent*1.28)return;
 float r=dist/extent;
 float maturity=smoothstep(.46,.78,centre.w);
 float altNorm=saturate((z-500.0)/11900.0);

 // Outward-sloping 3D Stadium Bowl: eye radius expands 3x from sea surface to tropopause!
 float stadiumR=lerp(.022,.082,pow(altNorm,.82));
 float eyeClear=lerp(1.0,smoothstep(stadiumR*.52,stadiumR*1.12,r),maturity);
 // Subtle low-level mesovortex "hub cloud" stratocumulus swirl at the bottom of the 12 km stadium bowl
 // (seen inside Category-5 Hurricane Isabel's eye from the ISS — gives instant vertical depth scale!)
 float hubZone=(1.0-smoothstep(950.0,1650.0,z))*smoothstep(420.0,680.0,z)*(1.0-smoothstep(stadiumR*.35,stadiumR*.95,r))*maturity;
 coverage=max(coverage*eyeClear,hubZone*.42);
 highCov*=lerp(1.0,smoothstep(stadiumR*.72,stadiumR*1.28,r),maturity);

 // Towering 12.6 km stadium eyewall ring & overshooting tropopause collar right outside stadiumR
 float wallCollar=exp(-pow((r-(stadiumR+.042))/.055,2.0))*maturity*(1.0-smoothstep(12200.0,13100.0,z));
 coverage=max(coverage,wallCollar*.92*eyeClear);
 convection=lerp(max(convection,wallCollar*.98),.10,hubZone*.92);

 // Azimuthal cyclonic swirl of 3D volumetric cloud coordinates around storm axis
 float swirlWeight=(1.0-smoothstep(.25,1.15,r))*smoothstep(.20,.65,centre.w);
 if(swirlWeight>.01)
 {
  float swirlAng=spin*(1.55*log(1.0+4.2*max(r,.02))-.35*rotPhase)*swirlWeight;
  float s,c;sincos(swirlAng,s,c);
  float3 axis=centre.xyz;
  a=normalize(a*c+cross(axis,a)*s+axis*dot(axis,a)*(1.0-c));
  b=normalize(b*c+cross(axis,b)*s+axis*dot(axis,b)*(1.0-c));
 }
}

// Uses the SAME spherical coordinates on the globe and every repeated 2D sheet.
float2 ScrollClimateDensities(float3 p,bool simpleMode)
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
 float3 a=up,b=up;
 if(!simpleMode)
 {
  a=normalize(up-localWind*phase*8);
  b=normalize(up+localWind*(1-phase)*8);
 }
 else
 {
  a=normalize(up-localWind*(sin(phase*6.2831853)*3.2));
 }

 if(hasCyclone)
 {
  ApplyTyphoon3DStadium(up,z,_OrbisCycloneNorth,1.0,_OrbisCyclonePhase.x,coverage,highWeather,convection,a,b);
  ApplyTyphoon3DStadium(up,z,_OrbisCycloneSouth,-1.0,_OrbisCyclonePhase.y,coverage,highWeather,convection,a,b);
 }

 if(coverage+highWeather<.006)return 0;
 float low=0,high=0;
 if(coverage>.004&&z<12950)
 {
  float2 span=OrbisCloudVerticalSpan(convection,saturate(coverage+.12));
  bool inMidBand=convection>.28&&convection<.76&&z>3800.0&&z<6150.0;
  if((z>span.x-90.0&&z<span.y*1.18+360.0)||inMidBand)
  {
   float altitude=z/7000;
   const float3x3 frame=float3x3(.36,.48,-.80,-.80,.60,0,.48,.64,.60);
   float3 radAxis=mul(frame,up);
   // Downwind upper-tropospheric anvil shear bends towering Cb tops cleanly along upper flow (ISS Borneo signature!)
   float anvilShear=1.55+2.60*smoothstep(.58,.96,convection)*smoothstep(1.05,1.65,altitude);
   float3 windShear=mul(frame,localWind)*(altitude*anvilShear);
   float3 q0=mul(frame,a);

   float shape=0,worleyMeso=.5,effCov=coverage;
   float4 erosion=float4(.68,.68,.42,.42);
   bool evalLow=false;
   if(simpleMode)
   {
    float3 bPos=q0*19.2+radAxis*(altitude*1.05)+windShear+float3(.41,.19,.83);
    float4 body=tex3Dlod(_CloudNoise,float4(bPos,0));
    worleyMeso=body.g*.64+body.b*.36;
    shape=saturate((body.r*.66+worleyMeso*.34-(1.0-worleyMeso)*.20)/.80+coverage*.08+.05);
    effCov=saturate(coverage+.08);
    evalLow=true;
   }
   else
   {
    // Variance-preserving dual-phase flow blend: keeps crisp cauliflower tops & unbroken slender curved bands!
    float invStd=rsqrt(max(.5,phase*phase+(1.0-phase)*(1.0-phase)));
    float3 q1=mul(frame,b);

    float3 mPos0=q0*6.4+radAxis*(altitude*.34)+windShear*.45+float3(.13,.71,.37);
    float3 mPos1=q1*6.4+radAxis*(altitude*.34)+windShear*.45+float3(.13,.71,.37);
    float4 macroRaw=lerp(tex3Dlod(_CloudNoise,float4(mPos0,0)),tex3Dlod(_CloudNoise,float4(mPos1,0)),phase);
    float4 macroBody=saturate(.5+(macroRaw-.5)*lerp(1.0,invStd,.65));

    float3 mesoWarp=(macroBody.gba-.5)*.13;
    float3 bPos0=q0*24.0+radAxis*(altitude*1.12)+windShear+mesoWarp+float3(.41,.19,.83);
    float3 bPos1=q1*24.0+radAxis*(altitude*1.12)+windShear+mesoWarp+float3(.41,.19,.83);
    float4 bodyRaw=lerp(tex3Dlod(_CloudNoise,float4(bPos0,0)),tex3Dlod(_CloudNoise,float4(bPos1,0)),phase);
    float4 body=saturate(.5+(bodyRaw-.5)*lerp(1.0,invStd,.78));

    worleyMeso=body.g*.64+body.b*.36;
    float basePW=macroBody.r*.26+body.r*.46+worleyMeso*.28;
    // Gently preserve slender curved weather bands in 3D while keeping ragged broken cumulus edges
    float ribbonLift=.055*smoothstep(.18,.66,coverage);
    shape=saturate((basePW-(1.0-worleyMeso)*.19)/.81+coverage*.06+ribbonLift);

    float covBound=.26+.74*saturate(coverage+.12);
    if(pow(shape,.68)>.84-covBound||coverage>.62||inMidBand)
    {
     float3 curl=(body.gba-.5)*.32;
     float3 dPos0=q0*74.0+radAxis*(altitude*2.65)+windShear*1.45+curl;
     float3 dPos1=q1*74.0+radAxis*(altitude*2.65)+windShear*1.45+curl;
     float4 eroRaw=lerp(tex3Dlod(_CloudDetailNoise,float4(dPos0,0)),tex3Dlod(_CloudDetailNoise,float4(dPos1,0)),phase);
     erosion=saturate(.5+(eroRaw-.5)*lerp(1.0,invStd,.85));
     float edgeInvW=1.0-(erosion.r*.64+erosion.g*.36);
     float edgeTear=edgeInvW*.19*(1.0-smoothstep(.60,.90,coverage))*smoothstep(.02,.26,coverage);
     effCov=saturate(coverage-edgeTear);
     evalLow=true;
    }
   }
   if(evalLow)
   {
    float tower=smoothstep(.45,.96,convection);
    // ISCCP exponential optical-depth curve: translucent gossamer skirts (0.08-0.46) -> opaque snow-white cores (0.9-1.8)
    float opticalGain=(lerp(.07,.46,smoothstep(.04,.48,effCov))+.98*pow(smoothstep(.42,.94,effCov),1.45))*lerp(.86,1.24,tower);
    low=OrbisHPLowDensityDetailed(z,.26+.74*effCov,convection,shape,erosion,.46+.54*worleyMeso)*opticalGain;
   }
  }
 }
 if(highWeather>.004&&z>6950)
 {
  // High-tropospheric Curved Brushed-Silk Cirrus & Glaciated Anvil Plumes (7.2 - 13.3 km):
  // Uses orthogonal 3D rotated sphere frame + true altitude Z coordinate (zero equatorial horizontal banding!)
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
   // Gentle 3D curved mares'-tail swirl + fallstreak virga hook + TRUE 2-tap spherical LIC along axisAlong
   // (zero extra texture fetches, genuinely brushes 3D cirrus fibers along local upper flow!)
   float curve=(ice.b-.5)*1.10+(ice.a-.5)*.65;
   float fallHook=(z-8400)*.00024;
   float altZ=(z-7200.0)/5600.0;
   float3 uSwirl=normalize(upper+axisCross*(curve*.048)+axisAlong*fallHook);
   float3 uLic=normalize(uSwirl+axisAlong*.028);
   const float3x3 cFrame=float3x3(.48,.64,.60,-.80,.60,0,.36,.48,-.80);
   float3 cRot0=mul(cFrame,uSwirl);
   float3 cRot1=mul(cFrame,uLic);
   float4 tapA=tex3Dlod(_CloudDetailNoise,float4(cRot0*13.5+float3(0,0,altZ*.90)+(ice.b-.5)*.26,0));
   float4 tapB=tex3Dlod(_CloudDetailNoise,float4(cRot1*13.5+float3(0,0,altZ*.90)+(ice.b-.5)*.26,0));
   float streakA=(tapA.b+tapB.b)*.5;
   float streakB=(tapA.g+tapB.g)*.5;
   fine=saturate(streakA*.62+streakB*.38);
  }
  float highCover=highWeather*(.18+1.06*smoothstep(.18,.86,fine));
  high=OrbisHPHighDensity(z,.20+.70*highCover,.38,.58+.42*ice.r,ice.g)*highCover*1.18;
 }
 return float2(low,high)*_Density*max(0,_Cover);
}
#endif
