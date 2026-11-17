#ifndef ORBIS_FLAT_SURFACE_FIELD
#define ORBIS_FLAT_SURFACE_FIELD
#include "FlatVisualTexture.cginc"
#include "../GeographicCoordinates.cginc"
float4 _MapSize;
float _FieldReady,_SmoothTerrain,_Vegetation,_Bathymetry,_Relief;
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
    return field;
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
float2 FlatFieldGradient(float2 cell,float useDepth)
{
    float2 uv=cell*_MapSize.zw,dx=float2(_MapSize.z,0),dy=float2(0,_MapSize.w);
    float4 west=FlatVisualSample(float4(uv-dx,0,0)),east=FlatVisualSample(float4(uv+dx,0,0));
    float4 south=FlatVisualSample(float4(uv-dy,0,0)),north=FlatVisualSample(float4(uv+dy,0,0));
    if(useDepth>.5)return float2(log2(1+max(0,east.b))-log2(1+max(0,west.b)),log2(1+max(0,north.b))-log2(1+max(0,south.b)))*680;
    return float2(east.r-west.r,north.r-south.r)*.5;
}
float2 FlatTerrainMetricGradient(float2 cell,float2 gradient)
{
    float latitude=(cell.y*_MapSize.w-.5)*3.141592654;
    return gradient*float2(1/max(.08,cos(latitude)),2*_MapSize.y/_MapSize.x);
}
float FlatTerrainHeight(float3 direction)
{
    float2 uv=OrbisGeographicUV(direction);
    return FlatVisualSample(float4(uv,0,0)).r;
}
// A second, geodesic stencil describes the mountain mass around each slope.
// Its distances cross poles and longitude seams on the sphere, independent of
// projection, camera bearing and zoom. Only existing presentation heights are read.
float FlatTerrainRelief(float2 cell,float height,float2 gradient,float3 localLight)
{
    if(_FieldReady<.5||_Relief<.01)return 1;
    float2 uv=cell*_MapSize.zw;
    float longitude=(uv.x-.5)*6.283185307,latitude=(uv.y-.5)*3.141592654;
    float3 radial=FlatRadial(uv);
    float3 east=float3(cos(longitude),0,-sin(longitude));
    float3 north=float3(-sin(latitude)*sin(longitude),cos(latitude),-sin(latitude)*cos(longitude));
    float angle=12.566370614/_MapSize.x,s=sin(angle),c=cos(angle);
    float westHeight=FlatTerrainHeight(radial*c-east*s),eastHeight=FlatTerrainHeight(radial*c+east*s);
    float southHeight=FlatTerrainHeight(radial*c-north*s),northHeight=FlatTerrainHeight(radial*c+north*s);
    float2 broadGradient=float2(eastHeight-westHeight,northHeight-southHeight)*.25;
    // Final source rows have distinct longitude samples. Fade this wider stencil
    // before a sample enters that unresolved cap or crosses the pole.
    float polarDistance=min(uv.y,1-uv.y)*_MapSize.y;
    float stencilRows=4*_MapSize.y/_MapSize.x;
    float broadWeight=smoothstep(stencilRows+.5,stencilRows+1.5,polarDistance);
    float2 slope=lerp(FlatTerrainMetricGradient(cell,gradient),broadGradient,.38*broadWeight);
    float3 normal=normalize(float3(-slope/320,1));
    float3 key=normalize(localLight),fill=normalize(float3(.52,-.40,.75));
    float diffuse=.72*saturate(dot(normal,key))+.28*saturate(dot(normal,fill));
    float flatDiffuse=.72*saturate(key.z)+.28*fill.z;
    float form=(height-(westHeight+eastHeight+southHeight+northHeight)*.25)/260*broadWeight;
    float shade=(1+(diffuse-flatDiffuse)*1.12)*(1-.22*saturate(-form))+.08*saturate(form);
    return lerp(1,clamp(shade,.58,1.24),saturate(_Relief));
}
float FlatTerrainSnow(float coverage,float2 cell,float2 gradient,float pixelsPerTile)
{
    if(coverage<=0)return 0;
    float3 position=FlatDetailPosition(cell);
    float breakup=FlatDetailNoise(position*2.3+47)*.7+FlatDetailNoise(position*7.1+13)*.3;
    coverage=saturate(coverage+(breakup-.5)*1.6*coverage*(1-coverage)*smoothstep(3,14,pixelsPerTile));
    float exposed=smoothstep(300,950,length(FlatTerrainMetricGradient(cell,gradient)));
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
float3 FlatTerrainFine(float3 col,float2 cell,float pixelsPerTile,float2 heightGradient)
{
    float3 position=FlatDetailPosition(cell);
    float footprint=max(length(ddx(position)),length(ddy(position)));
    float terrainWeight=smoothstep(4,24,pixelsPerTile)*(1-smoothstep(.20,.65,footprint));
    float grainWeight=terrainWeight*(1-smoothstep(.40,1.35,footprint*25));
    float fineWeight=terrainWeight*(1-smoothstep(.40,1.35,footprint*11));
    float grain=FlatDetailNoise(position*25+53);
    float4 brokenNoise=FlatDetailNoiseWithGradient(position*11+19);
    float broken=brokenNoise.x;
    float4 ridgeNoise=FlatDetailNoiseWithGradient(position*1.6+19);
    float crag=ridgeNoise.x;
    // A fine soil grain replaces broad colour bubbles. It remains fixed on the
    // sphere while the camera pans, turns or crosses the date line.
    col*=1+(grain-.5)*.045*grainWeight+(broken-.5)*.045*fineWeight;
    if(_FieldReady<.5)return col;
    float4 field=FlatVisualSample(float4(cell*_MapSize.zw,0,0));
    float latitude=(cell.y*_MapSize.w-.5)*3.141592654;
    float2 gradient=FlatTerrainMetricGradient(cell,heightGradient);
    float slope=length(gradient);
    float exposed=smoothstep(140,430,slope)*(1-smoothstep(.45,.85,field.a));
    exposed*=smoothstep(.25,.70,crag*.65+broken*.35);
    exposed=max(exposed,smoothstep(1900,3200,field.r)*.35);
    float strata=.5+.5*sin(field.r*.025+broken*3);
    float3 rock=FlatColor(lerp(float3(.34,.34,.29),float3(.55,.53,.45),crag*.7+strata*.3));
    // Ridge breakup adds small face changes to the real elevation gradient.
    // It never modifies elevation, drainage, ownership or movement data.
    // Analytic geographic gradients keep the same rock face under camera
    // rotation. Screen-quad derivatives select different sides of sharp ridges.
    float3 ridgeDerivative=ridgeNoise.yzw*1.6+brokenNoise.yzw*.55;
    float longitude=(cell.x*_MapSize.z-.5)*6.283185307;
    float3 east=float3(cos(longitude),0,-sin(longitude));
    float3 north=float3(-sin(latitude)*sin(longitude),cos(latitude),-sin(latitude)*cos(longitude));
    float2 ridgeGradient=float2(dot(ridgeDerivative,east),dot(ridgeDerivative,north));
    float bevel=clamp(1+dot(ridgeGradient,float2(-.10,.13)),.80,1.16);
    rock*=lerp(1,bevel,fineWeight)*(.90+.16*grain);
    return lerp(col,rock,exposed*terrainWeight*.72);
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
    return FlatTerrainRelief(cell,FlatVisualSample(float4(cell*_MapSize.zw,0,0)).r,gradient,localLight);
}
float3 FlatVegetation(float3 col,float2 cell,float canopy,float pixelsPerTile)
{
    if(_Vegetation<.5)return col; // Uniform branch: no divergent derivative lanes.
    float detailFade=smoothstep(3,9,pixelsPerTile)*_Vegetation;
    float2 p=cell*2,unitId=floor(p),q=frac(p)-.5;
    float seed=FlatHash(unitId),seed2=FlatHash(unitId+float2(71,19));
    float2 centre=(float2(seed,seed2)-.5)*.45;
    float radius=lerp(.12,.27,seed2),distanceToCrown=length((q-centre)*float2(1,1.14));
    float aa=max(fwidth(distanceToCrown),.015);
    float occupied=1-step(saturate((canopy-.24)*1.5),seed);
    float crown=(1-smoothstep(radius-aa,radius+aa,distanceToCrown))*occupied;
    float shadow=(1-smoothstep(radius-aa,radius+aa,length((q-centre-float2(.075,-.09))*float2(1,1.14))))*occupied;
    float crownLight=lerp(.70,1.23,saturate(.55+dot(q-centre,float2(-1.4,1.8))));
    // Small canopy marks use crown shading; displaced shadows are handled by PixelProps.
    col=lerp(col,col*crownLight,crown*detailFade);
    float3 radial=FlatRadial(cell*_MapSize.zw);
    float grain=.5+.5*sin(radial.x*970+sin(radial.y*650))*sin(radial.z*1170);
    float footprint=max(length(ddx(radial*1170)),length(ddy(radial*1170)));
    float grainFade=(1-smoothstep(.6,2.2,footprint))*_Vegetation;
    return col*lerp(1,.95+.10*grain,grainFade);
}
float FlatContour(float metres,float interval,out float major)
{
    float pixelHeight=fwidth(metres),baseInterval=max(25,interval);
    float multiplier=exp2(ceil(log2(max(1,pixelHeight*5/baseInterval))));
    float scaled=metres/(baseInterval*multiplier),aa=max(fwidth(scaled),1e-6);
    float distanceToContour=abs(frac(scaled+.5)-.5);
    float nearest=floor(scaled+.5);
    major=1-step(.01,abs(frac(nearest*.2+.5)-.5));
    return (1-smoothstep(aa*.25,aa*.85,distanceToContour))*smoothstep(.001,.02,pixelHeight);
}
#endif
