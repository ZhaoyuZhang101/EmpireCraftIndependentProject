#ifndef ORBIS_GEOGRAPHIC_COORDINATES
#define ORBIS_GEOGRAPHIC_COORDINATES
// Range reduction keeps the atan series within tan(pi/8). Geographic addresses
// retain subpixel precision at close zoom and at the poles on native GPUs.
float OrbisGeographicAtan2(float y,float x)
{
    float ax=abs(x),ay=abs(y);
    float ratio=min(ax,ay)/max(1e-30,max(ax,ay));
    float reduced=step(.41421356237,ratio);
    float t=lerp(ratio,(ratio-1)/(ratio+1),reduced),s=t*t;
    float angle=t*(1+s*(-1.0/3+s*(1.0/5+s*(-1.0/7+s*(1.0/9+s*(-1.0/11+s*(1.0/13+s*(-1.0/15+s/17))))))));
    angle+=reduced*.785398163397448;
    angle=ay>ax?1.570796326794897-angle:angle;
    angle=x<0?3.141592653589793-angle:angle;
    return y<0?-angle:angle;
}
float2 OrbisGeographicUV(float3 direction)
{
    return float2(frac(OrbisGeographicAtan2(direction.x,direction.z)/6.283185307179586+.5),
                  OrbisGeographicAtan2(direction.y,length(direction.xz))/3.141592653589793+.5);
}
float2 OrbisLocalGeographicUV(float3 center,float3 east,float2 xy)
{
    float longitude=OrbisGeographicAtan2(-east.z,east.x)+xy.x;
    float latitude=OrbisGeographicAtan2(center.y,length(center.xz))+xy.y;
    latitude=frac(latitude/6.283185307179586+.5)*6.283185307179586-3.141592653589793;
    if(abs(latitude)>1.570796326794897)
    {
        latitude=sign(latitude)*3.141592653589793-latitude;
        longitude+=3.141592653589793;
    }
    return float2(frac(longitude/6.283185307179586+.5),latitude/3.141592653589793+.5);
}
float3 OrbisLocalDirection(float3 center,float3 east,float3 north,float2 xy,float blend)
{
    float angle=length(xy);
    float3 tangent=east*xy.x+north*xy.y;
    float3 geodesic=center*cos(angle)+tangent*(angle<1e-5?1:sin(angle)/angle);
    float2 uv=OrbisLocalGeographicUV(center,east,xy);
    float longitude=(uv.x-.5)*6.283185307179586,latitude=(uv.y-.5)*3.141592653589793;
    float3 geographic=float3(sin(longitude)*cos(latitude),sin(latitude),cos(longitude)*cos(latitude));
    return normalize(lerp(geodesic,geographic,blend));
}
#endif
