#version 300 es
precision highp float;
precision highp int;
precision highp sampler3D;
#define UNITY_COLORSPACE_GAMMA
in vec2 vUV;
out vec4 targetColor;
struct v2f_img {vec2 uv;};
vec3 GammaToLinearSpace(vec3 s){return s*(s*(s*.305306011+.682171111)+.012522878);}
float saturate(float x){return clamp(x,0.0,1.0);}
vec2 saturate(vec2 x){return clamp(x,0.0,1.0);}
vec3 saturate(vec3 x){return clamp(x,0.0,1.0);}
vec4 saturate(vec4 x){return clamp(x,0.0,1.0);}
vec4 hlslTextureLod(sampler2D s,vec4 uv){return textureLod(s,uv.xy,uv.w);}
void OrbisPatchClip(vec3 p,vec2 q){}
uniform sampler3D _CloudNoise;
vec4 hlslTexture3DLod(sampler3D s,vec4 uv){return textureLod(s,uv.xyz,uv.w);}uniform float _Density,_Cover,_ProbeMode,_ProbeOffset,_ProbeAlpha;
#define ORBIS_PI 3.14159265359
vec2 OrbisWeatherUV(vec3 p)
{
    p=normalize(p);
    float longitude=abs(p.x)+abs(p.z)<1e-7?0.0:atan(p.x,p.z);
    return vec2(longitude/(2.0*ORBIS_PI)+.5,asin(clamp(p.y,-1.0,1.0))/ORBIS_PI+.5);
}
vec2 OrbisSphere(vec3 origin,vec3 direction,float radius)
{
    float b=dot(origin,direction),c=dot(origin,origin)-radius*radius;
    float d=b*b-c;
    if(d<0.0)return vec2(1e8,-1e8);
    float h=sqrt(max(0.0,d));return vec2(-b-h,-b+h);
}
float OrbisSunVisible(vec3 p,vec3 sun)
{
    // Offset OUTWARD, not along the sun ray: a light-directed offset can put
    // a night-side surface origin inside the sphere and incorrectly declare it lit.
    float radius=length(p);
    if(radius<.999999)return 0.0;
    vec2 hit=OrbisSphere(p+(p/max(radius,1e-6))*.00001,sun,1.0);
    return (hit.y>0.0 && hit.x>=0.0)?0.0:1.0;
}
#ifndef ORBIS_SCROLL_CLOUD_SOURCE
#define ORBIS_SCROLL_CLOUD_SOURCE
#ifndef ORBIS_SCROLL_HEIGHT
#define ORBIS_SCROLL_HEIGHT
uniform vec2 _ScrollPoleHeights;
uniform float _ScrollHeightScale;
float ScrollRadius(vec4 field,vec2 uv,float rows)
{
 float h=clamp(field.r+(field.g>0.0?field.b:0.0),0.0,4000.0);
 float polar=mix(_ScrollPoleHeights.x,_ScrollPoleHeights.y,step(.5,uv.y));
 float blend=smoothstep(0.0,1.0,min(uv.y,1.0-uv.y)*rows);
 return 1.0+mix(polar,h,blend)*_ScrollHeightScale;
}
#endif

uniform sampler2D _ScrollWeatherTex, _ScrollHeightTex;
uniform vec4 _ScrollSourceSize;
uniform float _ScrollWeatherTime;
float ScrollCloudGround(vec2 uv)
{
 return ScrollRadius(hlslTextureLod(_ScrollHeightTex,vec4(uv,0.0,0.0)),uv,_ScrollSourceSize.y);
}
// Uses the SAME spherical coordinates on the globe and every repeated 2.0D sheet.
float ScrollClimateDensity(vec3 p)
{
 vec2 uv=OrbisWeatherUV(p);
 float ground=ScrollCloudGround(uv);
 float z=(length(p)-ground)/.000008;
 if(z<400.0||z>13500.0)return 0.0;
 vec4 weather=hlslTextureLod(_ScrollWeatherTex,vec4(uv,0.0,0.0));
 if(weather.r+weather.g<.015)return 0.0;
 vec3 up=normalize(p);
 float angle=_ScrollWeatherTime*.005,s=sin(angle),c=cos(angle);
 vec3 advected=vec3(c*up.x+s*up.z,up.y,-s*up.x+c*up.z);
 vec3 q=advected*vec3(9.0,9.0,9.0)+vec3(.13,.71,.37)+up*(z*.00028);
 float shape=hlslTexture3DLod(_CloudNoise,vec4(q,0.0)).r;
 float erosion=hlslTexture3DLod(_CloudNoise,vec4(q*2.73+vec3(.2,.1,.7),0.0)).g;
 float body=saturate((weather.r-.12)*1.7);
 float profile=smoothstep(450.0,1150.0,z)*(1.0-smoothstep(2100.0+weather.r*2400.0,5300.0+weather.r*1300.0,z));
 float sculpt=smoothstep(.30-body*.16,.64-body*.12,shape-(1.0-erosion)*.10);
 float low=body*profile*sculpt*1.7;
 float high=weather.g*exp(-pow((z-9000.0)/1900.0,2.0))*smoothstep(.25,.68,shape)*.42;
 return (low+high)*_Density*max(0.0,_Cover);
}
#endif

uniform float _ScrollCloudSource,_LightSteps,_CloudMS;
#define ORBIS_CLOUD_TOP 1.190
#define ORBIS_CLOUD_BASE 1.0004
#define ORBIS_EXTINCTION 135.0
float OrbisCloudDensity(vec3 p){return ScrollClimateDensity(p);}
float OrbisLightTau(float index,vec4 first,vec2 last)
{
 return index==0.0?first.x:index==1.0?first.y:index==2.0?first.z:index==3.0?first.w:index==4.0?last.x:last.y;
}
vec2 OrbisLightSegment(float index,float count,float span)
{
 float a=index/count,b=(index+1.0)/count;
 if(_ScrollCloudSource>.5){a*=a;b*=b;}
 return vec2(a,b)*span;
}
vec2 LightField(vec3 p,vec3 sun)
{
 if(OrbisSunVisible(p,sun)<.5)return vec2(0.0,0.0);
 float lengthToExit=max(0.0,OrbisSphere(p,sun,ORBIS_CLOUD_TOP).y);
 vec2 inner=OrbisSphere(p,sun,ORBIS_CLOUD_BASE);
 if(inner.x>0.0)lengthToExit=min(lengthToExit,inner.x);
 if(lengthToExit<=0.0)return vec2(1.0,0.0);
 // The loop cap and quadrature divisor MUST use the same bounded count.
 float lightSteps=clamp(_LightSteps,1.0,6.0);
 float ds=lengthToExit/lightSteps;
 vec4 tauFirst=vec4(0.0);vec2 tauLast=vec2(0.0);float totalTau=0.0;
   for(float j=0.0;j<6.0;j++)
 {
  if(j>=lightSteps)break;
  vec2 segment=OrbisLightSegment(j,lightSteps,lengthToExit);
  float t=(segment.x+segment.y)*.5;
  float sampleTau=OrbisCloudDensity(p+sun*t)*ORBIS_EXTINCTION*(segment.y-segment.x);
  if(j==0.0)tauFirst.x=sampleTau;
  else if(j==1.0)tauFirst.y=sampleTau;
  else if(j==2.0)tauFirst.z=sampleTau;
  else if(j==3.0)tauFirst.w=sampleTau;
  else if(j==4.0)tauLast.x=sampleTau;
  else tauLast.y=sampleTau;
  totalTau+=sampleTau;
 }
 float direct=exp(-min(totalTau,30.0));
 if(_CloudMS<.5)return vec2(direct,0.0);
 // Reduced HP-inspired isotropic diffuse-source integral, NOT the author's full
 // HDRP phi_fwd implementation. Attribution/derivation and scope in documentation.
 // albedo=.999 -> sqrt(3.0*(1.0-albedo))=.05477 in isotropic optical-depth space.
 float diffuse=0.0,travelTau=0.0;
   for(float k=0.0;k<6.0;k++)
 {
  if(k>=lightSteps)break;
  float localTau=OrbisLightTau(k,tauFirst,tauLast);
  float entryTau=max(0.0,totalTau-travelTau-localTau*.5);
  float isotropization=1.0-exp(-.35*entryTau);
  float absorption=exp(-.001*entryTau);
  vec2 segment=OrbisLightSegment(k,lightSteps,lengthToExit);
  float kernel=exp(-.05477*(travelTau+localTau*.5))/(1.0+(segment.x+segment.y)*.5*22.0);
  diffuse+=kernel*absorption*isotropization*localTau;
  travelTau+=localTau;
 }
 float boundaryConfidence=saturate(lengthToExit/.015);
 float thinSuppression=1.0-exp(-totalTau*1.2);
 diffuse=min(1.4,diffuse*.28)*thinSuppression*boundaryConfidence;
 return vec2(direct,diffuse);
}


struct v2f {vec4 pos;vec2 uv;};
vec4 frag(v2f i)
{
 vec2 uv=i.uv+vec2(_ProbeOffset,0.0);
 vec3 up,ro,rd;float begin=0.0,end=.20;
 vec3 bg=vec3(.07,.20,.28);
 if(_ProbeMode<.5)
 {
  float lon=(uv.x-.5)*6.283185307,lat=(uv.y-.5)*3.141592654;
  up=vec3(sin(lon)*cos(lat),sin(lat),cos(lon)*cos(lat));
  ro=up*1.20;rd=-up;end=1.20-ScrollCloudGround(uv);
 }
 else
 {
  ro=vec3(0.0,0.0,3.1);rd=normalize(vec3((uv-.5)*2.5,-2.3));
  float b=dot(ro,rd),disc=b*b-dot(ro,ro)+1.2*1.2;
  if(disc<0.0)return vec4(0.0,0.0,0.0,1.0);
  begin=-b-sqrt(disc);end=-b+sqrt(disc);
  float groundDisc=b*b-dot(ro,ro)+1.024*1.024;
  if(groundDisc>0.0)end=min(end,-b-sqrt(groundDisc));
  bg=groundDisc>0.0?vec3(.10,.27,.18):vec3(0.0,0.0,0.0);
 }
 float stepSize=(end-begin)/64.0,transmission=1.0;vec3 light=vec3(0.0,0.0,0.0);
 vec3 sun=normalize(vec3(.4,.5,.8));
  for(float j=0.0;j<64.0;j++)
 {
  vec3 at=ro+rd*(begin+(j+.5)*stepSize);
  float density=ScrollClimateDensity(at);
  float shadow=LightField(at,sun).x;
  float phase=.24+.76*shadow*smoothstep(-.15,.2,dot(normalize(at),sun));
  float removed=1.0-exp(-density*135.0*stepSize);
  light+=transmission*vec3(.90,.94,1.0)*phase*removed;transmission*=1.0-removed;
 }
 if(_ProbeAlpha>.5)return vec4(vec3(1.0-transmission,1.0-transmission,1.0-transmission),1.0);
 return vec4(bg*transmission+light,1.0);
}

void main(){v2f i;i.uv=vUV;i.pos=gl_FragCoord;targetColor=frag(i);}