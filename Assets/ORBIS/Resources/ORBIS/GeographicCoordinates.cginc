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
float3 OrbisLocalDirection(float3 center,float3 east,float3 north,float2 xy,float blend)
{
    float angle=length(xy);
    float3 tangent=east*xy.x+north*xy.y;
    float3 geodesic=center*cos(angle)+tangent*(angle<1e-5?1:sin(angle)/angle);
    float squared=dot(xy,xy)*.25;
    float3 conformal=(center*(1-squared)+tangent)/(1+squared);
    return normalize(lerp(geodesic,conformal,blend));
}
#endif
