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
// Cloud translucency globals (0 = reference look): _CloudCoverCut removes the thinnest
// baked veil, _CloudThinFade lowers the density of thin cover while cores stay opaque,
// _CloudHighCut thins cirrus, _CloudDensityCut scales all low-cloud density,
// _CloudThinEdge moves the thin-cover fade toward thicker cover (sparser white),
// _CloudStreak breaks uniform cirrus film into wispy streaks.
// _CloudSoftLod reads a coarser weather mip so texel-size cloud specks feather out from orbit.
float _CloudCoverCut,_CloudThinFade,_CloudHighCut,_CloudDensityCut,_CloudThinEdge,_CloudStreak,_CloudSoftLod;
// Orbit veil (0 = off): from orbit a cloud is a translucent sheet whose optical depth follows
// the baked cover continuously, alpha = 1 - exp(-tau); only the highest cover turns opaque.
// _CloudOrbitTau scales the low-cloud optical depth, _CloudVeilStart is the cover where it
// starts, _CloudOrbitHighTau is the optical depth of full cirrus cover.
// _CloudOrbitCore adds the optical depth of the few near-overcast cores that read as solid white.
// _CloudOrbitSmooth low-passes the inside of cloud sheets (a coarser weather mip and less
// thickness noise) while edges keep their detail.
// _CloudOrbitVolume keeps the volumetric cloud in the thickest cores, so they stand out of the veil in relief.
// _CloudOrbitCoreStart is the baked cover where that opaque core begins.
float _CloudOrbitVeil,_CloudOrbitTau,_CloudVeilStart,_CloudOrbitHighTau,_CloudOrbitCore,_CloudOrbitSmooth,_CloudOrbitVolume,_CloudOrbitCoreStart;
// _CloudOrbitVolumeStart is the baked cover above which a core turns volumetric.
float _CloudOrbitVolumeStart;

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
 if(dist>=extent*1.28)return;
 float r=dist/extent;
 float maturity=smoothstep(.44,.76,centre.w);
 float altNorm=saturate((z-500.0)/11900.0);

 // Outward-sloping 3D Stadium Bowl: eye radius expands ~4x from sea surface to tropopause!
 float stadiumR=lerp(.022,.090,pow(altNorm,.78));
 float eyeClear=lerp(1.0,smoothstep(stadiumR*.50,stadiumR*1.10,r),maturity);
 // Subtle low-level mesovortex "hub cloud" stratocumulus swirl at the bottom of the 12 km stadium bowl
 // (seen inside Category-5 Hurricane Isabel's eye from the ISS — gives instant vertical depth scale!)
 float hubZone=(1.0-smoothstep(950.0,1750.0,z))*smoothstep(420.0,680.0,z)*(1.0-smoothstep(stadiumR*.32,stadiumR*.92,r))*maturity;
 coverage=max(coverage*eyeClear,hubZone*.54);
 highCov*=lerp(1.0,smoothstep(stadiumR*.70,stadiumR*1.24,r),maturity);

 // Organic swirling Central Dense Overcast (CDO) cohesion & towering 12.6 km stadium eyewall rim
 // (preserves rich 3D spiral cloud-top relief while preventing dark-gray mottled holes inside the sunlit CDO!)
 float stormCdo=(1.0-smoothstep(.16,.52,r))*smoothstep(.24,.68,centre.w)*eyeClear;
 float cdoCap=stormCdo*(1.0-smoothstep(11800.0,12900.0,z));
 float wallCollar=exp(-pow((r-(stadiumR+.046))/.060,2.0))*maturity*(1.0-smoothstep(12100.0,13000.0,z));
 coverage=max(coverage,max(wallCollar*.94,cdoCap*.76)*eyeClear);
 highCov=max(highCov,stormCdo*maturity*.76*smoothstep(7400.0,9200.0,z));
 convection=lerp(max(convection,max(wallCollar*.98,cdoCap*.86)),.12,hubZone*.92);
 cdoSmooth=max(cdoSmooth,stormCdo);

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
 float4 weather=tex2Dlod(_ScrollWeatherTex,float4(uv,0,max(0,_CloudSoftLod)));
 float coverage=weather.r,highWeather=weather.g,convection=weather.b;
 float bakedCover=coverage,bakedHigh=highWeather;
 float orbit=saturate(_CloudOrbitVeil);
 float coreVolume=saturate(_CloudOrbitVolume)*orbit*smoothstep(_CloudOrbitVolumeStart,_CloudOrbitVolumeStart+.14,bakedCover);
 float coverCut=_CloudCoverCut;
 coverage=saturate((coverage-coverCut)/max(.05,1.0-coverCut));
 highWeather*=1.0-saturate(_CloudHighCut);
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

 float cdoSmooth=0;
 if(hasCyclone)
 {
  ApplyTyphoon3DStadium(up,z,_OrbisCycloneNorth,1.0,_OrbisCyclonePhase.x,coverage,highWeather,convection,cdoSmooth,a,b);
  ApplyTyphoon3DStadium(up,z,_OrbisCycloneSouth,-1.0,_OrbisCyclonePhase.y,coverage,highWeather,convection,cdoSmooth,a,b);
 }

 if(coverage+highWeather<.006)return 0;
 float low=0,high=0;
 if(coverage>.004&&z<13100&&(orbit<.999||coreVolume>.002))
 {
  float2 span=OrbisCloudVerticalSpan(convection,saturate(coverage+.12));
  bool inMidBand=convection>.28&&convection<.76&&z>3800.0&&z<6150.0;
  if((z>span.x-90.0&&z<span.y*1.14+300.0)||inMidBand)
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
    // Sync simpleMode frequency (30.0) & vertical shear (1.12) with the primary body pass so LightField
    // sun-shadow probes NEVER cast phantom dark-gray spots from mismatched 19.2 vs 30.0 cloud-top bumps!
    float3 bPos=q0*30.0+radAxis*(altitude*1.12)+windShear+float3(.41,.19,.83);
    float4 body=tex3Dlod(_CloudNoise,float4(bPos,0));
    worleyMeso=body.g*.64+body.b*.36;
    float baseS=body.r*.68+worleyMeso*.32;
    baseS=lerp(baseS,.68+.32*baseS,cdoSmooth*.68);
    worleyMeso=lerp(worleyMeso,.66+.34*worleyMeso,cdoSmooth*.65);
    shape=saturate((baseS-(1.0-worleyMeso)*.16*(1.0-.60*cdoSmooth))/.84+coverage*.06+.10*smoothstep(.06,.52,coverage));
    effCov=saturate(coverage+.04);
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

    float3 mesoWarp=(macroBody.gba-.5)*.11;
    float3 bPos0=q0*30.0+radAxis*(altitude*1.12)+windShear+mesoWarp+float3(.41,.19,.83);
    float3 bPos1=q1*30.0+radAxis*(altitude*1.12)+windShear+mesoWarp+float3(.41,.19,.83);
    float4 bodyRaw=lerp(tex3Dlod(_CloudNoise,float4(bPos0,0)),tex3Dlod(_CloudNoise,float4(bPos1,0)),phase);
    float4 body=saturate(.5+(bodyRaw-.5)*lerp(1.0,invStd,.78));

    worleyMeso=body.g*.64+body.b*.36;
    float basePW=macroBody.r*.32+body.r*.42+worleyMeso*.26;
    // Inside a mature typhoon CDO canopy, gently lift 3D noise floor to keep creamy swirling tops without gray holes!
    basePW=lerp(basePW,.68+.32*basePW,cdoSmooth*.68);
    worleyMeso=lerp(worleyMeso,.66+.34*worleyMeso,cdoSmooth*.65);
    // Keep soft synoptic mist envelopes connected while 3D fine detail sculpts lacy micro-structure
    float mistCohesion=.12*smoothstep(.06,.52,coverage);
    shape=saturate((basePW-(1.0-worleyMeso)*.16*(1.0-.60*cdoSmooth))/.84+coverage*.06+mistCohesion);

    float covBound=.16+.84*saturate(coverage+.16);
    if(pow(shape,.68)>.86-covBound||coverage>.52||inMidBand)
    {
     float3 curl=(body.gba-.5)*.24;
     // High-frequency 3D cellular granulation (k=112 -> authentic 20-45 km EPIC marine cumulus & open-cell lace!)
     float3 dPos0=q0*112.0+radAxis*(altitude*2.85)+windShear*1.45+curl;
     float3 dPos1=q1*112.0+radAxis*(altitude*2.85)+windShear*1.45+curl;
     float4 eroRaw=lerp(tex3Dlod(_CloudDetailNoise,float4(dPos0,0)),tex3Dlod(_CloudDetailNoise,float4(dPos1,0)),phase);
     erosion=saturate(.5+(eroRaw-.5)*lerp(1.0,invStd,.85));

     // Fine-grained marine Sc & subtropical trade cumulus speckling (zero extra texture reads!):
     // Turns broad thin marine sheets into thousands of delicate 20-45 km milky puffs & lacy open cells like DSCOVR EPIC!
     float fineScMask=1.0-smoothstep(.08,.34,convection);
     float fineTradeMask=smoothstep(.08,.22,convection)*(1.0-smoothstep(.34,.56,convection));
     float fineLaceWeight=saturate(fineScMask*.85+fineTradeMask*.58)*(1.0-smoothstep(.52,.84,coverage))*(1.0-cdoSmooth);
     float fineCellular=saturate((1.0-(erosion.r*.58+erosion.g*.42))*.72+erosion.b*.28);
     float openCellFoam=smoothstep(.26,.78,abs(2.0*erosion.g-1.0)*.65+fineCellular*.35);
     float finePattern=lerp(fineCellular,openCellFoam,fineScMask*.55);
     shape=lerp(shape,saturate(shape*.48+finePattern*.66),fineLaceWeight*.72);
     worleyMeso=lerp(worleyMeso,finePattern,fineLaceWeight*.65);
     // Keep sunlit typhoon CDO tops silky & creamy with gentle overshooting cloud relief
     erosion=lerp(erosion,float4(.72,.72,.54,.54),cdoSmooth*.46);

     float edgeInvW=1.0-(erosion.r*.62+erosion.g*.38);
     float edgeTear=edgeInvW*.13*(1.0-smoothstep(.58,.90,coverage))*smoothstep(.02,.22,coverage)*(1.0-.88*cdoSmooth);
     effCov=saturate(coverage-edgeTear);
     evalLow=true;
    }
   }
   if(evalLow)
   {
    float tower=smoothstep(.45,.96,convection);
    // True EPIC exponential vapor curve: airy translucent sea-mist & fine marine lace (0.04-0.28)
    // transitioning smoothly into dazzling luminous snow-white convective & frontal cores (0.90-1.85)!
    float thinMist=lerp(.040,.27,smoothstep(.02,.44,effCov));
    float denseCore=1.48*pow(smoothstep(.36,.92,effCov),1.48);
    float opticalGain=(thinMist+denseCore)*lerp(.84,1.32,tower);
    low=OrbisHPLowDensityDetailed(z,.14+.86*effCov,convection,shape,erosion,.42+.58*worleyMeso)*opticalGain;
   }
  }
 }
 if(highWeather>.004&&z>6950&&orbit<.999)
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
  float highMod=lerp(.26+.94*smoothstep(.15,.84,fine),.74+.34*fine,cdoSmooth*.78);
  float highCover=highWeather*highMod;
  // Wispy cirrus streaks, except inside a cyclone's smooth central overcast.
  highCover*=lerp(1.0,smoothstep(.34,.70,fine)*1.5,saturate(_CloudStreak)*(1.0-saturate(cdoSmooth)));
  high=OrbisHPHighDensity(z,.16+.74*highCover,.36,lerp(.56+.44*ice.r,.84+.16*ice.r,cdoSmooth*.75),ice.g)*highCover*1.16;
 }
 if(orbit>0)
 {
  // 720 is ORBIS_EXTINCTION: density * 720 * path (in radii) is optical depth.
  float perMetre=720.0*_CloudHeightScale;
  // One noise read frays the sheet below the weather map's texel size: it erodes edges and
  // varies the thickness, so veils look wispy instead of showing texture steps.
  float4 fray=tex3Dlod(_CloudNoise,float4(up*23.0+float3(.41,.19,.83),0));
  float wisp=fray.r*.6+fray.g*.4;
  // Undo the volumetric cover cut: the veil follows the baked cover itself.
  float veilCover=coverage>.001?coverage*max(.05,1.0-coverCut)+coverCut:bakedCover;
  // Layers: a smooth low veil, fibrous cirrus above it, and volumetric cores standing out of it.
  float smoothing=saturate(_CloudOrbitSmooth),softHigh=bakedHigh;
  [branch] if(smoothing>.001)
  {
   float4 soft=tex2Dlod(_ScrollWeatherTex,float4(uv,0,2));
   float interior=smoothstep(.34,.62,soft.r)*smoothing;
   // Keep explicit cyclone structure, which is written into the fine cover only.
   interior*=saturate(1.0-abs(veilCover-bakedCover)*8.0);
   veilCover=lerp(veilCover,soft.r,interior);softHigh=lerp(bakedHigh,soft.g,smoothing*.35);
   wisp=lerp(wisp,.5,interior*.75);
  }
  float frayed=veilCover+(wisp-.5)*.16*smoothstep(.02,.20,veilCover);
  float x=saturate((frayed-_CloudVeilStart)/max(.05,1.0-_CloudVeilStart));
  float core=smoothstep(_CloudOrbitCoreStart,_CloudOrbitCoreStart+.20,frayed);
  float tau=(_CloudOrbitTau*pow(x,1.2)/(1.08-x)*(.45+1.1*wisp)+_CloudOrbitCore*core*core)*lerp(1.0,2.2,smoothstep(.58,.92,convection));
  float2 slab=OrbisCloudVerticalSpan(convection,saturate(veilCover+.12));
  float thick=max(400.0,slab.y-slab.x),hLow=(z-slab.x)/thick;
  float lowSlab=tau*smoothstep(0,.15,hLow)*(1.0-smoothstep(.8,1.0,hLow))/(perMetre*thick*.825);
  float shellFill=smoothstep(.90,1.0,orbit);
  lowSlab=lerp(lowSlab,tau/(perMetre*13100.0),shellFill);
  float hc=pow(saturate(softHigh),.65);
  float hb=8350.0-1150.0*hc,ht=9100.0+4050.0*hc;
  float tauHigh=_CloudOrbitHighTau*pow(saturate(softHigh),1.3)*(.5+fray.b);
  float highSlab=tauHigh*smoothstep(hb-350.0,hb+350.0,z)*(1.0-smoothstep(ht-350.0,ht+350.0,z))/(perMetre*max(700.0,ht-hb));
  highSlab=lerp(highSlab,tauHigh/(perMetre*13100.0),shellFill);
  // The dense volumetric cloud hands over early, so mid-zoom is not thicker than either end.
  float volume=(1.0-orbit)*(1.0-orbit)*(1.0-orbit);
  low=low*max(volume,coreVolume)+lowSlab*orbit;high=high*volume+highSlab*orbit;
 }
 float thinEdge=saturate(_CloudThinEdge);
 float thinFade=smoothstep(lerp(.25,.55,thinEdge),lerp(.85,.97,thinEdge),bakedCover);
 low*=lerp(lerp(1.0,thinFade*sqrt(thinFade),saturate(_CloudThinFade))*(1.0-saturate(_CloudDensityCut)),1.0,orbit);
 return float2(low,high)*_Density*max(0,_Cover);
}
#endif
