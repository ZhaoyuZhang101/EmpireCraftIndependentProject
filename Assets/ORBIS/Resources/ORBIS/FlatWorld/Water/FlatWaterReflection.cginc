#ifndef ORBIS_FLAT_WATER_REFLECTION
#define ORBIS_FLAT_WATER_REFLECTION
#include "../FlatCloudCover.cginc"

float3 FlatWaterSkyReflection(float2 cell,float3 ray,float3 localSun,float3 light,float clouds)
{
    float horizon=pow(1-saturate(ray.z),3);
    float3 sky=lerp(FlatColor(float3(.30,.47,.64)),FlatColor(float3(.62,.71,.77)),horizon);
    float dusk=exp(-abs(localSun.z)*12)*horizon;
    sky=lerp(sky,FlatColor(float3(.71,.43,.29)),dusk*.45);
    if(clouds>.001&&ray.z>.02)
    {
        float2 uv=cell*_MapSize.zw;
        float longitude=(uv.x-.5)*6.283185307,latitude=(uv.y-.5)*3.141592654;
        float3 radial=FlatRadial(uv),east=float3(cos(longitude),0,-sin(longitude));
        float3 north=float3(-sin(latitude)*sin(longitude),cos(latitude),-sin(latitude)*cos(longitude));
        float3 direction=east*ray.x+north*ray.y+radial*ray.z;
        float travel=-ray.z+sqrt(ray.z*ray.z+.006*(2+.006));
        float4 column=FlatCloudColumn(OrbisGeographicUV(normalize(radial+direction*travel)));
        float cover=saturate(column.r*.85+column.g*.30)*clouds;
        sky=lerp(sky,FlatColor(float3(.88,.91,.94))*(1-column.r*.18),cover);
    }
    return sky*light;
}

#ifdef ORBIS_FLAT_PIXEL_DETAIL
float4 FlatWaterPropReflection(float2 cell,float waterHeight,float3 ray,float3 light)
{
    float4 reflected=0;float nearest=1e10;
    if(abs(ray.y)<.03||ray.z<.12)return reflected;
    float2 id=floor(cell);
    float3 direction=float3(ray.x,ray.y/PDTreeUp.y,ray.z);
    [loop] [fastopt] for(int row=0;row<=2;row++)
    [loop] [fastopt] for(int column=-2;column<=2;column++)
    {
        float2 source=id+float2(column,row*sign(ray.y));
        if(source.y<0||source.y>=_MapSize.y)continue;
        float2 uv=(source+.5)*_MapSize.zw;
        float4 info=tex2Dlod(_InfoTex,float4(uv,0,0))*255;
        if((info.g>.5&&info.g<4.5)||info.b>1.5)continue;
        bool settlement=info.b>.5;
        float scale=max(.02,cos((uv.y-.5)*3.141592654));
        float4 placement=settlement?float4(16,16-11*scale,1,0):PDTreePlacement(source);
        float2 ground=((cell-source)*32-placement.xy)/placement.z;
        ground.y/=scale;
        float fade=1-smoothstep(44,58,length(ground));
        if(fade<.001)continue;
        float travel=-ground.y/(PDTreeUp.y*direction.y);
        if(travel<=0||travel*scale*placement.z>=nearest)continue;
        float2 rootUV=(source+placement.xy/32)*_MapSize.zw;
        float elevation=max(0,FlatVisualSample(float4(rootUV,0,0)).r);
        float heightScale=.000006*32/((6.283185307/_MapSize.x)*scale*placement.z);
        float height=(waterHeight-elevation)*heightScale+direction.z*travel;
        float2 pixel=float2(ground.x+direction.x*travel,height*PDTreeUp.z);
        if(abs(pixel.x)>14||pixel.y<0||pixel.y>40)continue;
        float4 art;
        if(settlement)art=PDSettlementArtwork(floor(pixel+float2(16,5)),light);
        else
        {
            float tree;
            float4 ecology=FlatEcologyData(uv,info,tree);placement.w=ecology.a;
            if(!PDTreeAvailable(source,placement,info,ecology))continue;
            float shore=1-smoothstep(.2,1.2,-FlatVisualSample(float4(uv,0,0)).g);
            float species=PDTreeSpecies(ecology,info,shore,tree);
            float leaf=FlatVegetationSeason(source+.5).y;
            float3 sun=FlatGeographicLight(uv,normalize(_FlatSun+float3(0,1e-7,0)));
            art=PDTreeArtwork(floor(pixel),source,species,placement.w,leaf,light,sun);
        }
        if(art.a<.5)continue;
        nearest=travel*scale*placement.z;
        reflected=float4(art.rgb,art.a*fade*smoothstep(.03,.12,abs(ray.y)));
    }
    return reflected;
}
#endif

float3 FlatReflectWater(float3 color,float2 cell,float4 field,float waterHeight,float3 view,float3 sun,
                       float3 light,FlatWaterSurfaceData surface,float clouds,float props)
{
    float3 ray=reflect(-view,surface.normal);
    float nv=saturate(dot(view,surface.normal));
    float skyVisible=smoothstep(0,.12,ray.z);
    float weight=(.06+.45*pow(1-nv,3))*surface.visible*skyVisible;
    float3 sky=FlatWaterSkyReflection(cell,ray,sun,light,clouds);
    // Replace the original constant sky contribution after depth/foam blending.
    color-=light*float3(.34,.48,.58)*surface.skyWeight*skyVisible;
    color=lerp(max(0,color),sky,weight);
    #ifdef ORBIS_FLAT_PIXEL_DETAIL
    if(props>.001&&field.g<1.6&&_FieldReady>.5)
    {
        float4 reflected=FlatWaterPropReflection(cell,waterHeight,ray,light);
        float opacity=max(.16,weight*2.5)*surface.visible*props;
        color=lerp(color,reflected.rgb,reflected.a*opacity);
    }
    #endif
    return color;
}
#endif
