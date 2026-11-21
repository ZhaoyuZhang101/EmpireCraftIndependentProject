#ifndef ORBIS_FLAT_SURFACE_FIELD
#define ORBIS_FLAT_SURFACE_FIELD
#include "FlatVisualTexture.cginc"
#include "FlatReliefProjection.cginc"
#include "../GeographicCoordinates.cginc"
float4 _MapSize;
#include "FlatEarthRelief.cginc"
float4 _PoleBedDepth;
// All meridians meet at one bed and depth. Preserve the measured terminal ring.
float4 FlatPolarField(float4 field,float latitudeUV)
{
    float2 pole=latitudeUV<.5?_PoleBedDepth.xy:_PoleBedDepth.zw;
    field.rb=lerp(pole,field.rb,OrbisPolarRingWeight(latitudeUV,_MapSize.y));
    return field;
}
float _FieldReady,_SmoothTerrain,_Vegetation,_Bathymetry,_Relief;
float _LocalPhysicalArtwork;
// One longitude cell sets the prop width. Match its ground length north/south
// around the fixed root. The local chart resolves the narrowest polar rows.
float FlatArtworkLatitudeScale(float latitudeUV)
{
    return max(lerp(.02,.000001,_LocalPhysicalArtwork),cos((latitudeUV-.5)*3.141592654));
}
float FlatTreeLatitudeScale(float2 tile,float rootY)
{return FlatArtworkLatitudeScale((tile.y+lerp(.5,rootY/32,_LocalPhysicalArtwork))*_MapSize.w);}
void FlatCellScreenDerivatives(float2 cell,out float2 dx,out float2 dy)
{
    dx=ddx(cell);dy=ddy(cell);
    dx.x-=round(dx.x/_MapSize.x)*_MapSize.x;dy.x-=round(dy.x/_MapSize.x)*_MapSize.x;
}
float4 FlatMinmod(float4 a,float4 b)
{
    return .5*(sign(a)+sign(b))*min(abs(a),abs(b));
}
float4 FlatCubic(float4 p0,float4 p1,float4 p2,float4 p3,float t)
{
    float4 m1=FlatMinmod(p1-p0,p2-p1),m2=FlatMinmod(p2-p1,p3-p2);
    float t2=t*t,t3=t2*t;
    return (2*t3-3*t2+1)*p1+(t3-2*t2+t)*m1+(-2*t3+3*t2)*p2+(t3-t2)*m2;
}
float4 FlatFieldRow(float2 centre,float t)
{
    float2 dx=float2(_MapSize.z,0);
    return FlatCubic(FlatVisualSample(float4(centre-dx,0,0)),FlatVisualSample(float4(centre,0,0)),
                     FlatVisualSample(float4(centre+dx,0,0)),FlatVisualSample(float4(centre+dx*2,0,0)),t);
}
float FlatShoreDetail(float2 cell)
{
    float2 offset=frac(cell)-.5;
    float anchor=smoothstep(0,.08,dot(offset,offset));
    float lon=(cell.x*_MapSize.z-.5)*6.283185307,lat=(cell.y*_MapSize.w-.5)*3.141592654;
    float3 p=float3(sin(lon)*cos(lat),sin(lat),cos(lon)*cos(lat));
    float detail=.65*sin(p.x*157+p.z*93+sin(p.y*83))+.35*sin(p.x*311-p.z*193+p.y*137);
    return detail*.045*anchor;
}
// R metres; G signed Euclidean shoreline (positive in area water); B metres depth;
// A climate-derived canopy density. No biome/owner/material ID is interpolated.
float4 FlatField(float2 cell)
{
    float2 p=cell-.5,baseCell=floor(p),t=frac(p),centre=(baseCell+.5)*_MapSize.zw,dy=float2(0,_MapSize.w);
    float4 field=FlatCubic(FlatFieldRow(centre-dy,t.x),FlatFieldRow(centre,t.x),
                          FlatFieldRow(centre+dy,t.x),FlatFieldRow(centre+dy*2,t.x),t.y);
    field.g+=FlatShoreDetail(cell)*(1-smoothstep(.2,.9,abs(field.g)));
    return FlatPolarField(field,cell.y*_MapSize.w);
}
float2 FlatBlendUV(float2 cell)
{
    float2 p=cell-.5,t=frac(p);t=t*t*(3-2*t);
    return (floor(p)+.5+t)*_MapSize.zw;
}
float3 FlatColor(float3 srgb)
{
#ifdef UNITY_COLORSPACE_GAMMA
    return srgb;
#else
    return GammaToLinearSpace(srgb);
#endif
}
float3 FlatRadial(float2 uv)
{
    float lon=(uv.x-.5)*6.283185307,lat=(uv.y-.5)*3.141592654,c=cos(lat);
    return float3(sin(lon)*c,sin(lat),cos(lon)*c);
}
float3 FlatGeographicLight(float2 uv,float3 direction)
{
    float longitude=(uv.x-.5)*6.283185307,latitude=(uv.y-.5)*3.141592654;
    float3 east=float3(cos(longitude),0,-sin(longitude));
    float3 north=float3(-sin(latitude)*sin(longitude),cos(latitude),-sin(latitude)*cos(longitude));
    return float3(dot(direction,east),dot(direction,north),dot(direction,FlatRadial(uv)));
}
void FlatChartGeographicBasis(float2 uv,float2 chart,out float2 right,out float2 up)
{
    float3 radial=FlatRadial(uv);
    float2 dx=ddx(chart),dy=ddy(chart);
    float determinant=dx.x*dy.y-dx.y*dy.x;
    float inverse=(determinant<0?-1:1)/max(1e-20,abs(determinant));
    float3 horizontal=(ddx(radial)*dy.y-ddy(radial)*dx.y)*inverse;
    float3 vertical=(ddy(radial)*dx.x-ddx(radial)*dy.x)*inverse;
    right=FlatGeographicLight(uv,horizontal).xy;
    right*=rsqrt(max(1e-20,dot(right,right)));
    up=FlatGeographicLight(uv,vertical).xy;
    up*=rsqrt(max(1e-20,dot(up,up)));
}
// Geographic UVs address simulation cells. Artwork distances live on the sphere,
// so one metre of detail has the same scale at the equator and near either pole.
float3 FlatDetailPosition(float2 cell)
{return FlatRadial(cell*_MapSize.zw)*(_MapSize.x/6.283185307);}
float FlatDetailHash(float3 p)
{p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);}
float4 FlatDetailNoiseWithGradient(float3 p)
{
    float3 base=floor(p),fraction=frac(p),f=fraction*fraction*(3-2*fraction);
    float3 derivative=6*fraction*(1-fraction);
    float4 lower=float4(FlatDetailHash(base),FlatDetailHash(base+float3(1,0,0)),FlatDetailHash(base+float3(0,1,0)),FlatDetailHash(base+float3(1,1,0)));
    float4 upper=float4(FlatDetailHash(base+float3(0,0,1)),FlatDetailHash(base+float3(1,0,1)),FlatDetailHash(base+float3(0,1,1)),FlatDetailHash(base+1));
    float bottom=lerp(lerp(lower.x,lower.y,f.x),lerp(lower.z,lower.w,f.x),f.y);
    float top=lerp(lerp(upper.x,upper.y,f.x),lerp(upper.z,upper.w,f.x),f.y);
    float dx=lerp(lerp(lower.y-lower.x,lower.w-lower.z,f.y),lerp(upper.y-upper.x,upper.w-upper.z,f.y),f.z)*derivative.x;
    float dy=lerp(lerp(lower.z-lower.x,lower.w-lower.y,f.x),lerp(upper.z-upper.x,upper.w-upper.y,f.x),f.z)*derivative.y;
    return float4(lerp(bottom,top,f.z),dx,dy,(top-bottom)*derivative.z);
}
float FlatDetailNoise(float3 p){return FlatDetailNoiseWithGradient(p).x;}
float3 FlatDetailWeights(float3 radial)
{float3 w=pow(abs(radial),4);return w/max(1e-6,w.x+w.y+w.z);}
// Fixed spherical noise moves the ecological blend ONLY. Coastlines, drainage,
// ownership and ice retain their authoritative coordinates, through every zoom.
float2 FlatEcotoneCell(float2 cell,float shore)
{
    if(shore>=-.05)return cell;
    float3 radial=FlatRadial(cell*_MapSize.zw),p=radial*38;
    float3 tangent=float3(FlatDetailNoise(p+13),FlatDetailNoise(p+37),FlatDetailNoise(p+71))-.5;
    tangent-=radial*dot(tangent,radial);
    float grain=FlatDetailNoise(radial*123)-.5;
    float inland=smoothstep(.05,2.0,-shore);
    float3 warped=normalize(radial+tangent*(.025+grain*.014)*inland);
    return OrbisGeographicUV(warped)*_MapSize.xy;
}
float3 FlatEcotoneColor(float3 baseColor,float2 cell,float canopy,float wetness)
{
    float3 p=FlatDetailPosition(cell);
    float patch=FlatDetailNoise(p*.55)*.65+FlatDetailNoise(p*2.1+29)*.35;
    float cover=smoothstep(.16,.82,canopy+(patch-.5)*.20);
    float3 grass=FlatColor(lerp(float3(.48,.49,.25),float3(.32,.46,.23),saturate(wetness)));
    float3 forest=FlatColor(float3(.14,.31,.20));
    // Continuous understory prevents categorical palette rectangles within forests.
    return lerp(baseColor,lerp(grass,forest,cover),saturate(canopy*1.6)*.65);
}
float FlatDetailTexture(sampler2D artwork,float3 p,float3 weights,float2 offset,float lod)
{
    return tex2Dlod(artwork,float4(p.yz+offset,0,lod)).r*weights.x+
           tex2Dlod(artwork,float4(p.zx+offset,0,lod)).r*weights.y+
           tex2Dlod(artwork,float4(p.xy+offset,0,lod)).r*weights.z;
}
float2 FlatDetailNormal(sampler2D artwork,float3 p,float3 weights,float2 offset,float lod,float3 right,float3 up)
{
    float2 x=UnpackNormal(tex2Dlod(artwork,float4(p.yz+offset,0,lod))).xy;
    float2 y=UnpackNormal(tex2Dlod(artwork,float4(p.zx+offset,0,lod))).xy;
    float2 z=UnpackNormal(tex2Dlod(artwork,float4(p.xy+offset,0,lod))).xy;
    float3 g=float3(0,x.x,x.y)*weights.x+float3(y.y,0,y.x)*weights.y+float3(z.x,z.y,0)*weights.z;
    return float2(dot(g,right),dot(g,up));
}
float FlatHash(float2 p)
{
    // Integer number of canopy cells around the complete map.
    p.x=fmod(p.x+_MapSize.x*4,_MapSize.x*2);
    return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);
}
float2 FlatHeightGradient(float h,float2 cell)
{
    float2 dx=ddx(cell),dy=ddy(cell);
    dx.x-=round(dx.x/max(1,_MapSize.x))*_MapSize.x;dy.x-=round(dy.x/max(1,_MapSize.x))*_MapSize.x;
    float det=dx.x*dy.y-dx.y*dy.x;
    float safeDet=(det<0?-1:1)*max(abs(det),1e-8);
    return clamp(float2(ddx(h)*dy.y-ddy(h)*dx.y,ddy(h)*dx.x-ddx(h)*dy.x)/safeDet,-4000,4000);
}
float2 FlatMeridianSample(float2 uv)
{
    if(uv.y<0)return float2(uv.x+.5,-uv.y);
    if(uv.y>1)return float2(uv.x+.5,2-uv.y);
    return uv;
}
// Four equal great-circle steps in the site's tangent frame. The two east/west
// endpoints have the same latitude, including when the footprint straddles a pole.
void FlatGroundSamples(float2 uv,float steps,out float4 west,out float4 east,out float4 south,out float4 north)
{
    float latitude=(uv.y-.5)*3.141592654;
    float angle=steps*6.283185307*_MapSize.z;
    float sl,cl,s,c;sincos(latitude,sl,cl);sincos(angle,s,c);
    float longitudeStep=OrbisGeographicAtan2(s,cl*c)/6.283185307;
    float sideV=.5+OrbisGeographicAtan2(sl*c,length(float2(cl*c,s)))/3.141592654;
    west=FlatPolarField(FlatVisualSample(float4(uv.x-longitudeStep,sideV,0,0)),sideV);
    east=FlatPolarField(FlatVisualSample(float4(uv.x+longitudeStep,sideV,0,0)),sideV);
    float2 dy=float2(0,angle/3.141592654);
    float2 southUV=FlatMeridianSample(uv-dy),northUV=FlatMeridianSample(uv+dy);
    south=FlatPolarField(FlatVisualSample(float4(southUV,0,0)),southUV.y);
    north=FlatPolarField(FlatVisualSample(float4(northUV,0,0)),northUV.y);
}
// Height difference per equatorial tile of ground distance, along east and north.
float2 FlatFieldGradient(float2 cell,float useDepth)
{
    float4 west,east,south,north;FlatGroundSamples(cell*_MapSize.zw,1,west,east,south,north);
    if(useDepth>.5)return float2(log2(1+max(0,west.b))-log2(1+max(0,east.b)),log2(1+max(0,south.b))-log2(1+max(0,north.b)))*680;
    return float2(east.r-west.r,north.r-south.r)*.5;
}
float2 FlatTerrainMetricGradient(float2 cell,float2 gradient)
{
    float latitude=(cell.y*_MapSize.w-.5)*3.141592654;
    return gradient*float2(1/max(.08,cos(latitude)),2*_MapSize.y/_MapSize.x);
}
// XYZ is the geographic direction to the light. W fades direct relief at the
// horizon and with lunar phase. A fixed cartographic key serves the unlit map.
float4 FlatTerrainLight(float3 localSun,float4 localMoon,float dayNight)
{
    float sun=smoothstep(0,.16,localSun.z);
    float moon=(1-sun)*smoothstep(0,.20,localMoon.z)*saturate(localMoon.w/.12)*.48;
    float3 direction=lerp(float3(-.45,.60,.75),localSun*sun+localMoon.xyz*moon,dayNight);
    direction*=rsqrt(max(1e-8,dot(direction,direction)));
    return float4(direction,lerp(1,sun+moon,dayNight));
}
float4 FlatGroundProperties(float4 geometry)
{
    float material=floor(geometry.g+.5);
    float retention=material<.5?.72:material<1.5?.24:material<2.5?1:material<3.5?.48:material<4.5?.72:0;
    return float4(1-step(.5,abs(material-1)),1-step(.5,abs(material-3)),retention,geometry.b);
}
// Filter physical properties after decoding each categorical ID. Sampling a
// mean ID between sand and rock could introduce a band of silt or gravel.
float4 FlatNaturalGround(sampler2D geometry,float2 cell,float4 centre,float pixelsPerTile,float waterMask)
{
    float4 ground=float4(0,0,.68,0);
    float detail=smoothstep(4,16,pixelsPerTile)*(1-saturate(waterMask));
    [branch] if(detail>0)
    {
        float2 tile=(floor(cell)+.5)*_MapSize.zw,offset=frac(cell)-.5;
        float2 neighbour=sign(offset)*_MapSize.zw,blend=abs(offset);
        float4 x=FlatGroundProperties(tex2Dlod(geometry,float4(tile+float2(neighbour.x,0),0,0)));
        float4 y=FlatGroundProperties(tex2Dlod(geometry,float4(tile+float2(0,neighbour.y),0,0)));
        float4 xy=FlatGroundProperties(tex2Dlod(geometry,float4(tile+neighbour,0,0)));
        float4 mixed=lerp(lerp(FlatGroundProperties(centre),x,blend.x),lerp(y,xy,blend.x),blend.y);
        ground.rgb=lerp(ground.rgb,mixed.rgb,detail);
        ground.a=mixed.a;
    }
    else ground.a=tex2Dlod(geometry,float4(cell*_MapSize.zw,0,0)).b;
    return ground;
}
float FlatWetMineral(float retention,float wetness,float canopy)
{
    return smoothstep(.48,.92,wetness)*(1-smoothstep(.16,.70,canopy))*retention;
}
// A second, geodesic stencil describes the mountain mass around each slope.
// Its distances cross poles and longitude seams on the sphere, independent of
// projection, camera bearing and zoom. Only existing presentation heights are read.
float4 FlatTerrainRelief(float2 cell,float height,float2 gradient,float4 light,float3 view,float wetMineral,float surveyWeight)
{
    if(_FieldReady<.5||_Relief<.01)return float4(1,1,1,0);
    float2 uv=cell*_MapSize.zw;
    float4 west,east,south,north;FlatGroundSamples(uv,2,west,east,south,north);
    float2 broadGradient=float2(east.r-west.r,north.r-south.r)*.25;
    // Final source rows have distinct longitude samples. Fade this wider stencil
    // before a sample enters that unresolved cap or crosses the pole.
    float polarDistance=min(uv.y,1-uv.y)*_MapSize.y;
    float stencilRows=4*_MapSize.y/_MapSize.x;
    float broadWeight=smoothstep(stencilRows+.5,stencilRows+1.5,polarDistance)*(1-surveyWeight);
    float2 slope=lerp(gradient,broadGradient,.38*broadWeight);
    float3 normal=normalize(float3(-slope/180,1));
    float direct=saturate(dot(normal,light.xyz));
    float face=(direct-saturate(light.z))*light.w;
    float4 neighbours=float4(west.r,east.r,south.r,north.r);
    float form=(height-dot(neighbours,float4(.25,.25,.25,.25)))/220*broadWeight;
    // Surrounding higher ground reduces sky light in valleys and on lower faces.
    // Reuse the geodesic stencil so the shadow follows the actual mountain mass.
    float enclosure=dot(max(neighbours-height,0),float4(.25,.25,.25,.25))/400*broadWeight;
    float sky=1-.26*saturate(enclosure)-.12*(1-normal.z);
    // Diffuse sky softens the shadow side. A broader highlight roll-off keeps
    // differently inclined sunward slopes distinguishable under a grazing sun.
    float faceResponse=face/(1+2.8*max(face,0)+1.35*max(-face,0));
    float shade=(1+faceResponse*1.06)*sky+.078*saturate(form);
    float3 tint=max(face,0)*float3(.035,.008,-.018)+max(-face,0)*float3(-.035,0,.045);
    // Broad dielectric reflection from exposed damp earth. Reuse the landform
    // normal and the same geographic eye direction used for water reflections.
    float3 halfVector=view+light.xyz;
    halfVector*=rsqrt(max(1e-7,dot(halfVector,halfVector)));
    float sheen=pow(saturate(dot(normal,halfVector)),24)*direct*light.w*wetMineral*.038;
    float strength=saturate(_Relief);
    return float4(lerp(1,clamp(shade+tint,.58,1.24),strength),sheen*strength);
}
float4 FlatTerrainRelief(float2 cell,float height,float2 gradient,float4 light,float3 view,float wetMineral)
{
    return FlatTerrainRelief(cell,height,gradient,light,view,wetMineral,0);
}
float FlatTerrainSnow(float coverage,float2 cell,float2 gradient,float pixelsPerTile)
{
    if(coverage<=0)return 0;
    float3 position=FlatDetailPosition(cell);
    float breakup=FlatDetailNoise(position*2.3+47)*.7+FlatDetailNoise(position*7.1+13)*.3;
    coverage=saturate(coverage+(breakup-.5)*1.6*coverage*(1-coverage)*smoothstep(3,14,pixelsPerTile));
    float exposed=smoothstep(150,650,length(gradient));
    return coverage*(1-exposed*.78);
}
float3 FlatElevationColor(float metres)
{
    float3 land=lerp(float3(.35,.50,.34),float3(.59,.60,.39),smoothstep(0,700,metres));
    land=lerp(land,float3(.66,.56,.43),smoothstep(700,1800,metres));
    land=lerp(land,float3(.65,.66,.65),smoothstep(1800,2900,metres));
    land=lerp(land,float3(.91,.92,.89),smoothstep(2900,4200,metres));
    float3 seabed=lerp(float3(.25,.53,.60),float3(.07,.18,.29),smoothstep(0,3600,-metres));
    return FlatColor(lerp(seabed,land,smoothstep(-30,30,metres)));
}
float3 FlatTerrainFine(float3 col,float2 cell,float pixelsPerTile,float2 heightGradient,float4 field,float2 sandGravel,float wetness,float4 light)
{
    float3 position=FlatDetailPosition(cell);
    float footprint=max(length(ddx(position)),length(ddy(position)));
    float terrainWeight=smoothstep(4,24,pixelsPerTile)*(1-smoothstep(.20,.65,footprint));
    float3 result=col;
    [branch] if(terrainWeight>0)
    {
        float grainWeight=terrainWeight*(1-smoothstep(.40,1.35,footprint*25));
        float fineWeight=terrainWeight*(1-smoothstep(.40,1.35,footprint*11));
        float grain=FlatDetailNoise(position*25+53);
        float4 brokenNoise=FlatDetailNoiseWithGradient(position*11+19);
        float broken=brokenNoise.x;
        float4 ridgeNoise=FlatDetailNoiseWithGradient(position*1.6+19);
        float crag=ridgeNoise.x;
        // A fine soil grain replaces broad colour bubbles. It remains fixed on the
        // sphere while the camera pans, turns or crosses the date line.
        float bare=1-smoothstep(.20,.70,field.a);
        float grainAmount=lerp(.052,.096,sandGravel.y*bare)*(1-.28*wetness);
        result*=1+(grain-.5)*grainAmount*grainWeight+(broken-.5)*.045*fineWeight;
        // Fine airlaid sand carries low ripples. Wet grains and plant cover damp
        // their contrast. The spherical coordinate keeps the bands on the ground.
        float ripple=sin(dot(position,float3(11,7,-15))+crag*3.6);
        result*=1+ripple*sandGravel.x*bare*(1-wetness)*fineWeight*.033;
        if(_FieldReady>=.5)
        {
            float latitude=(cell.y*_MapSize.w-.5)*3.141592654;
            float slope=length(heightGradient);
            float exposed=smoothstep(140,430,slope)*(1-smoothstep(.45,.85,field.a));
            exposed*=smoothstep(.25,.70,crag*.65+broken*.35);
            exposed=max(exposed,smoothstep(1900,3200,field.r)*.35);
            // Exposure follows altitude and slope. Rock albedo varies over the
            // outcrop, so an entire mountainside cannot stripe at a fixed height.
            float rockTone=smoothstep(.19,.81,crag*.70+broken*.30);
            float3 rock=FlatColor(lerp(float3(.26,.28,.30),float3(.60,.59,.55),rockTone));
            // Ridge breakup adds small face changes to the real elevation gradient.
            // It never modifies elevation, drainage, ownership or movement data.
            // Analytic geographic gradients keep the same rock face under camera
            // rotation. Screen-quad derivatives select different sides of sharp ridges.
            float3 ridgeDerivative=ridgeNoise.yzw*1.6+brokenNoise.yzw*.55;
            float longitude=(cell.x*_MapSize.z-.5)*6.283185307;
            float3 east=float3(cos(longitude),0,-sin(longitude));
            float3 north=float3(-sin(latitude)*sin(longitude),cos(latitude),-sin(latitude)*cos(longitude));
            float2 ridgeGradient=float2(dot(ridgeDerivative,east),dot(ridgeDerivative,north));
            float bevel=clamp(1-dot(ridgeGradient,light.xy)*light.w*.17,.82,1.16);
            rock*=lerp(1,bevel,fineWeight)*(.90+.16*grain);
            result=lerp(result,rock,exposed*terrainWeight*.72);
        }
    }
    return result;
}
float FlatRelief(float h,float2 cell,float3 localLight)
{
    float2 g=FlatHeightGradient(h,cell);
    float3 n=normalize(float3(-g/450,1));
    float shade=.64+.48*saturate(dot(n,normalize(localLight)));
    return lerp(1,clamp(shade,.55,1.12),_Relief);
}
// Shading uses a wider reconstruction stencil than contour placement. This
// avoids a checkerboard of derivative lobes on quantised 100m authoring terraces.
float FlatGradientRelief(float2 gradient,float3 localLight)
{
    if(_FieldReady<.5||_Relief<.01)return 1;
    float3 normal=normalize(float3(-gradient/450,1));
    return lerp(1,clamp(.64+.48*saturate(dot(normal,normalize(localLight))),.55,1.12),_Relief);
}
float FlatFieldRelief(float2 cell,float useDepth,float3 localLight)
{
    if(_FieldReady<.5||_Relief<.01)return 1;
    float2 gradient=FlatFieldGradient(cell,useDepth);
    if(useDepth>.5)return FlatGradientRelief(gradient,localLight);
    float4 relief=FlatTerrainRelief(cell,FlatVisualSample(float4(cell*_MapSize.zw,0,0)).r,gradient,
                                    float4(normalize(localLight),1),float3(0,0,1),0);
    return dot(relief.rgb,float3(.2126,.7152,.0722));
}
#include "FlatVegetationSeason.cginc"
float4 FlatCanopyPlacement(float2 cell)
{
    float2 id=floor(cell*2);
    float2 seeds=float2(FlatHash(id),FlatHash(id+float2(71,19)));
    return float4((id+.5+(seeds-.5)*.45)*.5,seeds);
}
float3 FlatCanopyColor(float3 col,float2 cell,float canopy,float pixelsPerTile,float2 seeds,float2 fromRoot)
{
    float detailFade=smoothstep(3,9,pixelsPerTile)*_Vegetation;
    float radius=lerp(.12,.27,seeds.y),distanceToCrown=length(fromRoot*float2(1,1.14));
    float aa=max(fwidth(distanceToCrown),.015);
    float occupied=1-step(saturate((canopy-.24)*1.5),seeds.x);
    float crown=(1-smoothstep(radius-aa,radius+aa,distanceToCrown))*occupied;
    float crownLight=lerp(.70,1.23,saturate(.55+dot(fromRoot,float2(-1.4,1.8))));
    // Small canopy marks use crown shading; displaced shadows are handled by PixelProps.
    col=lerp(col,col*crownLight,crown*detailFade);
    float3 radial=FlatRadial(cell*_MapSize.zw);
    float grain=.5+.5*sin(radial.x*970+sin(radial.y*650))*sin(radial.z*1170);
    float footprint=max(length(ddx(radial*1170)),length(ddy(radial*1170)));
    float grainFade=(1-smoothstep(.6,2.2,footprint))*_Vegetation;
    return col*lerp(1,.95+.10*grain,grainFade);
}
float3 FlatVegetation(float3 col,float2 cell,float canopy,float pixelsPerTile)
{
    if(_Vegetation<.5)return col;
    float4 plant=FlatCanopyPlacement(cell);
    float2 offset=(cell-plant.xy)*2;
    offset.y/=lerp(1,FlatArtworkLatitudeScale(plant.y*_MapSize.w),_LocalPhysicalArtwork);
    return FlatCanopyColor(col,cell,canopy,pixelsPerTile,plant.zw,offset);
}
float FlatGentleContourWeight(float2 gradient)
{
    return 1-smoothstep(50,160,length(gradient));
}
float FlatContour(float metres,float interval,float pixelsPerTile,out float major)
{
    // Nested elevation levels stay fixed while zoom fades the intermediate lines.
    // Screen-space spacing also limits line density on steep or distant slopes.
    float pixelHeight=fwidth(metres),baseInterval=max(10,interval*.5);
    float zoomLevel=1-smoothstep(8,48,pixelsPerTile);
    float densityLevel=log2(max(1,pixelHeight*24/baseInterval));
    float level=max(zoomLevel,densityLevel),blend=smoothstep(0,1,frac(level));
    float2 spacing=baseInterval*exp2(floor(level))*float2(1,2);
    float2 scaled=metres/spacing,aa=max(pixelHeight/spacing,1e-6);
    float2 nearest=floor(scaled+.5);
    float2 indexLine=1-step(.01,abs(frac(nearest*.25+.5)-.5));
    float2 distanceToContour=abs(frac(scaled+.5)-.5);
    float2 lines=1-smoothstep(aa*.20,aa*lerp(.80,1.05,indexLine),distanceToContour);
    float coverage=lerp(lines.x,lines.y,blend);
    major=lerp(lines.x*indexLine.x,lines.y*indexLine.y,blend)/max(1e-6,coverage);
    return coverage*smoothstep(.001,.02,pixelHeight);
}
#endif
