#ifndef ORBIS_FLAT_PRECIPITATION
#define ORBIS_FLAT_PRECIPITATION

#include "FlatVegetationWind.cginc"
#include "FlatCycloneWind.cginc"

struct FlatPrecipitation
{
    float rain, streak, spray, ripple, flake;
};

// Each event owns a geographic footprint. The drop approaches its own impact
// point, then the same event opens a ring or throws spray at that point.
float3 FlatRainEvent(float2 cell,float spacing,float salt,float speed,float2 wind,float2 rimLight,
                     float latitudeScale,float pixelsPerTile,float rain)
{
    float2 count=max(1,floor(_MapSize.xy*spacing+.5));
    float2 q=cell*_MapSize.zw*count+salt;
    q.x+=frac(floor(q.y)*.618034)*.85;
    float2 id=floor(q);
    id.x-=floor(id.x/count.x)*count.x;
    float cycle=_SkyClock*speed+FlatDetailHash(float3(id,salt));
    float phase=frac(cycle);
    float3 eventID=float3(id,fmod(floor(cycle),1024.0)+salt);
    float lottery=FlatDetailHash(eventID+41.9);
    float active=smoothstep(.98-rain*.90,1.02-rain*.90,lottery);
    float2 landing=float2(.27+.46*FlatDetailHash(eventID+13.4),.21+.19*FlatDetailHash(eventID+28.7));
    float2 p=frac(q)-landing;
    p.y/=latitudeScale;

    // East/north wind displaces the drop downwind during descent. Projected
    // vertical height shortens as the drop reaches the surface.
    float2 lift=float2(0,.40)-wind*.135;
    float fall=saturate(phase/.57);
    float2 head=lift*(1-fall*fall);
    float2 tail=lift*min(fall*fall,.18+.46*rain);
    float2 delta=p-head;
    float along=saturate(dot(delta,tail)/max(.00001,dot(tail,tail)));
    float aa=clamp(.62*spacing/max(1,pixelsPerTile),.006,.095);
    float distanceToDrop=length(delta-tail*along);
    float streak=(1-smoothstep(.010,.016+aa,distanceToDrop))*(1-along*.76);
    streak*=smoothstep(.01,.14,fall)*(1-smoothstep(.55,.575,phase));

    float age=saturate((phase-.57)/.43);
    float impact=smoothstep(.56,.59,phase)*(1-smoothstep(.35,1,age));
    float radius=lerp(.016,.16,age*(1.50-.50*age));
    float2 ringPoint=p*float2(1,1.12);
    float radialDistance=length(ringPoint);
    float litArc=smoothstep(-.65,.75,dot(ringPoint,rimLight)/max(.012,radialDistance));
    float ring=1-smoothstep(.009,.012+aa,abs(radialDistance-radius));
    float echo=1-smoothstep(.006,.009+aa,abs(radialDistance-radius*.57));
    ring=(ring+echo*.23*smoothstep(.3,.6,age))*(.18+.82*litArc);
    float2 ejecta=float2(radius*.65,age*(1-age)*.27);
    float spray=1-smoothstep(.014,.022+aa,length(float2(abs(p.x),p.y)-ejecta));
    return float3(streak,spray*impact*(1-age),ring*impact)*active;
}

float FlatSnowEvent(float2 cell,float salt,float2 wind,float latitudeScale,float pixelsPerTile,float snow)
{
    float2 count=max(1,floor(_MapSize.xy*1.5+.5));
    float2 q=cell*_MapSize.zw*count+salt;
    q.x+=frac(floor(q.y)*.414214)*.8;
    float2 id=floor(q);
    id.x-=floor(id.x/count.x)*count.x;
    float cycle=_SkyClock*.78+FlatDetailHash(float3(id,salt));
    float phase=frac(cycle);
    float3 eventID=float3(id,fmod(floor(cycle),1024.0)+salt);
    float seed=FlatDetailHash(eventID+6.2);
    float2 centre=float2(.32+.36*seed,.22+.20*FlatDetailHash(eventID+17.3));
    float2 drift=float2(sin(phase*5.7+seed*6.28)*.075,.35*(1-phase))+wind*(phase-.6)*.13;
    float2 p=frac(q)-centre;
    p.y/=latitudeScale;
    p-=drift;
    float aa=clamp(.75/max(1,pixelsPerTile),.006,.09);
    float flake=1-smoothstep(.018,.043+aa,abs(p.x)+abs(p.y)*.84);
    return flake*smoothstep(.96-snow*.86,1-snow*.86,FlatDetailHash(eventID+32.4))
                *smoothstep(0,.14,phase)*(1-smoothstep(.73,.99,phase));
}

FlatPrecipitation FlatSamplePrecipitation(float2 uv,float2 cell,float4 cloud,float pixelsPerTile,float visible)
{
    FlatPrecipitation result=(FlatPrecipitation)0;
    float enabled=_FlatRain*_FlatClouds*_SkyReady*smoothstep(4,16,pixelsPerTile);
    // The HP column stores snow in B and liquid precipitation in A, after
    // testing temperature and integrating optical thickness. Reject clear edges.
    float rain=saturate(cloud.a*smoothstep(.52,.81,cloud.r)*enabled);
    float snow=saturate(cloud.b*smoothstep(.30,.68,max(cloud.r,cloud.g))*enabled);
    [branch] if(visible>.0001 && max(rain,snow)>.001)
    {
        float2 wind=FlatSeasonalWind(uv)+FlatCurrentCycloneWind(uv);
        wind*=min(1,2.2/max(.001,length(wind)));
        float latitudeScale=FlatArtworkLatitudeScale(uv.y);
        float close=smoothstep(9,27,pixelsPerTile);
        if(rain>.001)
        {
            float strength=sqrt(rain);
            float2 rimLight=FlatGeographicLight(uv,_FlatSun).xy*.60+float2(-.42,.63);
            rimLight*=rsqrt(max(.001,dot(rimLight,rimLight)));
            float3 drops=FlatRainEvent(cell,1.25,7.3,2.5,wind,rimLight,latitudeScale,pixelsPerTile,strength);
            drops+=FlatRainEvent(cell,2.0,23.7,3.4,wind,rimLight,latitudeScale,pixelsPerTile,strength)*.72;
            drops=saturate(drops)*strength*close;
            result.rain=rain;result.streak=drops.x;result.spray=drops.y;result.ripple=drops.z;
        }
        if(snow>.001)
        {
            float strength=sqrt(snow);
            result.flake=FlatSnowEvent(cell,11.7,wind,latitudeScale,pixelsPerTile,strength)*strength*close;
        }
    }
    return result;
}

float3 FlatRainOnSurface(float3 col,FlatPrecipitation weather,float water,float frozen,float3 light)
{
    float liquid=water*(1-frozen);
    float soil=(1-water)*(1-frozen);
    col*=1-soil*weather.rain*float3(.13,.10,.075);
    col=lerp(col,FlatColor(float3(.68,.75,.76))*light,soil*weather.spray*.42);
    return lerp(col,FlatColor(float3(.70,.84,.88))*light,liquid*weather.ripple*.58);
}

#endif
