
#define ORBIS_PI 3.14159265359
float saturate(float v){return clamp(v,0.0,1.0);}
vec2 OrbisSphere(vec3 origin,vec3 direction,float radius){float b=dot(origin,direction),c=dot(origin,origin)-radius*radius;float d=b*b-c;if(d<0.0)return vec2(1e8,-1e8);float h=sqrt(max(0.0,d));return vec2(-b-h,-b+h);}
float OrbisSunVisible(vec3 p,vec3 sun){float r=length(p);if(r<.999999)return 0.0;vec2 hit=OrbisSphere(p+p/max(r,1e-6)*.00001,sun,1.0);return hit.y>0.0&&hit.x>=0.0?0.0:1.0;}


#ifndef ORBIS_ATMOSPHERE_GLSL_PORT
#define ORBIS_ATMOSPHERE_GLSL_PORT






uniform float _Atmosphere,_AirDensity,_Haze,_SolarEnergy;
uniform vec4 _AtmoBetaRayleigh;   
uniform vec4 _AtmoBetaMie;        
uniform vec4 _AtmoBetaAbsorption; 
uniform vec4 _AtmoParameters;    
uniform vec2 _AtmoSunDisk;       
#define ORBIS_AIR_RADIUS 1.20

vec3 OrbisGLSLDensity(vec3 p)
{
    float h=length(p)-1.0;
    if(h<0.0||h>=ORBIS_AIR_RADIUS-1.0)return vec3(0);
    float ray=exp(-h/max(1e-5,_AtmoBetaRayleigh.w));
    float mie=exp(-h/max(1e-5,_AtmoBetaMie.w));
    float denom=(_AtmoBetaAbsorption.w-h)/max(1e-5,_AtmoParameters.x);
    float ozone=ray/(1.0+denom*denom);
    
    
    float top=1.0-smoothstep(.188,.20,h);
    return vec3(ray,mie,ozone)*top*max(0.0,_AirDensity)*saturate(_Atmosphere);
}
vec3 OrbisGLSLExtinction(vec3 rho)
{
    return rho.x*_AtmoBetaRayleigh.xyz+rho.y*_AtmoBetaMie.xyz*max(0.0,_Haze)
          +rho.z*_AtmoBetaAbsorption.xyz;
}
vec2 OrbisGLSLPhase(float cosine)
{
    float mu=clamp(cosine,-1.0,1.0),g=clamp(_AtmoParameters.y,-.95,.95);
    float mu2=mu*mu,gg=g*g;
    float ray=3.0/(16.0*ORBIS_PI)*(1.0+mu2);
    
    
    float mie=3.0/(8.0*ORBIS_PI)*(1.0-gg)*(1.0+mu2)
             /((2.0+gg)*pow(max(1e-4,1.0+gg-2.0*g*mu),1.5));
    return vec2(ray,mie);
}
vec2 OrbisGLSLViewInterval(vec3 ro,vec3 rd,float maxDistance)
{
    vec2 outer=OrbisSphere(ro,rd,ORBIS_AIR_RADIUS);
    float begin=max(0.0,outer.x),end=min(outer.y,maxDistance);
    
    
    vec2 ground=OrbisSphere(ro,rd,1.0);
    if(ground.y>0.0&&ground.x>=0.0)end=min(end,ground.x);
    if(dot(ro,ro)<.999998)return vec2(1,0);
    return vec2(begin,end);
}
vec3 OrbisGLSLSunTransmittance(vec3 p,vec3 sun)
{
    
    
    if(OrbisSunVisible(p,sun)<.5)return vec3(0);
    if(_Atmosphere<.5||_AirDensity<=0.0)return vec3(1);
    vec2 outer=OrbisSphere(p,sun,ORBIS_AIR_RADIUS);
    float begin=max(0.0,outer.x),end=outer.y;
    if(end<=begin)return vec3(1);
    float closest=clamp(-dot(p,sun),begin,end),total=end-begin;
    vec3 tau=vec3(0);
    int budget=clamp(int(_AtmoParameters.w),4,24);
    
    
     for(int side=0;side<2;side++)
    {
        float span=side==0?closest-begin:end-closest;
        if(span<1e-7)continue;
        int count=clamp(int(ceil(float(budget)*span/max(total,1e-6))),4,24);
         for(int j=0;j<24;j++)
        {
            if(j>=count)break;
            float u0=float(j)/float(count),u1=float(j+1)/float(count);
            float a=span*u0*u0,b=span*u1*u1;
            float t=closest+(side==0?-1.0:1.0)*(.5*(a+b));
            tau+=OrbisGLSLExtinction(OrbisGLSLDensity(p+sun*t))*(b-a);
        }
    }
    return exp(-min(max(tau,0.0),60.0));
}
vec3 OrbisGLSLSource(vec3 rho,vec2 phase,vec3 sunT)
{
    return (rho.x*_AtmoBetaRayleigh.xyz*phase.x+
            rho.y*_AtmoBetaMie.xyz*max(0.0,_Haze)*phase.y)
            *sunT*max(0.0,_AtmoParameters.z)*max(0.0,_SolarEnergy);
}
void OrbisGLSLAccumulate(vec3 source,vec3 sigma,float ds,inout vec3 light,inout vec3 transmission)
{
    
    
    
    vec3 tau=max(sigma,0.0)*max(ds,0.0);
    vec3 stepT=exp(-min(tau,60.0));
    vec3 small=tau*(1.0-tau*.5+tau*tau/6.0);
    vec3 removed=mix(small,1.0-stepT,step(.001,tau));
    light+=transmission*source*(removed/max(sigma,1e-8));
    transmission*=stepT;
}
#endif
